namespace MizanDesktop.Core;

public enum ParityInvoiceType { Sale, Purchase, ReturnSale, ReturnPurchase, Quotation }
public enum ParityPartyType { Customer, Supplier }
public enum ParityPaymentType { CustomerReceipt, SupplierPayment, CashIncome, CashExpense }
public enum ParityStockMovementType { Purchase, Sale, PurchaseReturn, SaleReturn, Adjustment, Opening, Transfer }

public sealed record ParityProduct(
    Guid Id,
    string Name,
    string Barcode,
    string Category,
    string Unit,
    decimal CostPrice,
    decimal SalePrice,
    decimal WholesalePrice,
    decimal SpecialPrice,
    decimal Quantity,
    decimal MinStockAlert,
    DateTime? ExpiryDate,
    string Currency);

public sealed record ParityParty(
    Guid Id,
    string Name,
    ParityPartyType Type,
    string Phone,
    string Address,
    string Currency,
    decimal CreditLimit,
    string PriceTier,
    decimal Balance);

public sealed record ParityInvoiceLine(
    Guid Id,
    Guid? ProductId,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal UnitCost,
    string Unit);

public sealed record ParityInvoice(
    Guid Id,
    string Number,
    ParityInvoiceType Type,
    Guid? PartyId,
    string Currency,
    DateTime Date,
    decimal Discount,
    decimal Fees,
    decimal PaidAmount,
    string PaymentMethod,
    string FundId,
    IReadOnlyList<ParityInvoiceLine> Lines);


public sealed record SyncImportCounts(
    int Products,
    int Parties,
    int Invoices,
    int Payments,
    int CashTransactions,
    int StockMovements,
    int JournalEntries)
{
    public int Total => Products + Parties + Invoices + Payments + CashTransactions + StockMovements + JournalEntries;
}
