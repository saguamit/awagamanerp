using Awagaman_ERP.Data;
using Awagaman_ERP.Models;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Data.SQLite;

namespace Awagaman_ERP.Tests
{
    [TestFixture]
    [NonParallelizable]
    [Apartment(ApartmentState.STA)]
    public class LedgerFlowRegressionTests
    {
        private string _tempRoot;
        private MainWindow _window;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "Awagaman ERP Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            AppDatabase.SetDatabasePathForTesting(Path.Combine(_tempRoot, "awagaman_erp.db"));

            if (Application.Current == null)
            {
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }

            _window = new MainWindow();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (_window != null)
                {
                    _window.Close();
                    _window = null;
                }
            }
            catch
            {
                // Best effort cleanup.
            }

            AppDatabase.SetDatabasePathForTesting(null);

            try
            {
                if (!string.IsNullOrWhiteSpace(_tempRoot) && Directory.Exists(_tempRoot))
                {
                    Directory.Delete(_tempRoot, true);
                }
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        [Test]
        public void Challan_LR_Bill_And_CBS_Flow_Stays_Consistent_After_Later_Challan_Update()
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var challanNo = $"CH-FLOW-{stamp}";
            var lrNo = $"LR-FLOW-{stamp}";
            var billNo = $"BILL-FLOW-{stamp}";
            var party = $"FLOW PARTY {stamp}";

            var challanRepo = new ChallanRepository();
            var lrRepo = new LRRepository();
            var billRepo = new BillRepository();
            var receiptRepo = new BillReceiptRepository();
            var cbsRepo = new CashBankStatementRepository();

            var challan = new ChallanEntry
            {
                Sr = 1,
                ChallanNumber = challanNo,
                Date = new DateTime(2026, 6, 2),
                LRNumber = lrNo,
                BrokerName = "BROKER A",
                From = "NOIDA",
                To = "FARIDABAD",
                VehicleNumber = "HR29AJ5151",
                VehicleType = "TRUCK",
                LorryHire = 50000m,
                LessTDS = 0m,
                AdvanceAmount = 12000m,
                AdvanceNEFT = 12000m,
                AdvanceCash = 0m,
                AdvanceDate = new DateTime(2026, 6, 2),
                Detention = 500m,
                Hamali = 200m,
                Deduction = 0m,
                BalancePaidNEFT = 10000m,
                BalancePaidCash = 0m,
                BalancePaidDate = new DateTime(2026, 6, 10),
                BillAmount = 0m,
                Margin = 0m
            };
            challan.RecalculateBalance();
            challanRepo.Upsert(challan);

            var lr = new LREntry
            {
                Sr = 1,
                LRNo = lrNo,
                Date = new DateTime(2026, 6, 2),
                ConsignorName = "OLD CONSIGNOR",
                ConsignorAddress = "OLD ADDRESS",
                ConsignorGST = "OLDGST",
                ConsigneeName = "OLD CONSIGNEE",
                ConsigneeAddress = "OLD CONSIGNEE ADDRESS",
                ConsigneeGST = "OLDCGST",
                From = "OLD FROM",
                To = "OLD TO",
                VehicleNo = "OLDVEH",
                VehicleType = "OLDTYPE",
                CHNo = challanNo,
                TotalFreight = 65000m,
                Hamali = 500m,
                Detention = 1000m,
                Others = 250m,
                StCharge = 100m,
                NEFT = 0m,
                CASH = 0m,
                TDS = 0m,
                Ded = 0m,
                BillNo = string.Empty,
                BillDate = null,
                BILL = 0m,
                BillParty = party,
                Broker = "OLD BROKER",
                FrtType = "FRT",
                PayType = "TO PAY",
                Comm = 0m,
                Paid = string.Empty
            };
            lrRepo.Upsert(lr);

            InvokePrivate(_window, "SyncLinkedLREntriesFromChallan", challan);

            var lrAfterChallan = lrRepo.GetAll().Single(x => string.Equals((x.LRNo ?? string.Empty).Trim(), lrNo, StringComparison.OrdinalIgnoreCase));
            Assert.That(lrAfterChallan.From, Is.EqualTo(challan.From));
            Assert.That(lrAfterChallan.To, Is.EqualTo(challan.To));
            Assert.That(lrAfterChallan.VehicleNo, Is.EqualTo(challan.VehicleNumber));
            Assert.That(lrAfterChallan.VehicleType, Is.EqualTo(challan.VehicleType));
            Assert.That(lrAfterChallan.Broker, Is.EqualTo(challan.BrokerName));
            Assert.That(string.IsNullOrWhiteSpace(lrAfterChallan.BillNo), Is.True, "LR should still be unbilled before bill creation.");

            InvokePrivate(_window, "SyncSystemCBSFromChallan");

            var cbsRows = cbsRepo.GetAll().Where(x => string.Equals((x.AccountName ?? string.Empty).Trim(), "LHS", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.That(cbsRows.Count, Is.EqualTo(2), "LHS should have separate advance and balance rows.");
            Assert.That(cbsRows.Sum(x => x.BankCr), Is.EqualTo(22000m));
            Assert.That(cbsRows.Sum(x => x.CashCr), Is.EqualTo(0m));

            var bill = new BillEntry
            {
                Sr = 1,
                BillNo = billNo,
                BillDate = new DateTime(2026, 6, 12),
                Party = party,
                LRNo = lrNo,
                LRDate = challan.Date,
                From = challan.From,
                To = challan.To,
                VehicleType = challan.VehicleType,
                Freight = 65000m,
                Detention = 500m,
                HML = 200m,
                OTHR = 250m,
                StCharge = 100m,
                RCVD = 0m,
                TDS = 0m,
                DED = 0m,
                MOP = "NEFT",
                MR = "MR-1",
                Remarks = "Initial bill",
                Date = new DateTime(2026, 6, 12)
            };
            billRepo.Upsert(bill);

            InvokePrivate(_window, "SyncLREntriesFromBillNo", billNo);

            var lrAfterBill = lrRepo.GetAll().Single(x => string.Equals((x.LRNo ?? string.Empty).Trim(), lrNo, StringComparison.OrdinalIgnoreCase));
            Assert.That(lrAfterBill.BillNo, Is.EqualTo(billNo));
            Assert.That(lrAfterBill.BillParty, Is.EqualTo(party));

            var pendingStillExists = lrRepo.GetAll()
                .Any(x => string.Equals((x.LRNo ?? string.Empty).Trim(), lrNo, StringComparison.OrdinalIgnoreCase) &&
                          string.IsNullOrWhiteSpace((x.BillNo ?? string.Empty).Trim()));
            Assert.That(pendingStillExists, Is.False, "Bill-linked LR should not stay in pending-bill state.");

            InvokePrivate(_window, "ApplyReceiveOnBill", billNo, 50000m, 0m, 0m, "NEFT", "MR-100", new DateTime(2026, 6, 15), "First part payment");
            InvokePrivate(_window, "ApplyReceiveOnBill", billNo, 10000m, 0m, 0m, "NEFT", "MR-101", new DateTime(2026, 6, 20), "Second part payment");
            InvokePrivate(_window, "ApplyReceiveOnBill", billNo, 10000m, 0m, 0m, "CASH", "MR-102", new DateTime(2026, 6, 25), "Third part payment");

            var receipts = receiptRepo.GetByBillNo(billNo);
            Assert.That(receipts.Count, Is.EqualTo(3), "Bill receipt history should store every payment separately.");
            Assert.That(receipts.Sum(x => x.RCVD), Is.EqualTo(70000m));

            var bfrsRows = cbsRepo.GetAll().Where(x => string.Equals((x.AccountName ?? string.Empty).Trim(), "BFRS", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.That(bfrsRows.Count, Is.EqualTo(3), "BFRS should record each bill receipt as a separate transaction.");
            Assert.That(bfrsRows.Sum(x => x.BankDr), Is.EqualTo(60000m));
            Assert.That(bfrsRows.Sum(x => x.CashDr), Is.EqualTo(10000m));

            challan.BalancePaidNEFT = 15000m;
            challan.BalancePaidCash = 0m;
            challan.BalancePaidDate = new DateTime(2026, 6, 28);
            challanRepo.Upsert(challan);

            InvokePrivate(_window, "SyncAllChallanBillingFromLR", true);
            InvokePrivate(_window, "SyncSystemCBSFromChallan");

            var lrAfterChallanPayment = lrRepo.GetAll().Single(x => string.Equals((x.LRNo ?? string.Empty).Trim(), lrNo, StringComparison.OrdinalIgnoreCase));
            Assert.That(lrAfterChallanPayment.BillNo, Is.EqualTo(billNo), "Later challan balance payments must not clear the LR bill link.");

            var pendingAfterChallanPayment = lrRepo.GetAll()
                .Count(x => string.Equals((x.LRNo ?? string.Empty).Trim(), lrNo, StringComparison.OrdinalIgnoreCase) &&
                            string.IsNullOrWhiteSpace((x.BillNo ?? string.Empty).Trim()));
            Assert.That(pendingAfterChallanPayment, Is.EqualTo(0), "The same LR should not reappear in pending bills after challan payment.");
        }

        [Test]
        public void One_Bill_With_Multiple_LRs_Creates_Separate_BillRows_And_Links_All_LRs()
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var challanNo = $"CH-MULTI-{stamp}";
            var lrNo1 = $"LR-MULTI-1-{stamp}";
            var lrNo2 = $"LR-MULTI-2-{stamp}";
            var billNo = $"BILL-MULTI-{stamp}";
            var party = $"MULTI PARTY {stamp}";

            var challanRepo = new ChallanRepository();
            var lrRepo = new LRRepository();
            var billRepo = new BillRepository();

            var challan = new ChallanEntry
            {
                Sr = 1,
                ChallanNumber = challanNo,
                Date = new DateTime(2026, 6, 2),
                LRNumber = $"{lrNo1}, {lrNo2}",
                BrokerName = "BROKER B",
                From = "GURGAON",
                To = "NOIDA",
                VehicleNumber = "HR51AB1111",
                VehicleType = "TRUCK",
                LorryHire = 30000m,
                LessTDS = 0m,
                AdvanceAmount = 0m,
                AdvanceNEFT = 0m,
                AdvanceCash = 0m,
                Detention = 0m,
                Hamali = 0m,
                Deduction = 0m,
                BalancePaidNEFT = 0m,
                BalancePaidCash = 0m,
                BillAmount = 0m,
                Margin = 0m
            };
            challan.RecalculateBalance();
            challanRepo.Upsert(challan);

            var lr1 = new LREntry
            {
                Sr = 1,
                LRNo = lrNo1,
                Date = challan.Date,
                From = challan.From,
                To = challan.To,
                VehicleNo = challan.VehicleNumber,
                VehicleType = challan.VehicleType,
                CHNo = challanNo,
                TotalFreight = 40000m,
                Hamali = 100m,
                Detention = 200m,
                Others = 50m,
                StCharge = 25m,
                NEFT = 0m,
                CASH = 0m,
                TDS = 0m,
                Ded = 0m,
                BillNo = string.Empty,
                BillParty = party,
                Broker = challan.BrokerName
            };
            var lr2 = new LREntry
            {
                Sr = 2,
                LRNo = lrNo2,
                Date = challan.Date,
                From = challan.From,
                To = challan.To,
                VehicleNo = challan.VehicleNumber,
                VehicleType = challan.VehicleType,
                CHNo = challanNo,
                TotalFreight = 50000m,
                Hamali = 150m,
                Detention = 250m,
                Others = 75m,
                StCharge = 35m,
                NEFT = 0m,
                CASH = 0m,
                TDS = 0m,
                Ded = 0m,
                BillNo = string.Empty,
                BillParty = party,
                Broker = challan.BrokerName
            };
            lrRepo.Upsert(lr1);
            lrRepo.Upsert(lr2);

            var bill = new BillEntry
            {
                Sr = 1,
                BillNo = billNo,
                BillDate = new DateTime(2026, 6, 12),
                Party = party,
                LRNo = $"{lrNo1}, {lrNo2}",
                LRDate = challan.Date,
                From = challan.From,
                To = challan.To,
                VehicleType = challan.VehicleType,
                Freight = 90000m,
                Detention = 450m,
                HML = 250m,
                OTHR = 125m,
                StCharge = 60m,
                RCVD = 0m,
                TDS = 0m,
                DED = 0m,
                MOP = "NEFT",
                MR = "MR-MULTI",
                Remarks = "Multi LR bill",
                Date = new DateTime(2026, 6, 12)
            };

            InvokePrivate(_window, "SaveBillRowsFromFormEntry", bill);
            InvokePrivate(_window, "SyncLREntriesFromBillNo", billNo);

            var billRows = billRepo.GetPage(1, 100).Where(x => string.Equals((x.BillNo ?? string.Empty).Trim(), billNo, StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.That(billRows.Count, Is.EqualTo(2), "A multi-LR bill should create one bill row per LR.");
            Assert.That(billRows.All(x => x.Party == party), Is.True);
            Assert.That(billRows.Select(x => x.LRNo).Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(2));

            var lr1After = lrRepo.GetAll().Single(x => string.Equals((x.LRNo ?? string.Empty).Trim(), lrNo1, StringComparison.OrdinalIgnoreCase));
            var lr2After = lrRepo.GetAll().Single(x => string.Equals((x.LRNo ?? string.Empty).Trim(), lrNo2, StringComparison.OrdinalIgnoreCase));
            Assert.That(lr1After.BillNo, Is.EqualTo(billNo));
            Assert.That(lr2After.BillNo, Is.EqualTo(billNo));
            Assert.That(CountPendingBillsForLr(lrNo1), Is.EqualTo(0));
            Assert.That(CountPendingBillsForLr(lrNo2), Is.EqualTo(0));
        }

        [Test]
        public void Paid_Challan_Does_Not_Reappear_In_Pending_Bills_After_LR_Is_Billed()
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var challanNo = $"CH-PENDING-{stamp}";
            var lrNo = $"LR-PENDING-{stamp}";
            var billNo = $"BILL-PENDING-{stamp}";
            var party = $"PENDING PARTY {stamp}";

            var challanRepo = new ChallanRepository();
            var lrRepo = new LRRepository();
            var billRepo = new BillRepository();

            var challan = new ChallanEntry
            {
                Sr = 1,
                ChallanNumber = challanNo,
                Date = new DateTime(2026, 6, 2),
                LRNumber = lrNo,
                BrokerName = "BROKER C",
                From = "NOIDA",
                To = "FARIDABAD",
                VehicleNumber = "HR29AJ9999",
                VehicleType = "TRUCK",
                LorryHire = 50000m,
                LessTDS = 0m,
                AdvanceAmount = 10000m,
                AdvanceNEFT = 10000m,
                AdvanceCash = 0m,
                AdvanceDate = new DateTime(2026, 6, 2),
                Detention = 500m,
                Hamali = 200m,
                Deduction = 0m,
                BalancePaidNEFT = 0m,
                BalancePaidCash = 0m,
                BillAmount = 0m,
                Margin = 0m
            };
            challan.RecalculateBalance();
            challanRepo.Upsert(challan);

            var lr = new LREntry
            {
                Sr = 1,
                LRNo = lrNo,
                Date = challan.Date,
                From = challan.From,
                To = challan.To,
                VehicleNo = challan.VehicleNumber,
                VehicleType = challan.VehicleType,
                CHNo = challanNo,
                TotalFreight = 70000m,
                Hamali = 500m,
                Detention = 1000m,
                Others = 250m,
                StCharge = 100m,
                NEFT = 0m,
                CASH = 0m,
                TDS = 0m,
                Ded = 0m,
                BillNo = string.Empty,
                BillParty = party,
                Broker = challan.BrokerName
            };
            lrRepo.Upsert(lr);

            InvokePrivate(_window, "SyncLinkedLREntriesFromChallan", challan);

            var bill = new BillEntry
            {
                Sr = 1,
                BillNo = billNo,
                BillDate = new DateTime(2026, 6, 12),
                Party = party,
                LRNo = lrNo,
                LRDate = challan.Date,
                From = challan.From,
                To = challan.To,
                VehicleType = challan.VehicleType,
                Freight = 70000m,
                Detention = 500m,
                HML = 200m,
                OTHR = 250m,
                StCharge = 100m,
                RCVD = 0m,
                TDS = 0m,
                DED = 0m,
                MOP = "NEFT",
                MR = "MR-PENDING",
                Remarks = "Pending bug case",
                Date = new DateTime(2026, 6, 12)
            };
            billRepo.Upsert(bill);

            InvokePrivate(_window, "SyncLREntriesFromBillNo", billNo);

            challan.BalancePaidNEFT = 15000m;
            challan.BalancePaidCash = 0m;
            challan.BalancePaidDate = new DateTime(2026, 6, 28);
            challanRepo.Upsert(challan);

            InvokePrivate(_window, "SyncAllChallanBillingFromLR", true);
            InvokePrivate(_window, "SyncSystemCBSFromChallan");

            var lrAfter = lrRepo.GetAll().Single(x => string.Equals((x.LRNo ?? string.Empty).Trim(), lrNo, StringComparison.OrdinalIgnoreCase));
            Assert.That(lrAfter.BillNo, Is.EqualTo(billNo), "A paid challan should not clear an already-created bill link.");
            Assert.That(CountPendingBillsForLr(lrNo), Is.EqualTo(0), "The LR should not reappear in the pending bill list.");
        }

        private static void InvokePrivate(object target, string methodName, params object[] args)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var method = target.GetType().GetMethod(methodName, flags);
            if (method == null)
            {
                throw new MissingMethodException(target.GetType().FullName, methodName);
            }

            method.Invoke(target, args);
        }

        private static int CountPendingBillsForLr(string lrNo)
        {
            lrNo = (lrNo ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(lrNo)) return 0;

            using (var conn = new System.Data.SQLite.SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = conn.CreateCommand())
            {
                conn.Open();
                cmd.CommandText = @"
SELECT COUNT(*)
FROM LREntries
WHERE TRIM(COALESCE(LRNo,'')) = @lrNo
  AND (BillNo IS NULL OR TRIM(COALESCE(BillNo,'')) = '');";
                cmd.Parameters.AddWithValue("@lrNo", lrNo);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }
    }
}
