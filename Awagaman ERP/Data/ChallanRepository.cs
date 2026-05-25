using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    public sealed class ChallanRepository : IChallanRepository
    {
        public ChallanRepository()
        {
            AppDatabase.EnsureInitialized();
            MigrateLegacyXmlIfNeeded();
        }

        public List<ChallanEntry> GetAll()
        {
            var entries = new List<ChallanEntry>();

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand("SELECT * FROM Challans ORDER BY Sr, Id;", connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var entry = new ChallanEntry
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Sr = Convert.ToInt32(reader["Sr"]),
                            ChallanNumber = reader["ChallanNumber"] as string,
                            Date = ParseDate(reader["Date"], DateTime.Today),
                            LRNumber = reader["LRNumber"] as string,
                            BrokerName = reader["BrokerName"] as string,
                            From = reader["FromLocation"] as string,
                            To = reader["ToLocation"] as string,
                            VehicleNumber = reader["VehicleNumber"] as string,
                            VehicleType = reader["VehicleType"] as string,
                            DriverName = reader["DriverName"] as string,
                            DriverMobile = reader["DriverMobile"] as string,
                            EngineNo = reader["EngineNo"] as string,
                            LicenceNo = reader["LicenceNo"] as string,
                            PolicyNo = reader["PolicyNo"] as string,
                            ChassisNo = reader["ChassisNo"] as string,
                            OwnerName = reader["OwnerName"] as string,
                            PAN = reader["PAN"] as string,
                            LorryHire = GetDecimal(reader["LorryHire"]),
                            LessTDS = GetDecimal(reader["LessTDS"]),
                            AdvanceAmount = GetDecimal(reader["AdvanceAmount"]),
                            AdvanceNEFT = GetDecimal(reader["AdvanceNEFT"]),
                            AdvanceCash = GetDecimal(reader["AdvanceCash"]),
                            AdvanceDate = ParseNullableDate(reader["AdvanceDate"]),
                            Detention = GetDecimal(reader["Detention"]),
                            Hamali = GetDecimal(reader["Hamali"]),
                            Deduction = GetDecimal(reader["Deduction"]),
                            BalancePaidNEFT = GetDecimal(reader["BalancePaidNEFT"]),
                            BalancePaidCash = GetDecimal(reader["BalancePaidCash"]),
                            BalancePaidDate = ParseNullableDate(reader["BalancePaidDate"]),
                            PaidTo = reader["PaidTo"] as string,
                            Remarks = reader["Remarks"] as string,
                            BillAmount = GetDecimal(reader["BillAmount"]),
                            Margin = GetDecimal(reader["Margin"])
                        };

                        entry.RecalculateBalance();
                        entries.Add(entry);
                    }
                }
            }

            return entries;
        }

        public List<ChallanEntry> GetPage(int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true)
        {
            var entries = new List<ChallanEntry>();
            int offset = (pageNumber - 1) * pageSize;
            string orderBy = BuildOrderBy(sortColumn, sortAscending);

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand($"SELECT * FROM Challans ORDER BY {orderBy} LIMIT @limit OFFSET @offset;", connection))
            {
                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", offset);
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        entries.Add(new ChallanEntry
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Sr = Convert.ToInt32(reader["Sr"]),
                            ChallanNumber = reader["ChallanNumber"] as string,
                            Date = ParseDate(reader["Date"], DateTime.Today),
                            LRNumber = reader["LRNumber"] as string,
                            BrokerName = reader["BrokerName"] as string,
                            From = reader["FromLocation"] as string,
                            To = reader["ToLocation"] as string,
                            VehicleNumber = reader["VehicleNumber"] as string,
                            VehicleType = reader["VehicleType"] as string,
                            DriverName = reader["DriverName"] as string,
                            DriverMobile = reader["DriverMobile"] as string,
                            EngineNo = reader["EngineNo"] as string,
                            LicenceNo = reader["LicenceNo"] as string,
                            PolicyNo = reader["PolicyNo"] as string,
                            ChassisNo = reader["ChassisNo"] as string,
                            OwnerName = reader["OwnerName"] as string,
                            PAN = reader["PAN"] as string,
                            LorryHire = GetDecimal(reader["LorryHire"]),
                            LessTDS = GetDecimal(reader["LessTDS"]),
                            AdvanceAmount = GetDecimal(reader["AdvanceAmount"]),
                            AdvanceNEFT = GetDecimal(reader["AdvanceNEFT"]),
                            AdvanceCash = GetDecimal(reader["AdvanceCash"]),
                            AdvanceDate = ParseNullableDate(reader["AdvanceDate"]),
                            Detention = GetDecimal(reader["Detention"]),
                            Hamali = GetDecimal(reader["Hamali"]),
                            Deduction = GetDecimal(reader["Deduction"]),
                            BalancePaidNEFT = GetDecimal(reader["BalancePaidNEFT"]),
                            BalancePaidCash = GetDecimal(reader["BalancePaidCash"]),
                            BalancePaidDate = ParseNullableDate(reader["BalancePaidDate"]),
                            PaidTo = reader["PaidTo"] as string,
                            Remarks = reader["Remarks"] as string,
                            BillAmount = GetDecimal(reader["BillAmount"]),
                            Margin = GetDecimal(reader["Margin"])
                        });
                    }
                }
            }
            foreach (var entry in entries) entry.RecalculateBalance();
            return entries;
        }

        public List<ChallanEntry> Search(string searchFilter, int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true)
        {
            var entries = new List<ChallanEntry>();
            int offset = (pageNumber - 1) * pageSize;

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                var orderBy = BuildOrderBy(sortColumn, sortAscending);
                command.CommandText = $@"SELECT * FROM Challans WHERE 
                    ChallanNumber LIKE @filter OR LRNumber LIKE @filter OR VehicleNumber LIKE @filter 
                    OR VehicleType LIKE @filter OR DriverName LIKE @filter OR BrokerName LIKE @filter 
                    OR FromLocation LIKE @filter OR ToLocation LIKE @filter OR OwnerName LIKE @filter 
                    ORDER BY {orderBy} LIMIT @limit OFFSET @offset;";
                command.Parameters.AddWithValue("@filter", $"%{searchFilter}%");
                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", offset);
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        entries.Add(new ChallanEntry
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Sr = Convert.ToInt32(reader["Sr"]),
                            ChallanNumber = reader["ChallanNumber"] as string,
                            Date = ParseDate(reader["Date"], DateTime.Today),
                            LRNumber = reader["LRNumber"] as string,
                            BrokerName = reader["BrokerName"] as string,
                            From = reader["FromLocation"] as string,
                            To = reader["ToLocation"] as string,
                            VehicleNumber = reader["VehicleNumber"] as string,
                            VehicleType = reader["VehicleType"] as string,
                            DriverName = reader["DriverName"] as string,
                            DriverMobile = reader["DriverMobile"] as string,
                            EngineNo = reader["EngineNo"] as string,
                            LicenceNo = reader["LicenceNo"] as string,
                            PolicyNo = reader["PolicyNo"] as string,
                            ChassisNo = reader["ChassisNo"] as string,
                            OwnerName = reader["OwnerName"] as string,
                            PAN = reader["PAN"] as string,
                            LorryHire = GetDecimal(reader["LorryHire"]),
                            LessTDS = GetDecimal(reader["LessTDS"]),
                            AdvanceAmount = GetDecimal(reader["AdvanceAmount"]),
                            AdvanceNEFT = GetDecimal(reader["AdvanceNEFT"]),
                            AdvanceCash = GetDecimal(reader["AdvanceCash"]),
                            AdvanceDate = ParseNullableDate(reader["AdvanceDate"]),
                            Detention = GetDecimal(reader["Detention"]),
                            Hamali = GetDecimal(reader["Hamali"]),
                            Deduction = GetDecimal(reader["Deduction"]),
                            BalancePaidNEFT = GetDecimal(reader["BalancePaidNEFT"]),
                            BalancePaidCash = GetDecimal(reader["BalancePaidCash"]),
                            BalancePaidDate = ParseNullableDate(reader["BalancePaidDate"]),
                            PaidTo = reader["PaidTo"] as string,
                            Remarks = reader["Remarks"] as string,
                            BillAmount = GetDecimal(reader["BillAmount"]),
                            Margin = GetDecimal(reader["Margin"])
                        });
                    }
                }
            }
            foreach (var entry in entries) entry.RecalculateBalance();
            return entries;
        }

        public List<ChallanEntry> SearchAdvanced(string challanNo, string lrNo, string from, string to, int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true)
        {
            var entries = new List<ChallanEntry>();
            int offset = (pageNumber - 1) * pageSize;
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                var orderBy = BuildOrderBy(sortColumn, sortAscending);
                var conditions = new List<string>();
                if (!string.IsNullOrWhiteSpace(challanNo)) { conditions.Add("ChallanNumber LIKE @challanNo"); command.Parameters.AddWithValue("@challanNo", $"%{challanNo}%"); }
                if (!string.IsNullOrWhiteSpace(lrNo)) { conditions.Add("LRNumber LIKE @lrNo"); command.Parameters.AddWithValue("@lrNo", $"%{lrNo}%"); }
                if (!string.IsNullOrWhiteSpace(from)) { conditions.Add("FromLocation LIKE @from"); command.Parameters.AddWithValue("@from", $"%{from}%"); }
                if (!string.IsNullOrWhiteSpace(to)) { conditions.Add("ToLocation LIKE @to"); command.Parameters.AddWithValue("@to", $"%{to}%"); }
                string where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
                command.CommandText = $"SELECT * FROM Challans {where} ORDER BY {orderBy} LIMIT @limit OFFSET @offset;";
                command.Parameters.AddWithValue("@limit", pageSize);
                command.Parameters.AddWithValue("@offset", offset);
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        entries.Add(MapReader(reader));
                }
            }
            foreach (var entry in entries) entry.RecalculateBalance();
            return entries;
        }

        public int GetTotalCountAdvanced(string challanNo, string lrNo, string from, string to)
        {
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                var conditions = new List<string>();
                if (!string.IsNullOrWhiteSpace(challanNo)) { conditions.Add("ChallanNumber LIKE @challanNo"); command.Parameters.AddWithValue("@challanNo", $"%{challanNo}%"); }
                if (!string.IsNullOrWhiteSpace(lrNo)) { conditions.Add("LRNumber LIKE @lrNo"); command.Parameters.AddWithValue("@lrNo", $"%{lrNo}%"); }
                if (!string.IsNullOrWhiteSpace(from)) { conditions.Add("FromLocation LIKE @from"); command.Parameters.AddWithValue("@from", $"%{from}%"); }
                if (!string.IsNullOrWhiteSpace(to)) { conditions.Add("ToLocation LIKE @to"); command.Parameters.AddWithValue("@to", $"%{to}%"); }
                string where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
                command.CommandText = $"SELECT COUNT(*) FROM Challans {where};";
                connection.Open();
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public int GetTotalCount()
        {
            return GetTotalCount("");
        }

        public int GetTotalCount(string searchFilter)
        {
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                if (string.IsNullOrWhiteSpace(searchFilter))
                {
                    command.CommandText = "SELECT COUNT(*) FROM Challans;";
                }
                else
                {
                    command.CommandText = @"SELECT COUNT(*) FROM Challans WHERE 
                        ChallanNumber LIKE @filter OR LRNumber LIKE @filter OR VehicleNumber LIKE @filter 
                        OR VehicleType LIKE @filter OR DriverName LIKE @filter OR BrokerName LIKE @filter 
                        OR FromLocation LIKE @filter OR ToLocation LIKE @filter OR OwnerName LIKE @filter;";
                    command.Parameters.AddWithValue("@filter", $"%{searchFilter}%");
                }
                connection.Open();
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public int GetMaxSr()
        {
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand("SELECT COALESCE(MAX(Sr), 0) FROM Challans;", connection))
            {
                connection.Open();
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public ChallanEntry FindByChallanNumber(string challanNumber)
        {
            if (string.IsNullOrWhiteSpace(challanNumber)) return null;
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand("SELECT * FROM Challans WHERE LOWER(ChallanNumber) = LOWER(@num) LIMIT 1;", connection))
            {
                command.Parameters.AddWithValue("@num", challanNumber.Trim());
                connection.Open();
                using (var reader = command.ExecuteReader())
                    if (reader.Read()) return MapReader(reader);
            }
            return null;
        }

        public HashSet<int> GetChallanIdsWithComments()
        {
            var ids = new HashSet<int>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT DISTINCT ChallanId FROM ChallanComments;", c))
            {
                c.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) ids.Add(Convert.ToInt32(r["ChallanId"]));
            }
            return ids;
        }

        public void Upsert(ChallanEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            entry.RecalculateBalance();

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                connection.Open();

                if (entry.Id <= 0)
                {
                    command.CommandText = @"
INSERT INTO Challans (
    Sr, ChallanNumber, Date, LRNumber, BrokerName, FromLocation, ToLocation, VehicleNumber, VehicleType,
    DriverName, DriverMobile, EngineNo, LicenceNo, PolicyNo, ChassisNo, OwnerName, PAN,
    LorryHire, LessTDS, AdvanceAmount, AdvanceNEFT, AdvanceCash, AdvanceDate,
    Detention, Hamali, Deduction, BalancePaidNEFT, BalancePaidCash, BalancePaidDate,
    PaidTo, Remarks, BillAmount, Margin
) VALUES (
    @Sr, @ChallanNumber, @Date, @LRNumber, @BrokerName, @FromLocation, @ToLocation, @VehicleNumber, @VehicleType,
    @DriverName, @DriverMobile, @EngineNo, @LicenceNo, @PolicyNo, @ChassisNo, @OwnerName, @PAN,
    @LorryHire, @LessTDS, @AdvanceAmount, @AdvanceNEFT, @AdvanceCash, @AdvanceDate,
    @Detention, @Hamali, @Deduction, @BalancePaidNEFT, @BalancePaidCash, @BalancePaidDate,
    @PaidTo, @Remarks, @BillAmount, @Margin
);
SELECT last_insert_rowid();";
                    AddParameters(command, entry);
                    entry.Id = Convert.ToInt32((long)command.ExecuteScalar());
                }
                else
                {
                    command.CommandText = @"
UPDATE Challans SET
    Sr = @Sr,
    ChallanNumber = @ChallanNumber,
    Date = @Date,
    LRNumber = @LRNumber,
    BrokerName = @BrokerName,
    FromLocation = @FromLocation,
    ToLocation = @ToLocation,
    VehicleNumber = @VehicleNumber,
    VehicleType = @VehicleType,
    DriverName = @DriverName,
    DriverMobile = @DriverMobile,
    EngineNo = @EngineNo,
    LicenceNo = @LicenceNo,
    PolicyNo = @PolicyNo,
    ChassisNo = @ChassisNo,
    OwnerName = @OwnerName,
    PAN = @PAN,
    LorryHire = @LorryHire,
    LessTDS = @LessTDS,
    AdvanceAmount = @AdvanceAmount,
    AdvanceNEFT = @AdvanceNEFT,
    AdvanceCash = @AdvanceCash,
    AdvanceDate = @AdvanceDate,
    Detention = @Detention,
    Hamali = @Hamali,
    Deduction = @Deduction,
    BalancePaidNEFT = @BalancePaidNEFT,
    BalancePaidCash = @BalancePaidCash,
    BalancePaidDate = @BalancePaidDate,
    PaidTo = @PaidTo,
    Remarks = @Remarks,
    BillAmount = @BillAmount,
    Margin = @Margin
WHERE Id = @Id;";
                    AddParameters(command, entry);
                    command.Parameters.AddWithValue("@Id", entry.Id);
                    command.ExecuteNonQuery();
                }
            }

            try
            {
                new VehicleRepository().UpsertFromChallan(entry);
            }
            catch { }
        }

        public void Delete(ChallanEntry entry)
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
                    command.CommandText = "DELETE FROM Challans WHERE Id = @Id;";
                    command.Parameters.AddWithValue("@Id", entry.Id);
                }
                else
                {
                    command.CommandText = "DELETE FROM Challans WHERE ChallanNumber = @ChallanNumber;";
                    command.Parameters.AddWithValue("@ChallanNumber", entry.ChallanNumber ?? string.Empty);
                }

                command.ExecuteNonQuery();
            }
        }

        private void MigrateLegacyXmlIfNeeded()
        {
            if (HasRows())
            {
                return;
            }

            var legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "challan_data.xml");
            if (!File.Exists(legacyPath))
            {
                return;
            }

            try
            {
                var serializer = new XmlSerializer(typeof(ObservableCollection<ChallanEntry>));
                ObservableCollection<ChallanEntry> legacyEntries;

                using (var reader = new StreamReader(legacyPath))
                {
                    legacyEntries = (ObservableCollection<ChallanEntry>)serializer.Deserialize(reader);
                }

                foreach (var entry in legacyEntries ?? Enumerable.Empty<ChallanEntry>())
                {
                    Upsert(entry);
                }
            }
            catch
            {
                // Leave the database empty if legacy migration fails.
            }
        }

        private bool HasRows()
        {
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand("SELECT COUNT(1) FROM Challans;", connection))
            {
                connection.Open();
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }

        private static void AddParameters(SQLiteCommand command, ChallanEntry entry)
        {
            command.Parameters.AddWithValue("@Sr", entry.Sr);
            command.Parameters.AddWithValue("@ChallanNumber", entry.ChallanNumber ?? string.Empty);
            command.Parameters.AddWithValue("@Date", entry.Date.ToString("o"));
            command.Parameters.AddWithValue("@LRNumber", (object)entry.LRNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("@BrokerName", (object)entry.BrokerName ?? DBNull.Value);
            command.Parameters.AddWithValue("@FromLocation", (object)entry.From ?? DBNull.Value);
            command.Parameters.AddWithValue("@ToLocation", (object)entry.To ?? DBNull.Value);
            command.Parameters.AddWithValue("@VehicleNumber", (object)entry.VehicleNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("@VehicleType", (object)entry.VehicleType ?? DBNull.Value);
            command.Parameters.AddWithValue("@DriverName", (object)entry.DriverName ?? DBNull.Value);
            command.Parameters.AddWithValue("@DriverMobile", (object)entry.DriverMobile ?? DBNull.Value);
            command.Parameters.AddWithValue("@EngineNo", (object)entry.EngineNo ?? DBNull.Value);
            command.Parameters.AddWithValue("@LicenceNo", (object)entry.LicenceNo ?? DBNull.Value);
            command.Parameters.AddWithValue("@PolicyNo", (object)entry.PolicyNo ?? DBNull.Value);
            command.Parameters.AddWithValue("@ChassisNo", (object)entry.ChassisNo ?? DBNull.Value);
            command.Parameters.AddWithValue("@OwnerName", (object)entry.OwnerName ?? DBNull.Value);
            command.Parameters.AddWithValue("@PAN", (object)entry.PAN ?? DBNull.Value);
            command.Parameters.AddWithValue("@LorryHire", entry.LorryHire);
            command.Parameters.AddWithValue("@LessTDS", entry.LessTDS);
            command.Parameters.AddWithValue("@AdvanceAmount", entry.AdvanceAmount);
            command.Parameters.AddWithValue("@AdvanceNEFT", entry.AdvanceNEFT);
            command.Parameters.AddWithValue("@AdvanceCash", entry.AdvanceCash);
            command.Parameters.AddWithValue("@AdvanceDate", entry.AdvanceDate.HasValue ? (object)entry.AdvanceDate.Value.ToString("o") : DBNull.Value);
            command.Parameters.AddWithValue("@Detention", entry.Detention);
            command.Parameters.AddWithValue("@Hamali", entry.Hamali);
            command.Parameters.AddWithValue("@Deduction", entry.Deduction);
            command.Parameters.AddWithValue("@BalancePaidNEFT", entry.BalancePaidNEFT);
            command.Parameters.AddWithValue("@BalancePaidCash", entry.BalancePaidCash);
            command.Parameters.AddWithValue("@BalancePaidDate", entry.BalancePaidDate.HasValue ? (object)entry.BalancePaidDate.Value.ToString("o") : DBNull.Value);
            command.Parameters.AddWithValue("@PaidTo", (object)entry.PaidTo ?? DBNull.Value);
            command.Parameters.AddWithValue("@Remarks", (object)entry.Remarks ?? DBNull.Value);
            command.Parameters.AddWithValue("@BillAmount", entry.BillAmount);
            command.Parameters.AddWithValue("@Margin", entry.Margin);
        }

        private static ChallanEntry MapReader(System.Data.SQLite.SQLiteDataReader reader)
        {
            return new ChallanEntry
            {
                Id = Convert.ToInt32(reader["Id"]),
                Sr = Convert.ToInt32(reader["Sr"]),
                ChallanNumber = reader["ChallanNumber"] as string,
                Date = ParseDate(reader["Date"], DateTime.Today),
                LRNumber = reader["LRNumber"] as string,
                BrokerName = reader["BrokerName"] as string,
                From = reader["FromLocation"] as string,
                To = reader["ToLocation"] as string,
                VehicleNumber = reader["VehicleNumber"] as string,
                VehicleType = reader["VehicleType"] as string,
                DriverName = reader["DriverName"] as string,
                DriverMobile = reader["DriverMobile"] as string,
                EngineNo = reader["EngineNo"] as string,
                LicenceNo = reader["LicenceNo"] as string,
                PolicyNo = reader["PolicyNo"] as string,
                ChassisNo = reader["ChassisNo"] as string,
                OwnerName = reader["OwnerName"] as string,
                PAN = reader["PAN"] as string,
                LorryHire = GetDecimal(reader["LorryHire"]),
                LessTDS = GetDecimal(reader["LessTDS"]),
                AdvanceAmount = GetDecimal(reader["AdvanceAmount"]),
                AdvanceNEFT = GetDecimal(reader["AdvanceNEFT"]),
                AdvanceCash = GetDecimal(reader["AdvanceCash"]),
                AdvanceDate = ParseNullableDate(reader["AdvanceDate"]),
                Detention = GetDecimal(reader["Detention"]),
                Hamali = GetDecimal(reader["Hamali"]),
                Deduction = GetDecimal(reader["Deduction"]),
                BalancePaidNEFT = GetDecimal(reader["BalancePaidNEFT"]),
                BalancePaidCash = GetDecimal(reader["BalancePaidCash"]),
                BalancePaidDate = ParseNullableDate(reader["BalancePaidDate"]),
                PaidTo = reader["PaidTo"] as string,
                Remarks = reader["Remarks"] as string,
                BillAmount = GetDecimal(reader["BillAmount"]),
                Margin = GetDecimal(reader["Margin"])
            };
        }

        private static decimal GetDecimal(object value) => Convert.ToDecimal(value);

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
            // Map property names to DB column names
            switch (sortColumn.ToLower())
            {
                case "challannumber": return $"SUBSTR(ChallanNumber, INSTR(ChallanNumber, '/') + 1) {dir}, CAST(SUBSTR(ChallanNumber, 1, INSTR(ChallanNumber, '/') - 1) AS INTEGER) {dir}, Sr, Id";
                case "sr": return $"Sr {dir}, Id";
                case "date": return $"Date {dir}, Sr, Id";
                case "lrnumber": return $"LRNumber {dir}, Sr, Id";
                case "brokername": return $"BrokerName {dir}, Sr, Id";
                case "from": return $"FromLocation {dir}, Sr, Id";
                case "to": return $"ToLocation {dir}, Sr, Id";
                case "vehiclenumber": return $"VehicleNumber {dir}, Sr, Id";
                case "vehicletype": return $"VehicleType {dir}, Sr, Id";
                case "drivername": return $"DriverName {dir}, Sr, Id";
                case "drivermobile": return $"DriverMobile {dir}, Sr, Id";
                case "engineno": return $"EngineNo {dir}, Sr, Id";
                case "licenceno": return $"LicenceNo {dir}, Sr, Id";
                case "policyno": return $"PolicyNo {dir}, Sr, Id";
                case "chassisno": return $"ChassisNo {dir}, Sr, Id";
                case "ownername": return $"OwnerName {dir}, Sr, Id";
                case "pan": return $"PAN {dir}, Sr, Id";
                case "lorryhire": return $"LorryHire {dir}, Sr, Id";
                case "lesstds": return $"LessTDS {dir}, Sr, Id";
                case "advanceamount": return $"AdvanceAmount {dir}, Sr, Id";
                case "advanceneft": return $"AdvanceNEFT {dir}, Sr, Id";
                case "advancecash": return $"AdvanceCash {dir}, Sr, Id";
                case "advancepaid": return $"AdvanceDate {dir}, Sr, Id";
                case "detention": return $"Detention {dir}, Sr, Id";
                case "hamali": return $"Hamali {dir}, Sr, Id";
                case "deduction": return $"Deduction {dir}, Sr, Id";
                case "balancepaidneft": return $"BalancePaidNEFT {dir}, Sr, Id";
                case "balancepaidcash": return $"BalancePaidCash {dir}, Sr, Id";
                case "due": return $"(LorryHire - LessTDS + Detention + Hamali - Deduction - AdvanceAmount - BalancePaidNEFT - BalancePaidCash) {dir}, Sr, Id";
                default: return "Sr, Id";
            }
        }
    }
}
