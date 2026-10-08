using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MizanDesktop.Services;
using MizanDesktop.Views;

namespace MizanDesktop;

public partial class MainWindow : Window
{
    private readonly AppRepository _repo = new();
    public MainWindow()
    {
        InitializeComponent();
        PageSubtitle.Text=$"المستخدم: {App.CurrentUser?.Name ?? "admin"} — صلاحية: {App.CurrentUser?.Role ?? "ADMIN"}";
        ApplyRolePermissions();
        RefreshDashboard();
    }

    private void ApplyRolePermissions()
    {
        var role = App.CurrentUser?.Role ?? "CASHIER";
        if (role == "ADMIN") return;
        var service = new EnterpriseAccountingService();
        foreach (var button in FindVisualChildren<Button>(this))
        {
            var text = button.Content?.ToString() ?? "";
            var permission = text switch
            {
                var x when x.Contains("المبيعات") => "SALES",
                var x when x.Contains("المشتريات") => "PURCHASES",
                var x when x.Contains("المنتجات") || x.Contains("طبقات") => "PRODUCTS",
                var x when x.Contains("العملاء") || x.Contains("كشف الحساب") => "PARTIES",
                var x when x.Contains("الصندوق") || x.Contains("تحويل بين الصناديق") => "CASH",
                var x when x.Contains("المستخدمون") => "USERS",
                var x when x.Contains("استيراد") => "IMPORT",
                var x when x.Contains("الإعدادات") => "SETTINGS",
                var x when x.Contains("سجل التدقيق") || x.Contains("الإدارة المالية") || x.Contains("الحسابات") ||
                           x.Contains("دليل الحسابات") || x.Contains("التقارير") || x.Contains("أسعار الصرف") ||
                           x.Contains("المصروفات") || x.Contains("السندات") || x.Contains("الجرد") => "ACCOUNTING",
                _ => ""
            };
            if (string.IsNullOrEmpty(permission)) continue;
            button.Visibility = service.HasPermission(role, permission) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root == null) yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
        }
    }
    private void RefreshDashboard(){_repo.LoadAll();SalesTotal.Text=_repo.Invoices.Where(x=>x.Type==Models.InvoiceType.Sale&&x.Date.Date==DateTime.Today).Sum(x=>x.Total).ToString("N2");PurchaseTotal.Text=_repo.Invoices.Where(x=>x.Type==Models.InvoiceType.Purchase&&x.Date.Date==DateTime.Today).Sum(x=>x.Total).ToString("N2");InventoryTotal.Text=_repo.InventoryValue.ToString("N2");CashTotal.Text=_repo.CashBalance.ToString("N2");RecentInvoicesList.ItemsSource=_repo.Invoices.OrderByDescending(x=>x.Date).Take(10).Select(x=>new { x.Id, x.Number, x.PartyName, x.Total, DateLabel=x.Date.ToString("yyyy-MM-dd HH:mm"), TotalLabel=x.Total.ToString("N2"), TypeLabel=InvoiceTypeLabel(x.Type) }).ToList();}

    private static string InvoiceTypeLabel(Models.InvoiceType t) => t switch { Models.InvoiceType.Sale => "مبيعات", Models.InvoiceType.Purchase => "مشتريات", Models.InvoiceType.SaleReturn => "مرتجع مبيعات", Models.InvoiceType.PurchaseReturn => "مرتجع مشتريات", Models.InvoiceType.Quotation => "عرض سعر", _ => "عملية" };
    private void RecentInvoice_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (RecentInvoicesList.SelectedItem is null) return;
        var idProp = RecentInvoicesList.SelectedItem.GetType().GetProperty("Id");
        if (idProp?.GetValue(RecentInvoicesList.SelectedItem) is Guid id)
        {
            var inv = _repo.Invoices.FirstOrDefault(x => x.Id == id);
            if (inv is null) return;
            var title = inv.Type switch { Models.InvoiceType.Sale => "المبيعات", Models.InvoiceType.Purchase => "المشتريات", Models.InvoiceType.SaleReturn => "مرتجع المبيعات", Models.InvoiceType.PurchaseReturn => "مرتجع المشتريات", _ => "عروض الأسعار" };
            ShowView(title, $"تفاصيل {inv.Number}", new InvoicesView(_repo, inv.Type));
        }
    }

    private void ShowView(string title,string subtitle,UIElement view){PageTitle.Text=title;PageSubtitle.Text=subtitle;ContentHost.Children.Clear();ContentHost.Children.Add(view);}
    private void Dashboard_Click(object s,RoutedEventArgs e){RefreshDashboard();PageTitle.Text="لوحة التحكم";PageSubtitle.Text="نظرة عامة على نشاط المنشأة";ContentHost.Children.Clear();var grid=new Grid{Margin=new Thickness(10)};for(int i=0;i<2;i++)grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(120)});for(int i=0;i<2;i++)grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});grid.Children.Add(Card("مبيعات اليوم",_repo.Invoices.Where(x=>x.Type==Models.InvoiceType.Sale&&x.Date.Date==DateTime.Today).Sum(x=>x.Total).ToString("N2"),0,0));grid.Children.Add(Card("مشتريات اليوم",_repo.Invoices.Where(x=>x.Type==Models.InvoiceType.Purchase&&x.Date.Date==DateTime.Today).Sum(x=>x.Total).ToString("N2"),0,1));grid.Children.Add(Card("قيمة المخزون",_repo.InventoryValue.ToString("N2"),1,0));grid.Children.Add(Card("رصيد الصندوق",_repo.CashBalance.ToString("N2"),1,1));ContentHost.Children.Add(grid);}
    private static Border Card(string title,string value,int row,int col){var p=new StackPanel{Margin=new Thickness(8)};p.Children.Add(new TextBlock{Text=title,FontSize=16});p.Children.Add(new TextBlock{Text=value,FontSize=28,FontWeight=FontWeights.Bold,Margin=new Thickness(0,10,0,0)});var b=new Border{BorderBrush=System.Windows.Media.Brushes.LightGray,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(18),Child=p};Grid.SetRow(b,row);Grid.SetColumn(b,col);return b;}
    private void Products_Click(object s,RoutedEventArgs e)=>ShowView("المنتجات والمخزون","إدارة الأصناف والكميات والأسعار",new ProductsView(_repo));
    private void Sales_Click(object s,RoutedEventArgs e)=>ShowView("المبيعات","إنشاء ومراجعة فواتير البيع",new InvoicesView(_repo,Models.InvoiceType.Sale));
    private void Purchases_Click(object s,RoutedEventArgs e)=>ShowView("المشتريات","إنشاء ومراجعة فواتير الشراء",new InvoicesView(_repo,Models.InvoiceType.Purchase));
    private void Parties_Click(object s,RoutedEventArgs e)=>ShowView("العملاء والموردون","إدارة الأطراف والأرصدة المدينة والدائنة",new PartiesView(_repo));
    private void Cash_Click(object s,RoutedEventArgs e)=>ShowView("الصندوق","الصندوق الرئيسي والحركات النقدية",new CashBoxView(_repo));
    private void Inventory_Click(object s,RoutedEventArgs e)=>ShowView("الجرد والمخزون","الكميات والقيم والتنبيهات والجرد السريع",new InventoryView(_repo));
    private void Payments_Click(object s,RoutedEventArgs e)=>ShowView("السندات والمدفوعات","سندات القبض والصرف والحركات النقدية",new PaymentsView(_repo));
    private void Accounting_Click(object s,RoutedEventArgs e)=>ShowView("الحسابات والقيود","دفتر اليومية والقيود الناتجة عن العمليات",new AccountingView(_repo));
    private void Reports_Click(object s,RoutedEventArgs e)=>ShowView("التقارير","الأرباح والخسائر وميزان المراجعة وكشوف الحركة والتصدير",new ReportsView(_repo));
    private void FinancialReports_Click(object s,RoutedEventArgs e)=>ShowView("التقارير المالية","ميزان المراجعة والأرباح والخسائر والذمم",new FinancialReportsView(_repo));
    private void ExchangeRates_Click(object s,RoutedEventArgs e)=>ShowView("أسعار الصرف","إدارة أسعار العملات المستخدمة في النظام",new ExchangeRatesView(_repo));
    private void Settings_Click(object s,RoutedEventArgs e) => ShowView("الإعدادات", "المنشأة والنسخ الاحتياطي والترخيص وربط Android", new SettingsView(_repo));

    private void SalesReturns_Click(object s, RoutedEventArgs e) => ShowView("مرتجعات المبيعات", "إدارة مرتجعات العملاء وتحديث المخزون والحسابات", new InvoicesView(_repo, Models.InvoiceType.SaleReturn));
    private void PurchaseReturns_Click(object s, RoutedEventArgs e) => ShowView("مرتجعات المشتريات", "إدارة مرتجعات الموردين وتحديث المخزون والحسابات", new InvoicesView(_repo, Models.InvoiceType.PurchaseReturn));
    private void Quotations_Click(object s, RoutedEventArgs e) => ShowView("عروض الأسعار", "إنشاء ومراجعة عروض الأسعار دون التأثير على المخزون أو الصندوق", new InvoicesView(_repo, Models.InvoiceType.Quotation));
    private void Expenses_Click(object s, RoutedEventArgs e) => ShowView("المصروفات", "سندات صرف المصروفات وحركات الصندوق", new PaymentsView(_repo));
    private void ChartOfAccounts_Click(object s, RoutedEventArgs e) => ShowView("دليل الحسابات", "الحسابات الرئيسية المستخدمة في النظام", new Views.ChartOfAccountsView(_repo));
    private void Users_Click(object s, RoutedEventArgs e) => ShowView("المستخدمون والصلاحيات", "إدارة مستخدمي البرنامج وصلاحياتهم", new Views.UsersView(_repo));
    private void ImportAndroid_Click(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = "SQLite Android (*.db;*.sqlite)|*.db;*.sqlite|All files|*.*" };
        if (d.ShowDialog() != true) return;
        try
        {
            var report = new Services.AndroidDatabaseImporter().Import(d.FileName);
            _repo.LoadAll();
            MessageBox.Show(report.ToString(), "اكتمل استيراد Android", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "تعذر استيراد Android", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void InventoryAudit_Click(object s, RoutedEventArgs e) => ShowView("الجرد والتسوية", "مطابقة الرصيد الفعلي مع الرصيد النظامي واعتماد الفروقات", new Views.InventoryAuditView(_repo));
    private void CostLayers_Click(object s, RoutedEventArgs e) => ShowView("طبقات تكلفة المخزون", "تتبع تكلفة الشراء واستهلاك الطبقات بنظام FIFO", new Views.CostLayersView(_repo));
    private void CashTransfer_Click(object s, RoutedEventArgs e) => ShowView("تحويل بين الصناديق", "تحويل نقدي أو تصريف بين صندوقي الليرة والدولار", new Views.CashTransferView(_repo));
    private void AuditLog_Click(object s, RoutedEventArgs e) => ShowView("سجل التدقيق", "تاريخ العمليات الحساسة التي تمت على البرنامج", new Views.AuditLogView(_repo));

    private void Statements_Click(object sender, RoutedEventArgs e)
    { PageTitle.Text="كشف الحساب"; PageSubtitle.Text="حركة العملاء والموردين"; ContentHost.Children.Clear(); ContentHost.Children.Add(new Views.StatementView(_repo)); }    private void Enterprise_Click(object s, RoutedEventArgs e) => ShowView("الإدارة المالية المتقدمة", "المحاسبة والذمم وإغلاق الصندوق والإلغاء والاستيراد", new Views.EnterpriseView());

}
