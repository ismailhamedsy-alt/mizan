using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.Win32;
using MizanDesktop.Services;

namespace MizanDesktop.Views;

public partial class ReportsView : UserControl
{
    private readonly ReportingService _reporting;
    private DateRangeReport? _report;
    public ReportsView(AppRepository repo)
    {
        InitializeComponent(); _reporting = new ReportingService(repo);
        FromPicker.SelectedDate = DateTime.Today.AddDays(-29); ToPicker.SelectedDate = DateTime.Today; Refresh();
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
    private void Refresh()
    {
        var from = FromPicker.SelectedDate ?? DateTime.Today;
        var to = ToPicker.SelectedDate ?? from;
        if (to < from) { MessageBox.Show("تاريخ النهاية يجب أن يكون بعد تاريخ البداية."); return; }
        _report = _reporting.Build(from, to);
        Sales.Text = _report.NetSales.ToString("N2"); Profit.Text = _report.GrossProfit.ToString("N2"); Purchases.Text = _report.NetPurchases.ToString("N2");
        BalanceStatus.Text = _report.IsBalanced ? "متوازن ✓" : $"غير متوازن ({(_report.TrialDebit-_report.TrialCredit):N2})";
        ProfitGrid.ItemsSource = new[]{new {Item="صافي المبيعات",Value=_report.NetSales},new {Item="تكلفة البضاعة المباعة",Value=-_report.CostOfGoodsSold},new {Item="مجمل الربح",Value=_report.GrossProfit}};
        TrialGrid.ItemsSource = _report.TrialBalance; InvoiceGrid.ItemsSource = _report.Invoices;
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null) return;
        var d = new SaveFileDialog { Filter="CSV UTF-8 (*.csv)|*.csv", FileName=$"MizanReport-{_report.From:yyyyMMdd}-{_report.To:yyyyMMdd}.csv" };
        if (d.ShowDialog()==true) { File.WriteAllText(d.FileName, ReportingService.ToCsv(_report), new System.Text.UTF8Encoding(true)); MessageBox.Show("تم تصدير التقرير."); }
    }
    private void ExportXlsx_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null) return;
        var d = new SaveFileDialog { Filter="Excel Workbook (*.xlsx)|*.xlsx", FileName=$"MizanReport-{_report.From:yyyyMMdd}-{_report.To:yyyyMMdd}.xlsx" };
        if (d.ShowDialog()==true)
        {
            try
            {
                ReportingService.ToXlsx(_report, d.FileName);
                MessageBox.Show("تم إنشاء ملف Excel بنجاح.", "الميزان");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "تعذر تصدير Excel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null) return;
        var dlg = new System.Windows.Controls.PrintDialog(); if (dlg.ShowDialog()!=true) return;
        var doc = new FlowDocument { PagePadding = new Thickness(40), FontFamily = new System.Windows.Media.FontFamily("Segoe UI") };
        doc.Blocks.Add(new Paragraph(new Run("الميزان للمحاسبة - التقرير المالي")){TextAlignment=TextAlignment.Center, FontSize=20, FontWeight=FontWeights.Bold});
        doc.Blocks.Add(new Paragraph(new Run($"الفترة: {_report.From:yyyy-MM-dd} إلى {_report.To:yyyy-MM-dd}")));
        foreach (var x in new[]{("صافي المبيعات",_report.NetSales),("صافي المشتريات",_report.NetPurchases),("تكلفة البضاعة المباعة",_report.CostOfGoodsSold),("مجمل الربح",_report.GrossProfit),("قبض العملاء",_report.CustomerReceipts),("سداد الموردين",_report.SupplierPayments),("إيرادات نقدية",_report.CashIncome),("مصروفات نقدية",_report.CashExpenses)}) doc.Blocks.Add(new Paragraph(new Run($"{x.Item1}: {x.Item2:N2}")));
        dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator,"Mizan Report");
    }
}
