using System.Windows;
using System.Windows.Controls;
using MizanDesktop.Services;

namespace MizanDesktop.Views;

public sealed class ChartOfAccountsView : UserControl
{
    public ChartOfAccountsView(AppRepository repo)
    {
        FlowDirection = FlowDirection.RightToLeft;
        var root = new DockPanel { Margin = new Thickness(10) };
        var title = new TextBlock { Text = "دليل الحسابات", FontSize = 22, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,12) };
        DockPanel.SetDock(title, Dock.Top); root.Children.Add(title);
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true };
        grid.Columns.Add(new DataGridTextColumn { Header = "الكود", Binding = new System.Windows.Data.Binding("Code") });
        grid.Columns.Add(new DataGridTextColumn { Header = "اسم الحساب", Binding = new System.Windows.Data.Binding("Name") });
        grid.Columns.Add(new DataGridTextColumn { Header = "النوع", Binding = new System.Windows.Data.Binding("Type") });
        grid.Columns.Add(new DataGridTextColumn { Header = "العملة", Binding = new System.Windows.Data.Binding("Currency") });
        grid.ItemsSource = repo.Accounts(); root.Children.Add(grid); Content = root;
    }
}
