using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Awagaman_ERP.Helpers;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    public sealed class ChallanRepository : IChallanRepository
    {
        private const string PurchaseTableName = "Challans";
        private const string ChallanLedgerTableName = "ChallanLedgerEntries";

        public ChallanRepository()
        {
            AppDatabase.EnsureInitialized();
            MigrateLegacyXmlIfNeeded();
        }

        public string LedgerMode { get; set; } = "Purchase";

        private bool IsChallanLedgerMode =>
            string.Equals(LedgerMode, "Challan", StringComparison.OrdinalIgnoreCase);

        private string ActiveTableName => IsChallanLedgerMode ? ChallanLedgerTableName : PurchaseTableName;

        private static string NormalizeLookupChallanNumber(string challanNumber)
        {
            var raw = (challanNumber ?? string.Empty).Trim();
            if (raw.Length == 0)
            {
                return string.Empty;
            }

            var slashIndex = raw.IndexOf('/');
            if (slashIndex > 0)
            {
                var prefix = raw.Substring(0, slashIndex);
                var suffix = raw.Substring(slashIndex + 1).Trim();
                var digits = new string(prefix.Where(char.IsDigit).ToArray());
                if (digits.Length > 0 && suffix.Length > 0)
                {
                    return $"{digits.PadLeft(Math.Max(3, digits.Length), '0')}/{suffix}";
                }
            }

            return ChallanNumberFormatter.Normalize(raw, DateTime.Today);
        }

        private string AppendLedgerQuery(string route)
        {
            if (!IsChallanLedgerMode)
            {
                return route;
            }

            return route.Contains("?")
                ? route + "&ledgerKind=challan"
                : route + "?ledgerKind=challan";
        }

        public List<ChallanEntry> GetAll()
        {
            if (BackendSettings.UseRemoteApi)
            {
                return RemoteApiClient.GetList<ChallanEntry>(AppendLedgerQuery("api/challans"));
            }
            var entries = new List<ChallanEntry>();
            var orderBy = BuildOrderBy("challannumber", ascending: false);

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand($"SELECT * FROM {ActiveTableName} ORDER BY {orderBy};", connection))
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
                            Other = reader["OtherAmount"] == DBNull.Value ? 0m : GetDecimal(reader["OtherAmount"]),
                            Deduction = GetDecimal(reader["Deduction"]),
                            BalancePaidNEFT = GetDecimal(reader["BalancePaidNEFT"]),
                            BalancePaidCash = GetDecimal(reader["BalancePaidCash"]),
                            BalancePaidDate = ParseNullableDate(reader["BalancePaidDate"]),
                            PaidTo = reader["PaidTo"] as string,
                            Remarks = reader["Remarks"] as string,
                            BillAmount = GetDecimal(reader["BillAmount"]),
                            Margin = GetDecimal(reader["Margin"]),
                            PreserveImportedBilling = GetBoolean(reader, "PreserveImportedBilling")
                        };

                        entry.RecalculateBalance();
                        entries.Add(entry);
                    }
                }
            }

            return entries;
        }

        public List<ChallanEntry> GetPage(int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true, bool useLhsDerived = false)
        {
            if (BackendSettings.UseRemoteApi)
            {
                return GetRemotePage(pageNumber, pageSize, null, sortColumn, sortAscending, null, null, null, null, useLhsDerived).Items;
            }
            var entries = new List<ChallanEntry>();
            int offset = (pageNumber - 1) * pageSize;
            string orderBy = BuildOrderBy(sortColumn, sortAscending, useLhsDerived);

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand($"SELECT * FROM {ActiveTableName} ORDER BY {orderBy} LIMIT @limit OFFSET @offset;", connection))
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
                            Other = reader["OtherAmount"] == DBNull.Value ? 0m : GetDecimal(reader["OtherAmount"]),
                            Deduction = GetDecimal(reader["Deduction"]),
                            BalancePaidNEFT = GetDecimal(reader["BalancePaidNEFT"]),
                            BalancePaidCash = GetDecimal(reader["BalancePaidCash"]),
                            BalancePaidDate = ParseNullableDate(reader["BalancePaidDate"]),
                            PaidTo = reader["PaidTo"] as string,
                            Remarks = reader["Remarks"] as string,
                            BillAmount = GetDecimal(reader["BillAmount"]),
                            Margin = GetDecimal(reader["Margin"]),
                            PreserveImportedBilling = GetBoolean(reader, "PreserveImportedBilling")
                        });
                    }
                }
            }
            foreach (var entry in entries) entry.RecalculateBalance();
            return entries;
        }

        public List<ChallanEntry> Search(string searchFilter, int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true, bool useLhsDerived = false)
        {
            if (BackendSettings.UseRemoteApi)
            {
                return GetRemotePage(pageNumber, pageSize, searchFilter, sortColumn, sortAscending, null, null, null, null, useLhsDerived).Items;
            }
            var entries = new List<ChallanEntry>();
            int offset = (pageNumber - 1) * pageSize;

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                var orderBy = BuildOrderBy(sortColumn, sortAscending, useLhsDerived);
                command.CommandText = $@"SELECT * FROM {ActiveTableName} WHERE 
                    ChallanNumber LIKE @filter OR LRNumber LIKE @filter OR VehicleNumber LIKE @filter 
                    OR VehicleType LIKE @filter OR DriverName LIKE @filter OR DriverMobile LIKE @filter
                    OR BrokerName LIKE @filter OR FromLocation LIKE @filter OR ToLocation LIKE @filter 
                    OR OwnerName LIKE @filter OR EngineNo LIKE @filter OR LicenceNo LIKE @filter
                    OR PolicyNo LIKE @filter OR ChassisNo LIKE @filter OR PAN LIKE @filter
                    OR PaidTo LIKE @filter OR Remarks LIKE @filter
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
                            Other = reader["OtherAmount"] == DBNull.Value ? 0m : GetDecimal(reader["OtherAmount"]),
                            Deduction = GetDecimal(reader["Deduction"]),
                            BalancePaidNEFT = GetDecimal(reader["BalancePaidNEFT"]),
                            BalancePaidCash = GetDecimal(reader["BalancePaidCash"]),
                            BalancePaidDate = ParseNullableDate(reader["BalancePaidDate"]),
                            PaidTo = reader["PaidTo"] as string,
                            Remarks = reader["Remarks"] as string,
                            BillAmount = GetDecimal(reader["BillAmount"]),
                            Margin = GetDecimal(reader["Margin"]),
                            PreserveImportedBilling = GetBoolean(reader, "PreserveImportedBilling")
                        });
                    }
                }
            }
            foreach (var entry in entries) entry.RecalculateBalance();
            return entries;
        }

        public List<ChallanEntry> SearchAdvanced(string challanNo, string lrNo, string from, string to, int pageNumber, int pageSize, string sortColumn = "", bool sortAscending = true, bool useLhsDerived = false)
        {
            if (BackendSettings.UseRemoteApi)
            {
                return GetRemotePage(pageNumber, pageSize, null, sortColumn, sortAscending, challanNo, lrNo, from, to, useLhsDerived).Items;
            }
            var entries = new List<ChallanEntry>();
            int offset = (pageNumber - 1) * pageSize;
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                var orderBy = BuildOrderBy(sortColumn, sortAscending, useLhsDerived);
                var conditions = new List<string>();
                if (!string.IsNullOrWhiteSpace(challanNo)) { conditions.Add("ChallanNumber LIKE @challanNo"); command.Parameters.AddWithValue("@challanNo", $"%{challanNo}%"); }
                if (!string.IsNullOrWhiteSpace(lrNo)) { conditions.Add("LRNumber LIKE @lrNo"); command.Parameters.AddWithValue("@lrNo", $"%{lrNo}%"); }
                if (!string.IsNullOrWhiteSpace(from)) { conditions.Add("FromLocation LIKE @from"); command.Parameters.AddWithValue("@from", $"%{from}%"); }
                if (!string.IsNullOrWhiteSpace(to)) { conditions.Add("ToLocation LIKE @to"); command.Parameters.AddWithValue("@to", $"%{to}%"); }
                string where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
                command.CommandText = $"SELECT * FROM {ActiveTableName} {where} ORDER BY {orderBy} LIMIT @limit OFFSET @offset;";
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
            if (BackendSettings.UseRemoteApi)
            {
                return GetRemotePage(1, 1, null, string.Empty, true, challanNo, lrNo, from, to).TotalCount;
            }
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                var conditions = new List<string>();
                if (!string.IsNullOrWhiteSpace(challanNo)) { conditions.Add("ChallanNumber LIKE @challanNo"); command.Parameters.AddWithValue("@challanNo", $"%{challanNo}%"); }
                if (!string.IsNullOrWhiteSpace(lrNo)) { conditions.Add("LRNumber LIKE @lrNo"); command.Parameters.AddWithValue("@lrNo", $"%{lrNo}%"); }
                if (!string.IsNullOrWhiteSpace(from)) { conditions.Add("FromLocation LIKE @from"); command.Parameters.AddWithValue("@from", $"%{from}%"); }
                if (!string.IsNullOrWhiteSpace(to)) { conditions.Add("ToLocation LIKE @to"); command.Parameters.AddWithValue("@to", $"%{to}%"); }
                string where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
                command.CommandText = $"SELECT COUNT(*) FROM {ActiveTableName} {where};";
                connection.Open();
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public int GetTotalCount()
        {
            if (BackendSettings.UseRemoteApi)
            {
                return GetRemotePage(1, 1, null, string.Empty, true).TotalCount;
            }
            return GetTotalCount("");
        }

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
                {
                    command.CommandText = $"SELECT COUNT(*) FROM {ActiveTableName};";
                }
                else
                {
                    command.CommandText = $@"SELECT COUNT(*) FROM {ActiveTableName} WHERE 
                        ChallanNumber LIKE @filter OR LRNumber LIKE @filter OR VehicleNumber LIKE @filter 
                        OR VehicleType LIKE @filter OR DriverName LIKE @filter OR DriverMobile LIKE @filter
                        OR BrokerName LIKE @filter OR FromLocation LIKE @filter OR ToLocation LIKE @filter 
                        OR OwnerName LIKE @filter OR EngineNo LIKE @filter OR LicenceNo LIKE @filter
                        OR PolicyNo LIKE @filter OR ChassisNo LIKE @filter OR PAN LIKE @filter
                        OR PaidTo LIKE @filter OR Remarks LIKE @filter;";
                    command.Parameters.AddWithValue("@filter", $"%{searchFilter}%");
                }
                connection.Open();
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public decimal GetTotalDue(string searchFilter = "", bool useLhsDerived = false)
        {
            if (BackendSettings.UseRemoteApi)
            {
                var query = AppendLedgerQuery("api/challans/summary");
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(searchFilter))
                {
                    parts.Add($"search={RemoteApiClient.UrlEncode(searchFilter)}");
                }
                if (useLhsDerived) parts.Add("useLhsDerived=true");
                if (parts.Count > 0) query += "?" + string.Join("&", parts);

                try
                {
                    return RemoteApiClient.Get<RemoteChallanSummary>(query)?.TotalDue ?? 0m;
                }
                catch
                {
                    var rows = GetAllRemoteSafe();
                    if (string.IsNullOrWhiteSpace(searchFilter))
                    {
                        return rows.Sum(x => useLhsDerived ? x.ChallanDue : x.Due);
                    }

                    return rows.Where(e =>
                            Contains(e.ChallanNumber, searchFilter) ||
                            Contains(e.LRNumber, searchFilter) ||
                            Contains(e.VehicleNumber, searchFilter) ||
                            Contains(e.VehicleType, searchFilter) ||
                            Contains(e.DriverName, searchFilter) ||
                            Contains(e.BrokerName, searchFilter) ||
                            Contains(e.From, searchFilter) ||
                            Contains(e.To, searchFilter) ||
                            Contains(e.OwnerName, searchFilter))
                        .Sum(x => useLhsDerived ? x.ChallanDue : x.Due);
                }
            }

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                if (string.IsNullOrWhiteSpace(searchFilter))
                {
                    command.CommandText = useLhsDerived
                        ? $@"SELECT COALESCE(SUM(((LorryHire + OtherAmount) - LessTDS - AdvanceAmount + Detention + Hamali + Deduction) - BalancePaidNEFT - BalancePaidCash), 0) FROM {ActiveTableName};"
                        : $@"SELECT COALESCE(SUM((LorryHire - LessTDS - AdvanceAmount + Detention + Hamali + Deduction) - BalancePaidNEFT - BalancePaidCash), 0) FROM {ActiveTableName};";
                }
                else
                {
                    command.CommandText = useLhsDerived
                        ? $@"SELECT COALESCE(SUM(((LorryHire + OtherAmount) - LessTDS - AdvanceAmount + Detention + Hamali + Deduction) - BalancePaidNEFT - BalancePaidCash), 0) FROM {ActiveTableName} WHERE 
                        ChallanNumber LIKE @filter OR LRNumber LIKE @filter OR VehicleNumber LIKE @filter 
                        OR VehicleType LIKE @filter OR DriverName LIKE @filter OR DriverMobile LIKE @filter OR BrokerName LIKE @filter 
                        OR FromLocation LIKE @filter OR ToLocation LIKE @filter OR OwnerName LIKE @filter
                        OR EngineNo LIKE @filter OR LicenceNo LIKE @filter OR PolicyNo LIKE @filter OR ChassisNo LIKE @filter
                        OR PAN LIKE @filter OR PaidTo LIKE @filter OR Remarks LIKE @filter;"
                        : $@"SELECT COALESCE(SUM((LorryHire - LessTDS - AdvanceAmount + Detention + Hamali + Deduction) - BalancePaidNEFT - BalancePaidCash), 0) FROM {ActiveTableName} WHERE 
                        ChallanNumber LIKE @filter OR LRNumber LIKE @filter OR VehicleNumber LIKE @filter 
                        OR VehicleType LIKE @filter OR DriverName LIKE @filter OR DriverMobile LIKE @filter OR BrokerName LIKE @filter 
                        OR FromLocation LIKE @filter OR ToLocation LIKE @filter OR OwnerName LIKE @filter
                        OR EngineNo LIKE @filter OR LicenceNo LIKE @filter OR PolicyNo LIKE @filter OR ChassisNo LIKE @filter
                        OR PAN LIKE @filter OR PaidTo LIKE @filter OR Remarks LIKE @filter;";
                    command.Parameters.AddWithValue("@filter", $"%{searchFilter}%");
                }
                connection.Open();
                return Convert.ToDecimal(command.ExecuteScalar() ?? 0m);
            }
        }

        public int GetDueCount(string searchFilter = "", bool useLhsDerived = false)
        {
            if (BackendSettings.UseRemoteApi)
            {
                IEnumerable<ChallanEntry> rows = GetAllRemoteSafe();
                if (!string.IsNullOrWhiteSpace(searchFilter))
                {
                    rows = rows.Where(e =>
                        Contains(e.ChallanNumber, searchFilter) ||
                        Contains(e.LRNumber, searchFilter) ||
                        Contains(e.VehicleNumber, searchFilter) ||
                        Contains(e.VehicleType, searchFilter) ||
                        Contains(e.DriverName, searchFilter) ||
                        Contains(e.BrokerName, searchFilter) ||
                        Contains(e.From, searchFilter) ||
                        Contains(e.To, searchFilter) ||
                        Contains(e.OwnerName, searchFilter));
                }

                return rows.Count(x => (useLhsDerived ? x.ChallanDue : x.Due) > 0m);
            }

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                var dueExpr = useLhsDerived
                    ? "(((LorryHire + OtherAmount) - LessTDS - AdvanceAmount + Detention + Hamali + Deduction) - BalancePaidNEFT - BalancePaidCash)"
                    : "((LorryHire - LessTDS - AdvanceAmount + Detention + Hamali + Deduction) - BalancePaidNEFT - BalancePaidCash)";

                if (string.IsNullOrWhiteSpace(searchFilter))
                {
                    command.CommandText = $"SELECT COUNT(*) FROM {ActiveTableName} WHERE {dueExpr} > 0;";
                }
                else
                {
                    command.CommandText = $@"SELECT COUNT(*) FROM {ActiveTableName} WHERE {dueExpr} > 0 AND (
                        ChallanNumber LIKE @filter OR LRNumber LIKE @filter OR VehicleNumber LIKE @filter 
                        OR VehicleType LIKE @filter OR DriverName LIKE @filter OR DriverMobile LIKE @filter OR BrokerName LIKE @filter 
                        OR FromLocation LIKE @filter OR ToLocation LIKE @filter OR OwnerName LIKE @filter
                        OR EngineNo LIKE @filter OR LicenceNo LIKE @filter OR PolicyNo LIKE @filter OR ChassisNo LIKE @filter
                        OR PAN LIKE @filter OR PaidTo LIKE @filter OR Remarks LIKE @filter);";
                    command.Parameters.AddWithValue("@filter", $"%{searchFilter}%");
                }

                connection.Open();
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public decimal GetTotalDueAdvanced(string challanNo, string lrNo, string from, string to, bool useLhsDerived = false)
        {
            if (BackendSettings.UseRemoteApi)
            {
                var query = AppendLedgerQuery("api/challans/summary");
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(challanNo)) parts.Add($"challanNo={RemoteApiClient.UrlEncode(challanNo)}");
                if (!string.IsNullOrWhiteSpace(lrNo)) parts.Add($"lrNo={RemoteApiClient.UrlEncode(lrNo)}");
                if (!string.IsNullOrWhiteSpace(from)) parts.Add($"from={RemoteApiClient.UrlEncode(from)}");
                if (!string.IsNullOrWhiteSpace(to)) parts.Add($"to={RemoteApiClient.UrlEncode(to)}");
                if (useLhsDerived) parts.Add("useLhsDerived=true");
                if (parts.Count > 0) query += "?" + string.Join("&", parts);

                try
                {
                    return RemoteApiClient.Get<RemoteChallanSummary>(query)?.TotalDue ?? 0m;
                }
                catch
                {
                    var rows = GetAllRemoteSafe().Where(e =>
                        (string.IsNullOrWhiteSpace(challanNo) || Contains(e.ChallanNumber, challanNo)) &&
                        (string.IsNullOrWhiteSpace(lrNo) || Contains(e.LRNumber, lrNo)) &&
                        (string.IsNullOrWhiteSpace(from) || Contains(e.From, from)) &&
                        (string.IsNullOrWhiteSpace(to) || Contains(e.To, to)));
                    return rows.Sum(x => useLhsDerived ? x.ChallanDue : x.Due);
                }
            }

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                var conditions = new List<string>();
                if (!string.IsNullOrWhiteSpace(challanNo)) { conditions.Add("ChallanNumber LIKE @challanNo"); command.Parameters.AddWithValue("@challanNo", $"%{challanNo}%"); }
                if (!string.IsNullOrWhiteSpace(lrNo)) { conditions.Add("LRNumber LIKE @lrNo"); command.Parameters.AddWithValue("@lrNo", $"%{lrNo}%"); }
                if (!string.IsNullOrWhiteSpace(from)) { conditions.Add("FromLocation LIKE @from"); command.Parameters.AddWithValue("@from", $"%{from}%"); }
                if (!string.IsNullOrWhiteSpace(to)) { conditions.Add("ToLocation LIKE @to"); command.Parameters.AddWithValue("@to", $"%{to}%"); }
                string where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
                command.CommandText = useLhsDerived
                    ? $"SELECT COALESCE(SUM(((LorryHire + OtherAmount) - LessTDS - AdvanceAmount + Detention + Hamali + Deduction) - BalancePaidNEFT - BalancePaidCash), 0) FROM {ActiveTableName} {where};"
                    : $"SELECT COALESCE(SUM((LorryHire - LessTDS - AdvanceAmount + Detention + Hamali + Deduction) - BalancePaidNEFT - BalancePaidCash), 0) FROM {ActiveTableName} {where};";
                connection.Open();
                return Convert.ToDecimal(command.ExecuteScalar() ?? 0m);
            }
        }

        public int GetDueCountAdvanced(string challanNo, string lrNo, string from, string to, bool useLhsDerived = false)
        {
            if (BackendSettings.UseRemoteApi)
            {
                var rows = GetAllRemoteSafe().Where(e =>
                    (string.IsNullOrWhiteSpace(challanNo) || Contains(e.ChallanNumber, challanNo)) &&
                    (string.IsNullOrWhiteSpace(lrNo) || Contains(e.LRNumber, lrNo)) &&
                    (string.IsNullOrWhiteSpace(from) || Contains(e.From, from)) &&
                    (string.IsNullOrWhiteSpace(to) || Contains(e.To, to)));
                return rows.Count(x => (useLhsDerived ? x.ChallanDue : x.Due) > 0m);
            }

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                var conditions = new List<string>();
                if (!string.IsNullOrWhiteSpace(challanNo)) { conditions.Add("ChallanNumber LIKE @challanNo"); command.Parameters.AddWithValue("@challanNo", $"%{challanNo}%"); }
                if (!string.IsNullOrWhiteSpace(lrNo)) { conditions.Add("LRNumber LIKE @lrNo"); command.Parameters.AddWithValue("@lrNo", $"%{lrNo}%"); }
                if (!string.IsNullOrWhiteSpace(from)) { conditions.Add("FromLocation LIKE @from"); command.Parameters.AddWithValue("@from", $"%{from}%"); }
                if (!string.IsNullOrWhiteSpace(to)) { conditions.Add("ToLocation LIKE @to"); command.Parameters.AddWithValue("@to", $"%{to}%"); }
                var dueExpr = useLhsDerived
                    ? "(((LorryHire + OtherAmount) - LessTDS - AdvanceAmount + Detention + Hamali + Deduction) - BalancePaidNEFT - BalancePaidCash)"
                    : "((LorryHire - LessTDS - AdvanceAmount + Detention + Hamali + Deduction) - BalancePaidNEFT - BalancePaidCash)";
                conditions.Add($"{dueExpr} > 0");
                var where = "WHERE " + string.Join(" AND ", conditions);
                command.CommandText = $"SELECT COUNT(*) FROM {ActiveTableName} {where};";
                connection.Open();
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public int GetMaxSr()
        {
            if (BackendSettings.UseRemoteApi)
            {
                return GetAllRemoteSafe().Select(x => x.Sr).DefaultIfEmpty(0).Max();
            }
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand($"SELECT COALESCE(MAX(Sr), 0) FROM {ActiveTableName};", connection))
            {
                connection.Open();
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public ChallanEntry FindByChallanNumber(string challanNumber)
        {
            if (string.IsNullOrWhiteSpace(challanNumber)) return null;
            var normalizedInput = NormalizeLookupChallanNumber(challanNumber);
            if (BackendSettings.UseRemoteApi)
            {
                return GetAllRemoteSafe().Find(e =>
                    string.Equals(
                        NormalizeLookupChallanNumber(e.ChallanNumber),
                        normalizedInput,
                        StringComparison.OrdinalIgnoreCase));
            }
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand($"SELECT * FROM {ActiveTableName} WHERE LOWER(ChallanNumber) = LOWER(@num) LIMIT 1;", connection))
            {
                command.Parameters.AddWithValue("@num", normalizedInput);
                connection.Open();
                using (var reader = command.ExecuteReader())
                    if (reader.Read()) return MapReader(reader);
            }

            return GetAll()
                .FirstOrDefault(e => string.Equals(
                    NormalizeLookupChallanNumber(e.ChallanNumber),
                    normalizedInput,
                    StringComparison.OrdinalIgnoreCase));
        }

        public ChallanEntry FindById(int id)
        {
            if (id <= 0) return null;
            if (BackendSettings.UseRemoteApi)
            {
                try
                {
                    return RemoteApiClient.Get<ChallanEntry>(AppendLedgerQuery($"api/challans/{id}"));
                }
                catch
                {
                    return GetAllRemoteSafe().Find(e => e.Id == id);
                }
            }
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand($"SELECT * FROM {ActiveTableName} WHERE Id = @id LIMIT 1;", connection))
            {
                command.Parameters.AddWithValue("@id", id);
                connection.Open();
                using (var reader = command.ExecuteReader())
                    if (reader.Read()) return MapReader(reader);
            }
            return null;
        }

        public List<ChallanEntry> GetByChallanNumbers(IEnumerable<string> challanNumbers)
        {
            var keys = (challanNumbers ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (keys.Count == 0)
            {
                return new List<ChallanEntry>();
            }

            if (BackendSettings.UseRemoteApi)
            {
                try
                {
                    return RemoteApiClient.Post<List<ChallanEntry>>(AppendLedgerQuery("api/challans/by-numbers"), keys) ?? new List<ChallanEntry>();
                }
                catch
                {
                    var fallbackLookup = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
                    return GetAllRemoteSafe()
                        .Where(x => x != null && fallbackLookup.Contains((x.ChallanNumber ?? string.Empty).Trim()))
                        .ToList();
                }
            }

            var lookup = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
            return GetAll()
                .Where(x => x != null && lookup.Contains((x.ChallanNumber ?? string.Empty).Trim()))
                .ToList();
        }

        public List<ChallanEntry> GetPendingBookingItems(int limit = 0)
        {
            if (BackendSettings.UseRemoteApi)
            {
                var route = AppendLedgerQuery("api/challans/pending-bookings");
                if (limit > 0)
                {
                    route += route.Contains("?")
                        ? $"&limit={limit}"
                        : $"?limit={limit}";
                }

                return RemoteApiClient.GetList<ChallanEntry>(route);
            }

            return GetAll();
        }

        public RemotePagedResult<ChallanEntry> GetPendingBookingPage(int pageNumber, int pageSize, string search = "")
        {
            pageNumber = pageNumber <= 0 ? 1 : pageNumber;
            pageSize = pageSize <= 0 ? 50 : pageSize;

            if (BackendSettings.UseRemoteApi)
            {
                var route = AppendLedgerQuery($"api/challans/pending-bookings/page?page={pageNumber}&pageSize={pageSize}");
                if (!string.IsNullOrWhiteSpace(search))
                {
                    route += $"&search={RemoteApiClient.UrlEncode(search)}";
                }

                try
                {
                    return RemoteApiClient.GetPage<ChallanEntry>(route);
                }
                catch
                {
                }
            }

            var items = GetPendingBookingItems(0);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                items = items
                    .Where(entry =>
                        (!string.IsNullOrWhiteSpace(entry.ChallanNumber) && entry.ChallanNumber.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                        || (!string.IsNullOrWhiteSpace(entry.LRNumber) && entry.LRNumber.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                        || (!string.IsNullOrWhiteSpace(entry.From) && entry.From.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                        || (!string.IsNullOrWhiteSpace(entry.To) && entry.To.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                        || (!string.IsNullOrWhiteSpace(entry.VehicleNumber) && entry.VehicleNumber.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                        || (!string.IsNullOrWhiteSpace(entry.BrokerName) && entry.BrokerName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                        || entry.Date.ToString("dd-MMM-yyyy").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            return new RemotePagedResult<ChallanEntry>
            {
                TotalCount = items.Count,
                Items = items
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList()
            };
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

            entry.ChallanNumber = ChallanNumberFormatter.Normalize(entry.ChallanNumber, entry.Date);

            if (BackendSettings.UseRemoteApi)
            {
                try
                {
                    var createRoute = AppendLedgerQuery("api/challans");
                    if (entry.Id <= 0)
                    {
                        entry.Id = RemoteApiClient.PostAndReadInt(createRoute, entry);
                    }
                    else
                    {
                        RemoteApiClient.Put(AppendLedgerQuery($"api/challans/{entry.Id}"), entry);
                    }
                }
                catch (Exception)
                {
                    if (entry.Id <= 0 && !string.IsNullOrWhiteSpace(entry.ChallanNumber))
                    {
                        var existing = FindByChallanNumber(entry.ChallanNumber);
                        if (existing != null)
                        {
                            entry.Id = existing.Id;
                            return;
                        }
                    }

                    throw;
                }
                MasterDataCache.InvalidateVehicles();
                return;
            }

            entry.RecalculateBalance();

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                connection.Open();

                if (IsChallanLedgerMode)
                {
                    SaveToLocalTable(connection, command, ChallanLedgerTableName, entry, includeSourcePurchaseId: true);
                }
                else
                {
                    SaveToLocalTable(connection, command, PurchaseTableName, entry, includeSourcePurchaseId: false);
                    SyncLocalChallanMirror(connection, entry);
                }
            }

            try
            {
                new VehicleRepository().UpsertFromChallan(entry);
            }
            catch { }
        }

        private static void SaveToLocalTable(SQLiteConnection connection, SQLiteCommand command, string tableName, ChallanEntry entry, bool includeSourcePurchaseId)
        {
            command.Parameters.Clear();

            if (entry.Id <= 0)
            {
                command.CommandText = includeSourcePurchaseId
                    ? $@"
INSERT INTO {tableName} (
    SourcePurchaseId, Sr, ChallanNumber, Date, LRNumber, BrokerName, FromLocation, ToLocation, VehicleNumber, VehicleType,
    DriverName, DriverMobile, EngineNo, LicenceNo, PolicyNo, ChassisNo, OwnerName, PAN,
    LorryHire, LessTDS, AdvanceAmount, AdvanceNEFT, AdvanceCash, AdvanceDate,
    Detention, Hamali, OtherAmount, Deduction, BalancePaidNEFT, BalancePaidCash, BalancePaidDate,
    PaidTo, Remarks, BillAmount, Margin, PreserveImportedBilling
) VALUES (
    @SourcePurchaseId, @Sr, @ChallanNumber, @Date, @LRNumber, @BrokerName, @FromLocation, @ToLocation, @VehicleNumber, @VehicleType,
    @DriverName, @DriverMobile, @EngineNo, @LicenceNo, @PolicyNo, @ChassisNo, @OwnerName, @PAN,
    @LorryHire, @LessTDS, @AdvanceAmount, @AdvanceNEFT, @AdvanceCash, @AdvanceDate,
    @Detention, @Hamali, @OtherAmount, @Deduction, @BalancePaidNEFT, @BalancePaidCash, @BalancePaidDate,
    @PaidTo, @Remarks, @BillAmount, @Margin, @PreserveImportedBilling
);
SELECT last_insert_rowid();"
                    : $@"
INSERT INTO {tableName} (
    Sr, ChallanNumber, Date, LRNumber, BrokerName, FromLocation, ToLocation, VehicleNumber, VehicleType,
    DriverName, DriverMobile, EngineNo, LicenceNo, PolicyNo, ChassisNo, OwnerName, PAN,
    LorryHire, LessTDS, AdvanceAmount, AdvanceNEFT, AdvanceCash, AdvanceDate,
    Detention, Hamali, OtherAmount, Deduction, BalancePaidNEFT, BalancePaidCash, BalancePaidDate,
    PaidTo, Remarks, BillAmount, Margin, PreserveImportedBilling
) VALUES (
    @Sr, @ChallanNumber, @Date, @LRNumber, @BrokerName, @FromLocation, @ToLocation, @VehicleNumber, @VehicleType,
    @DriverName, @DriverMobile, @EngineNo, @LicenceNo, @PolicyNo, @ChassisNo, @OwnerName, @PAN,
    @LorryHire, @LessTDS, @AdvanceAmount, @AdvanceNEFT, @AdvanceCash, @AdvanceDate,
    @Detention, @Hamali, @OtherAmount, @Deduction, @BalancePaidNEFT, @BalancePaidCash, @BalancePaidDate,
    @PaidTo, @Remarks, @BillAmount, @Margin, @PreserveImportedBilling
);
SELECT last_insert_rowid();";

                AddParameters(command, entry, includeSourcePurchaseId);
                entry.Id = Convert.ToInt32((long)command.ExecuteScalar());
            }
            else
            {
                command.CommandText = includeSourcePurchaseId
                    ? $@"
UPDATE {tableName} SET
    SourcePurchaseId = @SourcePurchaseId,
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
    OtherAmount = @OtherAmount,
    Deduction = @Deduction,
    BalancePaidNEFT = @BalancePaidNEFT,
    BalancePaidCash = @BalancePaidCash,
    BalancePaidDate = @BalancePaidDate,
    PaidTo = @PaidTo,
    Remarks = @Remarks,
    BillAmount = @BillAmount,
    Margin = @Margin,
    PreserveImportedBilling = @PreserveImportedBilling
WHERE Id = @Id;"
                    : $@"
UPDATE {tableName} SET
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
    OtherAmount = @OtherAmount,
    Deduction = @Deduction,
    BalancePaidNEFT = @BalancePaidNEFT,
    BalancePaidCash = @BalancePaidCash,
    BalancePaidDate = @BalancePaidDate,
    PaidTo = @PaidTo,
    Remarks = @Remarks,
    BillAmount = @BillAmount,
    Margin = @Margin,
    PreserveImportedBilling = @PreserveImportedBilling
WHERE Id = @Id;";

                AddParameters(command, entry, includeSourcePurchaseId);
                command.Parameters.AddWithValue("@Id", entry.Id);
                command.ExecuteNonQuery();
            }
        }

        private static void SyncLocalChallanMirror(SQLiteConnection connection, ChallanEntry purchaseEntry)
        {
            var existing = FindLocalMirror(connection, purchaseEntry.Id, purchaseEntry.ChallanNumber);
            var mirror = CreateMirrorFromPurchase(purchaseEntry, existing);

            mirror.SourcePurchaseId = purchaseEntry.Id;

            using (var command = connection.CreateCommand())
            {
                SaveToLocalTable(connection, command, ChallanLedgerTableName, mirror, includeSourcePurchaseId: true);
            }
        }

        private static ChallanEntry FindLocalMirror(SQLiteConnection connection, int sourcePurchaseId, string challanNumber)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $@"
SELECT * FROM {ChallanLedgerTableName}
WHERE (SourcePurchaseId IS NOT NULL AND SourcePurchaseId = @SourcePurchaseId)
   OR LOWER(ChallanNumber) = LOWER(@ChallanNumber)
ORDER BY CASE WHEN SourcePurchaseId = @SourcePurchaseId THEN 0 ELSE 1 END, Id
LIMIT 1;";
                command.Parameters.AddWithValue("@SourcePurchaseId", sourcePurchaseId);
                command.Parameters.AddWithValue("@ChallanNumber", challanNumber ?? string.Empty);

                using (var reader = command.ExecuteReader())
                {
                    return reader.Read() ? MapReader(reader) : null;
                }
            }
        }

        private static ChallanEntry CreateMirrorFromPurchase(ChallanEntry purchaseEntry, ChallanEntry existingMirror)
        {
            var mirror = existingMirror?.CloneForPersistence() ?? purchaseEntry.CloneForPersistence();
            mirror.Id = existingMirror?.Id ?? 0;
            mirror.ChallanNumber = purchaseEntry.ChallanNumber;
            mirror.Date = purchaseEntry.Date;
            mirror.LRNumber = purchaseEntry.LRNumber;
            mirror.BrokerName = purchaseEntry.BrokerName;
            mirror.From = purchaseEntry.From;
            mirror.To = purchaseEntry.To;
            mirror.VehicleNumber = purchaseEntry.VehicleNumber;
            mirror.VehicleType = purchaseEntry.VehicleType;
            mirror.DriverName = purchaseEntry.DriverName;
            mirror.DriverMobile = purchaseEntry.DriverMobile;
            mirror.EngineNo = purchaseEntry.EngineNo;
            mirror.LicenceNo = purchaseEntry.LicenceNo;
            mirror.PolicyNo = purchaseEntry.PolicyNo;
            mirror.ChassisNo = purchaseEntry.ChassisNo;
            mirror.OwnerName = purchaseEntry.OwnerName;
            mirror.PAN = purchaseEntry.PAN;
            mirror.LorryHire = purchaseEntry.LorryHire;
            mirror.BillAmount = purchaseEntry.BillAmount;
            mirror.Margin = purchaseEntry.Margin;
            if (existingMirror == null)
            {
                mirror.LessTDS = purchaseEntry.LessTDS;
                mirror.AdvanceAmount = purchaseEntry.AdvanceAmount;
                mirror.AdvanceNEFT = purchaseEntry.AdvanceNEFT;
                mirror.AdvanceCash = purchaseEntry.AdvanceCash;
                mirror.AdvanceDate = purchaseEntry.AdvanceDate;
                mirror.Detention = purchaseEntry.Detention;
                mirror.Hamali = purchaseEntry.Hamali;
                mirror.Other = purchaseEntry.Other;
                mirror.Deduction = purchaseEntry.Deduction;
                mirror.BalancePaidNEFT = purchaseEntry.BalancePaidNEFT;
                mirror.BalancePaidCash = purchaseEntry.BalancePaidCash;
                mirror.BalancePaidDate = purchaseEntry.BalancePaidDate;
                mirror.PaidTo = purchaseEntry.PaidTo;
                mirror.Remarks = purchaseEntry.Remarks;
                mirror.PreserveImportedBilling = purchaseEntry.PreserveImportedBilling;
            }

            mirror.RecalculateBalance();
            return mirror;
        }

        public void Delete(ChallanEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (BackendSettings.UseRemoteApi)
            {
                if (entry.Id > 0)
                {
                    RemoteApiClient.Delete(AppendLedgerQuery($"api/challans/{entry.Id}"));
                }
                return;
            }

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                if (entry.Id > 0)
                {
                    command.CommandText = $"DELETE FROM {ActiveTableName} WHERE Id = @Id;";
                    command.Parameters.AddWithValue("@Id", entry.Id);
                }
                else
                {
                    command.CommandText = $"DELETE FROM {ActiveTableName} WHERE ChallanNumber = @ChallanNumber;";
                    command.Parameters.AddWithValue("@ChallanNumber", entry.ChallanNumber ?? string.Empty);
                }

                command.ExecuteNonQuery();

                if (!IsChallanLedgerMode)
                {
                    using (var mirrorDelete = connection.CreateCommand())
                    {
                        mirrorDelete.CommandText = $@"DELETE FROM {ChallanLedgerTableName}
WHERE SourcePurchaseId = @SourcePurchaseId
   OR LOWER(ChallanNumber) = LOWER(@ChallanNumber);";
                        mirrorDelete.Parameters.AddWithValue("@SourcePurchaseId", entry.Id);
                        mirrorDelete.Parameters.AddWithValue("@ChallanNumber", entry.ChallanNumber ?? string.Empty);
                        mirrorDelete.ExecuteNonQuery();
                    }
                }
            }
        }

        private List<ChallanEntry> GetAllRemoteSafe()
        {
            return RemoteApiClient.GetList<ChallanEntry>(AppendLedgerQuery("api/challans"))
                .OrderBy(e => ChallanNumberFormatter.GetFinancialYearStart(e.ChallanNumber, e.Date))
                .ThenBy(e => ChallanNumberFormatter.GetSequence(e.ChallanNumber))
                .ThenBy(e => e.Sr)
                .ThenBy(e => e.Id)
                .ToList();
        }

        private RemotePagedResult<ChallanEntry> GetRemotePage(
            int pageNumber,
            int pageSize,
            string searchFilter,
            string sortColumn,
            bool sortAscending,
            string challanNo = null,
            string lrNo = null,
            string from = null,
            string to = null,
            bool useLhsDerived = false)
        {
            var query = AppendLedgerQuery($"api/challans/page?page={pageNumber}&pageSize={pageSize}&asc={sortAscending.ToString().ToLowerInvariant()}");
            if (!string.IsNullOrWhiteSpace(searchFilter)) query += $"&search={RemoteApiClient.UrlEncode(searchFilter)}";
            if (!string.IsNullOrWhiteSpace(sortColumn)) query += $"&sort={RemoteApiClient.UrlEncode(sortColumn)}";
            if (!string.IsNullOrWhiteSpace(challanNo)) query += $"&challanNo={RemoteApiClient.UrlEncode(challanNo)}";
            if (!string.IsNullOrWhiteSpace(lrNo)) query += $"&lrNo={RemoteApiClient.UrlEncode(lrNo)}";
            if (!string.IsNullOrWhiteSpace(from)) query += $"&from={RemoteApiClient.UrlEncode(from)}";
            if (!string.IsNullOrWhiteSpace(to)) query += $"&to={RemoteApiClient.UrlEncode(to)}";
            if (useLhsDerived) query += "&useLhsDerived=true";
            try
            {
                return RemoteApiClient.GetPage<ChallanEntry>(query);
            }
            catch
            {
                var effectiveSortColumn = string.IsNullOrWhiteSpace(sortColumn) ? "ChallanNumber" : sortColumn;
                return BuildRemotePageFromLocalSort(pageNumber, pageSize, searchFilter, effectiveSortColumn, sortAscending, challanNo, lrNo, from, to, useLhsDerived);
            }
        }

        internal RemotePagedResult<ChallanEntry> GetRemotePageResult(
            int pageNumber,
            int pageSize,
            string searchFilter,
            string sortColumn,
            bool sortAscending,
            string challanNo = null,
            string lrNo = null,
            string from = null,
            string to = null,
            bool useLhsDerived = false)
        {
            return GetRemotePage(pageNumber, pageSize, searchFilter, sortColumn, sortAscending, challanNo, lrNo, from, to, useLhsDerived);
        }

        internal RemoteChallanLedgerPageResult GetRemoteLedgerPageResult(
            int pageNumber,
            int pageSize,
            string searchFilter,
            string sortColumn,
            bool sortAscending,
            string challanNo = null,
            string lrNo = null,
            string from = null,
            string to = null,
            bool useLhsDerived = false)
        {
            var query = AppendLedgerQuery($"api/challans/ledger-page?page={pageNumber}&pageSize={pageSize}&asc={sortAscending.ToString().ToLowerInvariant()}");
            if (!string.IsNullOrWhiteSpace(searchFilter)) query += $"&search={RemoteApiClient.UrlEncode(searchFilter)}";
            if (!string.IsNullOrWhiteSpace(sortColumn)) query += $"&sort={RemoteApiClient.UrlEncode(sortColumn)}";
            if (!string.IsNullOrWhiteSpace(challanNo)) query += $"&challanNo={RemoteApiClient.UrlEncode(challanNo)}";
            if (!string.IsNullOrWhiteSpace(lrNo)) query += $"&lrNo={RemoteApiClient.UrlEncode(lrNo)}";
            if (!string.IsNullOrWhiteSpace(from)) query += $"&from={RemoteApiClient.UrlEncode(from)}";
            if (!string.IsNullOrWhiteSpace(to)) query += $"&to={RemoteApiClient.UrlEncode(to)}";
            if (useLhsDerived) query += "&useLhsDerived=true";

            try
            {
                return RemoteApiClient.Get<RemoteChallanLedgerPageResult>(query) ?? new RemoteChallanLedgerPageResult();
            }
            catch
            {
                var page = GetRemotePage(pageNumber, pageSize, searchFilter, sortColumn, sortAscending, challanNo, lrNo, from, to, useLhsDerived);
                var summary = string.IsNullOrWhiteSpace(challanNo) && string.IsNullOrWhiteSpace(lrNo) && string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to)
                    ? GetTotalDue(searchFilter, useLhsDerived)
                    : GetTotalDueAdvanced(challanNo, lrNo, from, to, useLhsDerived);

                return new RemoteChallanLedgerPageResult
                {
                    Items = page?.Items ?? new List<ChallanEntry>(),
                    TotalCount = page?.TotalCount ?? 0,
                    TotalDue = summary,
                    CommentIds = new List<int>()
                };
            }
        }

        private RemotePagedResult<ChallanEntry> BuildRemotePageFromLocalSort(
            int pageNumber,
            int pageSize,
            string searchFilter,
            string sortColumn,
            bool sortAscending,
            string challanNo,
            string lrNo,
            string from,
            string to,
            bool useLhsDerived)
        {
            var filtered = GetAllRemoteSafe().Where(e =>
                (string.IsNullOrWhiteSpace(searchFilter) ||
                 Contains(e.ChallanNumber, searchFilter) ||
                 Contains(e.LRNumber, searchFilter) ||
                 Contains(e.VehicleNumber, searchFilter) ||
                 Contains(e.VehicleType, searchFilter) ||
                 Contains(e.DriverName, searchFilter) ||
                 Contains(e.DriverMobile, searchFilter) ||
                 Contains(e.BrokerName, searchFilter) ||
                 Contains(e.From, searchFilter) ||
                 Contains(e.To, searchFilter) ||
                 Contains(e.OwnerName, searchFilter) ||
                 Contains(e.EngineNo, searchFilter) ||
                 Contains(e.LicenceNo, searchFilter) ||
                 Contains(e.PolicyNo, searchFilter) ||
                 Contains(e.ChassisNo, searchFilter) ||
                 Contains(e.PAN, searchFilter) ||
                 Contains(e.PaidTo, searchFilter) ||
                 Contains(e.Remarks, searchFilter)) &&
                (string.IsNullOrWhiteSpace(challanNo) || Contains(e.ChallanNumber, challanNo)) &&
                (string.IsNullOrWhiteSpace(lrNo) || Contains(e.LRNumber, lrNo)) &&
                (string.IsNullOrWhiteSpace(from) || Contains(e.From, from)) &&
                (string.IsNullOrWhiteSpace(to) || Contains(e.To, to)));

            var sorted = ApplySort(filtered, sortColumn, sortAscending, useLhsDerived).ToList();
            return new RemotePagedResult<ChallanEntry>
            {
                TotalCount = sorted.Count,
                Items = sorted.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList()
            };
        }

        private static IEnumerable<ChallanEntry> ApplySort(IEnumerable<ChallanEntry> source, string sortColumn, bool ascending, bool useLhsDerived = false)
        {
            var ordered = source ?? Enumerable.Empty<ChallanEntry>();
            if (string.Equals(sortColumn, "challannumber", StringComparison.OrdinalIgnoreCase))
            {
                return ascending
                    ? ordered
                        .OrderBy(e => ChallanNumberFormatter.GetFinancialYearStart(e.ChallanNumber, e.Date))
                        .ThenBy(e => ChallanNumberFormatter.GetSequence(e.ChallanNumber))
                        .ThenBy(e => e.Id)
                    : ordered
                        .OrderByDescending(e => ChallanNumberFormatter.GetFinancialYearStart(e.ChallanNumber, e.Date))
                        .ThenByDescending(e => ChallanNumberFormatter.GetSequence(e.ChallanNumber))
                        .ThenByDescending(e => e.Id);
            }

            Func<ChallanEntry, object> keySelector;
            switch ((sortColumn ?? string.Empty).ToLowerInvariant())
            {
                case "challannumber": keySelector = e => e.ChallanNumber ?? string.Empty; break;
                case "date": keySelector = e => e.Date; break;
                case "lrnumber": keySelector = e => e.LRNumber ?? string.Empty; break;
                case "brokername": keySelector = e => e.BrokerName ?? string.Empty; break;
                case "from":
                case "fromlocation": keySelector = e => e.From ?? string.Empty; break;
                case "to":
                case "tolocation": keySelector = e => e.To ?? string.Empty; break;
                case "vehiclenumber": keySelector = e => e.VehicleNumber ?? string.Empty; break;
                case "vehicletype": keySelector = e => e.VehicleType ?? string.Empty; break;
                case "drivername": keySelector = e => e.DriverName ?? string.Empty; break;
                case "ownername": keySelector = e => e.OwnerName ?? string.Empty; break;
                case "lorryhire": keySelector = e => e.LorryHire; break;
                case "other":
                case "otheramount": keySelector = e => e.Other; break;
                case "lhs": keySelector = e => e.LHS; break;
                case "detention": keySelector = e => e.Detention; break;
                case "hamali": keySelector = e => e.Hamali; break;
                case "billamount": keySelector = e => e.BillAmount; break;
                case "balance": keySelector = e => useLhsDerived ? e.ChallanBalance : e.Balance; break;
                case "due": keySelector = e => useLhsDerived ? e.ChallanDue : e.Due; break;
                case "margin": keySelector = e => useLhsDerived ? e.ChallanMargin : e.Margin; break;
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
            using (var command = new SQLiteCommand($"SELECT COUNT(1) FROM {PurchaseTableName};", connection))
            {
                connection.Open();
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }

        private static void AddParameters(SQLiteCommand command, ChallanEntry entry, bool includeSourcePurchaseId = false)
        {
            if (includeSourcePurchaseId)
            {
                command.Parameters.AddWithValue("@SourcePurchaseId", entry.SourcePurchaseId.HasValue ? (object)entry.SourcePurchaseId.Value : DBNull.Value);
            }
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
            command.Parameters.AddWithValue("@OtherAmount", entry.Other);
            command.Parameters.AddWithValue("@Deduction", entry.Deduction);
            command.Parameters.AddWithValue("@BalancePaidNEFT", entry.BalancePaidNEFT);
            command.Parameters.AddWithValue("@BalancePaidCash", entry.BalancePaidCash);
            command.Parameters.AddWithValue("@BalancePaidDate", entry.BalancePaidDate.HasValue ? (object)entry.BalancePaidDate.Value.ToString("o") : DBNull.Value);
            command.Parameters.AddWithValue("@PaidTo", (object)entry.PaidTo ?? DBNull.Value);
            command.Parameters.AddWithValue("@Remarks", (object)entry.Remarks ?? DBNull.Value);
            command.Parameters.AddWithValue("@BillAmount", entry.BillAmount);
            command.Parameters.AddWithValue("@Margin", entry.Margin);
            command.Parameters.AddWithValue("@PreserveImportedBilling", entry.PreserveImportedBilling ? 1 : 0);
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
                Other = reader["OtherAmount"] == DBNull.Value ? 0m : GetDecimal(reader["OtherAmount"]),
                Deduction = GetDecimal(reader["Deduction"]),
                BalancePaidNEFT = GetDecimal(reader["BalancePaidNEFT"]),
                BalancePaidCash = GetDecimal(reader["BalancePaidCash"]),
                BalancePaidDate = ParseNullableDate(reader["BalancePaidDate"]),
                PaidTo = reader["PaidTo"] as string,
                Remarks = reader["Remarks"] as string,
                BillAmount = GetDecimal(reader["BillAmount"]),
                Margin = GetDecimal(reader["Margin"]),
                PreserveImportedBilling = HasColumn(reader, "PreserveImportedBilling") && GetBoolean(reader, "PreserveImportedBilling"),
                SourcePurchaseId = HasColumn(reader, "SourcePurchaseId") && reader["SourcePurchaseId"] != DBNull.Value
                    ? Convert.ToInt32(reader["SourcePurchaseId"])
                    : (int?)null
            };
        }

        private static bool GetBoolean(IDataRecord reader, string columnName)
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

        private static bool HasColumn(IDataRecord reader, string columnName)
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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

        private static string BuildOrderBy(string sortColumn, bool ascending, bool useLhsDerived = false)
        {
            if (string.IsNullOrEmpty(sortColumn)) return "Sr, Id";
            var dir = ascending ? "ASC" : "DESC";
            // Map property names to DB column names
            switch (sortColumn.ToLower())
            {
                case "challannumber":
                    return $@"COALESCE(
                                   CASE
                                       WHEN INSTR(COALESCE(ChallanNumber, ''), '/') > 0 THEN
                                           CASE
                                               WHEN CAST(COALESCE(NULLIF(SUBSTR(TRIM(SUBSTR(ChallanNumber, INSTR(ChallanNumber, '/') + 1)), 1, 2), ''), '0') AS INTEGER) >= 50
                                                   THEN 1900 + CAST(COALESCE(NULLIF(SUBSTR(TRIM(SUBSTR(ChallanNumber, INSTR(ChallanNumber, '/') + 1)), 1, 2), ''), '0') AS INTEGER)
                                               ELSE 2000 + CAST(COALESCE(NULLIF(SUBSTR(TRIM(SUBSTR(ChallanNumber, INSTR(ChallanNumber, '/') + 1)), 1, 2), ''), '0') AS INTEGER)
                                           END
                                   END,
                                   CASE WHEN CAST(STRFTIME('%m', Date) AS INTEGER) >= 4
                                       THEN CAST(STRFTIME('%Y', Date) AS INTEGER)
                                       ELSE CAST(STRFTIME('%Y', Date) AS INTEGER) - 1
                                   END
                               ) {dir},
                               CAST(COALESCE(NULLIF(TRIM(SUBSTR(ChallanNumber, 1, CASE WHEN INSTR(ChallanNumber, '/') > 0 THEN INSTR(ChallanNumber, '/') - 1 ELSE LENGTH(ChallanNumber) END)), ''), '0') AS INTEGER) {dir},
                               Sr {dir}, Id {dir}";
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
                case "other":
                case "otheramount": return $"OtherAmount {dir}, Sr, Id";
                case "lhs": return $"(LorryHire + OtherAmount) {dir}, Sr, Id";
                case "detention": return $"Detention {dir}, Sr, Id";
                case "hamali": return $"Hamali {dir}, Sr, Id";
                case "deduction": return $"Deduction {dir}, Sr, Id";
                case "balancepaidneft": return $"BalancePaidNEFT {dir}, Sr, Id";
                case "balancepaidcash": return $"BalancePaidCash {dir}, Sr, Id";
                case "balance": return useLhsDerived
                    ? $"((LorryHire + OtherAmount) - LessTDS - AdvanceAmount) {dir}, Sr, Id"
                    : $"(LorryHire - LessTDS - AdvanceAmount) {dir}, Sr, Id";
                case "due": return useLhsDerived
                    ? $"((LorryHire + OtherAmount) - LessTDS + Detention + Hamali + Deduction - AdvanceAmount - BalancePaidNEFT - BalancePaidCash) {dir}, Sr, Id"
                    : $"(LorryHire - LessTDS + Detention + Hamali + Deduction - AdvanceAmount - BalancePaidNEFT - BalancePaidCash) {dir}, Sr, Id";
                case "margin": return useLhsDerived
                    ? $"(CASE WHEN BillAmount = 0 THEN 0 ELSE BillAmount - ((LorryHire + OtherAmount) + Detention + Hamali) END) {dir}, Sr, Id"
                    : $"Margin {dir}, Sr, Id";
                default: return "Sr, Id";
            }
        }
    }
}
