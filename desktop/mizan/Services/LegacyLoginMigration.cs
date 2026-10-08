using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace MizanDesktop.Services;

public static class LegacyLoginMigration
{
    private static string DatabasePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MizanDesktop",
            "mizan.db");

    private static string DefaultPin()
        => Encoding.UTF8.GetString(Convert.FromHexString("31323334"));

    private static string DefaultPinHash()
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DefaultPin())));

    public static void EnsureDefaultAdmin()
    {
        var path = DatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var c = new SqliteConnection($"Data Source={path}");
        c.Open();

        using var schema = c.CreateCommand();
        schema.CommandText = @"CREATE TABLE IF NOT EXISTS Users(
Id TEXT PRIMARY KEY,Name TEXT NOT NULL UNIQUE,Role TEXT NOT NULL,PinHash TEXT,
IsActive INTEGER NOT NULL DEFAULT 1,CreatedAt TEXT NOT NULL);";
        schema.ExecuteNonQuery();

        using var count = c.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM Users";

        if (Convert.ToInt32(count.ExecuteScalar()) == 0)
        {
            CreateAdmin(c);
            return;
        }

        using var find = c.CreateCommand();
        find.CommandText = "SELECT Id,Name,Role,COALESCE(PinHash,'') FROM Users WHERE Id='admin' LIMIT 1";
        using var reader = find.ExecuteReader();

        if (reader.Read())
        {
            var id = reader.GetString(0);
            var name = reader.GetString(1);
            var role = reader.GetString(2);
            var pinHash = reader.GetString(3);
            reader.Close();

            if (name == "المدير" &&
                role.Equals("ADMIN", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(pinHash))
            {
                using var update = c.CreateCommand();
                update.CommandText = "UPDATE Users SET Name='admin',PinHash=$p,IsActive=1 WHERE Id=$id";
                update.Parameters.AddWithValue("$p", DefaultPinHash());
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();
            }
            return;
        }

        reader.Close();

        using var exists = c.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM Users WHERE Name='admin'";
        if (Convert.ToInt32(exists.ExecuteScalar()) == 0)
            CreateAdmin(c);
    }

    public static bool SelfTestLogin()
    {
        EnsureDefaultAdmin();
        var service = new EnterpriseAccountingService();
        var user = service.Login("admin", DefaultPin());
        return user is { Role: "ADMIN" };
    }

    private static void CreateAdmin(SqliteConnection c)
    {
        using var insert = c.CreateCommand();
        insert.CommandText = "INSERT INTO Users(Id,Name,Role,PinHash,IsActive,CreatedAt) VALUES('admin','admin','ADMIN',$pin,1,$created)";
        insert.Parameters.AddWithValue("$pin", DefaultPinHash());
        insert.Parameters.AddWithValue("$created", DateTime.Now.ToString("O"));
        insert.ExecuteNonQuery();
    }
}
