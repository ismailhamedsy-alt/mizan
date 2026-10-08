namespace MizanDesktop.Models;
public enum PartyType { Customer, Supplier }
public sealed class Party { public Guid Id { get; set; }=Guid.NewGuid(); public string Name { get; set; }=""; public string Phone { get; set; }=""; public string Address { get; set; }=""; public PartyType Type { get; set; } public decimal Balance { get; set; } }
