using System.Net.Http;
using System.Text.Json;

namespace MizanDesktop.Services;

public sealed record AndroidServerStatus(bool IsOnline, string StoreName, string DeviceName, int InvoicesCount, int ProductsCount, string Message);

public sealed class AndroidLanService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public async Task<AndroidServerStatus> CheckStatusAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        host = NormalizeHost(host);
        if (string.IsNullOrWhiteSpace(host)) return new(false, "", "", 0, 0, "أدخل عنوان IP لهاتف Android الرئيسي.");
        if (port is < 1 or > 65535) return new(false, "", "", 0, 0, "رقم المنفذ غير صالح.");

        try
        {
            using var response = await _http.GetAsync($"http://{host}:{port}/api/status", cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new(false, "", "", 0, 0, $"استجاب Android بالرمز {(int)response.StatusCode}.");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string ReadString(string name) => root.TryGetProperty(name, out var e) ? e.GetString() ?? "" : "";
            int ReadInt(string name) => root.TryGetProperty(name, out var e) && e.TryGetInt32(out var n) ? n : 0;
            return new(true, ReadString("storeName"), ReadString("deviceName"), ReadInt("invoicesCount"), ReadInt("productsCount"), "تم الاتصال بخادم Android بنجاح.");
        }
        catch (TaskCanceledException) { return new(false, "", "", 0, 0, "انتهت مهلة الاتصال. تأكد أن الجهازين على نفس الشبكة."); }
        catch (HttpRequestException ex) { return new(false, "", "", 0, 0, "تعذر الاتصال: " + ex.Message); }
        catch (JsonException) { return new(false, "", "", 0, 0, "استجابة Android غير متوافقة."); }
    }

    private static string NormalizeHost(string host) => host.Trim()
        .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
        .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
        .TrimEnd('/');
}
