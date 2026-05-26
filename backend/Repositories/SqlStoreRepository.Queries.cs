using System.Data;
using Npgsql;
using TiendaMicroempresas.Api.Contracts.Customers;
using TiendaMicroempresas.Api.Contracts.Orders;
using TiendaMicroempresas.Api.Contracts.Products;
using TiendaMicroempresas.Api.Contracts.Store;

namespace TiendaMicroempresas.Api.Repositories;

// Metodos privados de lectura de datos: empresas, productos, pedidos y clientes.
// Son usados por los archivos parciales de dominio y nunca expuestos directamente por la interfaz.
public sealed partial class SqlStoreRepository
{
    // Lee todas las empresas activas. Si publishedOnly es true, omite productos no publicados.
    // Si businessId tiene valor, filtra a una sola empresa.
    private static List<MarketplaceBusinessDto> GetBusinesses(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        bool publishedOnly,
        int? businessId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;

        var sql = """
            SELECT
                b.BusinessId,
                b.Slug,
                b.OwnerName,
                b.BusinessName,
                b.Email,
                b.Phone,
                b.City,
                b.Address,
                b.Tagline,
                b.Description,
                b.ShippingLeadTime,
                b.MinimumOrderAmount,
                b.LogoUrl,
                b.BannerUrl,
                b.WebsiteUrl,
                p.ProductId,
                p.Name,
                p.Category,
                p.Description,
                p.Price,
                p.MinimumOrder,
                p.Stock,
                p.ImageUrl,
                p.IsFeatured,
                p.IsPublished
            FROM Businesses b
            LEFT JOIN Products p
                ON p.BusinessId = b.BusinessId
                AND p.IsArchived = FALSE
            """;

        if (publishedOnly)
        {
            sql += " AND p.IsPublished = TRUE";
        }

        sql += " WHERE 1 = 1";

        if (businessId.HasValue)
        {
            sql += " AND b.BusinessId = $1";
            AddParameter(command, businessId.Value);
        }

        sql += " ORDER BY b.BusinessName, p.IsFeatured DESC, p.Name;";
        command.CommandText = sql;

        using var reader = command.ExecuteReader();
        var businesses = new Dictionary<int, MarketplaceBusinessBuilder>();

        while (reader.Read())
        {
            var currentBusinessId = reader.GetInt32(0);

            if (!businesses.TryGetValue(currentBusinessId, out var builder))
            {
                builder = new MarketplaceBusinessBuilder(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.GetDecimal(11),
                    reader.GetString(12),
                    reader.GetString(13),
                    reader.GetString(14));

                businesses.Add(currentBusinessId, builder);
            }

            if (!reader.IsDBNull(15))
            {
                builder.Products.Add(new ProductDto(
                    reader.GetInt32(15),
                    currentBusinessId,
                    reader.GetString(16),
                    reader.GetString(17),
                    reader.GetString(18),
                    reader.GetDecimal(19),
                    reader.GetInt32(20),
                    reader.GetInt32(21),
                    reader.GetString(22),
                    reader.GetBoolean(23),
                    reader.GetBoolean(24)));
            }
        }

        return businesses.Values.Select(builder => builder.Build()).ToList();
    }

    private static MarketplaceBusinessDto GetBusinessById(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        int businessId,
        bool publishedOnly)
    {
        var business = GetBusinesses(connection, transaction, publishedOnly, businessId).SingleOrDefault();
        return business ?? throw new InvalidOperationException("La empresa solicitada no existe.");
    }

    // Lee un producto verificando que pertenezca a la empresa del token activo.
    private static ProductDto GetOwnedProductById(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        int businessId,
        int productId)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT ProductId, BusinessId, Name, Category, Description, Price, MinimumOrder, Stock, ImageUrl, IsFeatured, IsPublished
            FROM Products
            WHERE ProductId = ? AND BusinessId = ? AND IsArchived = FALSE;
            """,
            productId,
            businessId);

        using var reader = command.ExecuteReader(CommandBehavior.SingleRow);
        if (!reader.Read())
        {
            throw new InvalidOperationException("El producto no existe o no pertenece a tu empresa.");
        }

        return new ProductDto(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetDecimal(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetString(8),
            reader.GetBoolean(9),
            reader.GetBoolean(10));
    }

    private static CustomerDto GetCustomerById(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        int customerId)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT CustomerId, FullName, Email, Phone, City, Address, AuthProvider
            FROM Customers
            WHERE CustomerId = ?;
            """,
            customerId);

        using var reader = command.ExecuteReader(CommandBehavior.SingleRow);
        if (!reader.Read())
        {
            throw new InvalidOperationException("El cliente solicitado no existe.");
        }

        return new CustomerDto(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6));
    }

    // Resuelve el BusinessId a partir del token de sesion activo.
    // Lanza excepcion si el token no existe o ya expiro.
    private static int GetBusinessIdByToken(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string token)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT BusinessId
            FROM BusinessSessions
            WHERE SessionToken = ? AND ExpiresAt > CURRENT_TIMESTAMP;
            """,
            token);

        var result = command.ExecuteScalar();
        if (result is null)
        {
            throw new InvalidOperationException("La sesion del empresario no es valida. Inicia sesion nuevamente.");
        }

        return Convert.ToInt32(result);
    }

    // Resuelve el CustomerId a partir del token de sesion activo.
    private static int GetCustomerIdByToken(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string token)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT CustomerId
            FROM CustomerSessions
            WHERE SessionToken = ? AND ExpiresAt > CURRENT_TIMESTAMP;
            """,
            token);

        var result = command.ExecuteScalar();
        if (result is null)
        {
            throw new InvalidOperationException("La sesion del cliente no es valida. Inicia sesion nuevamente.");
        }

        return Convert.ToInt32(result);
    }

    private static BusinessOrderPolicy GetBusinessOrderPolicy(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        int businessId)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            "SELECT BusinessName, MinimumOrderAmount FROM Businesses WHERE BusinessId = ?;",
            businessId);

        using var reader = command.ExecuteReader(CommandBehavior.SingleRow);
        if (!reader.Read())
        {
            throw new InvalidOperationException("La empresa seleccionada ya no esta disponible.");
        }

        return new BusinessOrderPolicy(reader.GetString(0), reader.GetDecimal(1));
    }

    private static ProductOrderData GetProductForOrder(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int businessId,
        int productId)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT ProductId, Name, Price, MinimumOrder, Stock
            FROM Products
            WHERE ProductId = ? AND BusinessId = ? AND IsPublished = TRUE AND IsArchived = FALSE;
            """,
            productId,
            businessId);

        using var reader = command.ExecuteReader(CommandBehavior.SingleRow);
        if (!reader.Read())
        {
            throw new InvalidOperationException("Uno de los productos del pedido ya no esta disponible.");
        }

        return new ProductOrderData(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetDecimal(2),
            reader.GetInt32(3),
            reader.GetInt32(4));
    }

    // Lee todos los pedidos de una empresa con sus items. Marca los nuevos como vistos.
    private static List<BusinessOrderDto> GetBusinessOrdersFeed(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        int businessId)
    {
        var orders = new Dictionary<int, BusinessOrderBuilder>();

        using (var ordersCommand = CreateCommand(
                   connection,
                   transaction,
                   """
                   SELECT
                       OrderId,
                       CustomerFullName,
                       CustomerEmail,
                       CustomerPhone,
                       CustomerCity,
                       DeliveryAddress,
                       Notes,
                       Status,
                       Total,
                       IsNew,
                       CreatedAt,
                       PaymentMethod
                   FROM Orders
                   WHERE BusinessId = ?
                   ORDER BY CreatedAt DESC, OrderId DESC;
                   """,
                   businessId))
        {
            using var ordersReader = ordersCommand.ExecuteReader();
            while (ordersReader.Read())
            {
                var orderId = ordersReader.GetInt32(0);
                orders[orderId] = new BusinessOrderBuilder(
                    orderId,
                    ordersReader.GetString(1),
                    ordersReader.GetString(2),
                    ordersReader.GetString(3),
                    ordersReader.GetString(4),
                    ordersReader.GetString(5),
                    ordersReader.IsDBNull(6) ? string.Empty : ordersReader.GetString(6),
                    ordersReader.GetString(7),
                    ordersReader.GetDecimal(8),
                    ordersReader.GetDateTime(10),
                    ordersReader.GetBoolean(9),
                    ordersReader.IsDBNull(11) ? "No especificado" : ordersReader.GetString(11));
            }
        }

        if (orders.Count == 0)
        {
            return [];
        }

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE Orders
            SET IsNew = FALSE,
                ViewedAt = COALESCE(ViewedAt, CURRENT_TIMESTAMP)
            WHERE BusinessId = ? AND IsNew = TRUE;
            """,
            businessId);

        using (var itemsCommand = CreateCommand(
                   connection,
                   transaction,
                   """
                   SELECT
                       oi.OrderId,
                       oi.ProductId,
                       p.Name,
                       oi.Quantity,
                       oi.UnitPrice,
                       oi.LineTotal
                   FROM OrderItems oi
                   LEFT JOIN Products p ON p.ProductId = oi.ProductId
                   INNER JOIN Orders o ON o.OrderId = oi.OrderId
                   WHERE o.BusinessId = ?
                   ORDER BY oi.OrderId DESC, oi.OrderItemId ASC;
                   """,
                   businessId))
        {
            using var itemsReader = itemsCommand.ExecuteReader();
            while (itemsReader.Read())
            {
                var orderId = itemsReader.GetInt32(0);
                if (!orders.TryGetValue(orderId, out var builder))
                {
                    continue;
                }

                builder.Items.Add(new BusinessOrderItemDto(
                    itemsReader.GetInt32(1),
                    itemsReader.IsDBNull(2) ? "Producto" : itemsReader.GetString(2),
                    itemsReader.GetInt32(3),
                    itemsReader.GetDecimal(4),
                    itemsReader.GetDecimal(5)));
            }
        }

        return orders.Values.Select(order => order.Build()).ToList();
    }

    // Lee el historial de pedidos de un cliente con sus items.
    private static List<CustomerOrderDto> GetCustomerOrdersFeed(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        int customerId)
    {
        var orders = new Dictionary<int, CustomerOrderBuilder>();

        using (var ordersCommand = CreateCommand(
                   connection,
                   transaction,
                   """
                   SELECT
                       o.OrderId,
                       o.BusinessId,
                       COALESCE(b.BusinessName, 'Empresa eliminada') AS BusinessName,
                       o.Status,
                       o.Total,
                       o.CreatedAt,
                       o.DeliveryAddress,
                       o.Notes,
                       o.PaymentMethod
                   FROM Orders o
                   LEFT JOIN Businesses b ON b.BusinessId = o.BusinessId
                   WHERE o.CustomerId = ?
                   ORDER BY o.CreatedAt DESC, o.OrderId DESC;
                   """,
                   customerId))
        {
            using var ordersReader = ordersCommand.ExecuteReader();
            while (ordersReader.Read())
            {
                var orderId = ordersReader.GetInt32(0);
                orders[orderId] = new CustomerOrderBuilder(
                    orderId,
                    ordersReader.IsDBNull(1) ? 0 : ordersReader.GetInt32(1),
                    ordersReader.GetString(2),
                    ordersReader.GetString(3),
                    ordersReader.GetDecimal(4),
                    ordersReader.GetDateTime(5),
                    ordersReader.GetString(6),
                    ordersReader.IsDBNull(7) ? string.Empty : ordersReader.GetString(7),
                    ordersReader.IsDBNull(8) ? "No especificado" : ordersReader.GetString(8));
            }
        }

        if (orders.Count == 0)
        {
            return [];
        }

        using (var itemsCommand = CreateCommand(
                   connection,
                   transaction,
                   """
                   SELECT
                       oi.OrderId,
                       oi.ProductId,
                       p.Name,
                       oi.Quantity,
                       oi.UnitPrice,
                       oi.LineTotal
                   FROM OrderItems oi
                   LEFT JOIN Products p ON p.ProductId = oi.ProductId
                   INNER JOIN Orders o ON o.OrderId = oi.OrderId
                   WHERE o.CustomerId = ?
                   ORDER BY o.CreatedAt DESC, oi.OrderItemId ASC;
                   """,
                   customerId))
        {
            using var itemsReader = itemsCommand.ExecuteReader();
            while (itemsReader.Read())
            {
                var orderId = itemsReader.GetInt32(0);
                if (!orders.TryGetValue(orderId, out var builder))
                {
                    continue;
                }

                builder.Items.Add(new BusinessOrderItemDto(
                    itemsReader.GetInt32(1),
                    itemsReader.IsDBNull(2) ? "Producto" : itemsReader.GetString(2),
                    itemsReader.GetInt32(3),
                    itemsReader.GetDecimal(4),
                    itemsReader.GetDecimal(5)));
            }
        }

        return orders.Values.Select(order => order.Build()).ToList();
    }

    private static void EnsureProductOwnership(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        int businessId,
        int productId)
    {
        var result = ExecuteScalar(
            connection,
            transaction,
            "SELECT COUNT(*) FROM Products WHERE ProductId = ? AND BusinessId = ? AND IsArchived = FALSE;",
            productId,
            businessId);

        if (Convert.ToInt32(result) == 0)
        {
            throw new InvalidOperationException("El producto no existe o no pertenece a tu empresa.");
        }
    }
}
