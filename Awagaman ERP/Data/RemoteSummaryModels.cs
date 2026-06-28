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
}
