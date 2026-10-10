using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MizanDesktop.Services;

namespace MizanDesktop.Views;

public sealed class SettingsView : UserControl
{
    private readonly AppRepository _repo;
    private readonly AppSettingsService _settingsService = new();
    private readonly BackupService _backup = new();
    private readonly LicenseService _license = new();
    private readonly AndroidLanService _android = new();
    private readonly AndroidSyncService _sync = new();
    private readonly DesktopSettings _settings;

    private readonly TextBox _storeName = new();
    private readonly TextBox _deviceName = new();
    private readonly TextBox _storePhone = new();
    private readonly TextBox _storeAddress = new();
    private readonly ComboBox _currency = new();
    private readonly TextBox _androidHost = new();
    private readonly TextBox _androidPort = new();
    private readonly PasswordBox _syncPin = new();
    private readonly TextBox _licenseKey = new();
    private readonly TextBlock _syncStatus = new();
    private readonly TextBlock _licenseStatus = new();

    public SettingsView(AppRepository repo)
    {
        _repo = repo;
        _settings = _settingsService.Load();
        FlowDirection = FlowDirection.RightToLeft;
        Content = Build();
        LoadValues();
    }

    private UIElement Build()
    {
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new StackPanel { Margin = new Thickness(4) };
        scroll.Content = root;

        root.Children.Add(SectionTitle("إعدادات المنشأة والجهاز"));
        var general = TwoColumnGrid();
        AddField(general, 0, 0, "اسم المنشأة", _storeName);
        AddField(general, 0, 1, "اسم جهاز Windows", _deviceName);
        AddField(general, 1, 0, "هاتف المنشأة", _storePhone);
        AddField(general, 1, 1, "عنوان المنشأة", _storeAddress);
        _currency.ItemsSource = new[] { "SYP", "USD" };
        AddField(general, 2, 0, "العملة الافتراضية", _currency);
        var saveGeneral = new Button { Content = "حفظ الإعدادات", Padding = new Thickness(18, 10, 18, 10), Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Right };
        saveGeneral.Click += (_, _) => SaveGeneral();
        Grid.SetRow(saveGeneral, 2); Grid.SetColumn(saveGeneral, 1); general.Children.Add(saveGeneral);
        root.Children.Add(Card(general));

        root.Children.Add(SectionTitle("ربط Android عبر الشبكة المحلية"));
        var syncGrid = TwoColumnGrid();
        AddField(syncGrid, 0, 0, "IP الهاتف الرئيسي", _androidHost);
        AddField(syncGrid, 0, 1, "المنفذ", _androidPort);
        AddField(syncGrid, 1, 0, "PIN المزامنة", _syncPin);
        var test = new Button { Content = "اختبار الاتصال", Padding = new Thickness(18, 10, 18, 10), Margin = new Thickness(6) };
        test.Click += async (_, _) => await TestAndroidAsync();
        Grid.SetRow(test, 1); Grid.SetColumn(test, 1); syncGrid.Children.Add(test);
        var pull = new Button { Content = "⬇ جلب Android إلى Windows", Padding = new Thickness(18, 10, 18, 10), Margin = new Thickness(6) };
        pull.Click += async (_, _) => await RunSyncAsync(false);
        Grid.SetRow(pull, 3); Grid.SetColumn(pull, 0); syncGrid.Children.Add(pull);
        var both = new Button { Content = "⇄ مزامنة ثنائية كاملة", Padding = new Thickness(18, 10, 18, 10), Margin = new Thickness(6), FontWeight = FontWeights.Bold };
        both.Click += async (_, _) => await RunSyncAsync(true);
        Grid.SetRow(both, 3); Grid.SetColumn(both, 1); syncGrid.Children.Add(both);
        syncGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _syncStatus.Margin = new Thickness(8); _syncStatus.TextWrapping = TextWrapping.Wrap;
        Grid.SetRow(_syncStatus, 2); Grid.SetColumnSpan(_syncStatus, 2); syncGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); syncGrid.Children.Add(_syncStatus);
        root.Children.Add(Card(syncGrid));

        root.Children.Add(SectionTitle("النسخ الاحتياطي والاستعادة"));
        var backupPanel = new WrapPanel { Margin = new Thickness(6) };
        backupPanel.Children.Add(ActionButton("نسخة ZIP", CreateBackup));
        backupPanel.Children.Add(ActionButton("نسخة مشفّرة", CreateSecureBackup));
        backupPanel.Children.Add(ActionButton("استعادة ZIP", RestoreBackup));
        backupPanel.Children.Add(ActionButton("استعادة مشفّرة", RestoreSecureBackup));
        backupPanel.Children.Add(ActionButton("فتح مجلد البيانات", OpenDataFolder));
        root.Children.Add(Card(backupPanel));

        root.Children.Add(SectionTitle("الترخيص"));
        var licensePanel = new StackPanel { Margin = new Thickness(6) };
        licensePanel.Children.Add(new TextBlock { Text = $"معرّف الجهاز: {LicenseService.GetStableDeviceId()}", Margin = new Thickness(6), FontWeight = FontWeights.SemiBold });
        _licenseKey.AcceptsReturn = true; _licenseKey.Height = 85; _licenseKey.TextWrapping = TextWrapping.Wrap; _licenseKey.Margin = new Thickness(6);
        licensePanel.Children.Add(_licenseKey);
        var verify = ActionButton("تحقق واحفظ الترخيص", VerifyLicense); licensePanel.Children.Add(verify);
        _licenseStatus.Margin = new Thickness(6); _licenseStatus.TextWrapping = TextWrapping.Wrap; licensePanel.Children.Add(_licenseStatus);
        root.Children.Add(Card(licensePanel));

        root.Children.Add(SectionTitle("استيراد قاعدة Android"));
        var importPanel = new StackPanel { Margin = new Thickness(6) };
        importPanel.Children.Add(new TextBlock { Text = "يمكن نقل ملف قاعدة SQLite من Android ثم استيراده إلى Windows. يتم دمج البيانات مع قاعدة Windows وإعادة بناء طبقات FIFO.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) });
        importPanel.Children.Add(ActionButton("اختيار قاعدة Android واستيرادها", ImportAndroid));
        root.Children.Add(Card(importPanel));

        return scroll;
    }

    private void LoadValues()
    {
        _storeName.Text = _settings.StoreName;
        _deviceName.Text = _settings.DeviceName;
        _storePhone.Text = _settings.StorePhone;
        _storeAddress.Text = _settings.StoreAddress;
        _currency.SelectedItem = _settings.DefaultCurrency;
        _androidHost.Text = _settings.AndroidHost;
        _androidPort.Text = _settings.AndroidPort.ToString();
        _syncPin.Password = _settings.SyncPin;
        _licenseKey.Text = _settings.LicenseKey;
        if (_settings.LastAndroidConnection is DateTime d) _syncStatus.Text = $"آخر اتصال ناجح: {d:g}";
        if (!string.IsNullOrWhiteSpace(_settings.LicenseKey)) ShowLicenseStatus(_license.Verify(_settings.LicenseKey, LicenseService.GetStableDeviceId()));
    }

    private void SaveGeneral()
    {
        _settings.StoreName = _storeName.Text.Trim();
        _settings.DeviceName = _deviceName.Text.Trim();
        _settings.StorePhone = _storePhone.Text.Trim();
        _settings.StoreAddress = _storeAddress.Text.Trim();
        _settings.DefaultCurrency = _currency.SelectedItem?.ToString() ?? "SYP";
        _settings.AndroidHost = _androidHost.Text.Trim();
        _settings.AndroidPort = int.TryParse(_androidPort.Text, out var p) ? p : 8989;
        _settings.SyncPin = _syncPin.Password;
        _settingsService.Save(_settings);
        MessageBox.Show("تم حفظ الإعدادات.");
    }

    private async Task TestAndroidAsync()
    {
        SaveGeneralSilently();
        _syncStatus.Text = "جاري فحص الاتصال...";
        var result = await _android.CheckStatusAsync(_settings.AndroidHost, _settings.AndroidPort);
        if (result.IsOnline)
        {
            _settings.LastAndroidConnection = DateTime.Now;
            _settingsService.Save(_settings);
            _syncStatus.Text = $"✅ {result.Message}\nالمتجر: {result.StoreName}\nالجهاز: {result.DeviceName}\nالمنتجات: {result.ProductsCount:N0} | الفواتير: {result.InvoicesCount:N0}";
        }
        else _syncStatus.Text = "❌ " + result.Message;
    }

    private async Task RunSyncAsync(bool bothWays)
    {
        SaveGeneralSilently();
        _syncStatus.Text = bothWays ? "جاري تنفيذ المزامنة الثنائية..." : "جاري جلب بيانات Android...";
        try
        {
            var result = bothWays
                ? await _sync.SyncAsync(_settings.AndroidHost, _settings.AndroidPort, _settings.SyncPin)
                : await _sync.PullAsync(_settings.AndroidHost, _settings.AndroidPort, _settings.SyncPin);
            if (result.Success)
            {
                _settings.LastAndroidConnection = DateTime.Now; _settingsService.Save(_settings);
                _syncStatus.Text = $"✅ {result.Message}\nالمنتجات: {result.Products:N0} | الأطراف: {result.Parties:N0} | الفواتير: {result.Invoices:N0}\nالدفعات: {result.Payments:N0} | الصندوق: {result.Cash:N0} | المخزون: {result.Stock:N0} | القيود: {result.Journals:N0}";
                _repo.LoadAll();
            }
            else _syncStatus.Text = "❌ " + result.Message;
        }
        catch (Exception ex) { _syncStatus.Text = "❌ خطأ أثناء المزامنة: " + ex.Message; }
    }

    private void SaveGeneralSilently()
    {
        _settings.StoreName = _storeName.Text.Trim(); _settings.DeviceName = _deviceName.Text.Trim(); _settings.StorePhone = _storePhone.Text.Trim(); _settings.StoreAddress = _storeAddress.Text.Trim();
        _settings.StorePhone = _storePhone.Text.Trim();
        _settings.StoreAddress = _storeAddress.Text.Trim();
        _settings.DefaultCurrency = _currency.SelectedItem?.ToString() ?? "SYP";
        _settings.AndroidHost = _androidHost.Text.Trim(); _settings.AndroidPort = int.TryParse(_androidPort.Text, out var p) ? p : 8989;
        _settings.SyncPin = _syncPin.Password; _settingsService.Save(_settings);
    }

    private void CreateBackup()
    {
        var d = new SaveFileDialog { Filter = "Mizan Backup (*.zip)|*.zip", FileName = $"MizanBackup-{DateTime.Now:yyyyMMdd-HHmm}.zip" };
        if (d.ShowDialog() != true) return;
        Try(() => { _backup.CreateBackup(d.FileName); MessageBox.Show("تم إنشاء النسخة الاحتياطية بنجاح."); });
    }

    private void CreateSecureBackup()
    {
        var password = PromptPassword("كلمة مرور النسخة المشفّرة"); if (password == null) return;
        var d = new SaveFileDialog { Filter = "Mizan Secure Backup (*.mzb)|*.mzb", FileName = $"MizanSecure-{DateTime.Now:yyyyMMdd-HHmm}.mzb" };
        if (d.ShowDialog() != true) return;
        Try(() => { _backup.CreateSecureBackup(d.FileName, password); MessageBox.Show("تم إنشاء النسخة المشفّرة."); });
    }

    private void RestoreBackup()
    {
        var d = new OpenFileDialog { Filter = "Mizan Backup (*.zip)|*.zip" }; if (d.ShowDialog() != true) return;
        if (MessageBox.Show("سيتم استبدال قاعدة البيانات الحالية. هل تريد المتابعة؟", "استعادة", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Try(() => { _backup.RestoreBackup(d.FileName); MessageBox.Show("تمت الاستعادة. أعد تشغيل البرنامج."); });
    }

    private void RestoreSecureBackup()
    {
        var d = new OpenFileDialog { Filter = "Mizan Secure Backup (*.mzb)|*.mzb" }; if (d.ShowDialog() != true) return;
        var password = PromptPassword("كلمة مرور النسخة المشفّرة"); if (password == null) return;
        if (MessageBox.Show("سيتم استبدال قاعدة البيانات الحالية. هل تريد المتابعة؟", "استعادة", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Try(() => { _backup.RestoreSecureBackup(d.FileName, password); MessageBox.Show("تمت الاستعادة. أعد تشغيل البرنامج."); });
    }

    private void ImportAndroid()
    {
        var d = new OpenFileDialog { Filter = "SQLite Android (*.db;*.sqlite)|*.db;*.sqlite|All files|*.*" }; if (d.ShowDialog() != true) return;
        Try(() => { var report = new AndroidDatabaseImporter().Import(d.FileName); _repo.LoadAll(); MessageBox.Show(report.ToString(), "اكتمل الاستيراد"); });
    }

    private void VerifyLicense()
    {
        var value = _licenseKey.Text.Trim();
        var result = _license.Verify(value, LicenseService.GetStableDeviceId());
        ShowLicenseStatus(result);
        if (result.IsValid) { _settings.LicenseKey = value; _settingsService.Save(_settings); }
    }

    private void ShowLicenseStatus(LicenseService.LicenseVerification result)
    {
        if (!result.IsValid) { _licenseStatus.Text = "❌ " + result.Message; return; }
        var expiry = result.ExpiryDate > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(result.ExpiryDate).LocalDateTime.ToString("d") : "غير محدد";
        _licenseStatus.Text = $"✅ {result.Message} النوع: {result.LicenseType} | الانتهاء: {expiry}";
    }

    private static string? PromptPassword(string title)
    {
        var w = new Window { Title = title, Width = 420, Height = 180, WindowStartupLocation = WindowStartupLocation.CenterOwner, FlowDirection = FlowDirection.RightToLeft, ResizeMode = ResizeMode.NoResize };
        var p = new StackPanel { Margin = new Thickness(18) }; var box = new PasswordBox { Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 12) };
        p.Children.Add(new TextBlock { Text = "أدخل كلمة المرور:" }); p.Children.Add(box);
        var ok = new Button { Content = "موافق", Padding = new Thickness(18, 8, 18, 8), HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        ok.Click += (_, _) => w.DialogResult = true; p.Children.Add(ok); w.Content = p;
        return w.ShowDialog() == true ? box.Password : null;
    }

    private static void OpenDataFolder()
    {
        var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MizanDesktop");
        Directory.CreateDirectory(dir); Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }

    private static void Try(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error); } }
    private static TextBlock SectionTitle(string text) => new() { Text = text, FontSize = 21, FontWeight = FontWeights.Bold, Margin = new Thickness(4, 18, 4, 8) };
    private static Border Card(UIElement child) => new() { Child = child, Background = System.Windows.Media.Brushes.White, BorderBrush = System.Windows.Media.Brushes.LightGray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Margin = new Thickness(4) };
    private static Button ActionButton(string text, Action action) { var b = new Button { Content = text, Padding = new Thickness(16, 9, 16, 9), Margin = new Thickness(5) }; if (text.Contains("استعادة", StringComparison.Ordinal)) { b.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(185, 28, 28)); b.BorderBrush = b.Background; } else if (text.Contains("نسخة", StringComparison.Ordinal)) { b.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61)); b.BorderBrush = b.Background; } b.Click += (_, _) => action(); return b; }
    private static Grid TwoColumnGrid() { var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition()); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); return g; }
    private static void AddField(Grid grid, int row, int col, string label, Control control) { control.Margin = new Thickness(4); if (control is TextBox t) t.Padding = new Thickness(7); if (control is ComboBox c) c.Padding = new Thickness(7); var p = new StackPanel { Margin = new Thickness(4) }; p.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold }); p.Children.Add(control); Grid.SetRow(p, row); Grid.SetColumn(p, col); grid.Children.Add(p); }
}
