namespace MizanDesktop.Services;

public sealed record AccountBalance(string Account, decimal Debit, decimal Credit)
{
    public decimal Net => Debit - Credit;
    public decimal Balance => Net;
}
