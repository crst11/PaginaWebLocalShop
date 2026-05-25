using System.Data;
using Npgsql;
using TiendaMicroempresas.Api.Contracts.Customers;

namespace TiendaMicroempresas.Api.Repositories;

// Registro, login, actualizacion de perfil, historial de pedidos y eliminacion de clientes.
public sealed partial class SqlStoreRepository
{
    public async Task<CustomerAuthResponse> RegisterCustomerAsync(RegisterCustomerRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = CreateOpenConnection();
        var normalizedEmail = NormalizeEmail(request.Email);
        EnsureCustomerEmailAvailable(connection, null, normalizedEmail, null);

        var salt = CreateSalt();
        var hash = HashPassword(request.Password, salt);
        var customerId = InsertAndGetId(
            connection,
            null,
            """
            INSERT INTO Customers
            (
                FullName,
                Email,
                PasswordHash,
                PasswordSalt,
                Phone,
                City,
                Address,
                AuthProvider
            )
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            RETURNING CustomerId;
            """,
            RequireText(request.FullName, "Ingresa el nombre completo del comprador."),
            normalizedEmail,
            hash,
            salt,
            RequireText(request.Phone, "Ingresa un telefono de contacto."),
            RequireText(request.City, "Ingresa la ciudad de entrega."),
            RequireText(request.Address, "Ingresa la direccion de entrega."),
            "password");

        return await CreateCustomerSessionResponseAsync(connection, customerId);
    }

    public async Task<CustomerAuthResponse> LoginCustomerAsync(LoginCustomerRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = CreateOpenConnection();
        using var command = CreateCommand(
            connection,
            null,
            """
            SELECT CustomerId, PasswordHash, PasswordSalt
            FROM Customers
            WHERE Email = ?;
            """,
            NormalizeEmail(request.Email));

        using var reader = command.ExecuteReader(CommandBehavior.SingleRow);
        if (!reader.Read())
        {
            throw new InvalidOperationException("Correo o contrasena incorrectos.");
        }

        var customerId = reader.GetInt32(0);
        var passwordHash = reader.GetString(1);
        var passwordSalt = reader.GetString(2);

        if (!VerifyPassword(request.Password, passwordSalt, passwordHash))
        {
            throw new InvalidOperationException("Correo o contrasena incorrectos.");
        }

        return await CreateCustomerSessionResponseAsync(connection, customerId);
    }

    public Task<CustomerDto> GetCustomerByTokenAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = CreateOpenConnection();
        var customerId = GetCustomerIdByToken(connection, null, token);
        return Task.FromResult(GetCustomerById(connection, null, customerId));
    }

    public Task<CustomerDto> UpdateCustomerProfileAsync(string token, UpdateCustomerProfileRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = CreateOpenConnection();
        var customerId = GetCustomerIdByToken(connection, null, token);

        ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE Customers
            SET FullName = ?,
                Phone = ?,
                City = ?,
                Address = ?
            WHERE CustomerId = ?;
            """,
            RequireText(request.FullName, "Ingresa tu nombre completo."),
            RequireText(request.Phone, "Ingresa un telefono de contacto."),
            RequireText(request.City, "Ingresa tu ciudad."),
            RequireText(request.Address, "Ingresa tu direccion."),
            customerId);

        return Task.FromResult(GetCustomerById(connection, null, customerId));
    }

    public Task DeleteCustomerProfileAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = CreateOpenConnection();
        var customerId = GetCustomerIdByToken(connection, null, token);

        using var transaction = connection.BeginTransaction();
        try
        {
            ExecuteNonQuery(connection, transaction, "DELETE FROM CustomerSessions WHERE CustomerId = ?;", customerId);
            ExecuteNonQuery(connection, transaction, "UPDATE Orders SET CustomerId = NULL WHERE CustomerId = ?;", customerId);
            ExecuteNonQuery(connection, transaction, "DELETE FROM Customers WHERE CustomerId = ?;", customerId);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        return Task.CompletedTask;
    }

    public Task LogoutCustomerAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = CreateOpenConnection();
        ExecuteNonQuery(connection, null, "DELETE FROM CustomerSessions WHERE SessionToken = ?;", token);
        return Task.CompletedTask;
    }

    public Task<CustomerOrdersHistoryResponse> GetCustomerOrdersAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = CreateOpenConnection();
        var customerId = GetCustomerIdByToken(connection, null, token);
        var orders = GetCustomerOrdersFeed(connection, null, customerId);
        return Task.FromResult(new CustomerOrdersHistoryResponse(
            orders.Count,
            orders.Sum(order => order.Total),
            orders));
    }

    private Task<CustomerAuthResponse> CreateCustomerSessionResponseAsync(NpgsqlConnection connection, int customerId)
    {
        var token = Guid.NewGuid().ToString("N");
        ExecuteNonQuery(
            connection,
            null,
            """
            INSERT INTO CustomerSessions (CustomerId, SessionToken, ExpiresAt)
            VALUES (?, ?, CURRENT_TIMESTAMP + INTERVAL '14 days');
            """,
            customerId,
            token);

        return Task.FromResult(new CustomerAuthResponse(token, GetCustomerById(connection, null, customerId)));
    }
}
