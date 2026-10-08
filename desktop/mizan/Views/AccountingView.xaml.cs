using System.Windows; using System.Windows.Controls; using MizanDesktop.Services;
namespace MizanDesktop.Views;
public partial class AccountingView : UserControl {
 readonly AppRepository repo; public AccountingView(AppRepository r){InitializeComponent();repo=r;Refresh();}
 void Refresh(){Grid.ItemsSource=repo.JournalEntries.SelectMany(e=>e.Lines.Select(l=>new Row{Date=e.Date,Description=e.Description,Reference=e.Reference,Debit=l.Debit,Credit=l.Credit})).OrderByDescending(x=>x.Date).ToList();}
 void Refresh_Click(object s,RoutedEventArgs e)=>Refresh(); sealed class Row {public DateTime Date{get;set;} public string Description{get;set;}=""; public string Reference{get;set;}=""; public decimal Debit{get;set;} public decimal Credit{get;set;}}
}
