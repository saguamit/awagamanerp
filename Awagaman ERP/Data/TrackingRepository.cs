using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    public sealed class TrackingRepository : ITrackingRepository
    {
        public TrackingRepository()
        {
            AppDatabase.EnsureInitialized();
        }

        public List<TrackingEntry> GetAll()
        {
            if (BackendSettings.UseRemoteApi)
            {
                return RemoteApiClient.GetList<TrackingEntry>("api/tracking");
            }
            var entries = new List<TrackingEntry>();

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand("SELECT * FROM TrackingEntries ORDER BY Sr, Id;", connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        entries.Add(new TrackingEntry
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Sr = Convert.ToInt32(reader["Sr"]),
                            ChallanNo = reader["ChallanNo"] as string,
                            ChallanDate = ParseDate(reader["ChallanDate"], DateTime.Today),
                            From = reader["FromLocation"] as string,
                            To = reader["ToLocation"] as string,
                            VehicleNo = reader["VehicleNo"] as string,
                            DriverMobile = reader["DriverMobile"] as string,
                            EwayBillTillDate = ParseNullableDate(reader["EwayBillTillDate"]),
                            DispatchDate = ParseNullableDate(reader["DispatchDate"]),
                            DispatchTime = reader["DispatchTime"] as string,
                            DeliveredDate = ParseNullableDate(reader["DeliveredDate"]),
                            DeliveredTime = reader["DeliveredTime"] as string
                        });
                    }
                }
            }

            return entries;
        }

        public Dictionary<int, string> GetLatestReportForAll()
        {
            if (BackendSettings.UseRemoteApi)
            {
                try
                {
                    var result = new Dictionary<int, string>();
                    foreach (var item in RemoteApiClient.GetList<TrackingLatestReportItem>("api/tracking/latest-reports"))
                    {
                        if (item == null || item.TrackingEntryId <= 0 || !item.ReportDateTime.HasValue)
                        {
                            continue;
                        }

                        result[item.TrackingEntryId] = $"{item.ReportDateTime.Value:dd-MMM HH:mm} - {item.Remarks}";
                    }

                    return result;
                }
                catch
                {
                    var result = new Dictionary<int, string>();
                    foreach (var entry in RemoteApiClient.GetList<TrackingEntry>("api/tracking"))
                    {
                        var reports = RemoteApiClient.GetList<ReportingTrackEntry>($"api/tracking/{entry.Id}/reports");
                        var latest = reports.OrderByDescending(x => x.ReportDateTime).FirstOrDefault();
                        if (latest != null)
                        {
                            result[entry.Id] = $"{latest.ReportDateTime:dd-MMM HH:mm} - {latest.Remarks}";
                        }
                    }

                    return result;
                }
            }
            var localResult = new Dictionary<int, string>();

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand(@"
SELECT TrackingEntryId, ReportDateTime, Remarks FROM ReportingTracks
WHERE Id IN (SELECT MAX(Id) FROM ReportingTracks GROUP BY TrackingEntryId)
ORDER BY TrackingEntryId;", connection))
            {
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var id = Convert.ToInt32(reader["TrackingEntryId"]);
                        var dt = ParseDate(reader["ReportDateTime"], DateTime.Now);
                        var remarks = reader["Remarks"] as string ?? "";
                        localResult[id] = $"{dt:dd-MMM HH:mm} - {remarks}";
                    }
                }
            }

            return localResult;
        }

        public List<ReportingTrackEntry> GetReportingTracks(int trackingEntryId)
        {
            if (BackendSettings.UseRemoteApi)
            {
                return RemoteApiClient.GetList<ReportingTrackEntry>($"api/tracking/{trackingEntryId}/reports");
            }
            var entries = new List<ReportingTrackEntry>();

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = new SQLiteCommand("SELECT * FROM ReportingTracks WHERE TrackingEntryId = @Id ORDER BY ReportDateTime;", connection))
            {
                command.Parameters.AddWithValue("@Id", trackingEntryId);
                connection.Open();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        entries.Add(new ReportingTrackEntry
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            TrackingEntryId = Convert.ToInt32(reader["TrackingEntryId"]),
                            ReportDateTime = ParseDate(reader["ReportDateTime"], DateTime.Now),
                            Remarks = reader["Remarks"] as string
                        });
                    }
                }
            }

            return entries;
        }

        public void Upsert(TrackingEntry entry)
        {
            if (entry == null) return;

            if (BackendSettings.UseRemoteApi)
            {
                if (entry.Id <= 0)
                {
                    entry.Id = RemoteApiClient.PostAndReadInt("api/tracking", BuildRemotePayload(entry));
                }
                else
                {
                    RemoteApiClient.Put($"api/tracking/{entry.Id}", BuildRemotePayload(entry));
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
INSERT INTO TrackingEntries (
    Sr, ChallanNo, ChallanDate, FromLocation, ToLocation, VehicleNo, DriverMobile,
    EwayBillTillDate, DispatchDate, DispatchTime, DeliveredDate, DeliveredTime
) VALUES (
    @Sr, @ChallanNo, @ChallanDate, @FromLocation, @ToLocation, @VehicleNo, @DriverMobile,
    @EwayBillTillDate, @DispatchDate, @DispatchTime, @DeliveredDate, @DeliveredTime
);
SELECT last_insert_rowid();";
                    AddParameters(command, entry);
                    entry.Id = Convert.ToInt32((long)command.ExecuteScalar());
                }
                else
                {
                    command.CommandText = @"
UPDATE TrackingEntries SET
    Sr = @Sr,
    ChallanNo = @ChallanNo,
    ChallanDate = @ChallanDate,
    FromLocation = @FromLocation,
    ToLocation = @ToLocation,
    VehicleNo = @VehicleNo,
    DriverMobile = @DriverMobile,
    EwayBillTillDate = @EwayBillTillDate,
    DispatchDate = @DispatchDate,
    DispatchTime = @DispatchTime,
    DeliveredDate = @DeliveredDate,
    DeliveredTime = @DeliveredTime
WHERE Id = @Id;";
                    AddParameters(command, entry);
                    command.Parameters.AddWithValue("@Id", entry.Id);
                    command.ExecuteNonQuery();
                }
            }
        }

        public void AddReportingTrack(ReportingTrackEntry entry)
        {
            if (entry == null) return;

            if (BackendSettings.UseRemoteApi)
            {
                entry.Id = RemoteApiClient.PostAndReadInt($"api/tracking/{entry.TrackingEntryId}/reports", entry);
                return;
            }

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = @"
INSERT INTO ReportingTracks (TrackingEntryId, ReportDateTime, Remarks)
VALUES (@TrackingEntryId, @ReportDateTime, @Remarks);
SELECT last_insert_rowid();";
                command.Parameters.AddWithValue("@TrackingEntryId", entry.TrackingEntryId);
                command.Parameters.AddWithValue("@ReportDateTime", entry.ReportDateTime.ToString("o"));
                command.Parameters.AddWithValue("@Remarks", (object)entry.Remarks ?? DBNull.Value);
                entry.Id = Convert.ToInt32((long)command.ExecuteScalar());
            }
        }

        public void Delete(TrackingEntry entry)
        {
            if (entry == null) return;

            if (BackendSettings.UseRemoteApi)
            {
                if (entry.Id > 0)
                {
                    RemoteApiClient.Delete($"api/tracking/{entry.Id}");
                }
                return;
            }

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                if (entry.Id > 0)
                {
                    command.CommandText = "DELETE FROM TrackingEntries WHERE Id = @Id;";
                    command.Parameters.AddWithValue("@Id", entry.Id);
                }
                else
                {
                    command.CommandText = "DELETE FROM TrackingEntries WHERE ChallanNo = @ChallanNo;";
                    command.Parameters.AddWithValue("@ChallanNo", entry.ChallanNo ?? string.Empty);
                }
                command.ExecuteNonQuery();
            }
        }

        public void UpsertFromChallan(ChallanEntry challan)
        {
            if (challan == null)
            {
                return;
            }

            var challanNo = (challan.ChallanNumber ?? string.Empty).Trim();
            if (challanNo.Length == 0)
            {
                return;
            }

            TrackingEntry existing = null;

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = @"
SELECT Id, Sr, ChallanNo, ChallanDate, FromLocation, ToLocation, VehicleNo, DriverMobile,
       EwayBillTillDate, DispatchDate, DispatchTime, DeliveredDate, DeliveredTime
FROM TrackingEntries
WHERE LOWER(TRIM(COALESCE(ChallanNo, ''))) = LOWER(TRIM(@ChallanNo))
ORDER BY Id
LIMIT 1;";
                command.Parameters.AddWithValue("@ChallanNo", challanNo);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        existing = new TrackingEntry
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            Sr = Convert.ToInt32(reader["Sr"]),
                            ChallanNo = reader["ChallanNo"] as string,
                            ChallanDate = ParseDate(reader["ChallanDate"], challan.Date),
                            From = reader["FromLocation"] as string,
                            To = reader["ToLocation"] as string,
                            VehicleNo = reader["VehicleNo"] as string,
                            DriverMobile = reader["DriverMobile"] as string,
                            EwayBillTillDate = ParseNullableDate(reader["EwayBillTillDate"]),
                            DispatchDate = ParseNullableDate(reader["DispatchDate"]),
                            DispatchTime = reader["DispatchTime"] as string,
                            DeliveredDate = ParseNullableDate(reader["DeliveredDate"]),
                            DeliveredTime = reader["DeliveredTime"] as string
                        };
                    }
                }
            }

            var trackingEntry = existing ?? new TrackingEntry();
            trackingEntry.Sr = challan.Sr > 0 ? challan.Sr : trackingEntry.Sr;
            trackingEntry.ChallanNo = challanNo;
            trackingEntry.ChallanDate = challan.Date;
            trackingEntry.From = challan.From;
            trackingEntry.To = challan.To;
            trackingEntry.VehicleNo = challan.VehicleNumber;
            trackingEntry.DriverMobile = challan.DriverMobile;

            Upsert(trackingEntry);
        }

        public void DeleteByChallanNo(string challanNo)
        {
            challanNo = (challanNo ?? string.Empty).Trim();
            if (challanNo.Length == 0)
            {
                return;
            }

            if (BackendSettings.UseRemoteApi)
            {
                RemoteApiClient.Delete("api/tracking/by-challan/" + RemoteApiClient.UrlEncode(challanNo));
                return;
            }

            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = "DELETE FROM TrackingEntries WHERE LOWER(TRIM(COALESCE(ChallanNo, ''))) = LOWER(TRIM(@ChallanNo));";
                command.Parameters.AddWithValue("@ChallanNo", challanNo);
                command.ExecuteNonQuery();
            }
        }

        private static void AddParameters(SQLiteCommand command, TrackingEntry entry)
        {
            command.Parameters.AddWithValue("@Sr", entry.Sr);
            command.Parameters.AddWithValue("@ChallanNo", (object)entry.ChallanNo ?? DBNull.Value);
            command.Parameters.AddWithValue("@ChallanDate", entry.ChallanDate.ToString("o"));
            command.Parameters.AddWithValue("@FromLocation", (object)entry.From ?? DBNull.Value);
            command.Parameters.AddWithValue("@ToLocation", (object)entry.To ?? DBNull.Value);
            command.Parameters.AddWithValue("@VehicleNo", (object)entry.VehicleNo ?? DBNull.Value);
            command.Parameters.AddWithValue("@DriverMobile", (object)entry.DriverMobile ?? DBNull.Value);
            command.Parameters.AddWithValue("@EwayBillTillDate", entry.EwayBillTillDate.HasValue ? (object)entry.EwayBillTillDate.Value.ToString("o") : DBNull.Value);
            command.Parameters.AddWithValue("@DispatchDate", entry.DispatchDate.HasValue ? (object)entry.DispatchDate.Value.ToString("o") : DBNull.Value);
            command.Parameters.AddWithValue("@DispatchTime", (object)entry.DispatchTime ?? DBNull.Value);
            command.Parameters.AddWithValue("@DeliveredDate", entry.DeliveredDate.HasValue ? (object)entry.DeliveredDate.Value.ToString("o") : DBNull.Value);
            command.Parameters.AddWithValue("@DeliveredTime", (object)entry.DeliveredTime ?? DBNull.Value);
        }

        private static Dictionary<string, object> BuildRemotePayload(TrackingEntry entry)
        {
            return new Dictionary<string, object>
            {
                ["Id"] = entry.Id,
                ["Sr"] = entry.Sr,
                ["ChallanNo"] = entry.ChallanNo,
                ["ChallanDate"] = entry.ChallanDate.ToString("yyyy-MM-dd"),
                ["From"] = entry.From,
                ["To"] = entry.To,
                ["VehicleNo"] = entry.VehicleNo,
                ["DriverMobile"] = entry.DriverMobile,
                ["EwayBillTillDate"] = entry.EwayBillTillDate.HasValue ? (object)entry.EwayBillTillDate.Value.ToString("yyyy-MM-dd") : null,
                ["DispatchDate"] = entry.DispatchDate.HasValue ? (object)entry.DispatchDate.Value.ToString("yyyy-MM-dd") : null,
                ["DispatchTime"] = entry.DispatchTime,
                ["DeliveredDate"] = entry.DeliveredDate.HasValue ? (object)entry.DeliveredDate.Value.ToString("yyyy-MM-dd") : null,
                ["DeliveredTime"] = entry.DeliveredTime
            };
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

        private sealed class TrackingLatestReportItem
        {
            public int TrackingEntryId { get; set; }
            public DateTime? ReportDateTime { get; set; }
            public string Remarks { get; set; }
        }
    }
}
