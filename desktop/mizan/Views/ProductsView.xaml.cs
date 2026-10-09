using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using MizanDesktop.Models;

namespace MizanDesktop.Views;

public partial class ProductsView : UserControl
{
    private readonly Services.AppRepository _repo;
    private readonly ObservableCollection<Product> _items = [];
    public ProductsView(Services.AppRepository repo) { InitializeComponent(); _repo = repo; LoadItems(); }
    private void LoadItems(string? filter = null)
    {
        _items.Clear();
        foreach (var p in _repo.Products.Where(p => string.IsNullOrWhiteSpace(filter) || p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || p.Barcode.Contains(filter, StringComparison.OrdinalIgnoreCase) || p.Code.Contains(filter, StringComparison.OrdinalIgnoreCase))) _items.Add(p);
        Grid.ItemsSource = _items;
    }
    private void Refresh_Click(object s, RoutedEventArgs e) => LoadItems(SearchBox.Text);
    private void Search_TextChanged(object s, TextChangedEventArgs e) => LoadItems(SearchBox.Text);
    private void Add_Click(object s, RoutedEventArgs e) => EditProduct(null);
    private void Edit_Click(object s, RoutedEventArgs e) => EditProduct((s as Button)?.Tag as Product);
    private void Delete_Click(object s, RoutedEventArgs e)
    {
        if ((s as Button)?.Tag is not Product p) return;
        if (MessageBox.Show($"حذف المنتج «{p.Name}»؟", "تأكيد", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _repo.DeleteProduct(p.Id); LoadItems(SearchBox.Text);
    }
    private void EditProduct(Product? product)
    {
        var p = product ?? new Product();
        var dialog = new Window { Title = product is null ? "إضافة منتج" : "تعديل منتج", Width = 650, Height = 680, MinWidth = 580, MinHeight = 600, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner, FlowDirection = FlowDirection.RightToLeft, Background = System.Windows.Media.Brushes.White };
        var panel = new StackPanel { Margin = new Thickness(25) };
        var fields = new (string, Action<TextBox>)[] {
            ("اسم المنتج", t => p.Name=t.Text), ("الكود", t=>p.Code=t.Text), ("الباركود", t=>p.Barcode=t.Text), ("الوحدة", t=>p.Unit=t.Text),
            ("سعر الشراء", t=>p.PurchasePrice=Parse(t.Text)), ("سعر البيع", t=>p.SalePrice=Parse(t.Text)), ("الكمية", t=>p.Quantity=Parse(t.Text)), ("حد إعادة الطلب", t=>p.MinQuantity=Parse(t.Text))};
        foreach (var (label,set) in fields) { panel.Children.Add(new TextBlock { Text=label, Margin=new Thickness(0,8,0,4), FontWeight=FontWeights.SemiBold }); var t=new TextBox { Text=Value(p,label,p), Padding=new Thickness(8) }; t.Tag=set; panel.Children.Add(t); }
        var save=new Button { Content="حفظ", Padding=new Thickness(15,10,15,10), Margin=new Thickness(0,20,0,0) }; save.Click += (_,_) => { foreach(var tb in panel.Children.OfType<TextBox>()) ((Action<TextBox>)tb.Tag)(tb); if(string.IsNullOrWhiteSpace(p.Name)){MessageBox.Show("اسم المنتج مطلوب");return;} _repo.SaveProduct(p); dialog.Close(); LoadItems(SearchBox.Text); }; panel.Children.Add(save); dialog.Content=panel; dialog.ShowDialog();
    }
    private static decimal Parse(string s) => decimal.TryParse(s, out var v) ? v : 0;
    private static string Value(Product p,string label,Product _) => label switch { "اسم المنتج"=>p.Name,"الكود"=>p.Code,"الباركود"=>p.Barcode,"الوحدة"=>p.Unit,"سعر الشراء"=>p.PurchasePrice.ToString("0.##"),"سعر البيع"=>p.SalePrice.ToString("0.##"),"الكمية"=>p.Quantity.ToString("0.##"),"حد إعادة الطلب"=>p.MinQuantity.ToString("0.##"),_=>""};
}
