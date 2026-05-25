using System;
using Npgsql;

string[] connectionStrings = new string[] {
    "Host=aws-1-us-east-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.wvhpapkblbrixzyskngg;Password=jlHLABLc0bNzT6Vd;Pooling=true;Timeout=10;",
    "Host=aws-1-us-east-1.pooler.supabase.com;Port=6543;Database=postgres;Username=postgres.wvhpapkblbrixzyskngg;Password=jlHLABLc0bNzT6Vd;Pooling=true;Timeout=10;Server Compatibility Mode=NoTypeLoading;"
};

foreach (var cs in connectionStrings) {
    Console.WriteLine($"Testing: " + cs);
    try {
        using var conn = new NpgsqlConnection(cs);
        conn.Open();
        Console.WriteLine("SUCCESS!");
    } catch (Exception ex) {
        Console.WriteLine($"FAILED: " + ex.Message);
    }
}
