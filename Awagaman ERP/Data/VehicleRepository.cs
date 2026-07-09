using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    public class VehicleRepository
    {
        public VehicleRepository() { AppDatabase.EnsureInitialized(); }

        public List<VehicleEntry> GetAll()
        {
            if (BackendSettings.UseRemoteApi)
            {
                return MasterDataCache.GetVehicles(() => RemoteApiClient.GetList<VehicleEntry>("api/vehicles"));
            }
            var list = new List<VehicleEntry>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT * FROM VehicleLedger ORDER BY VehicleNumber;", c))
            {
                c.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) list.Add(Map(r));
                }
            }
            return list;
        }

        public List<VehicleEntry> SearchByVehicleNumber(string query)
        {
            if (BackendSettings.UseRemoteApi)
            {
                query = (query ?? string.Empty).Trim();
                return GetAll().FindAll(v =>
                    !string.IsNullOrWhiteSpace(v.VehicleNumber) &&
                    v.VehicleNumber.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            var list = new List<VehicleEntry>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = @"SELECT * FROM VehicleLedger
                                    WHERE VehicleNumber LIKE @q
                                    ORDER BY VehicleNumber
                                    LIMIT 20;";
                cmd.Parameters.AddWithValue("@q", "%" + (query ?? string.Empty).Trim().ToUpperInvariant() + "%");
                c.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) list.Add(Map(r));
                }
            }
            return list;
        }

        public VehicleEntry FindByVehicleNumber(string vehicleNumber)
        {
            if (string.IsNullOrWhiteSpace(vehicleNumber)) return null;
            if (BackendSettings.UseRemoteApi)
            {
                vehicleNumber = vehicleNumber.Trim();
                return GetAll().Find(v =>
                    string.Equals((v.VehicleNumber ?? string.Empty).Trim(), vehicleNumber, StringComparison.OrdinalIgnoreCase));
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM VehicleLedger WHERE UPPER(VehicleNumber) = @v LIMIT 1;";
                cmd.Parameters.AddWithValue("@v", vehicleNumber.Trim().ToUpperInvariant());
                c.Open();
                using (var r = cmd.ExecuteReader())
                {
                    if (r.Read()) return Map(r);
                }
            }
            return null;
        }

        public void UpsertFromChallan(ChallanEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.VehicleNumber)) return;
            if (BackendSettings.UseRemoteApi)
            {
                var existing = FindByVehicleNumber(entry.VehicleNumber);
                var payload = new VehicleEntry
                {
                    Id = existing != null ? existing.Id : 0,
                    Sr = existing != null ? existing.Sr : GetAll().Count + 1,
                    VehicleNumber = entry.VehicleNumber.Trim().ToUpperInvariant(),
                    OwnerName = entry.OwnerName,
                    PANNumber = entry.PAN,
                    EngineNumber = entry.EngineNo,
                    ChassisNumber = entry.ChassisNo,
                    VehicleType = entry.VehicleType,
                    DriverName = entry.DriverName,
                    DriverMobile = entry.DriverMobile,
                    LicenceNumber = entry.LicenceNo,
                    PolicyNumber = entry.PolicyNo
                };
                Upsert(payload);
                return;
            }
            var vno = entry.VehicleNumber.Trim().ToUpperInvariant();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            {
                c.Open();
                using (var cmd = c.CreateCommand())
                {
                    cmd.CommandText = @"SELECT Id FROM VehicleLedger WHERE UPPER(VehicleNumber) = @v LIMIT 1;";
                    cmd.Parameters.AddWithValue("@v", vno);
                    var idObj = cmd.ExecuteScalar();

                    if (idObj == null || idObj == DBNull.Value)
                    {
                        int nextSr = 1;
                        using (var srCmd = c.CreateCommand())
                        {
                            srCmd.CommandText = "SELECT COALESCE(MAX(Sr),0) + 1 FROM VehicleLedger;";
                            nextSr = Convert.ToInt32(srCmd.ExecuteScalar());
                        }
                        using (var ins = c.CreateCommand())
                        {
                            ins.CommandText = @"INSERT INTO VehicleLedger
                                (Sr, VehicleNumber, OwnerName, PANNumber, EngineNumber, ChassisNumber, VehicleType, DriverName, DriverMobile, LicenceNumber, PolicyNumber)
                                VALUES (@sr,@v,@o,@p,@e,@c,@t,@d,@m,@l,@pn);";
                            ins.Parameters.AddWithValue("@sr", nextSr);
                            ins.Parameters.AddWithValue("@v", vno);
                            ins.Parameters.AddWithValue("@o", (object)entry.OwnerName ?? DBNull.Value);
                            ins.Parameters.AddWithValue("@p", (object)entry.PAN ?? DBNull.Value);
                            ins.Parameters.AddWithValue("@e", (object)entry.EngineNo ?? DBNull.Value);
                            ins.Parameters.AddWithValue("@c", (object)entry.ChassisNo ?? DBNull.Value);
                            ins.Parameters.AddWithValue("@t", (object)entry.VehicleType ?? DBNull.Value);
                            ins.Parameters.AddWithValue("@d", (object)entry.DriverName ?? DBNull.Value);
                            ins.Parameters.AddWithValue("@m", (object)entry.DriverMobile ?? DBNull.Value);
                            ins.Parameters.AddWithValue("@l", (object)entry.LicenceNo ?? DBNull.Value);
                            ins.Parameters.AddWithValue("@pn", (object)entry.PolicyNo ?? DBNull.Value);
                            ins.ExecuteNonQuery();
                        }
                    }
                    else
                    {
                        using (var upd = c.CreateCommand())
                        {
                            upd.CommandText = @"UPDATE VehicleLedger SET
                                OwnerName = CASE WHEN @o IS NOT NULL AND TRIM(@o) <> '' THEN @o ELSE OwnerName END,
                                PANNumber = CASE WHEN @p IS NOT NULL AND TRIM(@p) <> '' THEN @p ELSE PANNumber END,
                                EngineNumber = CASE WHEN @e IS NOT NULL AND TRIM(@e) <> '' THEN @e ELSE EngineNumber END,
                                ChassisNumber = CASE WHEN @c IS NOT NULL AND TRIM(@c) <> '' THEN @c ELSE ChassisNumber END,
                                VehicleType = CASE WHEN @t IS NOT NULL AND TRIM(@t) <> '' THEN @t ELSE VehicleType END,
                                DriverName = CASE WHEN @d IS NOT NULL AND TRIM(@d) <> '' THEN @d ELSE DriverName END,
                                DriverMobile = CASE WHEN @m IS NOT NULL AND TRIM(@m) <> '' THEN @m ELSE DriverMobile END,
                                LicenceNumber = CASE WHEN @l IS NOT NULL AND TRIM(@l) <> '' THEN @l ELSE LicenceNumber END,
                                PolicyNumber = CASE WHEN @pn IS NOT NULL AND TRIM(@pn) <> '' THEN @pn ELSE PolicyNumber END
                                WHERE Id = @id;";
                            upd.Parameters.AddWithValue("@o", (object)entry.OwnerName ?? DBNull.Value);
                            upd.Parameters.AddWithValue("@p", (object)entry.PAN ?? DBNull.Value);
                            upd.Parameters.AddWithValue("@e", (object)entry.EngineNo ?? DBNull.Value);
                            upd.Parameters.AddWithValue("@c", (object)entry.ChassisNo ?? DBNull.Value);
                            upd.Parameters.AddWithValue("@t", (object)entry.VehicleType ?? DBNull.Value);
                            upd.Parameters.AddWithValue("@d", (object)entry.DriverName ?? DBNull.Value);
                            upd.Parameters.AddWithValue("@m", (object)entry.DriverMobile ?? DBNull.Value);
                            upd.Parameters.AddWithValue("@l", (object)entry.LicenceNo ?? DBNull.Value);
                            upd.Parameters.AddWithValue("@pn", (object)entry.PolicyNo ?? DBNull.Value);
                            upd.Parameters.AddWithValue("@id", Convert.ToInt32(idObj));
                            upd.ExecuteNonQuery();
                        }
                    }
                }
            }
            MasterDataCache.InvalidateVehicles();
        }

        public void Upsert(VehicleEntry e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.VehicleNumber)) return;
            if (BackendSettings.UseRemoteApi)
            {
                e.VehicleNumber = e.VehicleNumber.Trim().ToUpperInvariant();
                if (e.Id <= 0)
                {
                    e.Id = RemoteApiClient.PostAndReadInt("api/vehicles", e);
                }
                else
                {
                    RemoteApiClient.Put($"api/vehicles/{e.Id}", e);
                }
                MasterDataCache.InvalidateVehicles();
                return;
            }
            e.VehicleNumber = e.VehicleNumber.Trim().ToUpperInvariant();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = c.CreateCommand())
            {
                c.Open();
                if (e.Id <= 0)
                {
                    cmd.CommandText = @"INSERT INTO VehicleLedger
                        (Sr, VehicleNumber, OwnerName, PANNumber, EngineNumber, ChassisNumber, VehicleType, DriverName, DriverMobile, LicenceNumber, PolicyNumber)
                        VALUES (@sr,@v,@o,@p,@e,@c,@t,@d,@m,@l,@pn);";
                }
                else
                {
                    cmd.CommandText = @"UPDATE VehicleLedger SET
                        Sr=@sr, VehicleNumber=@v, OwnerName=@o, PANNumber=@p, EngineNumber=@e, ChassisNumber=@c, VehicleType=@t, DriverName=@d, DriverMobile=@m, LicenceNumber=@l, PolicyNumber=@pn
                        WHERE Id=@id;";
                    cmd.Parameters.AddWithValue("@id", e.Id);
                }
                cmd.Parameters.AddWithValue("@sr", e.Sr);
                cmd.Parameters.AddWithValue("@v", e.VehicleNumber);
                cmd.Parameters.AddWithValue("@o", (object)e.OwnerName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@p", (object)e.PANNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@e", (object)e.EngineNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@c", (object)e.ChassisNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@t", (object)e.VehicleType ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@d", (object)e.DriverName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@m", (object)e.DriverMobile ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@l", (object)e.LicenceNumber ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@pn", (object)e.PolicyNumber ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            MasterDataCache.InvalidateVehicles();
        }

        public void Delete(VehicleEntry entry)
        {
            if (entry == null || entry.Id <= 0) return;
            if (BackendSettings.UseRemoteApi)
            {
                RemoteApiClient.Delete($"api/vehicles/{entry.Id}");
                MasterDataCache.InvalidateVehicles();
                return;
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("DELETE FROM VehicleLedger WHERE Id = @id;", c))
            {
                cmd.Parameters.AddWithValue("@id", entry.Id);
                c.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private static VehicleEntry Map(SQLiteDataReader r)
        {
            return new VehicleEntry
            {
                Id = Convert.ToInt32(r["Id"]),
                Sr = Convert.ToInt32(r["Sr"]),
                VehicleNumber = r["VehicleNumber"] as string,
                OwnerName = r["OwnerName"] as string,
                PANNumber = r["PANNumber"] as string,
                EngineNumber = r["EngineNumber"] as string,
                ChassisNumber = r["ChassisNumber"] as string,
                VehicleType = r["VehicleType"] as string,
                DriverName = r["DriverName"] as string,
                DriverMobile = r["DriverMobile"] as string,
                LicenceNumber = r["LicenceNumber"] as string,
                PolicyNumber = r["PolicyNumber"] as string
            };
        }
    }
}
