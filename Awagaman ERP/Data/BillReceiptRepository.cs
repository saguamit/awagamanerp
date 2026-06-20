using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    public sealed class BillReceiptRepository
    {
        public BillReceiptRepository()
        {
            AppDatabase.EnsureInitialized();
            AppDatabase.EnsureBillTablesExist();
        }

        public List<BillReceiptEntry> GetByBillNo(string billNo)
        {
            if (BackendSettings.UseRemoteApi)
            {
                billNo = (billNo ?? string.Empty).Trim();
                if (billNo.Length == 0) return new List<BillReceiptEntry>();
                return RemoteApiClient.GetList<BillReceiptEntry>("api/bill-receipts")
                    .FindAll(x => string.Equals((x.BillNo ?? string.Empty).Trim(), billNo, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.ReceiptDate)
                    .ThenBy(x => x.Id)
                    .ToList();
            }
            var list = new List<BillReceiptEntry>();
            billNo = (billNo ?? string.Empty).Trim();
            if (billNo.Length == 0) return list;

            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand(@"
SELECT * FROM BillReceipts
WHERE TRIM(COALESCE(BillNo,'')) = @billNo
ORDER BY ReceiptDate ASC, Id ASC;", c))
            {
                cmd.Parameters.AddWithValue("@billNo", billNo);
                c.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(MapReader(r));
                }
            }

            return list;
        }

        public List<BillReceiptEntry> GetAll()
        {
            if (BackendSettings.UseRemoteApi)
            {
                return RemoteApiClient.GetList<BillReceiptEntry>("api/bill-receipts");
            }
            var list = new List<BillReceiptEntry>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT * FROM BillReceipts ORDER BY ReceiptDate DESC, Id DESC;", c))
            {
                c.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        list.Add(MapReader(r));
                }
            }
            return list;
        }

        public void Add(BillReceiptEntry entry)
        {
            if (entry == null) return;
            if (BackendSettings.UseRemoteApi)
            {
                entry.Id = RemoteApiClient.PostAndReadInt("api/bill-receipts", entry);
                return;
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = c.CreateCommand())
            {
                c.Open();
                cmd.CommandText = @"
INSERT INTO BillReceipts
(BillNo, Party, BillTotal, BillDate, ReceiptDate, RCVD, TDS, DED, MOP, MR, Remarks, DueAfter, CreatedAt)
VALUES
(@BillNo, @Party, @BillTotal, @BillDate, @ReceiptDate, @RCVD, @TDS, @DED, @MOP, @MR, @Remarks, @DueAfter, @CreatedAt);";
                cmd.Parameters.AddWithValue("@BillNo", (object)entry.BillNo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Party", (object)entry.Party ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@BillTotal", entry.BillTotal);
                cmd.Parameters.AddWithValue("@BillDate", entry.BillDate.HasValue ? (object)entry.BillDate.Value.ToString("o") : DBNull.Value);
                cmd.Parameters.AddWithValue("@ReceiptDate", entry.ReceiptDate.ToString("o"));
                cmd.Parameters.AddWithValue("@RCVD", entry.RCVD);
                cmd.Parameters.AddWithValue("@TDS", entry.TDS);
                cmd.Parameters.AddWithValue("@DED", entry.DED);
                cmd.Parameters.AddWithValue("@MOP", (object)entry.MOP ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MR", (object)entry.MR ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Remarks", (object)entry.Remarks ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@DueAfter", entry.DueAfter);
                cmd.Parameters.AddWithValue("@CreatedAt", entry.CreatedAt == default(DateTime) ? DateTime.Now.ToString("o") : entry.CreatedAt.ToString("o"));
                cmd.ExecuteNonQuery();
            }
        }

        public void Delete(BillReceiptEntry entry)
        {
            if (entry == null || entry.Id <= 0) return;
            if (BackendSettings.UseRemoteApi)
            {
                RemoteApiClient.Delete($"api/bill-receipts/{entry.Id}");
                return;
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("DELETE FROM BillReceipts WHERE Id = @id;", c))
            {
                cmd.Parameters.AddWithValue("@id", entry.Id);
                c.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private static BillReceiptEntry MapReader(SQLiteDataReader r)
        {
            DateTime receiptDate;
            DateTime createdAt;
            var receiptRaw = r["ReceiptDate"] as string;
            var createdRaw = r["CreatedAt"] as string;
            if (!DateTime.TryParse(receiptRaw, out receiptDate)) receiptDate = DateTime.Today;
            if (!DateTime.TryParse(createdRaw, out createdAt)) createdAt = DateTime.Now;

            return new BillReceiptEntry
            {
                Id = Convert.ToInt32(r["Id"]),
                BillNo = r["BillNo"] as string,
                Party = r["Party"] as string,
                BillTotal = Convert.ToDecimal(r["BillTotal"]),
                BillDate = DateTime.TryParse(r["BillDate"] as string, out var billDate) ? billDate : (DateTime?)null,
                ReceiptDate = receiptDate,
                RCVD = Convert.ToDecimal(r["RCVD"]),
                TDS = Convert.ToDecimal(r["TDS"]),
                DED = Convert.ToDecimal(r["DED"]),
                MOP = r["MOP"] as string,
                MR = r["MR"] as string,
                Remarks = r["Remarks"] as string,
                DueAfter = Convert.ToDecimal(r["DueAfter"]),
                CreatedAt = createdAt
            };
        }
    }
}
