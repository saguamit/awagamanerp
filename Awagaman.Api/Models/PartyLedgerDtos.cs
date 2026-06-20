namespace Awagaman.Api.Models;

public class BillReceiptEntry
{
    public int Id { get; set; }
    public string? BillNo { get; set; }
    public string? Party { get; set; }
    public decimal BillTotal { get; set; }
    public DateTime? BillDate { get; set; }
    public DateTime ReceiptDate { get; set; } = DateTime.Today;
    public decimal RCVD { get; set; }
    public decimal TDS { get; set; }
    public decimal DED { get; set; }
    public string? MOP { get; set; }
    public string? MR { get; set; }
    public string? Remarks { get; set; }
    public decimal DueAfter { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class BillEntry
{
    public int Id { get; set; }
    public int Sr { get; set; }
    public string? BillNo { get; set; }
    public DateTime BillDate { get; set; } = DateTime.Today;
    public string? Party { get; set; }
    public string? LRNo { get; set; }
    public DateTime? LRDate { get; set; }
    public string? FromLoc { get; set; }
    public string? ToLoc { get; set; }
    public string? VehicleType { get; set; }
    public decimal Freight { get; set; }
    public decimal Detention { get; set; }
    public decimal HML { get; set; }
    public decimal OTHR { get; set; }
    public decimal StCharge { get; set; }
    public decimal RCVD { get; set; }
    public decimal TDS { get; set; }
    public decimal DED { get; set; }
    public string? MOP { get; set; }
    public string? MR { get; set; }
    public string? Remarks { get; set; }
    public DateTime? Date { get; set; }
    public decimal Total => Freight + Detention + HML + OTHR + StCharge;
    public decimal Due => Total - RCVD - TDS - DED;
}
