using Npgsql;
using TiendaMicroempresas.Api.Contracts.Products;
using TiendaMicroempresas.Api.Contracts.Store;

namespace TiendaMicroempresas.Api.Repositories;

// Lectura del catalogo publico del marketplace y operaciones CRUD de productos de empresa.
public sealed partial class SqlStoreRepository
{
    public Task<bool> HasBusinessesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = CreateOpenConnection();
        var count = Convert.ToInt32(ExecuteScalar(connection, null, "SELECT COUNT(*) FROM Businesses;"));
        return Task.FromResult(count > 0);
    }

    public Task<StoreOverviewResponse> GetMarketplaceOverviewAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = CreateOpenConnection();
        var businesses = GetBusinesses(connection, null, publishedOnly: true, businessId: null);

        var featuredProducts = businesses
            .SelectMany(business => business.Products
                .Where(product => product.IsFeatured && product.IsPublished)
                .Select(product => new FeaturedProductDto(
                    product.ProductId,
                    product.BusinessId,
                    product.Name,
                    product.Category,
                    product.Description,
                    product.Price,
                    product.MinimumOrder,
                    product.Stock,
                    product.ImageUrl,
                    product.IsFeatured,
                    business.BusinessName,
                    business.Slug,
                    business.Tagline,
                    business.City)))
            .OrderByDescending(product => product.Stock)
            .ThenBy(product => product.BusinessName)
            .Take(8)
            .ToList();

        var categories = businesses
            .SelectMany(business => business.Products)
            .Select(product => product.Category.Trim())
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.Create(new System.Globalization.CultureInfo("es-CO"), false))
            .ToList();

        return Task.FromResult(new StoreOverviewResponse(
            businesses,
            featuredProducts,
            categories,
            businesses.Count,
            businesses.Sum(business => business.Products.Count)));
    }

    public Task<AdminCatalogResponse> GetAdminCatalogAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = CreateOpenConnection();
        var businessId = GetBusinessIdByToken(connection, null, token);
        var business = GetBusinessById(connection, null, businessId, publishedOnly: false);
        var categories = business.Products
            .Select(product => product.Category.Trim())
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(category => category, StringComparer.Create(new System.Globalization.CultureInfo("es-CO"), false))
            .ToList();

        return Task.FromResult(new AdminCatalogResponse(business, categories));
    }

    public Task<ProductDto> CreateProductAsync(string token, UpsertProductRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = CreateOpenConnection();
        var businessId = GetBusinessIdByToken(connection, null, token);
        var productId = InsertAndGetId(
            connection,
            null,
            """
            INSERT INTO Products
            (
                BusinessId,
                Name,
                Category,
                Description,
                Price,
                MinimumOrder,
                Stock,
                ImageUrl,
                IsFeatured,
                IsPublished
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            RETURNING ProductId;
            """,
            businessId,
            RequireText(request.Name, "Ingresa un nombre para el producto."),
            RequireText(request.Category, "Ingresa una categoria para el producto."),
            RequireText(request.Description, "Ingresa una descripcion para el producto."),
            RequireMoney(request.Price),
            RequireMinimumValue(request.MinimumOrder, 1, "El pedido minimo debe ser al menos 1."),
            RequireMinimumValue(request.Stock, 0, "El stock no puede ser negativo."),
            RequireImageUrl(request.ImageUrl, DefaultProductImageUrl),
            request.IsFeatured,
            request.IsPublished);

        return Task.FromResult(GetOwnedProductById(connection, null, businessId, productId));
    }

    public Task<ProductDto> UpdateProductAsync(string token, int productId, UpsertProductRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = CreateOpenConnection();
        var businessId = GetBusinessIdByToken(connection, null, token);
        EnsureProductOwnership(connection, null, businessId, productId);

        var affectedRows = ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE Products
            SET Name = ?,
                Category = ?,
                Description = ?,
                Price = ?,
                MinimumOrder = ?,
                Stock = ?,
                ImageUrl = ?,
                IsFeatured = ?,
                IsPublished = ?,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE ProductId = ? AND BusinessId = ? AND IsArchived = 0;
            """,
            RequireText(request.Name, "Ingresa un nombre para el producto."),
            RequireText(request.Category, "Ingresa una categoria para el producto."),
            RequireText(request.Description, "Ingresa una descripcion para el producto."),
            RequireMoney(request.Price),
            RequireMinimumValue(request.MinimumOrder, 1, "El pedido minimo debe ser al menos 1."),
            RequireMinimumValue(request.Stock, 0, "El stock no puede ser negativo."),
            RequireImageUrl(request.ImageUrl, DefaultProductImageUrl),
            request.IsFeatured,
            request.IsPublished,
            productId,
            businessId);

        if (affectedRows == 0)
        {
            throw new InvalidOperationException("El producto no existe o no pertenece a tu empresa.");
        }

        return Task.FromResult(GetOwnedProductById(connection, null, businessId, productId));
    }

    public Task DeleteProductAsync(string token, int productId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = CreateOpenConnection();
        var businessId = GetBusinessIdByToken(connection, null, token);
        EnsureProductOwnership(connection, null, businessId, productId);

        var affectedRows = ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE Products
            SET IsArchived = 1,
                IsPublished = 0,
                IsFeatured = 0,
                Stock = 0,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE ProductId = ? AND BusinessId = ? AND IsArchived = 0;
            """,
            productId,
            businessId);

        if (affectedRows == 0)
        {
            throw new InvalidOperationException("El producto no existe o no pertenece a tu empresa.");
        }

        return Task.CompletedTask;
    }
}
