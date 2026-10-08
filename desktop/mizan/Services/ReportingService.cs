using System.Text;
using System.IO.Compression;
using System.Xml.Linq;
using MizanDesktop.Models;

namespace MizanDesktop.Services;

public sealed class ReportingService
{
    private readonly AppRepository _repo;
    public ReportingService(AppRepository repo) => _repo = repo;

    public DateRangeReport Build(DateTime from, DateTime to)
    {
        _repo.LoadAll();
        var end = to.Date.AddDays(1);
        var invoices = _repo.Invoices.Where(i => i.Date >= from.Date && i.Date < end).ToList();
        var sales = invoices.Where(i => i.Type == InvoiceType.Sale).Sum(i => i.Total);
        var purchases = invoices.Where(i => i.Type == InvoiceType.Purchase).Sum(i => i.Total);
        var salesReturns = invoices.Where(i => i.Type == InvoiceType.SaleReturn).Sum(i => i.Total);
        var purchaseReturns = invoices.Where(i => i.Type == InvoiceType.PurchaseReturn).Sum(i => i.Total);
        var cashReceipts = _repo.Payments.Where(p => p.Date >= from.Date && p.Date < end && p.Type == PaymentType.CustomerReceipt).Sum(p => p.Amount);
        var cashPayments = _repo.Payments.Where(p => p.Date >= from.Date && p.Date < end && p.Type == PaymentType.SupplierPayment).Sum(p => p.Amount);
        var income = _repo.Payments.Where(p => p.Date >= from.Date && p.Date < end && p.Type == PaymentType.CashIncome).Sum(p => p.Amount);
        var expenses = _repo.Payments.Where(p => p.Date >= from.Date && p.Date < end && p.Type == PaymentType.CashExpense).Sum(p => p.Amount);

        var grossSales = sales - salesReturns;
        var netPurchases = purchases - purchaseReturns;
        var saleCogs = invoices.Where(i => i.Type == InvoiceType.Sale)
            .SelectMany(i => i.Lines)
            .Sum(l => l.Quantity * l.UnitCost);
        var returnCogs = invoices.Where(i => i.Type == InvoiceType.SaleReturn)
            .SelectMany(i => i.Lines)
            .Sum(l => l.Quantity * l.UnitCost);
        var cogs = saleCogs - returnCogs;
        var grossProfit = grossSales - cogs;

        var trial = _repo.JournalEntries
            .Where(e => e.Date >= from.Date && e.Date < end)
            .SelectMany(e => e.Lines)
            .GroupBy(l => l.Account)
            .Select(g => new AccountBalance(g.Key, g.Sum(x => x.Debit), g.Sum(x => x.Credit)))
            .OrderBy(x => x.Account).ToList();

        return new DateRangeReport(from.Date, to.Date, invoices, trial, grossSales, netPurchases, cogs, grossProfit,
            cashReceipts, cashPayments, income, expenses);
    }

    public static void ToXlsx(DateRangeReport report, string path) => DateRangeReport.ToXlsx(report, path);

    public static string ToCsv(DateRangeReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("تقرير الميزان");
        sb.AppendLine($"من,{r.From:yyyy-MM-dd},إلى,{r.To:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("البند,القيمة");
        sb.AppendLine($"صافي المبيعات,{r.NetSales:0.00}");
        sb.AppendLine($"صافي المشتريات,{r.NetPurchases:0.00}");
        sb.AppendLine($"تكلفة البضاعة المباعة,{r.CostOfGoodsSold:0.00}");
        sb.AppendLine($"مجمل الربح,{r.GrossProfit:0.00}");
        sb.AppendLine($"قبض العملاء,{r.CustomerReceipts:0.00}");
        sb.AppendLine($"سداد الموردين,{r.SupplierPayments:0.00}");
        sb.AppendLine($"إيرادات نقدية,{r.CashIncome:0.00}");
        sb.AppendLine($"مصروفات نقدية,{r.CashExpenses:0.00}");
        sb.AppendLine();
        sb.AppendLine("الحساب,مدين,دائن,الرصيد");
        foreach (var a in r.TrialBalance) sb.AppendLine($"{a.Account},{a.Debit:0.00},{a.Credit:0.00},{a.Net:0.00}");
        return sb.ToString();
    }
}

public sealed record DateRangeReport(DateTime From, DateTime To, List<Invoice> Invoices,
    List<AccountBalance> TrialBalance, decimal NetSales, decimal NetPurchases, decimal CostOfGoodsSold,
    decimal GrossProfit, decimal CustomerReceipts, decimal SupplierPayments, decimal CashIncome, decimal CashExpenses)
{
    public decimal TrialDebit => TrialBalance.Sum(x => x.Debit);
    public decimal TrialCredit => TrialBalance.Sum(x => x.Credit);
    public bool IsBalanced => Math.Abs(TrialDebit - TrialCredit) < 0.01m;
    public decimal NetCashMovements => CustomerReceipts - SupplierPayments + CashIncome - CashExpenses;
    /// <summary>Creates a real OOXML .xlsx workbook without requiring Office/Excel at runtime.</summary>
    public static void ToXlsx(DateRangeReport r, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(path)) File.Delete(path);

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Entry(string name, string content)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(content);
        }

        static string Esc(string? value) => System.Security.SecurityElement.Escape(value ?? "") ?? "";
        static string Cell(string value) => $"<c t=\"inlineStr\"><is><t>{Esc(value)}</t></is></c>";
        static string Num(decimal value) => $"<c><v>{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>";
        static string Row(params string[] cells) => $"<row>{string.Concat(cells)}</row>";

        var rows = new StringBuilder();
        rows.Append(Row(Cell("تقرير الميزان"), Cell(""), Cell("")));
        rows.Append(Row(Cell($"من {r.From:yyyy-MM-dd} إلى {r.To:yyyy-MM-dd}")));
        rows.Append(Row(Cell("البند"), Cell("القيمة")));
        rows.Append(Row(Cell("صافي المبيعات"), Num(r.NetSales)));
        rows.Append(Row(Cell("صافي المشتريات"), Num(r.NetPurchases)));
        rows.Append(Row(Cell("تكلفة البضاعة المباعة"), Num(r.CostOfGoodsSold)));
        rows.Append(Row(Cell("مجمل الربح"), Num(r.GrossProfit)));
        rows.Append(Row(Cell("قبض العملاء"), Num(r.CustomerReceipts)));
        rows.Append(Row(Cell("سداد الموردين"), Num(r.SupplierPayments)));
        rows.Append(Row(Cell("إيرادات نقدية"), Num(r.CashIncome)));
        rows.Append(Row(Cell("مصروفات نقدية"), Num(r.CashExpenses)));
        rows.Append(Row(Cell("")));
        rows.Append(Row(Cell("الحساب"), Cell("مدين"), Cell("دائن"), Cell("الصافي")));
        foreach (var a in r.TrialBalance)
            rows.Append(Row(Cell(a.Account), Num(a.Debit), Num(a.Credit), Num(a.Net)));

        Entry("[Content_Types].xml", """
<?xml version="1.0" encoding="UTF-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
<Default Extension="xml" ContentType="application/xml"/>
<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
</Types>
""");
        Entry("_rels/.rels", """
<?xml version="1.0" encoding="UTF-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
</Relationships>
""");
        Entry("xl/_rels/workbook.xml.rels", """
<?xml version="1.0" encoding="UTF-8"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
</Relationships>
""");
        Entry("xl/workbook.xml", """
<?xml version="1.0" encoding="UTF-8"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
<sheets><sheet name="تقرير الميزان" sheetId="1" r:id="rId1"/></sheets>
</workbook>
""");
        Entry("xl/worksheets/sheet1.xml", $"""
<?xml version="1.0" encoding="UTF-8"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
<sheetData>{rows}</sheetData>
</worksheet>
""");
    }

}
