namespace Awagaman.Api.Models;

public sealed class LRLedgerPageResult
{
    public int TotalCount { get; set; }
    public decimal TotalFreight { get; set; }
    public decimal TotalBalance { get; set; }
    public List<int> CommentIds { get; set; } = new();
    public IReadOnlyList<LREntry> Items { get; set; } = Array.Empty<LREntry>();
}
