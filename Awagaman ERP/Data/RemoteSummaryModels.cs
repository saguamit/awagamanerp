namespace Awagaman_ERP.Data
{
    internal sealed class RemoteChallanSummary
    {
        public int TotalCount { get; set; }
        public decimal TotalDue { get; set; }
    }

    internal sealed class RemoteChallanLedgerPageResult
    {
        public int TotalCount { get; set; }
        public decimal TotalDue { get; set; }
        public System.Collections.Generic.List<int> CommentIds { get; set; }
        public System.Collections.Generic.List<Awagaman_ERP.Models.ChallanEntry> Items { get; set; }
    }

    internal sealed class RemoteLRSummary
    {
        public int TotalCount { get; set; }
        public decimal TotalFreight { get; set; }
        public decimal TotalBalance { get; set; }
    }

    internal sealed class RemoteLRLedgerPageResult
    {
        public int TotalCount { get; set; }
        public decimal TotalFreight { get; set; }
        public decimal TotalBalance { get; set; }
        public System.Collections.Generic.List<int> CommentIds { get; set; }
        public System.Collections.Generic.List<Awagaman_ERP.Models.LREntry> Items { get; set; }
    }

    internal sealed class RemoteBillSummary
    {
        public int TotalCount { get; set; }
        public decimal TotalDue { get; set; }
    }

    public sealed class BillPartyDueSummaryItem
    {
        public string Party { get; set; }
        public int Bills { get; set; }
        public decimal Due { get; set; }
    }

    public sealed class BillDueDetailItem
    {
        public string BillNo { get; set; }
        public string LRNos { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public decimal Due { get; set; }
    }

    public sealed class BillPendingOptionItem
    {
        public string BillNo { get; set; }
        public string Party { get; set; }
        public string LRNos { get; set; }
        public decimal Total { get; set; }
        public decimal RCVD { get; set; }
        public decimal TDS { get; set; }
        public decimal DED { get; set; }
    }

    public sealed class RemoteBillPreviewResult
    {
        public string Party { get; set; }
        public string PartyAddress { get; set; }
        public string PartyGST { get; set; }
        public string PartyStateCode { get; set; }
        public string BillNo { get; set; }
        public string BillDate { get; set; }
        public decimal TotalAmount { get; set; }
        public System.Collections.Generic.List<RemoteBillPreviewLineItem> Lines { get; set; }
    }

    public sealed class RemoteBillPreviewLineItem
    {
        public string LRNo { get; set; }
        public string LRDate { get; set; }
        public string Invoice { get; set; }
        public string Vehicle { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string ChargesBreakdown { get; set; }
        public string WeightOrType { get; set; }
        public string Rate { get; set; }
        public string Amount { get; set; }
    }

    internal sealed class RemoteCreateLrFromChallanResponse
    {
        public Awagaman_ERP.Models.LREntry Entry { get; set; }
        public Awagaman_ERP.Models.ChallanEntry LinkedChallan { get; set; }
    }
}
