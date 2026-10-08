using System.IO;
using System.Text.Json;

namespace MizanDesktop.Services;

public sealed class DesktopSettings
{
    public string StoreName { get; set; } = "الميزان";
    public string DeviceName { get; set; } = Environment.MachineName;
    public string DefaultCurrency { get; set; } = "SYP";
    public string StorePhone { get; set; } = "";
    public string StoreAddress { get; set; } = "";
    public string InvoicePrefixSale { get; set; } = "S";
    public string InvoicePrefixPurchase { get; set; } = "P";
    public string InvoicePrefixSaleReturn { get; set; } = "RS";
    public string InvoicePrefixPurchaseReturn { get; set; } = "RP";
    public string InvoicePrefixQuotation { get; set; } = "QU";
    public string AndroidHost { get; set; } = "";
    public int AndroidPort { get; set; } = 8989;
    public string SyncPin { get; set; } = "";
    public string LicenseKey { get; set; } = "";
    public DateTime? LastAndroidConnection { get; set; }
}

public sealed class AppSettingsService
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettingsService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MizanDesktop");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    public DesktopSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new DesktopSettings();
            return JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(_path), JsonOptions) ?? new DesktopSettings();
        }
        catch { return new DesktopSettings(); }
    }

    public void Save(DesktopSettings settings)
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, _path, true);
    }

    public string SettingsPath => _path;
}
