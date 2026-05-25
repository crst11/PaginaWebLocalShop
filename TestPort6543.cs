using System;
using Npgsql;

var connStr = "Host=aws-1-us-east-1.pooler.supabase.com;Port=6543;Database=postgres;Username=postgres.wvhpapkblbrixzyskngg;Password=jlHLABLc0bNzT6Vd;Pooling=true;Timeout=10;CommandTimeout=10;Server Compatibility Mode=NoTypeLoading;";
Console.WriteLine("Connecting to port 6543...");
try {
    using var conn = new NpgsqlConnection(connStr);
    conn.Open();
    using var cmd = new NpgsqlCommand("SELECT 1", conn);
    var result = cmd.ExecuteScalar();
    Console.WriteLine($"SUCCESS on port 6543! Result: {result}");
} catch (Exception ex) {
    Console.WriteLine($"FAILED on port 6543: {ex.Message}");
}
