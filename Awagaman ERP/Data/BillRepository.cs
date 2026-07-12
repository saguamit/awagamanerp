using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    public class BillRepository
    {
        public BillRepository() { AppDatabase.EnsureInitialized(); AppDatabase.EnsureBillTablesExist(); }

        private static BillEntry MapReader(SQLiteDataReader r)
        {
            return new BillEntry
            {
                Id = Convert.ToInt32(r["Id"]),
                Sr = Convert.ToInt32(r["Sr"]),
                BillNo = r["BillNo"] as string,
                BillDate = DateTime.TryParse(r["BillDate"] as string, out var bd) ? bd : DateTime.Today,
                Party = r["Party"] as string,
                LRNo = r["LRNo"] as string,
                LRDate = DateTime.TryParse(r["LRDate"] as string, out var ld) ? ld : (DateTime?)null,
                From = r["FromLoc"] as string,
                To = r["ToLoc"] as string,
                VehicleType = r["VehicleType"] as string,
                Freight = GetDecimal(r["Freight"]),
                Detention = GetDecimal(r["Detention"]),
                HML = GetDecimal(r["HML"]),
                OTHR = GetDecimal(r["OTHR"]),
                StCharge = GetDecimal(r["StCharge"]),
                RCVD = GetDecimal(r["RCVD"]),
                TDS = GetDecimal(r["TDS"]),
                DED = GetDecimal(r["DED"]),
                MOP = r["MOP"] as string,
                MR = r["MR"] as string,
                Remarks = r["Remarks"] as string,
                Date = DateTime.TryParse(r["Date"] as string, out var dt) ? dt : DateTime.Today,
            };
        }

        private static decimal GetDecimal(object v) => Convert.ToDecimal(v);

        public List<BillEntry> GetPage(int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true)
        {
            if (BackendSettings.UseRemoteApi)
            {
                return GetRemotePage(pageNumber, pageSize, null, sortColumn, sortAscending).Items;
            }
            var list = new List<BillEntry>();
            int offset = (pageNumber - 1) * pageSize;
            string orderBy = BuildOrderBy(sortColumn, sortAscending);
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand($"SELECT * FROM Bills ORDER BY {orderBy} LIMIT @lim OFFSET @off;", c))
            {
                cmd.Parameters.AddWithValue("@lim", pageSize);
                cmd.Parameters.AddWithValue("@off", offset);
                c.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) list.Add(MapReader(r));
            }
            return list;
        }

        public List<BillEntry> GetAll()
        {
            if (BackendSettings.UseRemoteApi)
            {
                return GetAllRemoteSafe();
            }
            var list = new List<BillEntry>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT * FROM Bills ORDER BY BillDate DESC, Sr DESC, Id DESC;", c))
            {
                c.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) list.Add(MapReader(r));
            }
            return list;
        }

        public List<BillEntry> GetByBillNo(string billNo)
        {
            billNo = (billNo ?? string.Empty).Trim();
            if (billNo.Length == 0) return new List<BillEntry>();

            if (BackendSettings.UseRemoteApi)
            {
                return GetRemotePage(1, 500, billNo, "billno", false).Items
                    .Where(x => string.Equals((x.BillNo ?? string.Empty).Trim(), billNo, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.Id)
                    .ToList();
            }

            var list = new List<BillEntry>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand(@"
SELECT * FROM Bills
WHERE TRIM(COALESCE(BillNo,'')) = @billNo
ORDER BY Id ASC;", c))
            {
                cmd.Parameters.AddWithValue("@billNo", billNo);
                c.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) list.Add(MapReader(r));
            }
            return list;
        }

        public List<BillEntry> Search(string filter, int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true)
        {
            if (BackendSettings.UseRemoteApi)
            {
                return GetRemotePage(pageNumber, pageSize, filter, sortColumn, sortAscending).Items;
            }
            var list = new List<BillEntry>();
            int offset = (pageNumber - 1) * pageSize;
            string orderBy = BuildOrderBy(sortColumn, sortAscending);
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand(
                $@"SELECT * FROM Bills
WHERE BillNo LIKE @f OR Party LIKE @f OR LRNo LIKE @f OR FromLoc LIKE @f OR ToLoc LIKE @f
   OR VehicleType LIKE @f OR MOP LIKE @f OR MR LIKE @f OR Remarks LIKE @f
ORDER BY {orderBy} LIMIT @lim OFFSET @off;", c))
            {
                cmd.Parameters.AddWithValue("@f", $"%{filter}%");
                cmd.Parameters.AddWithValue("@lim", pageSize);
                cmd.Parameters.AddWithValue("@off", offset);
                c.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) list.Add(MapReader(r));
            }
            return list;
        }

        public List<BillEntry> SearchAdvanced(string filter, string party, bool dueOnly, int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true)
        {
            if (BackendSettings.UseRemoteApi)
            {
                return GetRemotePage(pageNumber, pageSize, filter, sortColumn, sortAscending, party, dueOnly).Items;
            }

            var list = new List<BillEntry>();
            int offset = (pageNumber - 1) * pageSize;
            string orderBy = BuildOrderBy(sortColumn, sortAscending);
            var conditions = new List<string>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = c.CreateCommand())
            {
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    conditions.Add(@"(BillNo LIKE @f OR Party LIKE @f OR LRNo LIKE @f OR FromLoc LIKE @f OR ToLoc LIKE @f
   OR VehicleType LIKE @f OR MOP LIKE @f OR MR LIKE @f OR Remarks LIKE @f)");
                    cmd.Parameters.AddWithValue("@f", $"%{filter}%");
                }
                if (!string.IsNullOrWhiteSpace(party))
                {
                    conditions.Add("Party LIKE @party");
                    cmd.Parameters.AddWithValue("@party", $"%{party.Trim()}%");
                }
                if (dueOnly)
                {
                    conditions.Add("(Freight+Detention+HML+OTHR+StCharge-RCVD-TDS-DED) > 0");
                }

                var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
                cmd.CommandText = $"SELECT * FROM Bills {where} ORDER BY {orderBy} LIMIT @lim OFFSET @off;";
                cmd.Parameters.AddWithValue("@lim", pageSize);
                cmd.Parameters.AddWithValue("@off", offset);
                c.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) list.Add(MapReader(r));
            }
            return list;
        }

        public List<BillEntry> SearchAdvancedAll(string filter, string party, bool dueOnly, string sortColumn = "", bool sortAscending = true)
        {
            if (!BackendSettings.UseRemoteApi)
            {
                var total = GetTotalCountAdvanced(filter, party, dueOnly);
                return SearchAdvanced(filter, party, dueOnly, 1, Math.Max(total, 1), sortColumn, sortAscending);
            }

            const int pageSize = 500;
            var all = new List<BillEntry>();
            for (var page = 1; ; page++)
            {
                var result = GetRemotePage(page, pageSize, filter, sortColumn, sortAscending, party, dueOnly);
                if (result.Items != null && result.Items.Count > 0)
                {
                    all.AddRange(result.Items);
                }

                if (all.Count >= result.TotalCount || result.Items == null || result.Items.Count == 0)
                {
                    break;
                }
            }
            return all;
        }

        public int GetTotalCount(string filter = "")
        {
            if (BackendSettings.UseRemoteApi)
            {
                return GetRemoteSummary(filter).TotalCount;
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand(
                string.IsNullOrWhiteSpace(filter) ? "SELECT COUNT(*) FROM Bills;"
                : @"SELECT COUNT(*) FROM Bills
WHERE BillNo LIKE @f OR Party LIKE @f OR LRNo LIKE @f OR FromLoc LIKE @f OR ToLoc LIKE @f
   OR VehicleType LIKE @f OR MOP LIKE @f OR MR LIKE @f OR Remarks LIKE @f;", c))
            {
                if (!string.IsNullOrWhiteSpace(filter)) cmd.Parameters.AddWithValue("@f", $"%{filter}%");
                c.Open();
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public int GetTotalCountAdvanced(string filter, string party, bool dueOnly)
        {
            if (BackendSettings.UseRemoteApi)
            {
                return GetRemoteSummary(filter, party, dueOnly).TotalCount;
            }

            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = c.CreateCommand())
            {
                var conditions = new List<string>();
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    conditions.Add("(BillNo LIKE @f OR Party LIKE @f OR LRNo LIKE @f OR FromLoc LIKE @f OR ToLoc LIKE @f OR MR LIKE @f OR Remarks LIKE @f)");
                    cmd.Parameters.AddWithValue("@f", $"%{filter}%");
                }
                if (!string.IsNullOrWhiteSpace(party))
                {
                    conditions.Add("Party LIKE @party");
                    cmd.Parameters.AddWithValue("@party", $"%{party.Trim()}%");
                }
                if (dueOnly)
                {
                    conditions.Add("(Freight+Detention+HML+OTHR+StCharge-RCVD-TDS-DED) > 0");
                }
                var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
                cmd.CommandText = $"SELECT COUNT(*) FROM Bills {where};";
                c.Open();
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public void Upsert(BillEntry e)
        {
            if (e == null) return;
            if (BackendSettings.UseRemoteApi)
            {
                if (e.Id <= 0)
                {
                    e.Id = RemoteApiClient.PostAndReadInt("api/bills", e);
                }
                else
                {
                    RemoteApiClient.Put($"api/bills/{e.Id}", e);
                }
                return;
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = c.CreateCommand())
            {
                c.Open();
                if (e.Id <= 0)
                {
                    cmd.CommandText = @"INSERT INTO Bills (Sr, BillNo, BillDate, Party, LRNo, LRDate, FromLoc, ToLoc, VehicleType,
                        Freight, Detention, HML, OTHR, StCharge, RCVD, TDS, DED, MOP, MR, Remarks, Date) VALUES (@Sr,@BillNo,@BillDate,@Party,@LRNo,@LRDate,@FromLoc,@ToLoc,@VehicleType,
                        @Freight,@Detention,@HML,@OTHR,@StCharge,@RCVD,@TDS,@DED,@MOP,@MR,@Remarks,@Date); SELECT last_insert_rowid();";
                }
                else
                {
                    cmd.CommandText = @"UPDATE Bills SET Sr=@Sr, BillNo=@BillNo, BillDate=@BillDate, Party=@Party, LRNo=@LRNo, LRDate=@LRDate,
                        FromLoc=@FromLoc, ToLoc=@ToLoc, VehicleType=@VehicleType, Freight=@Freight, Detention=@Detention,
                        HML=@HML, OTHR=@OTHR, StCharge=@StCharge, RCVD=@RCVD, TDS=@TDS, DED=@DED, MOP=@MOP, MR=@MR, Remarks=@Remarks, Date=@Date WHERE Id=@Id;";
                    cmd.Parameters.AddWithValue("@Id", e.Id);
                }
                cmd.Parameters.AddWithValue("@Sr", e.Sr);
                cmd.Parameters.AddWithValue("@BillNo", (object)e.BillNo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@BillDate", e.BillDate.ToString("o"));
                cmd.Parameters.AddWithValue("@Party", (object)e.Party ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@LRNo", (object)e.LRNo ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@LRDate", e.LRDate?.ToString("o") ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@FromLoc", (object)e.From ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ToLoc", (object)e.To ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@VehicleType", (object)e.VehicleType ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Freight", e.Freight);
                cmd.Parameters.AddWithValue("@Detention", e.Detention);
                cmd.Parameters.AddWithValue("@HML", e.HML);
                cmd.Parameters.AddWithValue("@OTHR", e.OTHR);
                cmd.Parameters.AddWithValue("@StCharge", e.StCharge);
                cmd.Parameters.AddWithValue("@RCVD", e.RCVD);
                cmd.Parameters.AddWithValue("@TDS", e.TDS);
                cmd.Parameters.AddWithValue("@DED", e.DED);
                cmd.Parameters.AddWithValue("@MOP", (object)e.MOP ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@MR", (object)e.MR ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Remarks", (object)e.Remarks ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Date", e.Date.ToString("o"));
                if (e.Id <= 0) e.Id = Convert.ToInt32((long)cmd.ExecuteScalar());
                else cmd.ExecuteNonQuery();
            }
        }

        public void Delete(BillEntry e)
        {
            if (e == null || e.Id <= 0) return;
            if (BackendSettings.UseRemoteApi)
            {
                RemoteApiClient.Delete($"api/bills/{e.Id}");
                return;
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("DELETE FROM Bills WHERE Id=@id;", c))
            {
                cmd.Parameters.AddWithValue("@id", e.Id);
                c.Open();
                cmd.ExecuteNonQuery();
            }
        }

        public HashSet<int> GetBillIdsWithComments()
        {
            var ids = new HashSet<int>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT DISTINCT BillId FROM BillComments;", c))
            {
                c.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) ids.Add(Convert.ToInt32(r["BillId"]));
            }
            return ids;
        }

        private static string BuildOrderBy(string col, bool asc)
        {
            if (string.IsNullOrEmpty(col)) return "Sr, Id";
            var d = asc ? "ASC" : "DESC";
            switch (col.ToLower())
            {
                case "sr": return $"Sr {d}, Id";
                case "billno":
                    return $@"
CASE
    WHEN CAST(STRFTIME('%m', BillDate) AS INTEGER) >= 4 THEN CAST(STRFTIME('%Y', BillDate) AS INTEGER)
    ELSE CAST(STRFTIME('%Y', BillDate) AS INTEGER) - 1
END {d},
CASE
    WHEN INSTR(BillNo, '/') > 0 THEN
        CASE
            WHEN CAST(TRIM(SUBSTR(BillNo, INSTR(BillNo, '/') + 1)) AS INTEGER) > 0
                 OR TRIM(SUBSTR(BillNo, INSTR(BillNo, '/') + 1)) = '0'
                THEN CAST(TRIM(SUBSTR(BillNo, INSTR(BillNo, '/') + 1)) AS INTEGER)
            ELSE CAST(TRIM(SUBSTR(BillNo, 1, INSTR(BillNo, '/') - 1)) AS INTEGER)
        END
    ELSE CAST(BillNo AS INTEGER)
END {d},
BillNo {d}, Sr, Id";
                case "billdate": return $"BillDate {d}, Sr, Id";
                case "party": return $"Party {d}, Sr, Id";
                case "lrno":
                    return $@"
CASE
    WHEN INSTR(LRNo, '/') > 0 THEN SUBSTR(LRNo, INSTR(LRNo, '/') + 1)
    ELSE ''
END {d},
CASE
    WHEN INSTR(LRNo, '/') > 0 THEN CAST(SUBSTR(LRNo, 1, INSTR(LRNo, '/') - 1) AS INTEGER)
    ELSE CAST(LRNo AS INTEGER)
END {d},
LRNo {d}, Sr, Id";
                case "lrdate": return $"LRDate {d}, Sr, Id";
                case "from": return $"FromLoc {d}, Sr, Id";
                case "to": return $"ToLoc {d}, Sr, Id";
                case "vehicletype": return $"VehicleType {d}, Sr, Id";
                case "freight": return $"Freight {d}, Sr, Id";
                case "detention": return $"Detention {d}, Sr, Id";
                case "hml": return $"HML {d}, Sr, Id";
                case "othr": return $"OTHR {d}, Sr, Id";
                case "stcharge": return $"StCharge {d}, Sr, Id";
                case "total": return $"(Freight+Detention+HML+OTHR+StCharge) {d}, Sr, Id";
                case "rcvd": return $"RCVD {d}, Sr, Id";
                case "tds": return $"TDS {d}, Sr, Id";
                case "ded": return $"DED {d}, Sr, Id";
                case "due": return $"(Freight+Detention+HML+OTHR+StCharge-RCVD-TDS-DED) {d}, Sr, Id";
                case "mop": return $"MOP {d}, Sr, Id";
                case "mr": return $"MR {d}, Sr, Id";
                case "remarks": return $"Remarks {d}, Sr, Id";
                case "date": return $"Date {d}, Sr, Id";
                default: return "Sr, Id";
            }
        }

        private static List<BillEntry> GetAllRemoteSafe()
        {
            return RemoteApiClient.GetList<BillEntry>("api/bills")
                .OrderByDescending(x => x.BillDate)
                .ThenByDescending(x => x.Sr)
                .ThenByDescending(x => x.Id)
                .ToList();
        }

        private static RemotePagedResult<BillEntry> GetRemotePage(int pageNumber, int pageSize, string filter, string sortColumn, bool sortAscending, string party = "", bool dueOnly = false)
        {
            var query = $"api/bills/page?page={pageNumber}&pageSize={pageSize}&asc={sortAscending.ToString().ToLowerInvariant()}";
            if (!string.IsNullOrWhiteSpace(filter)) query += $"&search={RemoteApiClient.UrlEncode(filter)}";
            if (!string.IsNullOrWhiteSpace(sortColumn)) query += $"&sort={RemoteApiClient.UrlEncode(sortColumn)}";
            if (!string.IsNullOrWhiteSpace(party)) query += $"&party={RemoteApiClient.UrlEncode(party)}";
            if (dueOnly) query += "&dueOnly=true";
            try
            {
                return RemoteApiClient.GetPage<BillEntry>(query);
            }
            catch
            {
                var filtered = GetAllRemoteSafe().Where(e =>
                    string.IsNullOrWhiteSpace(filter) ||
                    Contains(e.BillNo, filter) ||
                    Contains(e.Party, filter) ||
                    Contains(e.LRNo, filter) ||
                    Contains(e.From, filter) ||
                    Contains(e.To, filter) ||
                    Contains(e.VehicleType, filter) ||
                    Contains(e.MOP, filter) ||
                    Contains(e.MR, filter) ||
                    Contains(e.Remarks, filter));
                if (!string.IsNullOrWhiteSpace(party))
                {
                    filtered = filtered.Where(e => Contains(e.Party, party));
                }
                if (dueOnly)
                {
                    filtered = filtered.Where(e => e.Due > 0m);
                }
                var sorted = ApplySort(filtered, sortColumn, sortAscending).ToList();
                return new RemotePagedResult<BillEntry>
                {
                    TotalCount = sorted.Count,
                    Items = sorted.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList()
                };
            }
        }

        private static IEnumerable<BillEntry> ApplySort(IEnumerable<BillEntry> source, string col, bool asc)
        {
            var ordered = source ?? Enumerable.Empty<BillEntry>();
            Func<BillEntry, object> key;
            switch ((col ?? string.Empty).ToLowerInvariant())
            {
                case "billno": key = x => x.BillNo ?? string.Empty; break;
                case "billdate": key = x => x.BillDate; break;
                case "party": key = x => x.Party ?? string.Empty; break;
                case "lrno": key = x => x.LRNo ?? string.Empty; break;
                case "lrdate": key = x => x.LRDate ?? DateTime.MinValue; break;
                case "from": key = x => x.From ?? string.Empty; break;
                case "to": key = x => x.To ?? string.Empty; break;
                case "vehicletype": key = x => x.VehicleType ?? string.Empty; break;
                case "freight": key = x => x.Freight; break;
                case "detention": key = x => x.Detention; break;
                case "hml": key = x => x.HML; break;
                case "othr": key = x => x.OTHR; break;
                case "stcharge": key = x => x.StCharge; break;
                case "rcvd": key = x => x.RCVD; break;
                case "tds": key = x => x.TDS; break;
                case "ded": key = x => x.DED; break;
                case "mop": key = x => x.MOP ?? string.Empty; break;
                case "mr": key = x => x.MR ?? string.Empty; break;
                case "remarks": key = x => x.Remarks ?? string.Empty; break;
                case "date": key = x => x.Date; break;
                default: key = x => x.Sr; break;
            }
            return asc ? ordered.OrderBy(key).ThenBy(x => x.Id) : ordered.OrderByDescending(key).ThenByDescending(x => x.Id);
        }

        public int GetMaxSr()
        {
            if (BackendSettings.UseRemoteApi)
            {
                return GetAllRemoteSafe().Select(x => x.Sr).DefaultIfEmpty(0).Max();
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT COALESCE(MAX(Sr), 0) FROM Bills;", c))
            {
                c.Open();
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        private static bool Contains(string value, string filter)
            => !string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(filter) && value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

        public decimal GetTotalDue(string filter = "", string party = "", bool dueOnly = false)
        {
            if (BackendSettings.UseRemoteApi)
            {
                return GetRemoteSummary(filter, party, dueOnly).TotalDue;
            }

            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = c.CreateCommand())
            {
                var conditions = new List<string>();
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    conditions.Add("(BillNo LIKE @f OR Party LIKE @f OR LRNo LIKE @f OR FromLoc LIKE @f OR ToLoc LIKE @f OR MR LIKE @f OR Remarks LIKE @f)");
                    cmd.Parameters.AddWithValue("@f", $"%{filter}%");
                }
                if (!string.IsNullOrWhiteSpace(party))
                {
                    conditions.Add("Party LIKE @party");
                    cmd.Parameters.AddWithValue("@party", $"%{party.Trim()}%");
                }
                if (dueOnly)
                {
                    conditions.Add("(Freight+Detention+HML+OTHR+StCharge-RCVD-TDS-DED) > 0");
                }

                var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
                cmd.CommandText = $"SELECT COALESCE(SUM(Freight+Detention+HML+OTHR+StCharge-RCVD-TDS-DED), 0) FROM Bills {where};";
                c.Open();
                return Convert.ToDecimal(cmd.ExecuteScalar());
            }
        }

        private static RemoteBillSummary GetRemoteSummary(string filter = "", string party = "", bool dueOnly = false)
        {
            var query = "api/bills/summary";
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(filter)) parts.Add($"search={RemoteApiClient.UrlEncode(filter)}");
            if (!string.IsNullOrWhiteSpace(party)) parts.Add($"party={RemoteApiClient.UrlEncode(party)}");
            if (dueOnly) parts.Add("dueOnly=true");
            if (parts.Count > 0) query += "?" + string.Join("&", parts);

            try
            {
                return RemoteApiClient.Get<RemoteBillSummary>(query);
            }
            catch
            {
                var rows = GetAllRemoteSafe().Where(e =>
                    string.IsNullOrWhiteSpace(filter) ||
                    Contains(e.BillNo, filter) ||
                    Contains(e.Party, filter) ||
                    Contains(e.LRNo, filter) ||
                    Contains(e.From, filter) ||
                    Contains(e.To, filter) ||
                    Contains(e.MR, filter) ||
                    Contains(e.Remarks, filter));
                if (!string.IsNullOrWhiteSpace(party))
                {
                    rows = rows.Where(e => Contains(e.Party, party));
                }
                if (dueOnly)
                {
                    rows = rows.Where(e => e.Due > 0m);
                }
                var list = rows.ToList();
                return new RemoteBillSummary
                {
                    TotalCount = list.Count,
                    TotalDue = list.Sum(x => x?.Due ?? 0m)
                };
            }
        }
    }
}
