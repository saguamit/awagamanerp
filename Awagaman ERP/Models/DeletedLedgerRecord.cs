using System;

namespace Awagaman_ERP.Models
{
    public sealed class DeletedLedgerRecord
    {
        public int Id { get; set; }
        public string LedgerType { get; set; }
        public string EntityKey { get; set; }
        public string JsonData { get; set; }
        public DateTime DeletedUtc { get; set; }
        public DateTime DeletedLocal
        {
            get
            {
                var value = DeletedUtc;
                if (value.Kind == DateTimeKind.Unspecified)
                {
                    value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
                }
                return value.Kind == DateTimeKind.Local ? value : value.ToLocalTime();
            }
        }
    }

    public sealed class DeletedPurchaseRow
    {
        public DateTime DeletedAt { get; set; }
        public string ChallanNumber { get; set; }
        public DateTime Date { get; set; }
        public string LRNumber { get; set; }
        public string BrokerName { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string VehicleNumber { get; set; }
        public decimal BillAmount { get; set; }
        public decimal Margin { get; set; }
    }

    public sealed class DeletedLrRow
    {
        public DateTime DeletedAt { get; set; }
        public string LRNo { get; set; }
        public DateTime Date { get; set; }
        public string CHNo { get; set; }
        public string ConsignorName { get; set; }
        public string ConsigneeName { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public decimal LorryHire { get; set; }
        public decimal TotalBill { get; set; }
        public string BillNo { get; set; }
    }

    public sealed class DeletedBillRow
    {
        public DateTime DeletedAt { get; set; }
        public string BillNo { get; set; }
        public DateTime BillDate { get; set; }
        public string LRNo { get; set; }
        public string Party { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public decimal Total { get; set; }
        public decimal Received { get; set; }
        public decimal Due { get; set; }
    }
}
