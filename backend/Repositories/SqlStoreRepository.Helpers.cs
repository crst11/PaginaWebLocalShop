using Npgsql;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TiendaMicroempresas.Api.Repositories;

// Metodos de soporte reutilizados por todos los archivos parciales del repositorio:
// conexion, construccion de comandos, validaciones de entrada, hash de contrasenas y slugs.
public sealed partial class SqlStoreRepository
{
    private NpgsqlConnection CreateOpenConnection()
    {
        var builder = new NpgsqlConnectionStringBuilder(_connectionString);

        // CRITICAL: Force port 6543 (transaction-mode pooler, IPv4-only).
        // Render environment variables may override appsettings.json with port 5432
        // which resolves to an unreachable IPv6 address on Render's free tier.
        builder.Port = 6543;

        // Force IPv4 resolution: Render free tier cannot reach IPv6 addresses.
        try
        {
            var hostEntry = System.Net.Dns.GetHostEntry(builder.Host!);
            var ipv4 = hostEntry.AddressList
                .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

            if (ipv4 != null)
            {
                builder.Host = ipv4.ToString();
            }
        }
        catch
        {
            // If DNS resolution fails, try with the original hostname
        }

        var connection = new NpgsqlConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    // Genera y parametriza un NpgsqlCommand listo para ejecutar.
    private static NpgsqlCommand CreateCommand(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        params object?[] values)
    {
        if (values.Length > 0)
        {
            var parts = sql.Split('?');
            if (parts.Length - 1 == values.Length)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    sb.Append(parts[i]).Append($"${i + 1}");
                }
                sb.Append(parts[^1]);
                sql = sb.ToString();
            }
        }

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var value in values)
        {
            AddParameter(command, value);
        }

        return command;
    }

    private static void AddParameter(NpgsqlCommand command, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static object? ExecuteScalar(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        params object?[] values)
    {
        using var command = CreateCommand(connection, transaction, sql, values);
        return command.ExecuteScalar();
    }

    private static int InsertAndGetId(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        params object?[] values)
    {
        var result = ExecuteScalar(connection, transaction, sql, values);
        return Convert.ToInt32(result);
    }

    private static int ExecuteNonQuery(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        params object?[] values)
    {
        using var command = CreateCommand(connection, transaction, sql, values);
        return command.ExecuteNonQuery();
    }

    // Verifica que el correo no este ya registrado en Businesses.
    // Si excludedBusinessId tiene valor, omite esa empresa de la comprobacion (edicion de perfil).
    private static void EnsureEmailAvailable(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string email,
        int? excludedBusinessId)
    {
        string sql;
        object?[] values;

        if (excludedBusinessId.HasValue)
        {
            sql = "SELECT COUNT(*) FROM Businesses WHERE Email = ? AND BusinessId <> ?;";
            values = [email, excludedBusinessId.Value];
        }
        else
        {
            sql = "SELECT COUNT(*) FROM Businesses WHERE Email = ?;";
            values = [email];
        }

        var count = Convert.ToInt32(ExecuteScalar(connection, transaction, sql, values));
        if (count > 0)
        {
            throw new InvalidOperationException("Este correo ya esta registrado.");
        }
    }

    private static void EnsureCustomerEmailAvailable(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string email,
        int? excludedCustomerId)
    {
        string sql;
        object?[] values;

        if (excludedCustomerId.HasValue)
        {
            sql = "SELECT COUNT(*) FROM Customers WHERE Email = ? AND CustomerId <> ?;";
            values = [email, excludedCustomerId.Value];
        }
        else
        {
            sql = "SELECT COUNT(*) FROM Customers WHERE Email = ?;";
            values = [email];
        }

        var count = Convert.ToInt32(ExecuteScalar(connection, transaction, sql, values));
        if (count > 0)
        {
            throw new InvalidOperationException("Este correo ya esta registrado.");
        }
    }

    private static void EnsureBusinessNameAvailable(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string businessName,
        int? excludedBusinessId)
    {
        string sql;
        object?[] values;

        if (excludedBusinessId.HasValue)
        {
            sql = "SELECT COUNT(*) FROM Businesses WHERE LOWER(LTRIM(RTRIM(BusinessName))) = LOWER(?) AND BusinessId <> ?;";
            values = [businessName, excludedBusinessId.Value];
        }
        else
        {
            sql = "SELECT COUNT(*) FROM Businesses WHERE LOWER(LTRIM(RTRIM(BusinessName))) = LOWER(?);";
            values = [businessName];
        }

        var count = Convert.ToInt32(ExecuteScalar(connection, transaction, sql, values));
        if (count > 0)
        {
            throw new InvalidOperationException("Ya existe una empresa registrada con ese nombre.");
        }
    }

    // Construye un slug unico basado en el nombre de la empresa.
    // Si ya existe uno identico, agrega un sufijo numerico incremental.
    private static string BuildUniqueSlug(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string businessName,
        int? excludedBusinessId)
    {
        var rootSlug = Slugify(businessName);
        if (string.IsNullOrWhiteSpace(rootSlug))
        {
            rootSlug = "empresa";
        }

        var slug = rootSlug;
        var counter = 2;

        while (SlugExists(connection, transaction, slug, excludedBusinessId))
        {
            slug = $"{rootSlug}-{counter}";
            counter += 1;
        }

        return slug;
    }

    private static bool SlugExists(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string slug,
        int? excludedBusinessId)
    {
        string sql;
        object?[] values;

        if (excludedBusinessId.HasValue)
        {
            sql = "SELECT COUNT(*) FROM Businesses WHERE Slug = ? AND BusinessId <> ?;";
            values = [slug, excludedBusinessId.Value];
        }
        else
        {
            sql = "SELECT COUNT(*) FROM Businesses WHERE Slug = ?;";
            values = [slug];
        }

        return Convert.ToInt32(ExecuteScalar(connection, transaction, sql, values)) > 0;
    }

    private static string NormalizeEmail(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Ingresa un correo valido.");
        }

        return normalized;
    }

    private static string NormalizeOptionalText(string? value) => value?.Trim() ?? string.Empty;

    private static string RequireText(string value, string errorMessage)
    {
        var normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return normalized;
    }

    private static string RequireImageUrl(string value, string fallback)
    {
        var normalized = value.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static decimal RequireMoney(decimal value, string errorMessage = "El precio no puede ser negativo.")
    {
        if (value < 0)
        {
            throw new InvalidOperationException(errorMessage);
        }

        return value;
    }

    private static int RequireMinimumValue(int value, int minimum, string errorMessage)
    {
        if (value < minimum)
        {
            throw new InvalidOperationException(errorMessage);
        }

        return value;
    }

    // Convierte un texto libre en un slug URL-safe en minusculas sin acentos ni caracteres especiales.
    private static string Slugify(string value) =>
        value
            .Trim()
            .ToLowerInvariant()
            .Normalize(NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Aggregate(new StringBuilder(), (builder, character) =>
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                }
                else if (builder.Length > 0 && builder[^1] != '-')
                {
                    builder.Append('-');
                }

                return builder;
            })
            .ToString()
            .Trim('-');

    private static string CreateSalt() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

    private static string HashPassword(string password, string base64Salt)
    {
        var salt = Convert.FromBase64String(base64Salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            100_000,
            HashAlgorithmName.SHA256,
            32);

        return Convert.ToBase64String(hash);
    }

    private static bool VerifyPassword(string password, string base64Salt, string expectedHash) =>
        HashPassword(password, base64Salt) == expectedHash;
}
