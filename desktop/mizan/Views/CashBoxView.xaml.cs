using System.Windows;
using System.Windows.Controls;
using MizanDesktop.Services;
namespace MizanDesktop.Views;
public sealed class CashFundRow { public string Id{get;set;}=""; public string Name{get;set;}=""; public string Currency{get;set;}=""; public decimal Balance{get;set;} }
public partial class CashBoxView : UserControl
{
 private readonly AppRepository _repo; public CashBoxView(AppRepository repo){InitializeComponent();_repo=repo;Refresh();}
 private void Refresh(){Funds.ItemsSource=_repo.CashFunds().Select(x=>new CashFundRow{Id=x.Id,Name=x.Name,Currency=x.Currency,Balance=x.Balance}).ToList();Grid.ItemsSource=_repo.CashTransactions().Select(x=>new {x.Date,Fund=x.Fund,x.Type,x.Amount,x.Currency,x.Description}).ToList();}
 private void Add(string type){var funds=_repo.CashFunds().Select(x=>new CashFundRow{Id=x.Id,Name=x.Name,Currency=x.Currency,Balance=x.Balance}).ToList();if(funds.Count==0)return;var w=new Window{Title=type=="IN"?"سند قبض":"سند صرف",Width=600,Height=520,MinWidth=540,MinHeight=480,Owner=Window.GetWindow(this),WindowStartupLocation=WindowStartupLocation.CenterOwner,FlowDirection=FlowDirection.RightToLeft};var panel=new StackPanel{Margin=new Thickness(20)};var fund=new ComboBox{ItemsSource=funds,DisplayMemberPath="Name",SelectedIndex=0,Margin=new Thickness(0,5,0,10)};var amount=new TextBox{Text="0",Margin=new Thickness(0,5,0,10)};var desc=new TextBox{Text=type=="IN"?"إيراد نقدي":"مصروف نقدي",Margin=new Thickness(0,5,0,10)};panel.Children.Add(new TextBlock{Text="الصندوق"});panel.Children.Add(fund);panel.Children.Add(new TextBlock{Text="المبلغ"});panel.Children.Add(amount);panel.Children.Add(new TextBlock{Text="الوصف"});panel.Children.Add(desc);var save=new Button{Content="حفظ",Padding=new Thickness(15),Margin=new Thickness(0,15,0,0)};save.Click+=(_,_)=>{if(fund.SelectedItem is not CashFundRow f||!decimal.TryParse(amount.Text,out var a)||a<=0){MessageBox.Show("أدخل مبلغًا صحيحًا.");return;}try{_repo.AddCashTransaction(f.Id,type,a,f.Currency,desc.Text);w.Close();Refresh();}catch(Exception ex){MessageBox.Show(ex.Message);}};panel.Children.Add(save);w.Content=panel;w.ShowDialog();}
 private void In_Click(object s,RoutedEventArgs e)=>Add("IN"); private void Out_Click(object s,RoutedEventArgs e)=>Add("OUT"); private void Refresh_Click(object s,RoutedEventArgs e)=>Refresh();
}
