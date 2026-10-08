namespace MizanDesktop.Models;
public enum PaymentType { CustomerReceipt, SupplierPayment, CashIncome, CashExpense }
public sealed class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; } = DateTime.Now;
    public PaymentType Type { get; set; }
    public Guid? PartyId { get; set; }
    public string PartyName { get; set; } = "";
    public Guid? InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = PaymentMethods.Cash;
    public string Description { get; set; } = "";
}
