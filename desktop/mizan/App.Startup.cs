using System.Windows;
using System.Windows.Threading;

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
            var login = new LoginWindow();
            if (login.ShowDialog() == true)
            {
                var w = new MainWindow();
                MainWindow = w;
                w.Show();
            }
            else
            {
                Shutdown();
            }
        }
        catch (Exception ex)
        {
            LogStartupException(ex);
            MessageBox.Show(
                "حدث خطأ أثناء تشغيل الميزان.\n\n" + ex.Message +
                "\n\nتم حفظ التفاصيل في startup-crash.log",
                "الميزان",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogStartupException(e.Exception);
        e.Handled = true;
        MessageBox.Show(
            "حدث خطأ غير متوقع أثناء التشغيل.\n\n" + e.Exception.Message +
            "\n\nتم حفظ التفاصيل في startup-crash.log",
            "الميزان",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(1);
    }

    private static void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogStartupException(ex);
    }

    private static void LogStartupException(Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MizanDesktop");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "startup-crash.log");
            File.AppendAllText(
                path,
                $"[{DateTimeOffset.Now:O}] {ex}\n------------------------------\n");
        }
        catch
        {
            // Never throw from the crash logger.
        }
    }
}
