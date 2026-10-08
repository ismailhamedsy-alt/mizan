using System.Windows;
using System.Windows.Controls;
using MizanDesktop.Models;
using MizanDesktop.Services;

namespace MizanDesktop.Views;
public partial class PaymentsView : UserControl
{
    private readonly AppRepository _repo;
    public PaymentsView(AppRepository repo){InitializeComponent();_repo=repo;Load();}
    void Load(){PartyBox.ItemsSource=_repo.Parties;Grid.ItemsSource=null;Grid.ItemsSource=_repo.Payments;}
    void Save_Click(object sender,RoutedEventArgs e){
        if(!decimal.TryParse(AmountBox.Text,out var amount)||amount<=0){MessageBox.Show("أدخل مبلغًا صحيحًا أكبر من صفر.");return;}
        var type=(PaymentType)TypeBox.SelectedIndex;
        var party=PartyBox.SelectedItem as Party;
        if((type==PaymentType.CustomerReceipt||type==PaymentType.SupplierPayment)&&party is null){MessageBox.Show("اختر العميل أو المورد.");return;}
        if(type==PaymentType.CustomerReceipt&&party?.Type!=PartyType.Customer){MessageBox.Show("يجب اختيار عميل لسند القبض.");return;}
        if(type==PaymentType.SupplierPayment&&party?.Type!=PartyType.Supplier){MessageBox.Show("يجب اختيار مورد لسند الصرف.");return;}
        _repo.SavePayment(new Payment{Type=type,PartyId=party?.Id,PartyName=party?.Name??"",Amount=amount,Description=DescriptionBox.Text,Date=DateTime.Now});
        AmountBox.Clear();DescriptionBox.Clear();Load();MessageBox.Show("تم حفظ السند.");
    }
}
