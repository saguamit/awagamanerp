namespace Awagaman.Api.Models;

public sealed class ChallanSummaryResult
{
    public int TotalCount { get; set; }
    public decimal TotalDue { get; set; }
}

public sealed class ChallanLedgerPageResult
{
    public int TotalCount { get; set; }
    public decimal TotalDue { get; set; }
    public List<int> CommentIds { get; set; } = new();
    public List<ChallanEntry> Items { get; set; } = new();
}
