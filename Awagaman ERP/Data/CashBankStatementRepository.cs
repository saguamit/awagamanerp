using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
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
            if (BackendSettings.UseRemoteApi)
            {
                return RemoteApiClient.GetList<CashBankStatementEntry>("api/cbs/statements");
            }
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

        public List<CashBankStatementEntry> GetByAccount(string accountName, DateTime? fromDate = null, DateTime? toDate = null)
        {
            accountName = (accountName ?? string.Empty).Trim();
            if (accountName.Length == 0)
            {
                return new List<CashBankStatementEntry>();
            }

            if (BackendSettings.UseRemoteApi)
            {
                var route = "api/cbs/statements?account=" + RemoteApiClient.UrlEncode(accountName);
                if (fromDate.HasValue)
                {
                    route += "&fromDate=" + RemoteApiClient.UrlEncode(fromDate.Value.ToString("yyyy-MM-dd"));
                }
                if (toDate.HasValue)
                {
                    route += "&toDate=" + RemoteApiClient.UrlEncode(toDate.Value.ToString("yyyy-MM-dd"));
                }
                return RemoteApiClient.GetList<CashBankStatementEntry>(route);
            }

            var list = new List<CashBankStatementEntry>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = c.CreateCommand())
            {
                var conditions = new List<string> { "LOWER(TRIM(AccountName)) = LOWER(TRIM(@account))" };
                cmd.Parameters.AddWithValue("@account", accountName);
                if (fromDate.HasValue)
                {
                    conditions.Add("Date(Date) >= Date(@fromDate)");
                    cmd.Parameters.AddWithValue("@fromDate", fromDate.Value.ToString("yyyy-MM-dd"));
                }
                if (toDate.HasValue)
                {
                    conditions.Add("Date(Date) <= Date(@toDate)");
                    cmd.Parameters.AddWithValue("@toDate", toDate.Value.ToString("yyyy-MM-dd"));
                }

                cmd.CommandText = $@"SELECT * FROM CashBankStatements
WHERE {string.Join(" AND ", conditions)}
ORDER BY Date DESC, Id DESC;";
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

        public List<LhsSummaryEntry> GetLhsSummary(DateTime? fromDate = null, DateTime? toDate = null)
        {
            if (BackendSettings.UseRemoteApi)
            {
                try
                {
                    var route = "api/cbs/statements/lhs-summary";
                    var queryParts = new List<string>();
                    if (fromDate.HasValue)
                    {
                        queryParts.Add("fromDate=" + RemoteApiClient.UrlEncode(fromDate.Value.ToString("yyyy-MM-dd")));
                    }
                    if (toDate.HasValue)
                    {
                        queryParts.Add("toDate=" + RemoteApiClient.UrlEncode(toDate.Value.ToString("yyyy-MM-dd")));
                    }
                    if (queryParts.Count > 0)
                    {
                        route += "?" + string.Join("&", queryParts);
                    }

                    return RemoteApiClient.GetList<LhsSummaryEntry>(route);
                }
                catch
                {
                }
            }

            var entries = GetByAccount("LHS", fromDate, toDate)
                .OrderByDescending(x => x.Date)
                .ThenByDescending(x => x.Id)
                .ToList();
            var challanNumbers = entries
                .Select(x =>
                {
                    var txt = (x.Particulars ?? string.Empty);
                    var idx = txt.IndexOf("Challan ", StringComparison.OrdinalIgnoreCase);
                    return idx >= 0
                        ? txt.Substring(idx + 8).Split(new[] { ' ', '-', '|', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                        : string.Empty;
                })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var challanRepo = new ChallanRepository();
            var challans = challanRepo.GetByChallanNumbers(challanNumbers);

            return entries.Select(x =>
            {
                var txt = (x.Particulars ?? string.Empty);
                var idx = txt.IndexOf("Challan ", StringComparison.OrdinalIgnoreCase);
                var chNo = idx >= 0 ? txt.Substring(idx + 8).Split(new[] { ' ', '-', '|', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() : string.Empty;
                var ch = challans.FirstOrDefault(c => string.Equals((c.ChallanNumber ?? string.Empty).Trim(), (chNo ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
                return new LhsSummaryEntry
                {
                    Date = x.Date,
                    BrokerName = ch?.BrokerName ?? string.Empty,
                    From = ch?.From ?? string.Empty,
                    To = ch?.To ?? string.Empty,
                    VehicleNumber = ch?.VehicleNumber ?? string.Empty,
                    BankDr = x.BankDr,
                    BankCr = x.BankCr,
                    CashDr = x.CashDr,
                    CashCr = x.CashCr
                };
            }).ToList();
        }

        public List<CashBankStatementEntry> Search(string filter)
        {
            if (BackendSettings.UseRemoteApi)
            {
                filter = (filter ?? string.Empty).Trim();
                if (filter.Length == 0) return GetAll();
                return RemoteApiClient.GetList<CashBankStatementEntry>("api/cbs/statements")
                    .FindAll(x =>
                        Contains(x.CBS, filter) ||
                        Contains(x.AccountName, filter) ||
                        Contains(x.Particulars, filter) ||
                        Contains(x.Remarks, filter))
                    .OrderByDescending(x => x.Date)
                    .ThenByDescending(x => x.Id)
                    .ToList();
            }
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
            if (BackendSettings.UseRemoteApi)
            {
                return RemoteApiClient.GetList<CashBankStatementEntry>("api/cbs/statements").Select(x => x.Sr).DefaultIfEmpty(0).Max();
            }
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

            if (BackendSettings.UseRemoteApi)
            {
                if (entry.Id <= 0)
                {
                    entry.Id = RemoteApiClient.PostAndReadInt("api/cbs/statements", entry);
                }
                else
                {
                    RemoteApiClient.Put($"api/cbs/statements/{entry.Id}", entry);
                }
                return;
            }

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
            if (BackendSettings.UseRemoteApi)
            {
                RemoteApiClient.Delete($"api/cbs/statements/{entry.Id}");
                return;
            }
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

        private static bool Contains(string value, string filter)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.IsNullOrWhiteSpace(filter) &&
                   value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
