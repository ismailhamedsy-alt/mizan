using System.Windows; using System.Windows.Controls; using MizanDesktop.Services;
namespace MizanDesktop.Views;
public partial class StatementView:UserControl
{
 readonly StatementService service; public StatementView(AppRepository repo){InitializeComponent();service=new(repo);PartyBox.ItemsSource=repo.Parties;From.SelectedDate=DateTime.Today.AddMonths(-1);To.SelectedDate=DateTime.Today;}
 void Load_Click(object s,RoutedEventArgs e){if(PartyBox.SelectedItem is Models.Party p)Grid.ItemsSource=service.Party(p.Id,From.SelectedDate??DateTime.Today.AddMonths(-1),To.SelectedDate??DateTime.Today);}
 void Print_Click(object s,RoutedEventArgs e){var rows=Grid.ItemsSource as System.Collections.IEnumerable; if(rows==null)return; var text=string.Join("\n",rows.Cast<StatementRow>().Select(x=>$"{x.Date:yyyy-MM-dd} | {x.Kind} | {x.Reference} | {x.Debit:N2} | {x.Credit:N2} | {x.Balance:N2}"));PrintService.PrintText("كشف حساب",text);}
}
