namespace Awagaman.Api.Models;

public class CashBankStatementEntry
{
    public int Id { get; set; }
    public int Sr { get; set; }
    public string? CBS { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public string? AccountName { get; set; }
    public string? Particulars { get; set; }
    public string? Remarks { get; set; }
    public decimal BankDr { get; set; }
    public decimal BankCr { get; set; }
    public decimal CashDr { get; set; }
    public decimal CashCr { get; set; }
}
