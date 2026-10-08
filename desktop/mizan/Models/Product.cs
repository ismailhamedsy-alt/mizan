namespace MizanDesktop.Models;

public sealed class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "قطعة";
    public decimal PurchasePrice { get; set; }
    public decimal SalePrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal MinQuantity { get; set; }
    public bool IsActive { get; set; } = true;
}
