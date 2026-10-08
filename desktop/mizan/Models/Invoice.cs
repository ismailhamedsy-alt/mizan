namespace MizanDesktop.Models;

public enum InvoiceType { Sale, Purchase, SaleReturn, PurchaseReturn, Quotation }

public static class PaymentMethods
{
    public const string Cash = "CASH";
    public const string ShamCash = "SHAM_CASH";
    public const string BankTransfer = "BANK_TRANSFER";
    public const string Card = "CARD";
    public const string Deferred = "DEFERRED";
    public const string Cheque = "CHEQUE";

    public static string Label(string? method) => method switch
    {
        ShamCash => "شام كاش",
        BankTransfer => "تحويل بنكي",
        Card => "بطاقة / شبكة",
        Deferred => "آجل",
        Cheque => "شيك",
        _ => "نقدي"
    };
}

public sealed class InvoiceLine
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Total => Quantity * UnitPrice;
    public decimal CostTotal => Quantity * UnitCost;
}

public sealed class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Now;
    public InvoiceType Type { get; set; }
    public Guid? PartyId { get; set; }
    public string PartyName { get; set; } = "نقدي";
    public decimal Discount { get; set; }
    public decimal FeesAmount { get; set; }
    public string FeesNote { get; set; } = "";
    public decimal PaidAmount { get; set; }
    public string PaymentMethod { get; set; } = PaymentMethods.Cash;
    public string Currency { get; set; } = "SYP";
    public string Notes { get; set; } = "";
    public string Status { get; set; } = "CLOSED";
    public bool IsCredit { get; set; }
    public List<InvoiceLine> Lines { get; set; } = [];
    public decimal Subtotal => Lines.Sum(x => x.Total);
    public decimal Total => Math.Max(0, Subtotal - Discount + FeesAmount);
    public decimal Remaining => Math.Max(0, Total - PaidAmount);
    public decimal CostOfGoodsSold => Lines.Sum(x => x.CostTotal);
    public decimal GrossProfit => Total - CostOfGoodsSold - FeesAmount;
    public bool IsQuotation => Type == InvoiceType.Quotation;
    public bool IsReturn => Type is InvoiceType.SaleReturn or InvoiceType.PurchaseReturn;
}
