namespace Awagaman.Api.Models;

public sealed class BillSummaryResult
{
    public int TotalCount { get; set; }
    public decimal TotalDue { get; set; }
}

public sealed class BillLedgerPageResult
{
    public int TotalCount { get; set; }
    public List<int> CommentIds { get; set; } = new();
    public IReadOnlyList<BillEntry> Items { get; set; } = Array.Empty<BillEntry>();
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

public sealed class BillPreviewResult
{
    public string Party { get; set; } = string.Empty;
    public string PartyAddress { get; set; } = string.Empty;
    public string PartyGST { get; set; } = string.Empty;
    public string PartyStateCode { get; set; } = string.Empty;
    public string BillNo { get; set; } = string.Empty;
    public string BillDate { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public List<BillPreviewLineItem> Lines { get; set; } = new();
}

public sealed class BillPreviewLineItem
{
    public string LRNo { get; set; } = string.Empty;
    public string LRDate { get; set; } = string.Empty;
    public string Invoice { get; set; } = string.Empty;
    public string Vehicle { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string ChargesBreakdown { get; set; } = string.Empty;
    public string WeightOrType { get; set; } = string.Empty;
    public string Rate { get; set; } = string.Empty;
    public string Amount { get; set; } = string.Empty;
}
