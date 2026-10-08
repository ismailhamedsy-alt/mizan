namespace MizanDesktop.Services;
public static class BarcodeService
{
    public static string Normalize(string? value)=>new string((value??"").Where(char.IsLetterOrDigit).ToArray());
    public static string NextForProduct(string code)=>string.IsNullOrWhiteSpace(code)?DateTime.Now.ToString("yyyyMMddHHmmssfff"):Normalize(code);
}
