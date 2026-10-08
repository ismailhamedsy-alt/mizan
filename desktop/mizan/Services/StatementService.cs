using MizanDesktop.Models;
namespace MizanDesktop.Services;
public sealed record StatementRow(DateTime Date,string Kind,string Reference,string Description,decimal Debit,decimal Credit,decimal Balance);
public sealed class StatementService
{
    private readonly AppRepository repo;
    public StatementService(AppRepository repo)=>this.repo=repo;
    public List<StatementRow> Party(Guid partyId, DateTime from, DateTime to)
    {
        decimal balance=0; var rows=new List<StatementRow>();
        foreach(var i in repo.Invoices.Where(x=>x.PartyId==partyId && x.Date.Date>=from.Date && x.Date.Date<=to.Date).OrderBy(x=>x.Date))
        {
            decimal debit=0, credit=0;
            if(i.Type==InvoiceType.Sale) debit=i.Total; else if(i.Type==InvoiceType.SaleReturn) credit=i.Total; else if(i.Type==InvoiceType.Purchase) credit=i.Total; else if(i.Type==InvoiceType.PurchaseReturn) debit=i.Total;
            balance += debit-credit;
            rows.Add(new(i.Date,i.Type.ToString(),i.Number,i.PartyName,debit,credit,balance));
        }
        foreach(var p in repo.Payments.Where(x=>x.PartyId==partyId && x.Date.Date>=from.Date && x.Date.Date<=to.Date).OrderBy(x=>x.Date))
        {
            var debit=p.Type==PaymentType.CustomerReceipt?0:p.Amount; var credit=p.Type==PaymentType.CustomerReceipt?p.Amount:0; balance+=debit-credit;
            rows.Add(new(p.Date,p.Type.ToString(),"",p.Description,debit,credit,balance));
        }
        return rows.OrderBy(x=>x.Date).ToList();
    }
}
