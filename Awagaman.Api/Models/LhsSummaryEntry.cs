namespace Awagaman.Api.Models;

public class LhsSummaryEntry
{
    public DateTime Date { get; set; }
    public string? ChallanNumber { get; set; }
    public string? BrokerName { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? VehicleNumber { get; set; }
    public decimal AdvanceNeft { get; set; }
    public decimal AdvanceCash { get; set; }
    public decimal BalanceNeft { get; set; }
    public decimal BalanceCash { get; set; }
    public decimal BankDr { get; set; }
    public decimal BankCr { get; set; }
    public decimal CashDr { get; set; }
    public decimal CashCr { get; set; }
}
