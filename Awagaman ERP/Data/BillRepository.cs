using System;
using System.Collections.Generic;
using System.Data.SQLite;
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

        public List<BillEntry> Search(string filter, int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true)
        {
            var list = new List<BillEntry>();
            int offset = (pageNumber - 1) * pageSize;
            string orderBy = BuildOrderBy(sortColumn, sortAscending);
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand(
                $"SELECT * FROM Bills WHERE BillNo LIKE @f OR Party LIKE @f OR LRNo LIKE @f OR FromLoc LIKE @f OR ToLoc LIKE @f OR MR LIKE @f OR Remarks LIKE @f ORDER BY {orderBy} LIMIT @lim OFFSET @off;", c))
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

        public int GetTotalCount(string filter = "")
        {
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand(
                string.IsNullOrWhiteSpace(filter) ? "SELECT COUNT(*) FROM Bills;"
                : "SELECT COUNT(*) FROM Bills WHERE BillNo LIKE @f OR Party LIKE @f OR LRNo LIKE @f OR FromLoc LIKE @f OR ToLoc LIKE @f OR MR LIKE @f OR Remarks LIKE @f;", c))
            {
                if (!string.IsNullOrWhiteSpace(filter)) cmd.Parameters.AddWithValue("@f", $"%{filter}%");
                c.Open();
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public void Upsert(BillEntry e)
        {
            if (e == null) return;
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = c.CreateCommand())
            {
                c.Open();
                if (e.Id <= 0)
                {
                    cmd.CommandText = @"INSERT INTO Bills (Sr, BillNo, BillDate, Party, LRNo, LRDate, FromLoc, ToLoc, VehicleType,
                        Freight, Detention, HML, OTHR, RCVD, TDS, DED, MOP, MR, Remarks, Date) VALUES (@Sr,@BillNo,@BillDate,@Party,@LRNo,@LRDate,@FromLoc,@ToLoc,@VehicleType,
                        @Freight,@Detention,@HML,@OTHR,@RCVD,@TDS,@DED,@MOP,@MR,@Remarks,@Date); SELECT last_insert_rowid();";
                }
                else
                {
                    cmd.CommandText = @"UPDATE Bills SET Sr=@Sr, BillNo=@BillNo, BillDate=@BillDate, Party=@Party, LRNo=@LRNo, LRDate=@LRDate,
                        FromLoc=@FromLoc, ToLoc=@ToLoc, VehicleType=@VehicleType, Freight=@Freight, Detention=@Detention,
                        HML=@HML, OTHR=@OTHR, RCVD=@RCVD, TDS=@TDS, DED=@DED, MOP=@MOP, MR=@MR, Remarks=@Remarks, Date=@Date WHERE Id=@Id;";
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
    WHEN INSTR(BillNo, '/') > 0 THEN SUBSTR(BillNo, INSTR(BillNo, '/') + 1)
    ELSE ''
END {d},
CASE
    WHEN INSTR(BillNo, '/') > 0 THEN CAST(SUBSTR(BillNo, 1, INSTR(BillNo, '/') - 1) AS INTEGER)
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
                case "total": return $"(Freight+Detention+HML+OTHR) {d}, Sr, Id";
                case "rcvd": return $"RCVD {d}, Sr, Id";
                case "tds": return $"TDS {d}, Sr, Id";
                case "ded": return $"DED {d}, Sr, Id";
                case "due": return $"(Freight+Detention+HML+OTHR-RCVD-TDS-DED) {d}, Sr, Id";
                case "mop": return $"MOP {d}, Sr, Id";
                case "mr": return $"MR {d}, Sr, Id";
                case "remarks": return $"Remarks {d}, Sr, Id";
                case "date": return $"Date {d}, Sr, Id";
                default: return "Sr, Id";
            }
        }
    }
}
