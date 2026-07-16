using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
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
            if (BackendSettings.UseRemoteApi)
            {
                return RemoteApiClient.GetList<LREntry>("api/lr");
            }
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
            if (BackendSettings.UseRemoteApi)
            {
                return GetRemotePage(pageNumber, pageSize, null, sortColumn, sortAscending).Items;
            }
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

        internal RemotePagedResult<LREntry> GetPendingBillPage(int pageNumber, int pageSize, string searchFilter = "")
        {
            pageNumber = Math.Max(pageNumber, 1);
            pageSize = Math.Max(pageSize, 1);
            searchFilter = (searchFilter ?? string.Empty).Trim();

            if (BackendSettings.UseRemoteApi)
            {
                return RemoteApiClient.GetPage<LREntry>($"api/lr/pending-bill-page?page={pageNumber}&pageSize={pageSize}&search={RemoteApiClient.UrlEncode(searchFilter)}");
            }

            var result = new RemotePagedResult<LREntry>();
            int offset = (pageNumber - 1) * pageSize;
            var pendingWhere = @"
WHERE TRIM(COALESCE(lr.LRNo, '')) <> ''
  AND TRIM(COALESCE(lr.BillNo, '')) = ''
  AND NOT EXISTS (
      SELECT 1
      FROM Bills b
      WHERE LOWER(TRIM(COALESCE(b.LRNo, ''))) = LOWER(TRIM(COALESCE(lr.LRNo, '')))
        AND TRIM(COALESCE(b.BillNo, '')) <> ''
  )";

            if (!string.IsNullOrWhiteSpace(searchFilter))
            {
                pendingWhere += @"
  AND (
      lr.LRNo LIKE @search OR
      lr.ConsignorName LIKE @search OR
      lr.BillParty LIKE @search OR
      lr.FromLocation LIKE @search OR
      lr.ToLocation LIKE @search OR
      lr.VehicleNo LIKE @search OR
      lr.CHNo LIKE @search
  )";
            }

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var countCommand = new SQLiteCommand($"SELECT COUNT(*) FROM LREntries lr {pendingWhere};", connection))
            using (var pageCommand = new SQLiteCommand($"SELECT * FROM LREntries lr {pendingWhere} ORDER BY lr.Date DESC, lr.Id DESC LIMIT @limit OFFSET @offset;", connection))
            {
                if (!string.IsNullOrWhiteSpace(searchFilter))
                {
                    countCommand.Parameters.AddWithValue("@search", "%" + searchFilter + "%");
                    pageCommand.Parameters.AddWithValue("@search", "%" + searchFilter + "%");
                }
                pageCommand.Parameters.AddWithValue("@limit", pageSize);
                pageCommand.Parameters.AddWithValue("@offset", offset);
                connection.Open();
                result.TotalCount = Convert.ToInt32(countCommand.ExecuteScalar());
                using (var reader = pageCommand.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        result.Items.Add(ReadEntry(reader));
                    }
                }
            }

            return result;
        }

        public List<LREntry> Search(string searchFilter, int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true)
        {
            if (BackendSettings.UseRemoteApi)
            {
                return GetRemotePage(pageNumber, pageSize, searchFilter, sortColumn, sortAscending).Items;
            }
            var entries = new List<LREntry>();
            int offset = (pageNumber - 1) * pageSize;
            string orderBy = BuildOrderBy(sortColumn, sortAscending);
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $@"SELECT * FROM LREntries
WHERE LRNo LIKE @f OR ConsignorName LIKE @f OR ConsignorAddress LIKE @f OR ConsignorGST LIKE @f
   OR ConsigneeName LIKE @f OR ConsigneeAddress LIKE @f OR ConsigneeGST LIKE @f
   OR FromLocation LIKE @f OR ToLocation LIKE @f
   OR VehicleNo LIKE @f OR VehicleType LIKE @f
   OR PkgType LIKE @f OR Description LIKE @f OR Invoice LIKE @f OR Value LIKE @f
   OR BillNo LIKE @f OR BillParty LIKE @f OR Broker LIKE @f OR FrtType LIKE @f OR PayType LIKE @f OR Paid LIKE @f
   OR CHNo LIKE @f
ORDER BY {orderBy} LIMIT @limit OFFSET @offset;";
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
            if (BackendSettings.UseRemoteApi)
            {
                return GetRemotePage(1, 1, searchFilter, string.Empty, true).TotalCount;
            }
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                if (string.IsNullOrWhiteSpace(searchFilter))
                    command.CommandText = "SELECT COUNT(*) FROM LREntries;";
                else
                {
                    command.CommandText = @"SELECT COUNT(*) FROM LREntries
WHERE LRNo LIKE @f OR ConsignorName LIKE @f OR ConsignorAddress LIKE @f OR ConsignorGST LIKE @f
   OR ConsigneeName LIKE @f OR ConsigneeAddress LIKE @f OR ConsigneeGST LIKE @f
   OR FromLocation LIKE @f OR ToLocation LIKE @f
   OR VehicleNo LIKE @f OR VehicleType LIKE @f
   OR PkgType LIKE @f OR Description LIKE @f OR Invoice LIKE @f OR Value LIKE @f
   OR BillNo LIKE @f OR BillParty LIKE @f OR Broker LIKE @f OR FrtType LIKE @f OR PayType LIKE @f OR Paid LIKE @f
   OR CHNo LIKE @f;";
                    command.Parameters.AddWithValue("@f", $"%{searchFilter}%");
                }
                connection.Open();
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public decimal GetTotalFreight(string searchFilter = "")
        {
            if (BackendSettings.UseRemoteApi)
            {
                var query = "api/lr/summary";
                if (!string.IsNullOrWhiteSpace(searchFilter))
                {
                    query += $"?search={RemoteApiClient.UrlEncode(searchFilter)}";
                }

                try
                {
                    return RemoteApiClient.Get<RemoteLRSummary>(query)?.TotalFreight ?? 0m;
                }
                catch
                {
                    return GetAllRemoteSafe()
                        .Where(e => MatchesSearch(e, searchFilter))
                        .Sum(e => e?.TotalFreight ?? 0m);
                }
            }

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                if (string.IsNullOrWhiteSpace(searchFilter))
                {
                    command.CommandText = "SELECT COALESCE(SUM(TotalFreight), 0) FROM LREntries;";
                }
                else
                {
                    command.CommandText = @"SELECT COALESCE(SUM(TotalFreight), 0) FROM LREntries
WHERE LRNo LIKE @f OR ConsignorName LIKE @f OR ConsignorAddress LIKE @f OR ConsignorGST LIKE @f
   OR ConsigneeName LIKE @f OR ConsigneeAddress LIKE @f OR ConsigneeGST LIKE @f
   OR FromLocation LIKE @f OR ToLocation LIKE @f
   OR VehicleNo LIKE @f OR VehicleType LIKE @f
   OR PkgType LIKE @f OR Description LIKE @f OR Invoice LIKE @f OR Value LIKE @f
   OR BillNo LIKE @f OR BillParty LIKE @f OR Broker LIKE @f OR FrtType LIKE @f OR PayType LIKE @f OR Paid LIKE @f
   OR CHNo LIKE @f;";
                    command.Parameters.AddWithValue("@f", $"%{searchFilter}%");
                }

                connection.Open();
                return Convert.ToDecimal(command.ExecuteScalar() ?? 0m);
            }
        }

        public decimal GetTotalBalance(string searchFilter = "")
        {
            if (BackendSettings.UseRemoteApi)
            {
                var query = "api/lr/summary";
                if (!string.IsNullOrWhiteSpace(searchFilter))
                {
                    query += $"?search={RemoteApiClient.UrlEncode(searchFilter)}";
                }

                try
                {
                    return RemoteApiClient.Get<RemoteLRSummary>(query)?.TotalBalance ?? 0m;
                }
                catch
                {
                    return GetAllRemoteSafe()
                        .Where(e => MatchesSearch(e, searchFilter))
                        .Sum(e => e?.Bal ?? 0m);
                }
            }

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                if (string.IsNullOrWhiteSpace(searchFilter))
                {
                    command.CommandText = "SELECT COALESCE(SUM((NEFT + CASH - TDS + Ded)), 0) FROM LREntries;";
                }
                else
                {
                    command.CommandText = @"SELECT COALESCE(SUM((NEFT + CASH - TDS + Ded)), 0) FROM LREntries
WHERE LRNo LIKE @f OR ConsignorName LIKE @f OR ConsignorAddress LIKE @f OR ConsignorGST LIKE @f
   OR ConsigneeName LIKE @f OR ConsigneeAddress LIKE @f OR ConsigneeGST LIKE @f
   OR FromLocation LIKE @f OR ToLocation LIKE @f
   OR VehicleNo LIKE @f OR VehicleType LIKE @f
   OR PkgType LIKE @f OR Description LIKE @f OR Invoice LIKE @f OR Value LIKE @f
   OR BillNo LIKE @f OR BillParty LIKE @f OR Broker LIKE @f OR FrtType LIKE @f OR PayType LIKE @f OR Paid LIKE @f
   OR CHNo LIKE @f;";
                    command.Parameters.AddWithValue("@f", $"%{searchFilter}%");
                }

                connection.Open();
                return Convert.ToDecimal(command.ExecuteScalar() ?? 0m);
            }
        }

        public int GetMaxSr()
        {
            if (BackendSettings.UseRemoteApi)
            {
                return GetAllRemoteSafe().Select(e => e.Sr).DefaultIfEmpty(0).Max();
            }
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand("SELECT COALESCE(MAX(Sr), 0) FROM LREntries;", connection))
            {
                connection.Open();
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public bool ExistsLRNo(string lrNo, int excludeId = 0)
        {
            lrNo = (lrNo ?? string.Empty).Trim();
            if (lrNo.Length == 0)
            {
                return false;
            }

            if (BackendSettings.UseRemoteApi)
            {
                var route = $"api/lr/exists?lrNo={RemoteApiClient.UrlEncode(lrNo)}&excludeId={excludeId}";
                return RemoteApiClient.Get<RemoteExistsResult>(route)?.Exists ?? false;
            }

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT COUNT(*)
FROM LREntries
WHERE LOWER(TRIM(COALESCE(LRNo, ''))) = LOWER(TRIM(@lrNo))
  AND (@excludeId <= 0 OR Id <> @excludeId);";
                command.Parameters.AddWithValue("@lrNo", lrNo);
                command.Parameters.AddWithValue("@excludeId", excludeId);
                connection.Open();
                return Convert.ToInt32(command.ExecuteScalar() ?? 0) > 0;
            }
        }

        public List<LREntry> GetByLRNumbers(IEnumerable<string> lrNumbers)
        {
            return GetByLrNumbers(lrNumbers);
        }

        public List<LREntry> GetByChallanNumbers(IEnumerable<string> challanNumbers)
        {
            var keys = (challanNumbers ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (keys.Count == 0) return new List<LREntry>();

            if (BackendSettings.UseRemoteApi)
            {
                var lookup = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
                return GetAllRemoteSafe()
                    .Where(x => x != null && lookup.Contains((x.CHNo ?? string.Empty).Trim()))
                    .OrderBy(x => x.Id)
                    .ToList();
            }

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                var paramNames = keys.Select((_, i) => "@ch" + i).ToList();
                command.CommandText = $"SELECT * FROM LREntries WHERE TRIM(COALESCE(CHNo,'')) IN ({string.Join(",", paramNames)}) ORDER BY Id;";
                for (var i = 0; i < keys.Count; i++)
                {
                    command.Parameters.AddWithValue(paramNames[i], keys[i]);
                }

                var list = new List<LREntry>();
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read()) list.Add(ReadEntry(reader));
                }
                return list;
            }
        }

        public List<LREntry> GetByBillNo(string billNo)
        {
            billNo = (billNo ?? string.Empty).Trim();
            if (billNo.Length == 0) return new List<LREntry>();

            if (BackendSettings.UseRemoteApi)
            {
                return GetRemotePage(1, 500, billNo, "billno", false).Items
                    .Where(x => string.Equals((x.BillNo ?? string.Empty).Trim(), billNo, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.Id)
                    .ToList();
            }

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand(@"
SELECT * FROM LREntries
WHERE TRIM(COALESCE(BillNo,'')) = @billNo
ORDER BY Id;", connection))
            {
                command.Parameters.AddWithValue("@billNo", billNo);
                var list = new List<LREntry>();
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read()) list.Add(ReadEntry(reader));
                }
                return list;
            }
        }

        public LREntry GetById(int id)
        {
            if (id <= 0) return null;

            if (BackendSettings.UseRemoteApi)
            {
                try
                {
                    return RemoteApiClient.Get<LREntry>($"api/lr/{id}");
                }
                catch
                {
                    return null;
                }
            }

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand("SELECT * FROM LREntries WHERE Id = @id LIMIT 1;", connection))
            {
                command.Parameters.AddWithValue("@id", id);
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    return reader.Read() ? ReadEntry(reader) : null;
                }
            }
        }

        public List<LREntry> GetByLrNumbers(IEnumerable<string> lrNumbers)
        {
            var keys = (lrNumbers ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (keys.Count == 0) return new List<LREntry>();

            if (BackendSettings.UseRemoteApi)
            {
                try
                {
                    return RemoteApiClient.Post<List<LREntry>>("api/lr/by-nos", keys)?
                               .OrderBy(x => x.Id)
                               .ToList()
                           ?? new List<LREntry>();
                }
                catch
                {
                }
            }

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                var paramNames = keys.Select((_, i) => "@lr" + i).ToList();
                command.CommandText = $"SELECT * FROM LREntries WHERE TRIM(COALESCE(LRNo,'')) IN ({string.Join(",", paramNames)}) ORDER BY Id;";
                for (var i = 0; i < keys.Count; i++)
                {
                    command.Parameters.AddWithValue(paramNames[i], keys[i]);
                }

                var list = new List<LREntry>();
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read()) list.Add(ReadEntry(reader));
                }
                return list;
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
                SizeL = NormalizeDisplayDecimal(Convert.ToDecimal(reader["SizeL"])),
                SizeW = NormalizeDisplayDecimal(Convert.ToDecimal(reader["SizeW"])),
                SizeH = NormalizeDisplayDecimal(Convert.ToDecimal(reader["SizeH"])),
                ActualWeight = NormalizeDisplayDecimal(Convert.ToDecimal(reader["ActualWeight"])),
                ChargedWeight = NormalizeDisplayDecimal(Convert.ToDecimal(reader["ChargedWeight"])),
                PKG = Convert.ToInt32(reader["PKG"]),
                PkgType = reader["PkgType"] as string,
                Description = reader["Description"] as string,
                Invoice = reader["Invoice"] as string,
                Value = reader["Value"] as string,
                CHNo = reader["CHNo"] as string,
                TotalFreight = Convert.ToDecimal(reader["TotalFreight"]),
                Hamali = Convert.ToDecimal(reader["Hamali"]),
                Detention = Convert.ToDecimal(reader["Detention"]),
                Others = Convert.ToDecimal(reader["Others"]),
                StCharge = Convert.ToDecimal(reader["StCharge"]),
                NEFT = Convert.ToDecimal(reader["NEFT"]),
                CASH = Convert.ToDecimal(reader["CASH"]),
                TDS = Convert.ToDecimal(reader["TDS"]),
                Ded = Convert.ToDecimal(reader["Ded"]),
                BillNo = reader["BillNo"] as string,
                BillDate = ParseNullableDate(reader["BillDate"]),
                BILL = Convert.ToDecimal(reader["BILL"]),
                ChallanLorryHire = Convert.ToDecimal(reader["ChallanLorryHire"]),
                BillParty = reader["BillParty"] as string,
                Broker = reader["Broker"] as string,
                FrtType = reader["FrtType"] as string,
                PayType = reader["PayType"] as string,
                Comm = Convert.ToDecimal(reader["Comm"]),
                Paid = reader["Paid"] as string,
                PreserveImportedBilling = GetBoolean(reader, "PreserveImportedBilling")
            };
        }

        public void Upsert(LREntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (BackendSettings.UseRemoteApi)
            {
                if (entry.Id <= 0)
                {
                    entry.Id = RemoteApiClient.PostAndReadInt("api/lr", entry);
                }
                else
                {
                    RemoteApiClient.Put($"api/lr/{entry.Id}", entry);
                }
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
    VehicleNo, VehicleType, Weight, SizeL, SizeW, SizeH, ActualWeight, ChargedWeight, PKG, PkgType, Description, Invoice, Value, CHNo,
    TotalFreight, Hamali, Detention, Others, StCharge, NEFT, CASH, TDS, Ded, BillNo, BillDate, BILL, ChallanLorryHire, BillParty, Broker, FrtType, PayType, Comm, Paid, PreserveImportedBilling
) VALUES (
    @Sr, @LRNo, @Date, @ConsignorName, @ConsignorAddress, @ConsignorGST,
    @ConsigneeName, @ConsigneeAddress, @ConsigneeGST, @FromLocation, @ToLocation,
    @VehicleNo, @VehicleType, @Weight, @SizeL, @SizeW, @SizeH, @ActualWeight, @ChargedWeight, @PKG, @PkgType, @Description, @Invoice, @Value, @CHNo,
    @TotalFreight, @Hamali, @Detention, @Others, @StCharge, @NEFT, @CASH, @TDS, @Ded, @BillNo, @BillDate, @BILL, @ChallanLorryHire, @BillParty, @Broker, @FrtType, @PayType, @Comm, @Paid, @PreserveImportedBilling
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
    SizeL = @SizeL,
    SizeW = @SizeW,
    SizeH = @SizeH,
    ActualWeight = @ActualWeight,
    ChargedWeight = @ChargedWeight,
    PKG = @PKG,
    PkgType = @PkgType,
    Description = @Description,
    Invoice = @Invoice,
    Value = @Value,
    CHNo = @CHNo,
    TotalFreight = @TotalFreight,
    Hamali = @Hamali,
    Detention = @Detention,
    Others = @Others,
    StCharge = @StCharge,
    NEFT = @NEFT,
    CASH = @CASH,
    TDS = @TDS,
    Ded = @Ded,
    BillNo = @BillNo,
    BillDate = @BillDate,
    BILL = @BILL,
    ChallanLorryHire = @ChallanLorryHire,
    BillParty = @BillParty,
    Broker = @Broker,
    FrtType = @FrtType,
    PayType = @PayType,
    Comm = @Comm,
    Paid = @Paid,
    PreserveImportedBilling = @PreserveImportedBilling
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

            if (BackendSettings.UseRemoteApi)
            {
                if (entry.Id > 0)
                {
                    RemoteApiClient.Delete($"api/lr/{entry.Id}");
                }
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
            command.Parameters.AddWithValue("@Weight", entry.ActualWeight);
            command.Parameters.AddWithValue("@SizeL", entry.SizeL);
            command.Parameters.AddWithValue("@SizeW", entry.SizeW);
            command.Parameters.AddWithValue("@SizeH", entry.SizeH);
            command.Parameters.AddWithValue("@ActualWeight", entry.ActualWeight);
            command.Parameters.AddWithValue("@ChargedWeight", entry.ChargedWeight);
            command.Parameters.AddWithValue("@PKG", entry.PKG);
            command.Parameters.AddWithValue("@PkgType", (object)entry.PkgType ?? DBNull.Value);
            command.Parameters.AddWithValue("@Description", (object)entry.Description ?? DBNull.Value);
            command.Parameters.AddWithValue("@Invoice", (object)entry.Invoice ?? DBNull.Value);
            command.Parameters.AddWithValue("@Value", (object)entry.Value ?? DBNull.Value);
            command.Parameters.AddWithValue("@CHNo", (object)entry.CHNo ?? DBNull.Value);
            command.Parameters.AddWithValue("@TotalFreight", entry.TotalFreight);
            command.Parameters.AddWithValue("@Hamali", entry.Hamali);
            command.Parameters.AddWithValue("@Detention", entry.Detention);
            command.Parameters.AddWithValue("@Others", entry.Others);
            command.Parameters.AddWithValue("@StCharge", entry.StCharge);
            command.Parameters.AddWithValue("@NEFT", entry.NEFT);
            command.Parameters.AddWithValue("@CASH", entry.CASH);
            command.Parameters.AddWithValue("@TDS", entry.TDS);
            command.Parameters.AddWithValue("@Ded", entry.Ded);
            command.Parameters.AddWithValue("@BillNo", (object)entry.BillNo ?? DBNull.Value);
            command.Parameters.AddWithValue("@BillDate", entry.BillDate.HasValue ? (object)entry.BillDate.Value.ToString("o") : DBNull.Value);
            command.Parameters.AddWithValue("@BILL", entry.BILL);
            command.Parameters.AddWithValue("@ChallanLorryHire", entry.ChallanLorryHire);
            command.Parameters.AddWithValue("@BillParty", (object)entry.BillParty ?? DBNull.Value);
            command.Parameters.AddWithValue("@Broker", (object)entry.Broker ?? DBNull.Value);
            command.Parameters.AddWithValue("@FrtType", (object)entry.FrtType ?? DBNull.Value);
            command.Parameters.AddWithValue("@PayType", (object)entry.PayType ?? DBNull.Value);
            command.Parameters.AddWithValue("@Comm", entry.Comm);
            command.Parameters.AddWithValue("@Paid", (object)entry.Paid ?? DBNull.Value);
            command.Parameters.AddWithValue("@PreserveImportedBilling", entry.PreserveImportedBilling ? 1 : 0);
        }

        private static bool GetBoolean(SQLiteDataReader reader, string columnName)
        {
            try
            {
                var ordinal = reader.GetOrdinal(columnName);
                if (reader.IsDBNull(ordinal))
                {
                    return false;
                }

                var value = reader.GetValue(ordinal);
                if (value is bool boolValue)
                {
                    return boolValue;
                }

                return Convert.ToInt32(value) != 0;
            }
            catch (IndexOutOfRangeException)
            {
                return false;
            }
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

        // Remove trailing zeros at value level so grids that fall back to default ToString
        // still show 75 instead of 75.00 for these dimension/weight fields.
        private static decimal NormalizeDisplayDecimal(decimal value)
        {
            return decimal.Parse(value.ToString("0.####################", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
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
                case "sizel": return $"SizeL {dir}, Sr, Id";
                case "sizew": return $"SizeW {dir}, Sr, Id";
                case "sizeh": return $"SizeH {dir}, Sr, Id";
                case "actualweight": return $"ActualWeight {dir}, Sr, Id";
                case "chargedweight": return $"ChargedWeight {dir}, Sr, Id";
                case "pkg": return $"PKG {dir}, Sr, Id";
                case "pkgtype": return $"PkgType {dir}, Sr, Id";
                case "description": return $"Description {dir}, Sr, Id";
                case "invoice": return $"Invoice {dir}, Sr, Id";
                case "value": return $"Value {dir}, Sr, Id";
                case "chno": return $"CHNo {dir}, Sr, Id";
                case "totalfreight": return $"TotalFreight {dir}, Sr, Id";
                case "hamali": return $"Hamali {dir}, Sr, Id";
                case "detention": return $"Detention {dir}, Sr, Id";
                case "others": return $"Others {dir}, Sr, Id";
                case "stcharge": return $"StCharge {dir}, Sr, Id";
                case "totalbill": return $"(TotalFreight + Detention + Hamali + Others + StCharge) {dir}, Sr, Id";
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
                case "paytype": return $"PayType {dir}, Sr, Id";
                case "comm": return $"Comm {dir}, Sr, Id";
                case "paid": return $"Paid {dir}, Sr, Id";
                default: return "Sr, Id";
            }
        }

        private static List<LREntry> GetAllRemoteSafe()
        {
            return RemoteApiClient.GetList<LREntry>("api/lr")
                .OrderByDescending(e => GetLRSuffix(e?.LRNo))
                .ThenByDescending(e => GetLRSequence(e?.LRNo))
                .ThenByDescending(e => e?.Id ?? 0)
                .ToList();
        }

        private static RemotePagedResult<LREntry> GetRemotePage(int pageNumber, int pageSize, string searchFilter, string sortColumn, bool sortAscending)
        {
            var query = $"api/lr/page?page={pageNumber}&pageSize={pageSize}&asc={sortAscending.ToString().ToLowerInvariant()}";
            if (!string.IsNullOrWhiteSpace(searchFilter)) query += $"&search={RemoteApiClient.UrlEncode(searchFilter)}";
            if (!string.IsNullOrWhiteSpace(sortColumn)) query += $"&sort={RemoteApiClient.UrlEncode(sortColumn)}";
            try
            {
                return RemoteApiClient.GetPage<LREntry>(query);
            }
            catch
            {
                var filtered = GetAllRemoteSafe().Where(e =>
                    string.IsNullOrWhiteSpace(searchFilter) ||
                    Contains(e.LRNo, searchFilter) ||
                    Contains(e.ConsignorName, searchFilter) ||
                    Contains(e.ConsigneeName, searchFilter) ||
                    Contains(e.VehicleNo, searchFilter) ||
                    Contains(e.BillNo, searchFilter) ||
                    Contains(e.CHNo, searchFilter));
                var sorted = ApplySort(filtered, sortColumn, sortAscending).ToList();
                return new RemotePagedResult<LREntry>
                {
                    TotalCount = sorted.Count,
                    Items = sorted.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList()
                };
            }
        }

        internal static RemoteLRLedgerPageResult GetRemoteLedgerPageResult(int pageNumber, int pageSize, string searchFilter, string sortColumn, bool sortAscending)
        {
            var query = $"api/lr/ledger-page?page={pageNumber}&pageSize={pageSize}&asc={sortAscending.ToString().ToLowerInvariant()}";
            if (!string.IsNullOrWhiteSpace(searchFilter)) query += $"&search={RemoteApiClient.UrlEncode(searchFilter)}";
            if (!string.IsNullOrWhiteSpace(sortColumn)) query += $"&sort={RemoteApiClient.UrlEncode(sortColumn)}";

            try
            {
                return RemoteApiClient.Get<RemoteLRLedgerPageResult>(query) ?? new RemoteLRLedgerPageResult();
            }
            catch
            {
                var page = GetRemotePage(pageNumber, pageSize, searchFilter, sortColumn, sortAscending);
                var summaryQuery = "api/lr/summary";
                if (!string.IsNullOrWhiteSpace(searchFilter))
                {
                    summaryQuery += $"?search={RemoteApiClient.UrlEncode(searchFilter)}";
                }

                RemoteLRSummary summary = null;
                try
                {
                    summary = RemoteApiClient.Get<RemoteLRSummary>(summaryQuery);
                }
                catch
                {
                }

                HashSet<int> commentIds = null;
                try
                {
                    commentIds = new Data.CommentRepository().GetLREntryIdsWithComments();
                }
                catch
                {
                }

                return new RemoteLRLedgerPageResult
                {
                    TotalCount = page?.TotalCount ?? 0,
                    TotalFreight = summary?.TotalFreight ?? 0m,
                    TotalBalance = summary?.TotalBalance ?? 0m,
                    CommentIds = commentIds?.ToList() ?? new List<int>(),
                    Items = page?.Items ?? new List<LREntry>()
                };
            }
        }

        private sealed class RemoteExistsResult
        {
            public bool Exists { get; set; }
        }

        private static IEnumerable<LREntry> ApplySort(IEnumerable<LREntry> source, string sortColumn, bool ascending)
        {
            var ordered = source ?? Enumerable.Empty<LREntry>();
            if (string.Equals(sortColumn, "lrno", StringComparison.OrdinalIgnoreCase))
            {
                return ascending
                    ? ordered
                        .OrderBy(e => GetLRSuffix(e?.LRNo))
                        .ThenBy(e => GetLRSequence(e?.LRNo))
                        .ThenBy(e => e.Id)
                    : ordered
                        .OrderByDescending(e => GetLRSuffix(e?.LRNo))
                        .ThenByDescending(e => GetLRSequence(e?.LRNo))
                        .ThenByDescending(e => e.Id);
            }

            Func<LREntry, object> keySelector;
            switch ((sortColumn ?? string.Empty).ToLowerInvariant())
            {
                case "lrno": keySelector = e => e.LRNo ?? string.Empty; break;
                case "date": keySelector = e => e.Date; break;
                case "consignorname": keySelector = e => e.ConsignorName ?? string.Empty; break;
                case "consignee_name":
                case "consigneename": keySelector = e => e.ConsigneeName ?? string.Empty; break;
                case "from": keySelector = e => e.From ?? string.Empty; break;
                case "to": keySelector = e => e.To ?? string.Empty; break;
                case "vehicleno": keySelector = e => e.VehicleNo ?? string.Empty; break;
                case "vehicletype": keySelector = e => e.VehicleType ?? string.Empty; break;
                case "invoice": keySelector = e => e.Invoice ?? string.Empty; break;
                case "value": keySelector = e => e.Value ?? string.Empty; break;
                case "chno": keySelector = e => e.CHNo ?? string.Empty; break;
                case "totalfreight": keySelector = e => e.TotalFreight; break;
                case "hamali": keySelector = e => e.Hamali; break;
                case "detention": keySelector = e => e.Detention; break;
                case "others": keySelector = e => e.Others; break;
                case "stcharge": keySelector = e => e.StCharge; break;
                case "neft": keySelector = e => e.NEFT; break;
                case "cash": keySelector = e => e.CASH; break;
                case "tds": keySelector = e => e.TDS; break;
                case "ded": keySelector = e => e.Ded; break;
                case "billno": keySelector = e => e.BillNo ?? string.Empty; break;
                case "billdate": keySelector = e => e.BillDate ?? DateTime.MinValue; break;
                case "bill": keySelector = e => e.BILL; break;
                case "billparty": keySelector = e => e.BillParty ?? string.Empty; break;
                case "broker": keySelector = e => e.Broker ?? string.Empty; break;
                default: keySelector = e => e.Sr; break;
            }
            return ascending ? ordered.OrderBy(keySelector).ThenBy(e => e.Id) : ordered.OrderByDescending(keySelector).ThenByDescending(e => e.Id);
        }

        private static bool Contains(string value, string filter)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.IsNullOrWhiteSpace(filter) &&
                   value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetLRSuffix(string lrNo)
        {
            var raw = (lrNo ?? string.Empty).Trim();
            var slashIndex = raw.IndexOf('/');
            return slashIndex >= 0 && slashIndex < raw.Length - 1
                ? raw.Substring(slashIndex + 1).Trim()
                : string.Empty;
        }

        private static int GetLRSequence(string lrNo)
        {
            var raw = (lrNo ?? string.Empty).Trim();
            var prefix = raw;
            var slashIndex = raw.IndexOf('/');
            if (slashIndex > 0)
            {
                prefix = raw.Substring(0, slashIndex);
            }

            var digits = new string(prefix.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var parsed) ? parsed : 0;
        }

        private static bool MatchesSearch(LREntry entry, string searchFilter)
        {
            if (entry == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(searchFilter))
            {
                return true;
            }

            return Contains(entry.LRNo, searchFilter) ||
                   Contains(entry.ConsignorName, searchFilter) ||
                   Contains(entry.ConsignorAddress, searchFilter) ||
                   Contains(entry.ConsignorGST, searchFilter) ||
                   Contains(entry.ConsigneeName, searchFilter) ||
                   Contains(entry.ConsigneeAddress, searchFilter) ||
                   Contains(entry.ConsigneeGST, searchFilter) ||
                   Contains(entry.From, searchFilter) ||
                   Contains(entry.To, searchFilter) ||
                   Contains(entry.VehicleNo, searchFilter) ||
                   Contains(entry.VehicleType, searchFilter) ||
                   Contains(entry.PkgType, searchFilter) ||
                   Contains(entry.Description, searchFilter) ||
                   Contains(entry.Invoice, searchFilter) ||
                   Contains(entry.Value, searchFilter) ||
                   Contains(entry.BillNo, searchFilter) ||
                   Contains(entry.BillParty, searchFilter) ||
                   Contains(entry.Broker, searchFilter) ||
                   Contains(entry.FrtType, searchFilter) ||
                   Contains(entry.PayType, searchFilter) ||
                   Contains(entry.Paid, searchFilter) ||
                   Contains(entry.CHNo, searchFilter);
        }
    }
}
