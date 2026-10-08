using MizanDesktop.Models;
namespace MizanDesktop.Services;
public sealed record PartyAging(string Name, string Type, decimal Balance, string Bucket);
public sealed class FinancialReportService {
    private readonly AppRepository repo;
    public FinancialReportService(AppRepository repo)=>this.repo=repo;
    public List<AccountBalance> TrialBalance(DateTime from, DateTime to){
        repo.LoadAll(); var map=new Dictionary<string,(decimal d,decimal c)>(StringComparer.OrdinalIgnoreCase);
        foreach(var e in repo.JournalEntries.Where(x=>x.Date.Date>=from.Date&&x.Date.Date<=to.Date)) foreach(var l in e.Lines){
            map.TryGetValue(l.Account,out var v); map[l.Account]=(v.d+l.Debit,v.c+l.Credit);
        }
        return map.OrderBy(x=>x.Key).Select(x=>new AccountBalance(x.Key,x.Value.d,x.Value.c)).ToList();
    }
    public (decimal Sales,decimal SalesReturns,decimal Purchases,decimal PurchaseReturns,decimal Cogs,decimal GrossProfit,decimal Expenses,decimal NetProfit) ProfitLoss(DateTime from,DateTime to){
        repo.LoadAll(); var q=repo.Invoices.Where(i=>i.Date.Date>=from.Date&&i.Date.Date<=to.Date).ToList();
        var sales=q.Where(i=>i.Type==InvoiceType.Sale).Sum(i=>i.Total); var sr=q.Where(i=>i.Type==InvoiceType.SaleReturn).Sum(i=>i.Total);
        var pur=q.Where(i=>i.Type==InvoiceType.Purchase).Sum(i=>i.Total); var pr=q.Where(i=>i.Type==InvoiceType.PurchaseReturn).Sum(i=>i.Total);
        var cogs=q.Where(i=>i.Type==InvoiceType.Sale).Sum(i=>i.CostOfGoodsSold)-q.Where(i=>i.Type==InvoiceType.SaleReturn).Sum(i=>i.CostOfGoodsSold);
        var gross=(sales-sr)-cogs; var expenses=q.Where(i=>i.Type==InvoiceType.Purchase).Sum(i=>i.FeesAmount)+q.Where(i=>i.Type==InvoiceType.Sale).Sum(i=>i.FeesAmount);
        return (sales,sr,pur,pr,cogs,gross,expenses,gross-expenses);
    }
    public List<PartyAging> Aging(DateTime asOf){
        repo.LoadAll(); return repo.Parties.Select(p=>{ var bucket=p.Balance==0?"مسدد":Math.Abs(p.Balance)<100000?"أقل من 100 ألف":"100 ألف فأكثر"; return new PartyAging(p.Name,p.Type==PartyType.Customer?"عميل":"مورد",p.Balance,bucket); }).Where(x=>Math.Abs(x.Balance)>0.0001m).OrderByDescending(x=>Math.Abs(x.Balance)).ToList();
    }
}
