using System.Windows;
using System.Windows.Controls;
using MizanDesktop.Services;

namespace MizanDesktop.Views;

public sealed class InventoryAuditView : UserControl
{
    private readonly AppRepository _repo;
    private readonly DataGrid _grid = new()
    {
        AutoGenerateColumns = false,
        IsReadOnly = false,
        Margin = new Thickness(0, 12, 0, 10),
        CanUserAddRows = false,
        MinHeight = 240
    };
    private readonly TextBox _notes = new()
    {
        MinHeight = 54,
        MaxHeight = 90,
        TextWrapping = TextWrapping.Wrap,
        AcceptsReturn = true,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    public InventoryAuditView(AppRepository repo)
    {
        _repo = repo;
        FlowDirection = FlowDirection.RightToLeft;

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock { Text = "الجرد والتسوية الفعلية", FontSize = 24, FontWeight = FontWeights.Bold };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var load = new Button { Content = "تحميل الأصناف" };
        load.Click += (_, _) => Load();
        var post = new Button { Content = "اعتماد الجرد والتسوية" };
        post.Click += Post;
        actions.Children.Add(load);
        actions.Children.Add(post);
        Grid.SetRow(actions, 1);
        root.Children.Add(actions);

        _grid.Columns.Add(new DataGridTextColumn { Header = "الصنف", Binding = new System.Windows.Data.Binding("Name"), IsReadOnly = true, Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        _grid.Columns.Add(new DataGridTextColumn { Header = "الرصيد النظامي", Binding = new System.Windows.Data.Binding("SystemQty"), IsReadOnly = true, Width = 130 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "الكمية الفعلية", Binding = new System.Windows.Data.Binding("CountedQty"), Width = 130 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "الفرق", Binding = new System.Windows.Data.Binding("Difference"), IsReadOnly = true, Width = 100 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "تكلفة الوحدة", Binding = new System.Windows.Data.Binding("Cost"), IsReadOnly = true, Width = 120 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "سبب التسوية", Binding = new System.Windows.Data.Binding("Reason"), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
        Grid.SetRow(_grid, 2);
        root.Children.Add(_grid);

        var notesLabel = new TextBlock { Text = "ملاحظات الجرد", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 3) };
        Grid.SetRow(notesLabel, 3);
        root.Children.Add(notesLabel);
        Grid.SetRow(_notes, 4);
        root.Children.Add(_notes);

        Content = root;
        Load();
    }

    private void Load()
    {
        _repo.LoadAll();
        _grid.ItemsSource = _repo.Products.Select(p => new Row(p.Id, p.Name, p.Quantity, p.Quantity, p.PurchasePrice, "")).ToList();
    }

    private void Post(object? sender, RoutedEventArgs e)
    {
        try
        {
            _grid.CommitEdit(DataGridEditingUnit.Cell, true);
            _grid.CommitEdit(DataGridEditingUnit.Row, true);
            var rows = _grid.Items.OfType<Row>().ToList();
            if (rows.Count == 0) throw new InvalidOperationException("لا توجد أصناف لاعتماد الجرد.");
            var lines = rows.Select(r => (r.Id, r.CountedQty, r.Reason ?? "")).ToList();
            var number = _repo.CreateInventoryAudit(lines, _notes.Text);
            MessageBox.Show($"تم اعتماد الجرد بنجاح: {number}", "الجرد", MessageBoxButton.OK, MessageBoxImage.Information);
            Load();
            _notes.Clear();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "تعذر اعتماد الجرد", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed class Row
    {
        public Guid Id { get; }
        public string Name { get; }
        public decimal SystemQty { get; }
        public decimal CountedQty { get; set; }
        public decimal Cost { get; }
        public string Reason { get; set; }
        public decimal Difference => CountedQty - SystemQty;
        public Row(Guid id, string name, decimal systemQty, decimal counted, decimal cost, string reason)
        { Id = id; Name = name; SystemQty = systemQty; CountedQty = counted; Cost = cost; Reason = reason; }
    }
}
