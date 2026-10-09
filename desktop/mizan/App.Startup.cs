using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MizanDesktop.Models;

namespace MizanDesktop;

public partial class App
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        try
        {
            base.OnStartup(e);
            Services.LegacyLoginMigration.EnsureDefaultAdmin();

            if (HasArg(e, "--self-test-login")) { Shutdown(Services.LegacyLoginMigration.SelfTestLogin() ? 0 : 21); return; }
            if (HasArg(e, "--self-test-legacy-login")) { Shutdown(Services.LegacyLoginMigration.SelfTestLegacyLogin() ? 0 : 22); return; }
            if (HasArg(e, "--self-test-login-recovery")) { Shutdown(Services.LegacyLoginMigration.SelfTestLoginRecovery() ? 0 : 23); return; }
            if (HasArg(e, "--self-test-views")) { Shutdown(SelfTestViews() ? 0 : 24); return; }

            var login = new LoginWindow();
            if (login.ShowDialog() == true)
            {
                var w = new MainWindow();
                MainWindow = w;
                w.Show();
            }
            else Shutdown();
        }
        catch (Exception ex)
        {
            LogStartupException(ex);
            MessageBox.Show("حدث خطأ أثناء تشغيل الميزان.\n\n" + ex.Message +
                "\n\nتم حفظ التفاصيل في startup-crash.log", "الميزان",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static bool SelfTestViews()
    {
        var repo = new Services.AppRepository();
        FrameworkElement[] views =
        [
            new Views.AccountingView(repo),
            new Views.AuditLogView(repo),
            new Views.CashBoxView(repo),
            new Views.CashTransferView(repo),
            new Views.ChartOfAccountsView(repo),
            new Views.CostLayersView(repo),
            new Views.EnterpriseView(),
            new Views.ExchangeRatesView(repo),
            new Views.FinancialReportsView(repo),
            new Views.InventoryAuditView(repo),
            new Views.InventoryView(repo),
            new Views.InvoicesView(repo, InvoiceType.Sale),
            new Views.InvoicesView(repo, InvoiceType.Purchase),
            new Views.InvoicesView(repo, InvoiceType.SaleReturn),
            new Views.InvoicesView(repo, InvoiceType.PurchaseReturn),
            new Views.InvoicesView(repo, InvoiceType.Quotation),
            new Views.PartiesView(repo),
            new Views.PaymentsView(repo),
            new Views.ProductsView(repo),
            new Views.ReportsView(repo),
            new Views.SettingsView(repo),
            new Views.StatementView(repo),
            new Views.UsersView(repo)
        ];

        foreach (var view in views)
        {
            view.Measure(new Size(900, 640));
            view.Arrange(new Rect(0, 0, 900, 640));
            view.UpdateLayout();
            if (view is UserControl control && control.Content is null)
                throw new InvalidOperationException($"واجهة {view.GetType().Name} لم تنشئ محتوى.");
        }

        return true;
    }

    private static bool HasArg(StartupEventArgs e, string value) =>
        e.Args.Any(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase));

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogStartupException(e.Exception);
        e.Handled = true;
        MessageBox.Show("حدث خطأ غير متوقع أثناء التشغيل.\n\n" + e.Exception.Message +
            "\n\nتم حفظ التفاصيل في startup-crash.log", "الميزان",
            MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }

    private static void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex) LogStartupException(ex);
    }

    private static void LogStartupException(Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MizanDesktop");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "startup-crash.log"),
                $"[{DateTimeOffset.Now:O}] {ex}\n------------------------------\n");
        }
        catch { }
    }
}
