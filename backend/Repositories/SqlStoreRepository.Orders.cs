using Npgsql;
using System.Globalization;
using TiendaMicroempresas.Api.Contracts.Orders;

namespace TiendaMicroempresas.Api.Repositories;

// Creacion de pedidos individuales y checkout del carrito completo.
// Cada operacion usa una transaccion para garantizar consistencia de inventario.
public sealed partial class SqlStoreRepository
{
    public Task<OrderCreatedResponse> CreateOrderAsync(CreateOrderRequest request, string customerToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            var customerId = GetCustomerIdByToken(connection, transaction, customerToken);
            var createdOrder = CreateBusinessOrder(
                connection,
                transaction,
                request.BusinessId,
                customerId,
                request.FullName,
                request.Email,
                request.Phone,
                request.City,
                request.Address,
                request.Notes,
                request.PaymentMethod,
                request.Items.Select(item => (item.ProductId, item.Quantity)).ToList());

            transaction.Commit();

            return Task.FromResult(new OrderCreatedResponse(
                createdOrder.OrderId,
                createdOrder.Total,
                $"Pedido enviado a {createdOrder.BusinessName}. El equipo comercial revisara tu solicitud."));
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public Task<CheckoutCartResponse> CheckoutCartAsync(CheckoutCartRequest request, string? customerToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Items.Count == 0)
        {
            throw new InvalidOperationException("Debes agregar al menos un producto antes de comprar.");
        }

        using var connection = CreateOpenConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            int? customerId = null;
            if (!string.IsNullOrWhiteSpace(customerToken))
            {
                customerId = GetCustomerIdByToken(connection, transaction, customerToken);
            }

            // Agrupar items por empresa para crear un pedido por cada una.
            var itemsByBusiness = request.Items
                .GroupBy(item => item.BusinessId)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<(int ProductId, int Quantity)>)g
                        .Select(item => (item.ProductId, item.Quantity))
                        .ToList());

            var createdOrders = new List<BusinessCheckoutResultDto>();

            foreach (var (businessId, items) in itemsByBusiness)
            {
                var result = CreateBusinessOrder(
                    connection,
                    transaction,
                    businessId,
                    customerId,
                    request.FullName,
                    request.Email,
                    request.Phone,
                    request.City,
                    request.Address,
                    request.Notes,
                    request.PaymentMethod,
                    items);

                createdOrders.Add(new BusinessCheckoutResultDto(
                    result.OrderId,
                    result.BusinessId,
                    result.BusinessName,
                    result.Total));
            }

            transaction.Commit();

            var grandTotal = createdOrders.Sum(o => o.Total);
            var count = createdOrders.Count;
            var businessNames = createdOrders.Select(o => o.BusinessName).ToList();
            var message = count == 1
                ? $"Pedido enviado a {businessNames[0]}. El equipo comercial revisara tu solicitud pronto."
                : $"Pedidos enviados a {string.Join(", ", businessNames)}. Los equipos comerciales revisaran tus solicitudes pronto.";

            return Task.FromResult(new CheckoutCartResponse(count, grandTotal, message, createdOrders));
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public Task<BusinessOrdersFeedResponse> GetBusinessOrdersAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = CreateOpenConnection();
        var businessId = GetBusinessIdByToken(connection, null, token);
        var orders = GetBusinessOrdersFeed(connection, null, businessId);
        return Task.FromResult(new BusinessOrdersFeedResponse(
            orders.Count(order => order.IsNew),
            orders));
    }

    // Crea un pedido para una sola empresa dentro de una transaccion activa.
    // Valida stock, pedido minimo y descuenta inventario por cada linea.
    private static CreatedOrderResult CreateBusinessOrder(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int businessId,
        int? customerId,
        string fullName,
        string email,
        string phone,
        string city,
        string address,
        string? notes,
        string paymentMethod,
        IReadOnlyList<(int ProductId, int Quantity)> items)
    {
        var businessOrderPolicy = GetBusinessOrderPolicy(connection, transaction, businessId);
        var total = 0m;
        var orderLines = new List<(int ProductId, int Quantity, decimal UnitPrice)>();

        foreach (var item in items)
        {
            if (item.Quantity <= 0)
            {
                throw new InvalidOperationException("Todas las cantidades del pedido deben ser mayores a cero.");
            }

            var product = GetProductForOrder(connection, transaction, businessId, item.ProductId);

            if (item.Quantity > product.Stock)
            {
                throw new InvalidOperationException(
                    $"No hay inventario suficiente para {product.Name}. Stock disponible: {product.Stock}.");
            }

            total += product.Price * item.Quantity;
            orderLines.Add((item.ProductId, item.Quantity, product.Price));
        }

        if (total < businessOrderPolicy.MinimumOrderAmount)
        {
            throw new InvalidOperationException(
                $"El pedido minimo para {businessOrderPolicy.BusinessName} es de {businessOrderPolicy.MinimumOrderAmount.ToString("C0", CultureInfo.GetCultureInfo("es-CO"))}.");
        }

        var orderId = InsertAndGetId(
            connection,
            transaction,
            """
            INSERT INTO Orders
            (
                CustomerId,
                BusinessId,
                CustomerFullName,
                CustomerEmail,
                CustomerPhone,
                CustomerCity,
                DeliveryAddress,
                Notes,
                Status,
                Total,
                PaymentMethod
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            RETURNING OrderId;
            """,
            customerId,
            businessId,
            RequireText(fullName, "Ingresa el nombre completo del comprador."),
            NormalizeEmail(email),
            RequireText(phone, "Ingresa un telefono de contacto."),
            RequireText(city, "Ingresa la ciudad de entrega."),
            RequireText(address, "Ingresa la direccion de entrega."),
            NormalizeOptionalText(notes),
            "Pendiente",
            total,
            RequireText(paymentMethod, "Ingresa un metodo de pago."));

        foreach (var line in orderLines)
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO OrderItems (OrderId, ProductId, Quantity, UnitPrice, LineTotal)
                VALUES (?, ?, ?, ?, ?);
                """,
                orderId,
                line.ProductId,
                line.Quantity,
                line.UnitPrice,
                line.UnitPrice * line.Quantity);

            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE Products
                SET Stock = Stock - ?, UpdatedAt = CURRENT_TIMESTAMP
                WHERE ProductId = ? AND IsArchived = FALSE;
                """,
                line.Quantity,
                line.ProductId);
        }

        return new CreatedOrderResult(orderId, businessId, businessOrderPolicy.BusinessName, total);
    }
}
