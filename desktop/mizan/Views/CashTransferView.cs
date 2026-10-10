using System.Windows;
using System.Windows.Controls;
using MizanDesktop.Services;

namespace MizanDesktop.Views;

public sealed class CashTransferView : UserControl
{
    readonly AppRepository _repo; readonly ComboBox _from=new(); readonly ComboBox _to=new(); readonly TextBox _amount=new(); readonly TextBox _rate=new(){Text="1"}; readonly TextBox _notes=new(); readonly TextBlock _preview=new();
    public CashTransferView(AppRepository repo){_repo=repo;FlowDirection=FlowDirection.RightToLeft;Build();}
    void Build(){var root=new StackPanel{Margin=new Thickness(25)};root.Children.Add(new TextBlock{Text="تحويل وتصريف بين الصناديق",FontSize=24,FontWeight=FontWeights.Bold});
        Add(root,"الصندوق المصدر",_from);Add(root,"الصندوق المستلم",_to);Add(root,"المبلغ المصدر",_amount);Add(root,"سعر الصرف",_rate);Add(root,"ملاحظات",_notes);
        var b=new Button{Content="تنفيذ التحويل",Padding=new Thickness(15),Margin=new Thickness(0,15,0,8)};b.Click+=DoTransfer;root.Children.Add(b);root.Children.Add(_preview);Content=new ScrollViewer { Content=root, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };LoadFunds();}
    void Add(Panel p,string label,Control c){p.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,10,0,4),FontWeight=FontWeights.SemiBold});if(c is TextBox t)t.Height=32;if(c is ComboBox cb)cb.Height=32;p.Children.Add(c);}
    void LoadFunds(){var funds=_repo.CashFunds().Select(x=>new FundItem(x.Id,x.Name,x.Currency,x.Balance)).ToList();_from.ItemsSource=funds;_to.ItemsSource=funds;_from.DisplayMemberPath="Name";_to.DisplayMemberPath="Name";if(funds.Count>0)_from.SelectedIndex=0;if(funds.Count>1)_to.SelectedIndex=1;}
    sealed record FundItem(string Id,string Name,string Currency,decimal Balance);
    void DoTransfer(object? s,RoutedEventArgs e){try{if(_from.SelectedItem is not FundItem f||_to.SelectedItem is not FundItem t)throw new InvalidOperationException("اختر الصندوقين.");if(!decimal.TryParse(_amount.Text,out var a)||!decimal.TryParse(_rate.Text,out var r))throw new InvalidOperationException("أدخل مبلغًا وسعر صرف صحيحين.");_repo.TransferCash(f.Id,t.Id,a,r,_notes.Text);MessageBox.Show("تم تنفيذ التحويل بنجاح.");LoadFunds();}catch(Exception ex){MessageBox.Show(ex.Message,"تحويل الصندوق",MessageBoxButton.OK,MessageBoxImage.Error);}}
}
