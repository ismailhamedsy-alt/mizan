using System.Windows;using MizanDesktop.Services;
namespace MizanDesktop;
public partial class LoginWindow:Window{readonly EnterpriseAccountingService svc=new();public LoginWindow(){InitializeComponent();}void Login_Click(object s,RoutedEventArgs e){var u=svc.Login(User.Text.Trim(),Pin.Password);if(u is null){MessageBox.Show("اسم المستخدم أو PIN غير صحيح.");return;}App.SetUser(u.Value);DialogResult=true;Close();}}
