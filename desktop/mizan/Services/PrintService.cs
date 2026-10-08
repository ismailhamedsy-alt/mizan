using System.Windows; using System.Windows.Controls; using System.Windows.Documents; using System.Windows.Media;
using MizanDesktop.Models;
namespace MizanDesktop.Services;
public static class PrintService
{
 public static void PrintInvoice(Invoice invoice)
 {
  var dlg=new PrintDialog(); if(dlg.ShowDialog()!=true)return;
  var doc=new FlowDocument{PagePadding=new Thickness(35),FontFamily=new FontFamily("Segoe UI"),FontSize=12,FlowDirection=FlowDirection.RightToLeft};
  doc.Blocks.Add(new Paragraph(new Run("الميزان للمحاسبة")){FontSize=24,FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Center});
  doc.Blocks.Add(new Paragraph(new Run($"فاتورة {invoice.Type} رقم {invoice.Number}\nالتاريخ: {invoice.Date:yyyy-MM-dd HH:mm}\nالعميل/المورد: {invoice.PartyName}")));
  var table=new Table(); table.Columns.Add(new TableColumn());table.Columns.Add(new TableColumn());table.Columns.Add(new TableColumn());table.Columns.Add(new TableColumn());
  var rg=new TableRowGroup();table.RowGroups.Add(rg); var h=new TableRow(); foreach(var s in new[]{"المنتج","الكمية","السعر","الإجمالي"})h.Cells.Add(new TableCell(new Paragraph(new Run(s))){FontWeight=FontWeights.Bold}); rg.Rows.Add(h);
  foreach(var l in invoice.Lines){var r=new TableRow(); foreach(var s in new[]{l.ProductName,l.Quantity.ToString("0.##"),l.UnitPrice.ToString("0.00"),l.Total.ToString("0.00")})r.Cells.Add(new TableCell(new Paragraph(new Run(s))));rg.Rows.Add(r);} doc.Blocks.Add(table);
  doc.Blocks.Add(new Paragraph(new Run($"الإجمالي: {invoice.Total:0.00}")){FontSize=18,FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Left});
  dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator,"فاتورة الميزان");
 }
 public static void PrintText(string title,string text){var dlg=new PrintDialog();if(dlg.ShowDialog()!=true)return;var doc=new FlowDocument{PagePadding=new Thickness(35),FontFamily=new FontFamily("Segoe UI"),FontSize=12,FlowDirection=FlowDirection.RightToLeft};doc.Blocks.Add(new Paragraph(new Run(title)){FontSize=22,FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Center});doc.Blocks.Add(new Paragraph(new Run(text)));dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator,title);}
}
