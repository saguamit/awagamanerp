using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    public class PartyRepository
    {
        public PartyRepository() { AppDatabase.EnsureInitialized(); }

        public List<PartyEntry> GetAll()
        {
            if (BackendSettings.UseRemoteApi)
            {
                return MasterDataCache.GetParties(() => RemoteApiClient.GetList<PartyEntry>("api/parties"));
            }
            var list = new List<PartyEntry>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT * FROM Parties ORDER BY Sr, Id;", c))
            {
                c.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new PartyEntry
                        {
                            Id = Convert.ToInt32(r["Id"]),
                            Sr = Convert.ToInt32(r["Sr"]),
                            PartyName = r["PartyName"] as string,
                            Address = r["Address"] as string,
                            GSTNo = r["GSTNo"] as string
                        });
            }
            return list;
        }

        public PartyEntry FindByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (BackendSettings.UseRemoteApi)
            {
                return GetAll()
                    .Find(p => string.Equals((p.PartyName ?? string.Empty).Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT * FROM Parties WHERE LOWER(PartyName) = LOWER(@name)", c))
            {
                cmd.Parameters.AddWithValue("@name", name.Trim());
                c.Open();
                using (var r = cmd.ExecuteReader())
                    if (r.Read())
                        return new PartyEntry
                        {
                            Id = Convert.ToInt32(r["Id"]),
                            Sr = Convert.ToInt32(r["Sr"]),
                            PartyName = r["PartyName"] as string,
                            Address = r["Address"] as string,
                            GSTNo = r["GSTNo"] as string
                        };
            }
            return null;
        }

        public List<string> SearchNames(string filter)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(filter)) return list;
            if (BackendSettings.UseRemoteApi)
            {
                return GetAll()
                    .FindAll(p => !string.IsNullOrWhiteSpace(p.PartyName) &&
                                  p.PartyName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ConvertAll(p => p.PartyName);
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT PartyName FROM Parties WHERE PartyName LIKE @f ORDER BY PartyName LIMIT 20", c))
            {
                cmd.Parameters.AddWithValue("@f", $"%{filter}%");
                c.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) list.Add(r["PartyName"] as string);
            }
            return list;
        }

        public void Upsert(PartyEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.PartyName)) return;
            if (BackendSettings.UseRemoteApi)
            {
                if (entry.Id <= 0)
                {
                    entry.Id = RemoteApiClient.PostAndReadInt("api/parties", entry);
                }
                else
                {
                    RemoteApiClient.Put($"api/parties/{entry.Id}", entry);
                }
                MasterDataCache.InvalidateParties();
                return;
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = c.CreateCommand())
            {
                c.Open();
                if (entry.Id <= 0)
                {
                    cmd.CommandText = "INSERT INTO Parties (Sr, PartyName, Address, GSTNo) VALUES (@sr, @name, @addr, @gst); SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("@sr", entry.Sr);
                    cmd.Parameters.AddWithValue("@name", entry.PartyName.Trim());
                    cmd.Parameters.AddWithValue("@addr", (object)entry.Address ?? "");
                    cmd.Parameters.AddWithValue("@gst", (object)entry.GSTNo ?? "");
                    entry.Id = Convert.ToInt32((long)cmd.ExecuteScalar());
                }
                else
                {
                    cmd.CommandText = "UPDATE Parties SET PartyName=@name, Address=@addr, GSTNo=@gst WHERE Id=@id;";
                    cmd.Parameters.AddWithValue("@id", entry.Id);
                    cmd.Parameters.AddWithValue("@name", entry.PartyName.Trim());
                    cmd.Parameters.AddWithValue("@addr", (object)entry.Address ?? "");
                    cmd.Parameters.AddWithValue("@gst", (object)entry.GSTNo ?? "");
                    cmd.ExecuteNonQuery();
                }
            }
        }

        public void Delete(PartyEntry entry)
        {
            if (entry == null || entry.Id <= 0) return;
            if (BackendSettings.UseRemoteApi)
            {
                RemoteApiClient.Delete($"api/parties/{entry.Id}");
                MasterDataCache.InvalidateParties();
                return;
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("DELETE FROM Parties WHERE Id = @id;", c))
            {
                cmd.Parameters.AddWithValue("@id", entry.Id);
                c.Open();
                cmd.ExecuteNonQuery();
            }
        }
    }
}
