namespace Awagaman.Api.Models;

public class LREntry
{
    public int Id { get; set; }
    public int Sr { get; set; }
    public string? LRNo { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public string? ConsignorName { get; set; }
    public string? ConsignorAddress { get; set; }
    public string? ConsignorGST { get; set; }
    public string? ConsigneeName { get; set; }
    public string? ConsigneeAddress { get; set; }
    public string? ConsigneeGST { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? VehicleNo { get; set; }
    public string? VehicleType { get; set; }
    public decimal Weight { get; set; }
    public decimal SizeL { get; set; }
    public decimal SizeW { get; set; }
    public decimal SizeH { get; set; }
    public decimal ActualWeight { get; set; }
    public decimal ChargedWeight { get; set; }
    public int PKG { get; set; }
    public string? PkgType { get; set; }
    public string? Description { get; set; }
    public string? Invoice { get; set; }
    public string? Value { get; set; }
    public string? CHNo { get; set; }
    public decimal TotalFreight { get; set; }
    public decimal Hamali { get; set; }
    public decimal Detention { get; set; }
    public decimal Others { get; set; }
    public decimal StCharge { get; set; }
    public decimal NEFT { get; set; }
    public decimal CASH { get; set; }
    public decimal TDS { get; set; }
    public decimal Ded { get; set; }
    public string? BillNo { get; set; }
    public DateTime? BillDate { get; set; }
    public decimal BILL { get; set; }
    public decimal ChallanLorryHire { get; set; }
    public string? BillParty { get; set; }
    public string? Broker { get; set; }
    public string? FrtType { get; set; }
    public string? PayType { get; set; }
    public decimal Comm { get; set; }
    public string? Paid { get; set; }
    public bool PreserveImportedBilling { get; set; }
    public decimal TotalBill => PreserveImportedBilling ? BILL : (TotalFreight + Detention + Hamali + Others + StCharge);
    public decimal Bal => (NEFT + CASH) - TDS + Ded;
}
