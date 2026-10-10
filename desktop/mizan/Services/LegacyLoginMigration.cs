using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace MizanDesktop.Services;

public static class LegacyLoginMigration
{
    private static string DatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MizanDesktop", "mizan.db");

    private static string DefaultPin() => Encoding.UTF8.GetString(Convert.FromHexString("31323334"));
    private static string DefaultPinHash() =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DefaultPin())));

    public static void EnsureDefaultAdmin()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using var c = new SqliteConnection($"Data Source={DatabasePath}");
        c.Open();
        EnsureSchema(c);

        // One-time compatibility migration: older Windows builds stored an admin
        // row with another name/hash, so PIN 1234 was rejected after upgrade.
        if (!MigrationApplied(c, "default-admin-pin-v2"))
        {
            ResetAdminInConnection(c);
            using var mark = c.CreateCommand();
            mark.CommandText = "INSERT OR IGNORE INTO AppMigrations(MigrationId,AppliedAt) VALUES($id,$date)";
            mark.Parameters.AddWithValue("$id", "default-admin-pin-v2");
            mark.Parameters.AddWithValue("$date", DateTimeOffset.Now.ToString("O"));
            mark.ExecuteNonQuery();
            return;
        }

        using (var find = c.CreateCommand())
        {
            find.CommandText = "SELECT Id, COALESCE(PinHash,'') FROM Users WHERE Name='admin' LIMIT 1";
            using var reader = find.ExecuteReader();
            if (reader.Read())
            {
                var id = reader.GetString(0);
                var pinHash = reader.GetString(1);
                reader.Close();
                if (string.IsNullOrWhiteSpace(pinHash))
                {
                    using var update = c.CreateCommand();
                    update.CommandText = "UPDATE Users SET PinHash=$pin, Role='ADMIN', IsActive=1 WHERE Id=$id";
                    update.Parameters.AddWithValue("$pin", DefaultPinHash());
                    update.Parameters.AddWithValue("$id", id);
                    update.ExecuteNonQuery();
                }
                return;
            }
        }

        // Recover a legacy admin if its display name was localized by an older build.
        using (var legacy = c.CreateCommand())
        {
            legacy.CommandText = "SELECT Id, COALESCE(PinHash,'') FROM Users WHERE Role='ADMIN' AND (Id='admin' OR Name='المدير') ORDER BY CASE WHEN Id='admin' THEN 0 ELSE 1 END LIMIT 1";
            using var reader = legacy.ExecuteReader();
            if (reader.Read())
            {
                var id = reader.GetString(0);
                var oldHash = reader.GetString(1);
                reader.Close();
                using var update = c.CreateCommand();
                update.CommandText = "UPDATE Users SET Name='admin', PinHash=$pin, Role='ADMIN', IsActive=1 WHERE Id=$id";
                update.Parameters.AddWithValue("$pin", string.IsNullOrWhiteSpace(oldHash) ? DefaultPinHash() : oldHash);
                update.Parameters.AddWithValue("$id", id);
                update.ExecuteNonQuery();
                return;
            }
        }

        CreateAdmin(c);
    }

    private static void ResetAdminInConnection(SqliteConnection c)
    {
        string? id;
        using (var find = c.CreateCommand())
        {
            find.CommandText = "SELECT Id FROM Users WHERE Name='admin' LIMIT 1";
            id = Convert.ToString(find.ExecuteScalar());
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            using var findLegacy = c.CreateCommand();
            findLegacy.CommandText = "SELECT Id FROM Users WHERE Role='ADMIN' AND Name='المدير' ORDER BY CASE WHEN Id='admin' THEN 0 ELSE 1 END LIMIT 1";
            id = Convert.ToString(findLegacy.ExecuteScalar());
        }

        if (!string.IsNullOrWhiteSpace(id))
        {
            using var update = c.CreateCommand();
            update.CommandText = "UPDATE Users SET Name='admin', Role='ADMIN', PinHash=$pin, IsActive=1 WHERE Id=$id";
            update.Parameters.AddWithValue("$pin", DefaultPinHash());
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
            return;
        }

        CreateAdmin(c);
    }

    private static bool MigrationApplied(SqliteConnection c, string id)
    {
        using var q = c.CreateCommand();
        q.CommandText = "SELECT COUNT(*) FROM AppMigrations WHERE MigrationId=$id";
        q.Parameters.AddWithValue("$id", id);
        return Convert.ToInt32(q.ExecuteScalar()) > 0;
    }
    public static void ResetAdminCredentialsToDefault()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        using var c = new SqliteConnection($"Data Source={DatabasePath}");
        c.Open();
        EnsureSchema(c);
        using var tx = c.BeginTransaction();

        string? id;
        using (var find = c.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = "SELECT Id FROM Users WHERE Name='admin' LIMIT 1";
            id = Convert.ToString(find.ExecuteScalar());
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            using var findLegacy = c.CreateCommand();
            findLegacy.Transaction = tx;
            findLegacy.CommandText = "SELECT Id FROM Users WHERE Role='ADMIN' AND (Id='admin' OR Name='المدير') ORDER BY CASE WHEN Id='admin' THEN 0 ELSE 1 END LIMIT 1";
            id = Convert.ToString(findLegacy.ExecuteScalar());
        }

        if (!string.IsNullOrWhiteSpace(id))
        {
            using var update = c.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE Users SET Name='admin', Role='ADMIN', PinHash=$pin, IsActive=1 WHERE Id=$id";
            update.Parameters.AddWithValue("$pin", DefaultPinHash());
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
        }
        else
        {
            using var insert = c.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO Users(Id,Name,Role,PinHash,IsActive,CreatedAt) VALUES($id,'admin','ADMIN',$pin,1,$created)";
            insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            insert.Parameters.AddWithValue("$pin", DefaultPinHash());
            insert.Parameters.AddWithValue("$created", DateTime.Now.ToString("O"));
            insert.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public static bool SelfTestLogin()
    {
        EnsureDefaultAdmin();
        return new EnterpriseAccountingService().Login("admin", DefaultPin()) is { Role: "ADMIN" };
    }

    public static bool SelfTestLegacyLogin()
    {
        EnsureDefaultAdmin();
        using (var c = new SqliteConnection($"Data Source={DatabasePath}"))
        {
            c.Open();
            using var legacy = c.CreateCommand();
            legacy.CommandText = "UPDATE Users SET Name='المدير', PinHash='' WHERE Name='admin'";
            legacy.ExecuteNonQuery();
        }
        EnsureDefaultAdmin();
        return new EnterpriseAccountingService().Login("admin", DefaultPin()) is { Role: "ADMIN" };
    }

    public static bool SelfTestLoginRecovery()
    {
        EnsureDefaultAdmin();
        using (var c = new SqliteConnection($"Data Source={DatabasePath}"))
        {
            c.Open();
            using var corrupt = c.CreateCommand();
            corrupt.CommandText = "UPDATE Users SET PinHash='intentionally-invalid' WHERE Name='admin'";
            corrupt.ExecuteNonQuery();
        }
        ResetAdminCredentialsToDefault();
        return new EnterpriseAccountingService().Login("admin", DefaultPin()) is { Role: "ADMIN" };
    }

    private static void EnsureSchema(SqliteConnection c)
    {
        using var schema = c.CreateCommand();
        schema.CommandText = @"CREATE TABLE IF NOT EXISTS Users(
Id TEXT PRIMARY KEY, Name TEXT NOT NULL UNIQUE, Role TEXT NOT NULL, PinHash TEXT,
IsActive INTEGER NOT NULL DEFAULT 1, CreatedAt TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS AppMigrations(MigrationId TEXT PRIMARY KEY, AppliedAt TEXT NOT NULL);";
        schema.ExecuteNonQuery();
        EnsureColumn(c, "PinHash", "TEXT");
        EnsureColumn(c, "IsActive", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(c, "CreatedAt", "TEXT NOT NULL DEFAULT ''");
    }

    private static void EnsureColumn(SqliteConnection c, string name, string definition)
    {
        using var q = c.CreateCommand();
        q.CommandText = $"ALTER TABLE Users ADD COLUMN {name} {definition}";
        try { q.ExecuteNonQuery(); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1) { }
    }

    private static void CreateAdmin(SqliteConnection c)
    {
        using var insert = c.CreateCommand();
        insert.CommandText = "INSERT INTO Users(Id,Name,Role,PinHash,IsActive,CreatedAt) VALUES($id,'admin','ADMIN',$pin,1,$created)";
        insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        insert.Parameters.AddWithValue("$pin", DefaultPinHash());
        insert.Parameters.AddWithValue("$created", DateTime.Now.ToString("O"));
        insert.ExecuteNonQuery();
    }
}
