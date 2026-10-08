using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MizanDesktop.Services;

/// <summary>
/// Windows-side verifier for the same RSA-SHA256 license format used by Android.
/// The private signing key is never stored in the application.
/// </summary>
public sealed class LicenseService
{
    private const string PublicKeyBase64 =
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA4ksiBpWauWuU6lFtDx7H" +
        "ybxQ6RZycECZZ1bfzt6UlrRcD/VSS69U4XMkhd+IUUzKAVCpVmHqijjql67hwjyZ" +
        "0tGacLtwhpdxBENX9mLo+jZxTj08MdmBhQ6RibugRqP6r4EjdyViwey93V1wq6p7" +
        "c1rfsDNrla5Ta1tIxb5NJpnFg9asrHFUujbdkz4R33ENYO+G9PX/aGZhAVrEppEb" +
        "xIqDgywN4xiMNKMA0VKKx6mzymvjNnR2KaZ3UjbVDFm3U6UNi2cSCnKgSWA8PLG1" +
        "APPyxcwtet/t3w5aTB+FQV171RET1xp3QL7e+sPMFCkJ9sUDGwau+wqPUZOaVwGm" +
        "uwIDAQAB";

    public LicenseVerification Verify(string enteredKey, string deviceId)
    {
        var key = enteredKey.Trim();
        if (key.Length < 8) return LicenseVerification.Invalid("رمز الترخيص قصير.");

        if (!key.Contains('.'))
            return LicenseVerification.Invalid("يجب استخدام ترخيص ALM3 موقع رقمياً.");

        var clean = key.StartsWith("ALM3.", StringComparison.OrdinalIgnoreCase)
            ? key[5..] : key;
        var parts = clean.Split('.');
        if (parts.Length < 2) return LicenseVerification.Invalid("صيغة الترخيص غير صحيحة.");

        try
        {
            var payloadB64 = parts[0];
            var signatureB64 = parts[1];
            var payloadBytes = DecodeBase64Url(payloadB64);
            var signature = DecodeBase64Url(signatureB64);

            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKeyBase64), out _);
            if (!rsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                return LicenseVerification.Invalid("التوقيع الرقمي غير صالح.");

            var payload = Encoding.UTF8.GetString(payloadBytes);
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;

            var licensedDevice = root.TryGetProperty("deviceId", out var d) ? d.GetString() ?? "" : "";
            var expiry = root.TryGetProperty("expiryDate", out var e) && e.TryGetInt64(out var ts) ? ts : 0L;
            var plan = root.TryGetProperty("licenseType", out var p) ? p.GetString() ?? "" : "";

            var deviceOk = licensedDevice == "*" ||
                           licensedDevice.Equals("ALL", StringComparison.OrdinalIgnoreCase) ||
                           licensedDevice.Equals(deviceId, StringComparison.OrdinalIgnoreCase);
            if (!deviceOk) return LicenseVerification.Invalid("الترخيص مرتبط بجهاز آخر.");
            if (expiry > 0 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > expiry)
                return LicenseVerification.Invalid("الترخيص منتهي الصلاحية.");
            if (string.IsNullOrWhiteSpace(plan))
                return LicenseVerification.Invalid("نوع الترخيص غير موجود.");

            return new LicenseVerification(true, "الترخيص صالح.", licensedDevice, expiry, plan);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException)
        {
            return LicenseVerification.Invalid("تعذر قراءة الترخيص: " + ex.Message);
        }
    }

    public static string GetStableDeviceId()
    {
        var raw = $"{Environment.MachineName}|{Environment.OSVersion}|{Environment.UserName}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant()[..16];
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        while (s.Length % 4 != 0) s += "=";
        return Convert.FromBase64String(s);
    }

    public sealed record LicenseVerification(
        bool IsValid,
        string Message,
        string DeviceId,
        long ExpiryDate,
        string LicenseType)
    {
        public static LicenseVerification Invalid(string message) =>
            new(false, message, "", 0, "");
    }
}
