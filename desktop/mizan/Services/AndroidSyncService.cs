using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MizanDesktop.Core;

namespace MizanDesktop.Services;

public sealed record SyncRunResult(bool Success, string Message, int Products = 0, int Parties = 0, int Invoices = 0, int Payments = 0, int Cash = 0, int Stock = 0, int Journals = 0);

/// <summary>Real Android LAN synchronization client compatible with LocalSyncServer.</summary>
public sealed class AndroidSyncService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private string _sessionToken = "";

    public async Task<SyncRunResult> SyncAsync(string host, int port, string pin, CancellationToken ct = default)
    {
        host = Normalize(host);
        if (string.IsNullOrWhiteSpace(host)) return new(false, "عنوان Android غير محدد.");
        if (string.IsNullOrWhiteSpace(pin)) return new(false, "أدخل PIN المزامنة أولاً.");
        var deviceId = GetDeviceId();
        var deviceName = Environment.MachineName;

        var token = await HandshakeAsync(host, port, pin, deviceId, deviceName, ct);
        if (string.IsNullOrWhiteSpace(token)) return new(false, "فشل الاقتران الآمن مع Android.");

        try
        {
            var bridge = new AndroidParityRepository();
            var localPayload = bridge.BuildAndroidAppDataJson(deviceId, deviceName);
            var response = await SendSignedAsync(host, port, "/api/sync", localPayload, deviceId, deviceName, token, ct);
            if (!response.IsSuccessStatusCode)
                return new(false, $"فشلت المزامنة: HTTP {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");

            var mergedJson = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(mergedJson)) return new(false, "استجابة Android فارغة.");
            var counts = bridge.ImportAndroidAppDataJson(mergedJson);
            return new(true, "تمت المزامنة الثنائية مع Android بنجاح.", counts.Products, counts.Parties, counts.Invoices, counts.Payments, counts.CashTransactions, counts.StockMovements, counts.JournalEntries);
        }
        catch (Exception ex) { return new(false, "فشلت المزامنة: " + ex.Message); }
    }

    public async Task<SyncRunResult> PullAsync(string host, int port, string pin, CancellationToken ct = default)
    {
        host = Normalize(host); var deviceId = GetDeviceId(); var deviceName = Environment.MachineName;
        var token = await HandshakeAsync(host, port, pin, deviceId, deviceName, ct);
        if (string.IsNullOrWhiteSpace(token)) return new(false, "فشل الاقتران الآمن مع Android.");
        using var response = await SendSignedAsync(host, port, "/api/pull", "", deviceId, deviceName, token, ct);
        if (!response.IsSuccessStatusCode) return new(false, $"فشل السحب: HTTP {(int)response.StatusCode}");
        var json = await response.Content.ReadAsStringAsync(ct);
        var counts = new AndroidParityRepository().ImportAndroidAppDataJson(json);
        return new(true, "تم جلب بيانات Android إلى Windows بنجاح.", counts.Products, counts.Parties, counts.Invoices, counts.Payments, counts.CashTransactions, counts.StockMovements, counts.JournalEntries);
    }

    private async Task<string> HandshakeAsync(string host, int port, string pin, string deviceId, string deviceName, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { deviceId, deviceName });
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nonce = Guid.NewGuid().ToString();
        var hash = Sha256(body);
        var canonical = string.Join("\n", "POST", "/api/auth/handshake", now, nonce, deviceId, hash.ToLowerInvariant());
        var sig = Hmac(pin, canonical);
        using var req = new HttpRequestMessage(HttpMethod.Post, $"http://{host}:{port}/api/auth/handshake") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.TryAddWithoutValidation("X-Device-Id", deviceId);
        req.Headers.TryAddWithoutValidation("X-Device-Name", deviceName);
        req.Headers.TryAddWithoutValidation("X-Timestamp", now.ToString());
        req.Headers.TryAddWithoutValidation("X-Nonce", nonce);
        req.Headers.TryAddWithoutValidation("X-Body-Sha256", hash);
        req.Headers.TryAddWithoutValidation("X-Request-Signature", sig);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return "";
        var text = await resp.Content.ReadAsStringAsync(ct);
        try { _sessionToken = JsonDocument.Parse(text).RootElement.GetProperty("sessionToken").GetString() ?? ""; } catch { _sessionToken = ""; }
        return _sessionToken;
    }

    private async Task<HttpResponseMessage> SendSignedAsync(string host, int port, string path, string body, string deviceId, string deviceName, string token, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); var nonce = Guid.NewGuid().ToString(); var hash = Sha256(body);
        var canonical = string.Join("\n", "POST", path, now, nonce, token, hash.ToLowerInvariant());
        var sig = Hmac(token, canonical); var idempotency = Guid.NewGuid().ToString();
        using var req = new HttpRequestMessage(HttpMethod.Post, $"http://{host}:{port}{path}") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.TryAddWithoutValidation("X-Device-Id", deviceId); req.Headers.TryAddWithoutValidation("X-Device-Name", deviceName);
        req.Headers.TryAddWithoutValidation("X-Session-Token", token); req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        req.Headers.TryAddWithoutValidation("X-Timestamp", now.ToString()); req.Headers.TryAddWithoutValidation("X-Nonce", nonce);
        req.Headers.TryAddWithoutValidation("X-Body-Sha256", hash); req.Headers.TryAddWithoutValidation("X-Request-Signature", sig);
        req.Headers.TryAddWithoutValidation("X-Request-Id", idempotency); req.Headers.TryAddWithoutValidation("Idempotency-Key", idempotency);
        return await _http.SendAsync(req, ct);
    }

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Hmac(string key, string value) => Convert.ToHexString(new HMACSHA256(Encoding.UTF8.GetBytes(key)).ComputeHash(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Normalize(string host) => host.Trim().Replace("http://", "", StringComparison.OrdinalIgnoreCase).Replace("https://", "", StringComparison.OrdinalIgnoreCase).TrimEnd('/');
    private static string GetDeviceId() => "WIN-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.MachineName + "|MizanDesktop")))[..24];
}
