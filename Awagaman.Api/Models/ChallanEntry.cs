namespace Awagaman.Api.Models;

public class ChallanEntry
{
    public int Id { get; set; }
    public int Sr { get; set; }
    public string? ChallanNumber { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public string? LRNumber { get; set; }
    public string? BrokerName { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? VehicleNumber { get; set; }
    public string? VehicleType { get; set; }
    public string? DriverName { get; set; }
    public string? DriverMobile { get; set; }
    public string? EngineNo { get; set; }
    public string? LicenceNo { get; set; }
    public string? PolicyNo { get; set; }
    public string? ChassisNo { get; set; }
    public string? OwnerName { get; set; }
    public string? PAN { get; set; }
    public decimal LorryHire { get; set; }
    public decimal LessTDS { get; set; }
    public decimal AdvanceAmount { get; set; }
    public decimal AdvanceNEFT { get; set; }
    public decimal AdvanceCash { get; set; }
    public DateTime? AdvanceDate { get; set; }
    public decimal Detention { get; set; }
    public decimal Hamali { get; set; }
    public decimal Other { get; set; }
    public decimal Deduction { get; set; }
    public decimal BalancePaidNEFT { get; set; }
    public decimal BalancePaidCash { get; set; }
    public DateTime? BalancePaidDate { get; set; }
    public string? PaidTo { get; set; }
    public string? Remarks { get; set; }
    public decimal BillAmount { get; set; }
    public decimal Margin { get; set; }
    public decimal ImportedBalance { get; set; }
    public decimal ImportedDue { get; set; }
    public int? SourcePurchaseId { get; set; }
    public decimal LHS => LorryHire + Other;
    public decimal Balance => LorryHire - LessTDS - AdvanceAmount;
    public decimal ChallanBalance => LHS - LessTDS - AdvanceAmount;
    public decimal Due => (Balance + Detention + Hamali + Deduction) - BalancePaidNEFT - BalancePaidCash;
    public decimal ChallanDue => (ChallanBalance + Detention + Hamali + Deduction) - BalancePaidNEFT - BalancePaidCash;
    public decimal ChallanMargin => BillAmount == 0m ? 0m : BillAmount - (LHS + Detention + Hamali);
}
