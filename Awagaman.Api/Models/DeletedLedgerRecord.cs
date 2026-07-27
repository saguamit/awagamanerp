namespace Awagaman.Api.Models;

public sealed class DeletedLedgerRecord
{
    public int Id { get; set; }
    public string LedgerType { get; set; } = string.Empty;
    public string EntityKey { get; set; } = string.Empty;
    public string JsonData { get; set; } = string.Empty;
    public DateTime DeletedUtc { get; set; }
}
