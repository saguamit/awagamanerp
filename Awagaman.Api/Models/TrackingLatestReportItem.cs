namespace Awagaman.Api.Models;

public sealed class TrackingLatestReportItem
{
    public int TrackingEntryId { get; set; }
    public DateTime? ReportDateTime { get; set; }
    public string Remarks { get; set; } = string.Empty;
}
