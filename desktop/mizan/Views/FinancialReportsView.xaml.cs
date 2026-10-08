using System.Windows; using System.Windows.Controls; using MizanDesktop.Services;
namespace MizanDesktop.Views;
public partial class FinancialReportsView : UserControl { readonly AppRepository repo; readonly FinancialReportService svc; public FinancialReportsView(AppRepository r){InitializeComponent();repo=r;svc=new(r);From.SelectedDate=DateTime.Today.AddMonths(-1);To.SelectedDate=DateTime.Today;}
DateTime F => From.SelectedDate?.Date ?? DateTime.Today.AddMonths(-1);
DateTime T => To.SelectedDate?.Date ?? DateTime.Today;
void Trial_Click(object s,RoutedEventArgs e){var x=svc.TrialBalance(F,T);Grid.ItemsSource=x;Info.Text=$"الحسابات: {x.Count}   |   إجمالي المدين: {x.Sum(a=>a.Debit):N2}   |   إجمالي الدائن: {x.Sum(a=>a.Credit):N2}";}
void PL_Click(object s,RoutedEventArgs e){var x=svc.ProfitLoss(F,T);Grid.ItemsSource=new[]{new{البند="المبيعات",المبلغ=x.Sales},new{البند="مرتجعات المبيعات",المبلغ=x.SalesReturns},new{البند="صافي المبيعات",المبلغ=x.Sales-x.SalesReturns},new{البند="تكلفة البضاعة المباعة",المبلغ=x.Cogs},new{البند="مجمل الربح",المبلغ=x.GrossProfit},new{البند="المصروفات/الأجور",المبلغ=x.Expenses},new{البند="صافي الربح",المبلغ=x.NetProfit}};Info.Text=$"صافي الربح للفترة: {x.NetProfit:N2}";}
void Aging_Click(object s,RoutedEventArgs e){var x=svc.Aging(T);Grid.ItemsSource=x;Info.Text=$"الأطراف غير المسددة: {x.Count}   |   الرصيد الصافي: {x.Sum(a=>a.Balance):N2}";}}
