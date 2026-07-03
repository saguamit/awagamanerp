using System;

namespace Awagaman_ERP.Models
{
    public class LhsSummaryEntry
    {
        public DateTime Date { get; set; }
        public string BrokerName { get; set; }
        public string From { get; set; }
        public string To { get; set; }
        public string VehicleNumber { get; set; }
        public decimal BankDr { get; set; }
        public decimal BankCr { get; set; }
        public decimal CashDr { get; set; }
        public decimal CashCr { get; set; }
    }
}
