using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MizanDesktop.Services;

public sealed class BackupService
{
    private readonly string _dataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MizanDesktop");
    private readonly string _databasePath;

    private const string SecureMagic = "ALMIZAN_SECURE_BACKUP_V3";
    private const int SaltBytes = 16;
    private const int NonceBytes = 12;
    private const int KeyBytes = 32;
    private const int Iterations = 120_000;

    public BackupService()
    {
        _databasePath = Path.Combine(_dataDirectory, "mizan.db");
    }

    public string CreateBackup(string targetZip)
    {
        if (!File.Exists(_databasePath))
            throw new FileNotFoundException("قاعدة البيانات غير موجودة.", _databasePath);

        Directory.CreateDirectory(Path.GetDirectoryName(targetZip)!);
        if (File.Exists(targetZip)) File.Delete(targetZip);

        // SQLite WAL databases must be checkpointed before copying.
        using (var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_databasePath}"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }

        var temp = targetZip + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);
        using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(_databasePath, "mizan.db", CompressionLevel.Optimal);
            var stamp = archive.CreateEntry("backup-info.txt");
            using var writer = new StreamWriter(stamp.Open(), Encoding.UTF8);
            writer.WriteLine("Mizan Desktop backup");
            writer.WriteLine($"Created: {DateTimeOffset.Now:O}");
            writer.WriteLine($"Format: SQLite ZIP V1");
        }
        File.Move(temp, targetZip, true);
        return targetZip;
    }

    /// <summary>
    /// Password-protected backup compatible in concept with the Android V3 envelope:
    /// PBKDF2-SHA256 -> AES-256-GCM. The original SQLite ZIP remains inside the encrypted payload.
    /// </summary>
    public string CreateSecureBackup(string targetFile, string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("كلمة مرور النسخة الاحتياطية مطلوبة.", nameof(password));

        var plainZip = Path.Combine(Path.GetTempPath(), $"mizan-{Guid.NewGuid():N}.zip");
        try
        {
            CreateBackup(plainZip);
            var plaintext = File.ReadAllBytes(plainZip);
            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
            var key = DeriveKey(password, salt);
            var cipher = new byte[plaintext.Length];
            var tag = new byte[16];

            using (var aes = new AesGcm(key, tag.Length))
                aes.Encrypt(nonce, plaintext, cipher, tag);

            var envelope = new SecureBackupEnvelope(
                SecureMagic,
                3,
                Convert.ToBase64String(salt),
                Convert.ToBase64String(nonce),
                Convert.ToBase64String(cipher),
                Convert.ToBase64String(tag),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.WriteAllText(targetFile, JsonSerializer.Serialize(envelope), new UTF8Encoding(false));
            return targetFile;
        }
        finally
        {
            try { File.Delete(plainZip); } catch { }
        }
    }

    public void RestoreBackup(string sourceZip)
    {
        if (!File.Exists(sourceZip))
            throw new FileNotFoundException("ملف النسخة الاحتياطية غير موجود.", sourceZip);

        using var archive = ZipFile.OpenRead(sourceZip);
        var entry = archive.GetEntry("mizan.db")
            ?? throw new InvalidDataException("النسخة الاحتياطية لا تحتوي على mizan.db");

        var temp = _databasePath + ".restore.tmp";
        Directory.CreateDirectory(_dataDirectory);
        try
        {
            using (var input = entry.Open())
            using (var output = File.Create(temp))
                input.CopyTo(output);

            ValidateSQLite(temp);
            ReplaceDatabaseAtomically(temp);
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    public void RestoreSecureBackup(string sourceFile, string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("كلمة مرور النسخة الاحتياطية مطلوبة.", nameof(password));
        var envelope = JsonSerializer.Deserialize<SecureBackupEnvelope>(
            File.ReadAllText(sourceFile, Encoding.UTF8))
            ?? throw new InvalidDataException("ملف النسخة المشفرة غير صالح.");

        if (!string.Equals(envelope.Magic, SecureMagic, StringComparison.Ordinal))
            throw new InvalidDataException("صيغة النسخة المشفرة غير مدعومة.");

        byte[] plainZip;
        try
        {
            var salt = Convert.FromBase64String(envelope.Salt);
            var nonce = Convert.FromBase64String(envelope.Nonce);
            var cipher = Convert.FromBase64String(envelope.Ciphertext);
            var tag = Convert.FromBase64String(envelope.Tag);
            var key = DeriveKey(password, salt);
            plainZip = new byte[cipher.Length];
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, cipher, tag, plainZip);
        }
        catch (CryptographicException)
        {
            throw new InvalidDataException("كلمة المرور خاطئة أو النسخة المشفرة تالفة.");
        }

        var tempZip = Path.Combine(Path.GetTempPath(), $"mizan-restore-{Guid.NewGuid():N}.zip");
        try
        {
            File.WriteAllBytes(tempZip, plainZip);
            RestoreBackup(tempZip);
        }
        finally
        {
            try { File.Delete(tempZip); } catch { }
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);

    private void ValidateSQLite(string path)
    {
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Mode=ReadOnly");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA quick_check;";
        var result = Convert.ToString(cmd.ExecuteScalar());
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"فحص قاعدة البيانات فشل: {result}");
    }

    private void ReplaceDatabaseAtomically(string temp)
    {
        using var probe = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_databasePath}");
        try
        {
            probe.Open();
            using var cmd = probe.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }
        catch { /* database may not exist yet */ }

        File.Move(temp, _databasePath, true);
    }

    private sealed record SecureBackupEnvelope(
        string Magic,
        int Version,
        string Salt,
        string Nonce,
        string Ciphertext,
        string Tag,
        long CreatedAt);
}
