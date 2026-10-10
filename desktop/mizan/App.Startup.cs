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
            if (HasArg(e, "--self-test-login-upgrade")) { Shutdown(Services.LegacyLoginMigration.SelfTestLoginUpgrade() ? 0 : 25); return; }
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
            if (e.Args.Any(a => a.StartsWith("--self-test-", StringComparison.OrdinalIgnoreCase)))
            {
                Shutdown(90);
                return;
            }

            MessageBox.Show("حدث خطأ أثناء تشغيل الميزان.\n\n" + ex.Message +
                "\n\nتم حفظ التفاصيل في startup-crash.log", "الميزان",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static bool SelfTestViews()
    {
        var repo = new Services.AppRepository();
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MizanDesktop");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "ui-smoke.log");
        File.WriteAllText(logPath, $"UI smoke started {DateTimeOffset.Now:O}\n");

        (string Name, Func<FrameworkElement> Create)[] factories =
        [
            ("LoginWindow", () => new LoginWindow()),
            ("MainWindow", () => new MainWindow()),
            ("AccountingView", () => new Views.AccountingView(repo)),
            ("AuditLogView", () => new Views.AuditLogView(repo)),
            ("CashBoxView", () => new Views.CashBoxView(repo)),
            ("CashTransferView", () => new Views.CashTransferView(repo)),
            ("ChartOfAccountsView", () => new Views.ChartOfAccountsView(repo)),
            ("CostLayersView", () => new Views.CostLayersView(repo)),
            ("EnterpriseView", () => new Views.EnterpriseView()),
            ("ExchangeRatesView", () => new Views.ExchangeRatesView(repo)),
            ("FinancialReportsView", () => new Views.FinancialReportsView(repo)),
            ("InventoryAuditView", () => new Views.InventoryAuditView(repo)),
            ("InventoryView", () => new Views.InventoryView(repo)),
            ("Invoices-Sale", () => new Views.InvoicesView(repo, InvoiceType.Sale)),
            ("Invoices-Purchase", () => new Views.InvoicesView(repo, InvoiceType.Purchase)),
            ("Invoices-SaleReturn", () => new Views.InvoicesView(repo, InvoiceType.SaleReturn)),
            ("Invoices-PurchaseReturn", () => new Views.InvoicesView(repo, InvoiceType.PurchaseReturn)),
            ("Invoices-Quotation", () => new Views.InvoicesView(repo, InvoiceType.Quotation)),
            ("PartiesView", () => new Views.PartiesView(repo)),
            ("PaymentsView", () => new Views.PaymentsView(repo)),
            ("ProductsView", () => new Views.ProductsView(repo)),
            ("ReportsView", () => new Views.ReportsView(repo)),
            ("SettingsView", () => new Views.SettingsView(repo)),
            ("StatementView", () => new Views.StatementView(repo)),
            ("UsersView", () => new Views.UsersView(repo))
        ];

        foreach (var item in factories)
        {
            File.AppendAllText(logPath, $"Creating {item.Name}...\n");
            var view = item.Create();
            if (view is UserControl control && control.Content is null)
                throw new InvalidOperationException($"واجهة {item.Name} لم تنشئ محتوى.");
            if (view is Window window && window.Content is null)
                throw new InvalidOperationException($"النافذة {item.Name} لم تنشئ محتوى.");
            File.AppendAllText(logPath, $"OK {item.Name}\n");
        }

        File.AppendAllText(logPath, $"UI smoke passed: {factories.Length} views/windows\n");
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
