using System.Windows;
namespace MizanDesktop;
public partial class App{protected override void OnStartup(StartupEventArgs e){base.OnStartup(e);var login=new LoginWindow();if(login.ShowDialog()==true){var w=new MainWindow();MainWindow=w;w.Show();}else Shutdown();}}
