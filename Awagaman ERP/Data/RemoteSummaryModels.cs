namespace Awagaman_ERP.Data
{
    internal sealed class RemoteChallanSummary
    {
        public int TotalCount { get; set; }
        public decimal TotalDue { get; set; }
    }

    internal sealed class RemoteLRSummary
    {
        public int TotalCount { get; set; }
        public decimal TotalFreight { get; set; }
        public decimal TotalBalance { get; set; }
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
}
