namespace Awagaman.Api.Models;

public sealed class BillSummaryResult
{
    public int TotalCount { get; set; }
    public decimal TotalDue { get; set; }
}

public sealed class BillPartyDueSummaryItem
{
    public string Party { get; set; } = string.Empty;
    public int Bills { get; set; }
    public decimal Due { get; set; }
}

public sealed class BillDueDetailItem
{
    public string BillNo { get; set; } = string.Empty;
    public string LRNos { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public decimal Due { get; set; }
}

public sealed class BillPendingOptionItem
{
    public string BillNo { get; set; } = string.Empty;
    public string Party { get; set; } = string.Empty;
    public string LRNos { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal RCVD { get; set; }
    public decimal TDS { get; set; }
    public decimal DED { get; set; }
}
