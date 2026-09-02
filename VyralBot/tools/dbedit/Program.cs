using System;
using System.IO;
using Microsoft.Data.Sqlite;

if (args.Length == 0)
{
    Console.WriteLine("Usage: dbedit <path-to-vyralbot.db> [suffix]");
    return 1;
}

string dbPath = args[0];
string suffix = args.Length > 1 ? args[1] : "x";

if (!File.Exists(dbPath))
{
    Console.WriteLine($"Database not found: {dbPath}");
    return 2;
}

Console.WriteLine($"Opening DB: {dbPath}");

var connString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
using var conn = new SqliteConnection(connString);
conn.Open();

// Show existing AccessToken for Twitch (truncated for safety)
using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "SELECT AccessToken FROM Platforms WHERE Name = @name LIMIT 1";
    cmd.Parameters.AddWithValue("@name", "Twitch");
    var before = cmd.ExecuteScalar() as string;
    Console.WriteLine("Before: " + (before == null ? "<null>" : before.Substring(0, Math.Min(40, before.Length)) + (before.Length > 40 ? "..." : "")));
}

using (var tx = conn.BeginTransaction())
{
    using var cmd = conn.CreateCommand();
    if (suffix == "-")
    {
        // Remove last character from AccessToken (if present)
        cmd.CommandText = "UPDATE Platforms SET AccessToken = CASE WHEN AccessToken IS NULL OR length(AccessToken) = 0 THEN AccessToken ELSE substr(AccessToken, 1, length(AccessToken)-1) END WHERE Name = @name";
        cmd.Parameters.AddWithValue("@name", "Twitch");
    }
    else
    {
        cmd.CommandText = "UPDATE Platforms SET AccessToken = COALESCE(AccessToken, '') || @suf WHERE Name = @name";
        cmd.Parameters.AddWithValue("@suf", suffix);
        cmd.Parameters.AddWithValue("@name", "Twitch");
    }
    var rows = cmd.ExecuteNonQuery();
    Console.WriteLine($"Rows updated: {rows}");
    tx.Commit();
}

using (var cmd = conn.CreateCommand())
{
    cmd.CommandText = "SELECT AccessToken FROM Platforms WHERE Name = @name LIMIT 1";
    cmd.Parameters.AddWithValue("@name", "Twitch");
    var after = cmd.ExecuteScalar() as string;
    Console.WriteLine("After: " + (after == null ? "<null>" : after.Substring(0, Math.Min(40, after.Length)) + (after.Length > 40 ? "..." : "")));
}

conn.Close();
Console.WriteLine("Done.");
return 0;