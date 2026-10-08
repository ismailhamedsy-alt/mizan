using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MizanDesktop.Models;
using MizanDesktop.Services;

namespace MizanDesktop.Views;

public partial class InvoicesView : UserControl
{
    private readonly AppRepository _repo;
    private readonly InvoiceType _baseType;

    public InvoicesView(AppRepository repo, InvoiceType type)
    {
        InitializeComponent();
        _repo = repo;
        _baseType = type;
        Load();
    }

    private void Load(string? filter = null)
    {
        _repo.LoadAll();
        var q = _repo.Invoices.Where(x => IsVisibleType(x) && (string.IsNullOrWhiteSpace(filter) || x.Number.Contains(filter, StringComparison.OrdinalIgnoreCase) || x.PartyName.Contains(filter, StringComparison.OrdinalIgnoreCase))).ToList();
        Grid.ItemsSource = q;
        TotalText.Text = q.Sum(x => x.Total).ToString("N2");
        CountText.Text = q.Count.ToString();
    }

    private bool IsVisibleType(Invoice x) => x.Type == _baseType;

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => Load(SearchBox.Text);
    private void Refresh_Click(object sender, RoutedEventArgs e) => Load(SearchBox.Text);
    private void New_Click(object sender, RoutedEventArgs e) => NewInvoice();
    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not Invoice invoice) { MessageBox.Show("اختر عملية أولاً."); return; }
        PrintService.PrintInvoice(invoice);
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not Invoice invoice) { MessageBox.Show("اختر عملية أولاً."); return; }
        var w = new Window { Title = $"تفاصيل {invoice.Number}", Width = 850, Height = 620, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner, FlowDirection = FlowDirection.RightToLeft };
        var root = new Grid { Margin = new Thickness(16) }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = $"{WindowTitle(invoice.Type)} — {invoice.Number}\n{invoice.Date:g} | الطرف: {invoice.PartyName} | الدفع: {PaymentMethods.Label(invoice.PaymentMethod)}", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,12) });
        var dg = new DataGrid { ItemsSource = invoice.Lines, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false };
        dg.Columns.Add(new DataGridTextColumn { Header = "الصنف", Binding = new System.Windows.Data.Binding("ProductName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        dg.Columns.Add(new DataGridTextColumn { Header = "الكمية", Binding = new System.Windows.Data.Binding("Quantity") { StringFormat = "N2" }, Width = 110 });
        dg.Columns.Add(new DataGridTextColumn { Header = "السعر", Binding = new System.Windows.Data.Binding("UnitPrice") { StringFormat = "N2" }, Width = 130 });
        dg.Columns.Add(new DataGridTextColumn { Header = "الإجمالي", Binding = new System.Windows.Data.Binding("Total") { StringFormat = "N2" }, Width = 140 });
        System.Windows.Controls.Grid.SetRow(dg, 1); root.Children.Add(dg);
        var footer = new DockPanel { Margin = new Thickness(0,12,0,0) }; footer.Children.Add(new TextBlock { Text = $"الإجمالي: {invoice.Total:N2}  |  المدفوع: {invoice.PaidAmount:N2}  |  المتبقي: {invoice.Remaining:N2}", FontSize = 17, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var print = new Button { Content = "طباعة", Padding = new Thickness(18,8,18,8), HorizontalAlignment = HorizontalAlignment.Left }; print.Click += (_, _) => PrintService.PrintInvoice(invoice); DockPanel.SetDock(print, Dock.Left); footer.Children.Add(print);
        System.Windows.Controls.Grid.SetRow(footer, 2); root.Children.Add(footer); w.Content = root; w.ShowDialog();
    }

    private void NewInvoice()
    {
        _repo.LoadAll();
        var products = _repo.Products.Where(x => x.IsActive).ToList();
        if (products.Count == 0) { MessageBox.Show("أضف منتجات أولًا."); return; }

        var allowedTypes = new[] { _baseType };
        var inv = new Invoice { Type = _baseType, Number = NewNumber(_baseType) };
        var partyType = _baseType is InvoiceType.Sale or InvoiceType.SaleReturn or InvoiceType.Quotation ? PartyType.Customer : PartyType.Supplier;
        var parties = _repo.Parties.Where(p => p.Type == partyType).ToList();

        var w = new Window
        {
            Title = WindowTitle(_baseType),
            Width = 1000, Height = 760, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FlowDirection = FlowDirection.RightToLeft
        };

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        for (int i = 0; i < 6; i++) header.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 1 ? new GridLength(220) : new GridLength(1, GridUnitType.Star) });
        var typeBox = new ComboBox { ItemsSource = allowedTypes, SelectedIndex = 0, Padding = new Thickness(7), Margin = new Thickness(4), IsEnabled = false };
        var partyBox = new ComboBox { ItemsSource = parties, DisplayMemberPath = "Name", Padding = new Thickness(7), Margin = new Thickness(4) };
        var paymentBox = new ComboBox { Padding = new Thickness(7), Margin = new Thickness(4), ItemsSource = new[] { PaymentMethods.Cash, PaymentMethods.Deferred, PaymentMethods.Card, PaymentMethods.BankTransfer, PaymentMethods.ShamCash, PaymentMethods.Cheque }, SelectedIndex = 0 };
        var paidBox = new TextBox { Text = "0", Padding = new Thickness(7), Margin = new Thickness(4) };
        var discountBox = new TextBox { Text = "0", Padding = new Thickness(7), Margin = new Thickness(4) };
        var feesBox = new TextBox { Text = "0", Padding = new Thickness(7), Margin = new Thickness(4) };
        AddLabeled(header, "نوع الفاتورة", typeBox, 0);
        AddLabeled(header, "الطرف (اختياري للنقدي)", partyBox, 1);
        AddLabeled(header, "طريقة الدفع", paymentBox, 2);
        AddLabeled(header, "المدفوع", paidBox, 3);
        AddLabeled(header, "الخصم", discountBox, 4);
        AddLabeled(header, "أجور إضافية", feesBox, 5);
        System.Windows.Controls.Grid.SetRow(header, 0); root.Children.Add(header);

        var addGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        addGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        var productBox = new ComboBox { ItemsSource = products, DisplayMemberPath = "Name", Padding = new Thickness(8), Margin = new Thickness(4) };
        var qtyBox = new TextBox { Text = "1", Padding = new Thickness(8), Margin = new Thickness(4) };
        var priceBox = new TextBox { Padding = new Thickness(8), Margin = new Thickness(4) };
        var addButton = new Button { Content = "إضافة صنف", Padding = new Thickness(12), Margin = new Thickness(4) };
        var removeButton = new Button { Content = "حذف السطر", Padding = new Thickness(12), Margin = new Thickness(4) };
        AddLabeled(addGrid, "الصنف", productBox, 0); AddLabeled(addGrid, "الكمية", qtyBox, 1); AddLabeled(addGrid, "السعر", priceBox, 2);
        System.Windows.Controls.Grid.SetColumn(addButton, 3); addGrid.Children.Add(addButton); System.Windows.Controls.Grid.SetColumn(removeButton, 4); addGrid.Children.Add(removeButton);
        System.Windows.Controls.Grid.SetRow(addGrid, 1); root.Children.Add(addGrid);

        var linesGrid = new DataGrid { AutoGenerateColumns = false, CanUserAddRows = false, IsReadOnly = true, Margin = new Thickness(0, 0, 0, 12) };
        linesGrid.Columns.Add(new DataGridTextColumn { Header = "الصنف", Binding = new System.Windows.Data.Binding("ProductName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        linesGrid.Columns.Add(new DataGridTextColumn { Header = "الكمية", Binding = new System.Windows.Data.Binding("Quantity") { StringFormat = "N2" }, Width = 120 });
        linesGrid.Columns.Add(new DataGridTextColumn { Header = "السعر", Binding = new System.Windows.Data.Binding("UnitPrice") { StringFormat = "N2" }, Width = 130 });
        linesGrid.Columns.Add(new DataGridTextColumn { Header = "الإجمالي", Binding = new System.Windows.Data.Binding("Total") { StringFormat = "N2" }, Width = 140 });
        linesGrid.ItemsSource = inv.Lines;
        System.Windows.Controls.Grid.SetRow(linesGrid, 2); root.Children.Add(linesGrid);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) }); footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        var notes = new TextBox { AcceptsReturn = true, Height = 70, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(8), Margin = new Thickness(4) };
        AddLabeled(footer, "ملاحظات", notes, 0);
        var total = new TextBlock { FontSize = 23, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Text = "الإجمالي: 0.00" }; System.Windows.Controls.Grid.SetColumn(total, 1); footer.Children.Add(total);
        var save = new Button { Content = _baseType == InvoiceType.Quotation ? "حفظ عرض السعر" : "حفظ العملية", Padding = new Thickness(20,12,20,12), Margin = new Thickness(4) }; System.Windows.Controls.Grid.SetColumn(save, 2); footer.Children.Add(save);
        System.Windows.Controls.Grid.SetRow(footer, 3); root.Children.Add(footer);

        void Recalc()
        {
            inv.Discount = ParseDecimal(discountBox.Text); inv.FeesAmount = ParseDecimal(feesBox.Text); inv.PaidAmount = ParseDecimal(paidBox.Text); inv.PaymentMethod = paymentBox.SelectedItem?.ToString() ?? PaymentMethods.Cash; inv.Notes = notes.Text;
            total.Text = $"الإجمالي: {inv.Total:N2} | المتبقي: {inv.Remaining:N2}";
            if (inv.PaymentMethod == PaymentMethods.Deferred) { paidBox.Text = "0"; paidBox.IsEnabled = false; } else paidBox.IsEnabled = true;
        }

        productBox.SelectionChanged += (_, _) => { if (productBox.SelectedItem is Product p) priceBox.Text = ((inv.Type is InvoiceType.Purchase or InvoiceType.PurchaseReturn) ? p.PurchasePrice : p.SalePrice).ToString("0.##"); };
        typeBox.SelectionChanged += (_, _) => { inv.Type = (InvoiceType)typeBox.SelectedItem!; inv.Number = NewNumber(inv.Type); if (productBox.SelectedItem is Product p) priceBox.Text = ((inv.Type is InvoiceType.Purchase or InvoiceType.PurchaseReturn) ? p.PurchasePrice : p.SalePrice).ToString("0.##"); if (inv.IsQuotation) { paymentBox.SelectedItem = PaymentMethods.Deferred; paidBox.Text = "0"; } Recalc(); };
        paymentBox.SelectionChanged += (_, _) => Recalc(); discountBox.TextChanged += (_, _) => Recalc(); feesBox.TextChanged += (_, _) => Recalc(); paidBox.TextChanged += (_, _) => Recalc();
        addButton.Click += (_, _) =>
        {
            if (productBox.SelectedItem is not Product p) { MessageBox.Show("اختر صنفًا."); return; }
            var qty = ParseDecimal(qtyBox.Text); var price = ParseDecimal(priceBox.Text);
            if (qty <= 0 || price < 0) { MessageBox.Show("الكمية والسعر غير صالحين."); return; }
            inv.Lines.Add(new InvoiceLine { ProductId = p.Id, ProductName = p.Name, Quantity = qty, UnitPrice = price, UnitCost = p.PurchasePrice });
            linesGrid.Items.Refresh(); Recalc();
        };
        removeButton.Click += (_, _) => { if (linesGrid.SelectedItem is InvoiceLine line) { inv.Lines.Remove(line); linesGrid.Items.Refresh(); Recalc(); } };
        save.Click += (_, _) =>
        {
            Recalc();
            if (inv.Lines.Count == 0) { MessageBox.Show("أضف صنفًا واحدًا على الأقل."); return; }
            if (partyBox.SelectedItem is Party party) { inv.PartyId = party.Id; inv.PartyName = party.Name; }
            else { inv.PartyId = null; inv.PartyName = "نقدي"; }
            if (inv.IsQuotation) inv.PaidAmount = 0;
            if (inv.PaidAmount < 0 || inv.PaidAmount > inv.Total) { MessageBox.Show("قيمة المدفوع يجب أن تكون بين صفر وإجمالي الفاتورة."); return; }
            if (inv.PartyId == null && inv.PaymentMethod == PaymentMethods.Deferred) { MessageBox.Show("الفاتورة الآجلة تحتاج إلى اختيار عميل/مورد."); return; }
            try { _repo.SaveInvoice(inv); w.Close(); Load(SearchBox.Text); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "تعذر حفظ الفاتورة", MessageBoxButton.OK, MessageBoxImage.Error); }
        };

        w.Content = root;
        w.ShowDialog();
    }

    private static string WindowTitle(InvoiceType type) => type switch
    {
        InvoiceType.Sale => "إنشاء فاتورة مبيعات",
        InvoiceType.Purchase => "إنشاء فاتورة مشتريات",
        InvoiceType.SaleReturn => "إنشاء مرتجع مبيعات",
        InvoiceType.PurchaseReturn => "إنشاء مرتجع مشتريات",
        InvoiceType.Quotation => "إنشاء عرض سعر",
        _ => "عملية جديدة"
    };

    private string NewNumber(InvoiceType type)
    {
        var prefix = type switch { InvoiceType.Sale => "S", InvoiceType.Purchase => "P", InvoiceType.SaleReturn => "RS", InvoiceType.PurchaseReturn => "RP", _ => "QU" };
        var count = _repo.Invoices.Count(x => x.Type == type) + 1;
        return $"{prefix}-{DateTime.Now:yyyyMMdd}-{count:0000}";
    }

    private static decimal ParseDecimal(string? value) => decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out var x) ? x : 0;

    private static void AddLabeled(Grid parent, string label, FrameworkElement control, int column)
    {
        var panel = new StackPanel { Margin = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
        panel.Children.Add(control);
        System.Windows.Controls.Grid.SetColumn(panel, column); parent.Children.Add(panel);
    }
}
