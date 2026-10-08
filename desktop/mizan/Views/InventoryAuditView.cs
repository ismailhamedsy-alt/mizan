using System.Windows;
using System.Windows.Controls;
using MizanDesktop.Services;

namespace MizanDesktop.Views;

public sealed class InventoryAuditView : UserControl
{
    private readonly AppRepository _repo;
    private readonly DataGrid _grid = new() { AutoGenerateColumns=false, IsReadOnly=false, Margin=new Thickness(0,15,0,10) };
    private readonly TextBox _notes = new() { Height=70, TextWrapping=TextWrapping.Wrap };
    public InventoryAuditView(AppRepository repo)
    {
        _repo=repo; FlowDirection=FlowDirection.RightToLeft;
        var root=new StackPanel { Margin=new Thickness(20) };
        var title=new TextBlock { Text="الجرد والتسوية الفعلية", FontSize=24, FontWeight=FontWeights.Bold };
        root.Children.Add(title);
        var top=new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(0,12,0,0) };
        var load=new Button { Content="تحميل الأصناف", Padding=new Thickness(14), Margin=new Thickness(0,0,8,0) }; load.Click+=(_,_)=>Load();
        var post=new Button { Content="اعتماد الجرد والتسوية", Padding=new Thickness(14) }; post.Click+=Post;
        top.Children.Add(load); top.Children.Add(post); root.Children.Add(top);
        _grid.Columns.Add(new DataGridTextColumn{Header="الصنف",Binding=new System.Windows.Data.Binding("Name"),IsReadOnly=true});
        _grid.Columns.Add(new DataGridTextColumn{Header="الرصيد النظامي",Binding=new System.Windows.Data.Binding("SystemQty"),IsReadOnly=true});
        _grid.Columns.Add(new DataGridTextColumn{Header="الكمية الفعلية",Binding=new System.Windows.Data.Binding("CountedQty")});
        _grid.Columns.Add(new DataGridTextColumn{Header="الفرق",Binding=new System.Windows.Data.Binding("Difference"),IsReadOnly=true});
        _grid.Columns.Add(new DataGridTextColumn{Header="تكلفة الوحدة",Binding=new System.Windows.Data.Binding("Cost"),IsReadOnly=true});
        _grid.Columns.Add(new DataGridTextColumn{Header="سبب التسوية",Binding=new System.Windows.Data.Binding("Reason")});
        root.Children.Add(_grid);
        root.Children.Add(new TextBlock{Text="ملاحظات الجرد",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,5,0,5)}); root.Children.Add(_notes);
        Content=root; Load();
    }
    private void Load(){_repo.LoadAll(); _grid.ItemsSource=_repo.Products.Select(p=>new Row(p.Id,p.Name,p.Quantity,p.Quantity,p.PurchasePrice,"")).ToList();}
    private void Post(object? sender,RoutedEventArgs e)
    {
        try { var rows=_grid.Items.Cast<Row>().ToList(); var lines=rows.Select(r=>(r.Id,r.CountedQty,r.Reason??"")).ToList(); var number=_repo.CreateInventoryAudit(lines,_notes.Text); MessageBox.Show($"تم اعتماد الجرد بنجاح: {number}","الجرد"); Load(); }
        catch(Exception ex){MessageBox.Show(ex.Message,"تعذر اعتماد الجرد",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    private sealed class Row
    { public Guid Id{get;} public string Name{get;} public decimal SystemQty{get;} public decimal CountedQty{get;set;} public decimal Cost{get;} public string Reason{get;set;} public decimal Difference=>CountedQty-SystemQty; public Row(Guid id,string name,decimal systemQty,decimal counted,decimal cost,string reason){Id=id;Name=name;SystemQty=systemQty;CountedQty=counted;Cost=cost;Reason=reason;} }
}
