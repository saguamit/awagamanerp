namespace Awagaman.Api.Models;

public sealed class DashboardSummary
{
    public long ChallanCount { get; set; }
    public long DueChallanCount { get; set; }
    public decimal ChallanDueAmount { get; set; }
    public decimal BillDueAmount { get; set; }
    public decimal CBSBankNet { get; set; }
    public decimal CBSCashNet { get; set; }
    public long PendingBillCount { get; set; }
    public long NewBookingCount { get; set; }
}
