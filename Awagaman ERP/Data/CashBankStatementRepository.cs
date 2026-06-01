using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    public class CashBankStatementRepository
    {
        public CashBankStatementRepository()
        {
            AppDatabase.EnsureInitialized();
        }

        public List<CashBankStatementEntry> GetAll()
        {
            var list = new List<CashBankStatementEntry>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT * FROM CashBankStatements ORDER BY Date DESC, Id DESC;", c))
            {
                c.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(MapReader(r));
                    }
                }
            }
            return list;
        }

        public List<CashBankStatementEntry> Search(string filter)
        {
            var list = new List<CashBankStatementEntry>();
            filter = (filter ?? string.Empty).Trim();
            if (filter.Length == 0) return GetAll();

            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand(@"
SELECT * FROM CashBankStatements
WHERE CBS LIKE @f OR Date LIKE @f OR AccountName LIKE @f OR Particulars LIKE @f OR Remarks LIKE @f
ORDER BY Date DESC, Id DESC;", c))
            {
                cmd.Parameters.AddWithValue("@f", "%" + filter + "%");
                c.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(MapReader(r));
                    }
                }
            }
            return list;
        }

        public int GetMaxSr()
        {
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT COALESCE(MAX(Sr), 0) FROM CashBankStatements;", c))
            {
                c.Open();
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public void Upsert(CashBankStatementEntry entry)
        {
            if (entry == null) return;
            entry.CBS = string.IsNullOrWhiteSpace(entry.CBS) ? entry.Date.ToString("MMM-yy") : entry.CBS;

            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = c.CreateCommand())
            {
                c.Open();
                if (entry.Id <= 0)
                {
                    cmd.CommandText = @"
INSERT INTO CashBankStatements
(Sr, CBS, Date, AccountName, Particulars, Remarks, BankDr, BankCr, CashDr, CashCr)
VALUES
(@Sr, @CBS, @Date, @AccountName, @Particulars, @Remarks, @BankDr, @BankCr, @CashDr, @CashCr);
SELECT last_insert_rowid();";
                }
                else
                {
                    cmd.CommandText = @"
UPDATE CashBankStatements
SET Sr = @Sr,
    CBS = @CBS,
    Date = @Date,
    AccountName = @AccountName,
    Particulars = @Particulars,
    Remarks = @Remarks,
    BankDr = @BankDr,
    BankCr = @BankCr,
    CashDr = @CashDr,
    CashCr = @CashCr
WHERE Id = @Id;";
                    cmd.Parameters.AddWithValue("@Id", entry.Id);
                }

                cmd.Parameters.AddWithValue("@Sr", entry.Sr <= 0 ? GetMaxSr() + 1 : entry.Sr);
                cmd.Parameters.AddWithValue("@CBS", (object)entry.CBS ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Date", entry.Date.ToString("o"));
                cmd.Parameters.AddWithValue("@AccountName", (object)entry.AccountName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Particulars", (object)entry.Particulars ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Remarks", (object)entry.Remarks ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@BankDr", entry.BankDr);
                cmd.Parameters.AddWithValue("@BankCr", entry.BankCr);
                cmd.Parameters.AddWithValue("@CashDr", entry.CashDr);
                cmd.Parameters.AddWithValue("@CashCr", entry.CashCr);

                if (entry.Id <= 0)
                    entry.Id = Convert.ToInt32((long)cmd.ExecuteScalar());
                else
                    cmd.ExecuteNonQuery();
            }
        }

        public void Delete(CashBankStatementEntry entry)
        {
            if (entry == null || entry.Id <= 0) return;
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("DELETE FROM CashBankStatements WHERE Id = @id;", c))
            {
                cmd.Parameters.AddWithValue("@id", entry.Id);
                c.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private static CashBankStatementEntry MapReader(SQLiteDataReader r)
        {
            DateTime date;
            var dateRaw = r["Date"] as string;
            if (!DateTime.TryParse(dateRaw, out date))
            {
                date = DateTime.Today;
            }

            return new CashBankStatementEntry
            {
                Id = Convert.ToInt32(r["Id"]),
                Sr = Convert.ToInt32(r["Sr"]),
                CBS = r["CBS"] as string,
                Date = date,
                AccountName = r["AccountName"] as string,
                Particulars = r["Particulars"] as string,
                Remarks = r["Remarks"] as string,
                BankDr = Convert.ToDecimal(r["BankDr"]),
                BankCr = Convert.ToDecimal(r["BankCr"]),
                CashDr = Convert.ToDecimal(r["CashDr"]),
                CashCr = Convert.ToDecimal(r["CashCr"])
            };
        }
    }
}
