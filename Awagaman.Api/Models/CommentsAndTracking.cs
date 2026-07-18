namespace Awagaman.Api.Models;

public class ChallanComment
{
    public int Id { get; set; }
    public int ChallanId { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class LRComment
{
    public int Id { get; set; }
    public int LREntryId { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BillComment
{
    public int Id { get; set; }
    public int BillId { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TrackingEntry
{
    public int Id { get; set; }
    public int Sr { get; set; }
    public string? ChallanNo { get; set; }
    public DateTime ChallanDate { get; set; } = DateTime.Today;
    public string? From { get; set; }
    public string? To { get; set; }
    public string? VehicleNo { get; set; }
    public string? DriverMobile { get; set; }
    public DateTime? EwayBillTillDate { get; set; }
    public DateTime? DispatchDate { get; set; }
    public string? DispatchTime { get; set; }
    public DateTime? DeliveredDate { get; set; }
    public string? DeliveredTime { get; set; }
}

public class ReportingTrackEntry
{
    public int Id { get; set; }
    public int TrackingEntryId { get; set; }
    public DateTime ReportDateTime { get; set; } = DateTime.Now;
    public string? Remarks { get; set; }
}

public sealed class TrackingPageResult
{
    public int TotalCount { get; set; }
    public List<TrackingEntry> Items { get; set; } = new();
}
