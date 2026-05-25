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
                                (Sr, VehicleNumber, OwnerName, PANNumber, EngineNumber, ChassisNumber, VehicleType)
                                VALUES (@sr,@v,@o,@p,@e,@c,@t);";
                            ins.Parameters.AddWithValue("@sr", nextSr);
                            ins.Parameters.AddWithValue("@v", vno);
                            ins.Parameters.AddWithValue("@o", (object)entry.OwnerName ?? DBNull.Value);
                            ins.Parameters.AddWithValue("@p", (object)entry.PAN ?? DBNull.Value);
                            ins.Parameters.AddWithValue("@e", (object)entry.EngineNo ?? DBNull.Value);
                            ins.Parameters.AddWithValue("@c", (object)entry.ChassisNo ?? DBNull.Value);
                            ins.Parameters.AddWithValue("@t", (object)entry.VehicleType ?? DBNull.Value);
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
                                VehicleType = CASE WHEN @t IS NOT NULL AND TRIM(@t) <> '' THEN @t ELSE VehicleType END
                                WHERE Id = @id;";
                            upd.Parameters.AddWithValue("@o", (object)entry.OwnerName ?? DBNull.Value);
                            upd.Parameters.AddWithValue("@p", (object)entry.PAN ?? DBNull.Value);
                            upd.Parameters.AddWithValue("@e", (object)entry.EngineNo ?? DBNull.Value);
                            upd.Parameters.AddWithValue("@c", (object)entry.ChassisNo ?? DBNull.Value);
                            upd.Parameters.AddWithValue("@t", (object)entry.VehicleType ?? DBNull.Value);
                            upd.Parameters.AddWithValue("@id", Convert.ToInt32(idObj));
                            upd.ExecuteNonQuery();
                        }
                    }
                }
            }
        }

        public void Upsert(VehicleEntry e)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.VehicleNumber)) return;
            e.VehicleNumber = e.VehicleNumber.Trim().ToUpperInvariant();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = c.CreateCommand())
            {
                c.Open();
                if (e.Id <= 0)
                {
                    cmd.CommandText = @"INSERT INTO VehicleLedger
                        (Sr, VehicleNumber, OwnerName, PANNumber, EngineNumber, ChassisNumber, VehicleType)
                        VALUES (@sr,@v,@o,@p,@e,@c,@t);";
                }
                else
                {
                    cmd.CommandText = @"UPDATE VehicleLedger SET
                        Sr=@sr, VehicleNumber=@v, OwnerName=@o, PANNumber=@p, EngineNumber=@e, ChassisNumber=@c, VehicleType=@t
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
                VehicleType = r["VehicleType"] as string
            };
        }
    }
}

