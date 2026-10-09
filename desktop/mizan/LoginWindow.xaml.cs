using System.Windows;
using MizanDesktop.Services;

namespace MizanDesktop;

public partial class LoginWindow : Window
{
    private readonly EnterpriseAccountingService _service = new();

    public LoginWindow()
    {
        InitializeComponent();
        User.Text = "admin";
        Pin.Focus();
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        var username = User.Text.Trim();
        var pin = Pin.Password.Trim();

        try
        {
            var user = _service.Login(username, pin);
            if (user is null)
            {
                MessageBox.Show(
                    "اسم المستخدم أو رمز الدخول غير صحيح. إذا كانت هذه أول مرة، استخدم admin و1234، أو اضغط «استعادة دخول المدير».",
                    "تعذر تسجيل الدخول",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Pin.Clear();
                Pin.Focus();
                return;
            }

            App.SetUser(user.Value);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "تعذر التحقق من بيانات الدخول: " + ex.Message,
                "خطأ في تسجيل الدخول",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RecoverAdmin_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "سيتم إعادة اسم دخول المدير إلى admin ورمز الدخول إلى 1234.\n\nلن يتم حذف الفواتير أو المنتجات أو قاعدة البيانات.\nإذا كان لديك حساب مدير مخصص، فسيُعاد ضبطه إلى البيانات الافتراضية.\n\nهل تريد المتابعة؟",
            "استعادة دخول المدير",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            LegacyLoginMigration.ResetAdminCredentialsToDefault();
            User.Text = "admin";
            Pin.Clear();
            Pin.Focus();
            MessageBox.Show(
                "تمت استعادة الدخول. استخدم اسم المستخدم admin ورمز الدخول 1234.",
                "اكتملت الاستعادة",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "تعذرت استعادة الدخول: " + ex.Message,
                "فشل الاستعادة",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
