using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    public sealed class LRRepository : ILRRepository
    {
        public LRRepository()
        {
            AppDatabase.EnsureInitialized();
        }

        public List<LREntry> GetAll()
        {
            var entries = new List<LREntry>();

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand("SELECT * FROM LREntries ORDER BY Sr, Id;", connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        entries.Add(ReadEntry(reader));
                    }
                }
            }

            return entries;
        }

        public List<LREntry> GetPage(int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true)
        {
            var entries = new List<LREntry>();
            int offset = (pageNumber - 1) * pageSize;
            string orderBy = BuildOrderBy(sortColumn, sortAscending);
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand($"SELECT * FROM LREntries ORDER BY {orderBy} LIMIT @limit OFFSET @offset;", connection))
            {
                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", offset);
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        entries.Add(ReadEntry(reader));
                    }
                }
            }
            return entries;
        }

        public List<LREntry> Search(string searchFilter, int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true)
        {
            var entries = new List<LREntry>();
            int offset = (pageNumber - 1) * pageSize;
            string orderBy = BuildOrderBy(sortColumn, sortAscending);
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT * FROM LREntries WHERE LRNo LIKE @f OR ConsignorName LIKE @f OR ConsigneeName LIKE @f OR VehicleNo LIKE @f OR BillNo LIKE @f OR CHNo LIKE @f ORDER BY {orderBy} LIMIT @limit OFFSET @offset;";
                command.Parameters.AddWithValue("@f", $"%{searchFilter}%");
                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", offset);
                connection.Open();
                using (var reader = command.ExecuteReader())
                    while (reader.Read()) entries.Add(ReadEntry(reader));
            }
            return entries;
        }

        public int GetTotalCount() => GetTotalCount("");

        public int GetTotalCount(string searchFilter)
        {
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                if (string.IsNullOrWhiteSpace(searchFilter))
                    command.CommandText = "SELECT COUNT(*) FROM LREntries;";
                else
                {
                    command.CommandText = "SELECT COUNT(*) FROM LREntries WHERE LRNo LIKE @f OR ConsignorName LIKE @f OR ConsigneeName LIKE @f OR VehicleNo LIKE @f OR BillNo LIKE @f OR CHNo LIKE @f;";
                    command.Parameters.AddWithValue("@f", $"%{searchFilter}%");
                }
                connection.Open();
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public int GetMaxSr()
        {
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand("SELECT COALESCE(MAX(Sr), 0) FROM LREntries;", connection))
            {
                connection.Open();
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        private static LREntry ReadEntry(System.Data.SQLite.SQLiteDataReader reader)
        {
            return new LREntry
            {
                Id = Convert.ToInt32(reader["Id"]),
                Sr = Convert.ToInt32(reader["Sr"]),
                LRNo = reader["LRNo"] as string,
                Date = ParseDate(reader["Date"], DateTime.Today),
                ConsignorName = reader["ConsignorName"] as string,
                ConsignorAddress = reader["ConsignorAddress"] as string,
                ConsignorGST = reader["ConsignorGST"] as string,
                ConsigneeName = reader["ConsigneeName"] as string,
                ConsigneeAddress = reader["ConsigneeAddress"] as string,
                ConsigneeGST = reader["ConsigneeGST"] as string,
                From = reader["FromLocation"] as string,
                To = reader["ToLocation"] as string,
                VehicleNo = reader["VehicleNo"] as string,
                VehicleType = reader["VehicleType"] as string,
                Weight = Convert.ToDecimal(reader["Weight"]),
                PKG = Convert.ToInt32(reader["PKG"]),
                Description = reader["Description"] as string,
                Invoice = reader["Invoice"] as string,
                CHNo = reader["CHNo"] as string,
                TotalFreight = Convert.ToDecimal(reader["TotalFreight"]),
                Hamali = Convert.ToDecimal(reader["Hamali"]),
                Detention = Convert.ToDecimal(reader["Detention"]),
                Others = Convert.ToDecimal(reader["Others"]),
                NEFT = Convert.ToDecimal(reader["NEFT"]),
                CASH = Convert.ToDecimal(reader["CASH"]),
                TDS = Convert.ToDecimal(reader["TDS"]),
                Ded = Convert.ToDecimal(reader["Ded"]),
                BillNo = reader["BillNo"] as string,
                BillDate = ParseNullableDate(reader["BillDate"]),
                BILL = Convert.ToDecimal(reader["BILL"]),
                BillParty = reader["BillParty"] as string,
                Broker = reader["Broker"] as string,
                FrtType = reader["FrtType"] as string,
                Comm = Convert.ToDecimal(reader["Comm"]),
                Paid = reader["Paid"] as string
            };
        }

        public void Upsert(LREntry entry)
        {
            if (entry == null)
            {
                return;
            }

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                connection.Open();

                if (entry.Id <= 0)
                {
                    command.CommandText = @"
INSERT INTO LREntries (
    Sr, LRNo, Date, ConsignorName, ConsignorAddress, ConsignorGST,
    ConsigneeName, ConsigneeAddress, ConsigneeGST, FromLocation, ToLocation,
    VehicleNo, VehicleType, Weight, PKG, Description, Invoice, CHNo,
    TotalFreight, Hamali, Detention, Others, NEFT, CASH, TDS, Ded, BillNo, BillDate, BILL, BillParty, Broker, FrtType, Comm, Paid
) VALUES (
    @Sr, @LRNo, @Date, @ConsignorName, @ConsignorAddress, @ConsignorGST,
    @ConsigneeName, @ConsigneeAddress, @ConsigneeGST, @FromLocation, @ToLocation,
    @VehicleNo, @VehicleType, @Weight, @PKG, @Description, @Invoice, @CHNo,
    @TotalFreight, @Hamali, @Detention, @Others, @NEFT, @CASH, @TDS, @Ded, @BillNo, @BillDate, @BILL, @BillParty, @Broker, @FrtType, @Comm, @Paid
);
SELECT last_insert_rowid();";
                    AddParameters(command, entry);
                    entry.Id = Convert.ToInt32((long)command.ExecuteScalar());
                }
                else
                {
                    command.CommandText = @"
UPDATE LREntries SET
    Sr = @Sr,
    LRNo = @LRNo,
    Date = @Date,
    ConsignorName = @ConsignorName,
    ConsignorAddress = @ConsignorAddress,
    ConsignorGST = @ConsignorGST,
    ConsigneeName = @ConsigneeName,
    ConsigneeAddress = @ConsigneeAddress,
    ConsigneeGST = @ConsigneeGST,
    FromLocation = @FromLocation,
    ToLocation = @ToLocation,
    VehicleNo = @VehicleNo,
    VehicleType = @VehicleType,
    Weight = @Weight,
    PKG = @PKG,
    Description = @Description,
    Invoice = @Invoice,
    CHNo = @CHNo,
    TotalFreight = @TotalFreight,
    Hamali = @Hamali,
    Detention = @Detention,
    Others = @Others,
    NEFT = @NEFT,
    CASH = @CASH,
    TDS = @TDS,
    Ded = @Ded,
    BillNo = @BillNo,
    BillDate = @BillDate,
    BILL = @BILL,
    BillParty = @BillParty,
    Broker = @Broker,
    FrtType = @FrtType,
    Comm = @Comm,
    Paid = @Paid
WHERE Id = @Id;";
                    AddParameters(command, entry);
                    command.Parameters.AddWithValue("@Id", entry.Id);
                    command.ExecuteNonQuery();
                }
            }
        }

        public void Delete(LREntry entry)
        {
            if (entry == null)
            {
                return;
            }

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                if (entry.Id > 0)
                {
                    command.CommandText = "DELETE FROM LREntries WHERE Id = @Id;";
                    command.Parameters.AddWithValue("@Id", entry.Id);
                }
                else
                {
                    command.CommandText = "DELETE FROM LREntries WHERE LRNo = @LRNo;";
                    command.Parameters.AddWithValue("@LRNo", entry.LRNo ?? string.Empty);
                }

                command.ExecuteNonQuery();
            }
        }

        private static void AddParameters(SQLiteCommand command, LREntry entry)
        {
            command.Parameters.AddWithValue("@Sr", entry.Sr);
            command.Parameters.AddWithValue("@LRNo", entry.LRNo ?? string.Empty);
            command.Parameters.AddWithValue("@Date", entry.Date.ToString("o"));
            command.Parameters.AddWithValue("@ConsignorName", (object)entry.ConsignorName ?? DBNull.Value);
            command.Parameters.AddWithValue("@ConsignorAddress", (object)entry.ConsignorAddress ?? DBNull.Value);
            command.Parameters.AddWithValue("@ConsignorGST", (object)entry.ConsignorGST ?? DBNull.Value);
            command.Parameters.AddWithValue("@ConsigneeName", (object)entry.ConsigneeName ?? DBNull.Value);
            command.Parameters.AddWithValue("@ConsigneeAddress", (object)entry.ConsigneeAddress ?? DBNull.Value);
            command.Parameters.AddWithValue("@ConsigneeGST", (object)entry.ConsigneeGST ?? DBNull.Value);
            command.Parameters.AddWithValue("@FromLocation", (object)entry.From ?? DBNull.Value);
            command.Parameters.AddWithValue("@ToLocation", (object)entry.To ?? DBNull.Value);
            command.Parameters.AddWithValue("@VehicleNo", (object)entry.VehicleNo ?? DBNull.Value);
            command.Parameters.AddWithValue("@VehicleType", (object)entry.VehicleType ?? DBNull.Value);
            command.Parameters.AddWithValue("@Weight", entry.Weight);
            command.Parameters.AddWithValue("@PKG", entry.PKG);
            command.Parameters.AddWithValue("@Description", (object)entry.Description ?? DBNull.Value);
            command.Parameters.AddWithValue("@Invoice", (object)entry.Invoice ?? DBNull.Value);
            command.Parameters.AddWithValue("@CHNo", (object)entry.CHNo ?? DBNull.Value);
            command.Parameters.AddWithValue("@TotalFreight", entry.TotalFreight);
            command.Parameters.AddWithValue("@Hamali", entry.Hamali);
            command.Parameters.AddWithValue("@Detention", entry.Detention);
            command.Parameters.AddWithValue("@Others", entry.Others);
            command.Parameters.AddWithValue("@NEFT", entry.NEFT);
            command.Parameters.AddWithValue("@CASH", entry.CASH);
            command.Parameters.AddWithValue("@TDS", entry.TDS);
            command.Parameters.AddWithValue("@Ded", entry.Ded);
            command.Parameters.AddWithValue("@BillNo", (object)entry.BillNo ?? DBNull.Value);
            command.Parameters.AddWithValue("@BillDate", entry.BillDate.HasValue ? (object)entry.BillDate.Value.ToString("o") : DBNull.Value);
            command.Parameters.AddWithValue("@BILL", entry.BILL);
            command.Parameters.AddWithValue("@BillParty", (object)entry.BillParty ?? DBNull.Value);
            command.Parameters.AddWithValue("@Broker", (object)entry.Broker ?? DBNull.Value);
            command.Parameters.AddWithValue("@FrtType", (object)entry.FrtType ?? DBNull.Value);
            command.Parameters.AddWithValue("@Comm", entry.Comm);
            command.Parameters.AddWithValue("@Paid", (object)entry.Paid ?? DBNull.Value);
        }

        private static DateTime ParseDate(object value, DateTime fallback)
        {
            var raw = value as string;
            return DateTime.TryParse(raw, out var parsed) ? parsed : fallback;
        }

        private static DateTime? ParseNullableDate(object value)
        {
            var raw = value as string;
            return DateTime.TryParse(raw, out var parsed) ? parsed : (DateTime?)null;
        }

        private static string BuildOrderBy(string sortColumn, bool ascending)
        {
            if (string.IsNullOrEmpty(sortColumn)) return "Sr, Id";
            var dir = ascending ? "ASC" : "DESC";
            switch (sortColumn.ToLower())
            {
                case "sr": return $"Sr {dir}, Id";
                case "lrno":
                    return $@"
CASE
    WHEN INSTR(LRNo, '/') > 0 THEN SUBSTR(LRNo, INSTR(LRNo, '/') + 1)
    ELSE ''
END {dir},
CASE
    WHEN INSTR(LRNo, '/') > 0 THEN CAST(SUBSTR(LRNo, 1, INSTR(LRNo, '/') - 1) AS INTEGER)
    ELSE CAST(LRNo AS INTEGER)
END {dir},
LRNo {dir}, Sr, Id";
                case "date": return $"Date {dir}, Sr, Id";
                case "consignorname": return $"ConsignorName {dir}, Sr, Id";
                case "consignoraddress": return $"ConsignorAddress {dir}, Sr, Id";
                case "consignorgst": return $"ConsignorGST {dir}, Sr, Id";
                case "consigneename": return $"ConsigneeName {dir}, Sr, Id";
                case "consigneeaddress": return $"ConsigneeAddress {dir}, Sr, Id";
                case "consigneegst": return $"ConsigneeGST {dir}, Sr, Id";
                case "from": return $"FromLocation {dir}, Sr, Id";
                case "to": return $"ToLocation {dir}, Sr, Id";
                case "vehicleno": return $"VehicleNo {dir}, Sr, Id";
                case "vehicletype": return $"VehicleType {dir}, Sr, Id";
                case "weight": return $"Weight {dir}, Sr, Id";
                case "pkg": return $"PKG {dir}, Sr, Id";
                case "description": return $"Description {dir}, Sr, Id";
                case "invoice": return $"Invoice {dir}, Sr, Id";
                case "chno": return $"CHNo {dir}, Sr, Id";
                case "totalfreight": return $"TotalFreight {dir}, Sr, Id";
                case "hamali": return $"Hamali {dir}, Sr, Id";
                case "detention": return $"Detention {dir}, Sr, Id";
                case "others": return $"Others {dir}, Sr, Id";
                case "totalbill": return $"(TotalFreight + Detention + Hamali + Others) {dir}, Sr, Id";
                case "neft": return $"NEFT {dir}, Sr, Id";
                case "cash": return $"CASH {dir}, Sr, Id";
                case "tds": return $"TDS {dir}, Sr, Id";
                case "ded": return $"Ded {dir}, Sr, Id";
                case "bal": return $"(NEFT + CASH - TDS + Ded) {dir}, Sr, Id";
                case "billno": return $"BillNo {dir}, Sr, Id";
                case "billdate": return $"BillDate {dir}, Sr, Id";
                case "bill": return $"BILL {dir}, Sr, Id";
                case "billparty": return $"BillParty {dir}, Sr, Id";
                case "broker": return $"Broker {dir}, Sr, Id";
                case "frttype": return $"FrtType {dir}, Sr, Id";
                case "comm": return $"Comm {dir}, Sr, Id";
                case "paid": return $"Paid {dir}, Sr, Id";
                default: return "Sr, Id";
            }
        }
    }
}
