using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    public class CommentRepository
    {
        public CommentRepository() { AppDatabase.EnsureInitialized(); AppDatabase.EnsureBillTablesExist(); }

        public List<ChallanComment> GetByChallanId(int challanId)
        {
            if (BackendSettings.UseRemoteApi)
            {
                return RemoteApiClient.GetList<ChallanComment>($"api/comments/challan/{challanId}");
            }
            var list = new List<ChallanComment>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT * FROM ChallanComments WHERE ChallanId = @id ORDER BY CreatedAt DESC;", c))
            {
                cmd.Parameters.AddWithValue("@id", challanId);
                c.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new ChallanComment
                        {
                            Id = Convert.ToInt32(r["Id"]),
                            ChallanId = Convert.ToInt32(r["ChallanId"]),
                            Comment = r["Comment"] as string,
                            CreatedAt = DateTime.TryParse(r["CreatedAt"] as string, out var dt) ? dt : DateTime.Now
                        });
            }
            return list;
        }

        public void Add(ChallanComment comment)
        {
            if (BackendSettings.UseRemoteApi)
            {
                RemoteApiClient.PostAndReadInt("api/comments/challan", comment);
                return;
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("INSERT INTO ChallanComments (ChallanId, Comment, CreatedAt) VALUES (@cid, @cmt, @dt);", c))
            {
                cmd.Parameters.AddWithValue("@cid", comment.ChallanId);
                cmd.Parameters.AddWithValue("@cmt", comment.Comment ?? "");
                cmd.Parameters.AddWithValue("@dt", DateTime.Now.ToString("o"));
                c.Open();
                cmd.ExecuteNonQuery();
            }
        }

        public void Delete(int commentId)
        {
            if (BackendSettings.UseRemoteApi)
            {
                RemoteApiClient.Delete($"api/comments/challan/{commentId}");
                return;
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("DELETE FROM ChallanComments WHERE Id = @id;", c))
            {
                cmd.Parameters.AddWithValue("@id", commentId);
                c.Open();
                cmd.ExecuteNonQuery();
            }
        }

        // LR Comments
        public List<LRComment> GetLRByEntryId(int lrEntryId)
        {
            if (BackendSettings.UseRemoteApi)
            {
                return RemoteApiClient.GetList<LRComment>($"api/comments/lr/{lrEntryId}");
            }
            var list = new List<LRComment>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT * FROM LRComments WHERE LREntryId = @id ORDER BY CreatedAt DESC;", c))
            {
                cmd.Parameters.AddWithValue("@id", lrEntryId);
                c.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new LRComment
                        {
                            Id = Convert.ToInt32(r["Id"]),
                            LREntryId = Convert.ToInt32(r["LREntryId"]),
                            Comment = r["Comment"] as string,
                            CreatedAt = DateTime.TryParse(r["CreatedAt"] as string, out var dt) ? dt : DateTime.Now
                        });
            }
            return list;
        }

        public void AddLR(LRComment comment)
        {
            if (BackendSettings.UseRemoteApi)
            {
                RemoteApiClient.PostAndReadInt("api/comments/lr", comment);
                return;
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("INSERT INTO LRComments (LREntryId, Comment, CreatedAt) VALUES (@eid, @cmt, @dt);", c))
            {
                cmd.Parameters.AddWithValue("@eid", comment.LREntryId);
                cmd.Parameters.AddWithValue("@cmt", comment.Comment ?? "");
                cmd.Parameters.AddWithValue("@dt", DateTime.Now.ToString("o"));
                c.Open();
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteLR(int commentId)
        {
            if (BackendSettings.UseRemoteApi)
            {
                RemoteApiClient.Delete($"api/comments/lr/{commentId}");
                return;
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("DELETE FROM LRComments WHERE Id = @id;", c))
            {
                cmd.Parameters.AddWithValue("@id", commentId);
                c.Open();
                cmd.ExecuteNonQuery();
            }
        }

        public HashSet<int> GetLREntryIdsWithComments()
        {
            if (BackendSettings.UseRemoteApi)
            {
                return new HashSet<int>(RemoteApiClient.GetList<LRComment>("api/comments/lr/all").ConvertAll(x => x.LREntryId));
            }
            var ids = new HashSet<int>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT DISTINCT LREntryId FROM LRComments;", c))
            {
                c.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) ids.Add(Convert.ToInt32(r["LREntryId"]));
            }
            return ids;
        }

        // Bill Comments
        public List<BillComment> GetBillByBillId(int billId)
        {
            if (BackendSettings.UseRemoteApi)
            {
                return RemoteApiClient.GetList<BillComment>($"api/comments/bill/{billId}");
            }
            var list = new List<BillComment>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT * FROM BillComments WHERE BillId = @id ORDER BY CreatedAt DESC;", c))
            {
                cmd.Parameters.AddWithValue("@id", billId);
                c.Open();
                using (var r = cmd.ExecuteReader())
                    while (r.Read())
                        list.Add(new BillComment
                        {
                            Id = Convert.ToInt32(r["Id"]),
                            BillId = Convert.ToInt32(r["BillId"]),
                            Comment = r["Comment"] as string,
                            CreatedAt = DateTime.TryParse(r["CreatedAt"] as string, out var dt) ? dt : DateTime.Now
                        });
            }
            return list;
        }

        public void AddBill(BillComment comment)
        {
            if (BackendSettings.UseRemoteApi)
            {
                RemoteApiClient.PostAndReadInt("api/comments/bill", comment);
                return;
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("INSERT INTO BillComments (BillId, Comment, CreatedAt) VALUES (@eid, @cmt, @dt);", c))
            {
                cmd.Parameters.AddWithValue("@eid", comment.BillId);
                cmd.Parameters.AddWithValue("@cmt", comment.Comment ?? "");
                cmd.Parameters.AddWithValue("@dt", DateTime.Now.ToString("o"));
                c.Open();
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteBill(int commentId)
        {
            if (BackendSettings.UseRemoteApi)
            {
                RemoteApiClient.Delete($"api/comments/bill/{commentId}");
                return;
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("DELETE FROM BillComments WHERE Id = @id;", c))
            {
                cmd.Parameters.AddWithValue("@id", commentId);
                c.Open();
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteAllBillComments()
        {
            if (BackendSettings.UseRemoteApi)
            {
                return;
            }
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("DELETE FROM BillComments;", c))
            {
                c.Open();
                cmd.ExecuteNonQuery();
            }
        }

        public HashSet<int> GetBillIdsWithComments()
        {
            if (BackendSettings.UseRemoteApi)
            {
                return new HashSet<int>(RemoteApiClient.GetList<BillComment>("api/comments/bill/all").ConvertAll(x => x.BillId));
            }
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

        public HashSet<int> GetChallanIdsWithComments()
        {
            if (BackendSettings.UseRemoteApi)
            {
                return new HashSet<int>(RemoteApiClient.GetList<ChallanComment>("api/comments/challan/all").ConvertAll(x => x.ChallanId));
            }
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

        public Dictionary<int, string> GetLatestChallanCommentsByIds(IEnumerable<int> challanIds)
        {
            var ids = new HashSet<int>((challanIds ?? Enumerable.Empty<int>()).Where(x => x > 0));
            if (ids.Count == 0) return new Dictionary<int, string>();

            if (BackendSettings.UseRemoteApi)
            {
                return RemoteApiClient.GetList<ChallanComment>("api/comments/challan/all")
                    .Where(x => x != null && ids.Contains(x.ChallanId))
                    .GroupBy(x => x.ChallanId)
                    .ToDictionary(g => g.Key, g => (g.OrderByDescending(x => x.CreatedAt).FirstOrDefault()?.Comment ?? string.Empty).Trim());
            }

            var result = new Dictionary<int, string>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT ChallanId, Comment, CreatedAt FROM ChallanComments ORDER BY CreatedAt DESC;", c))
            {
                c.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var id = Convert.ToInt32(r["ChallanId"]);
                        if (!ids.Contains(id) || result.ContainsKey(id)) continue;
                        result[id] = (r["Comment"] as string ?? string.Empty).Trim();
                    }
                }
            }
            return result;
        }

        public Dictionary<int, string> GetLatestLRCommentsByIds(IEnumerable<int> lrEntryIds)
        {
            var ids = new HashSet<int>((lrEntryIds ?? Enumerable.Empty<int>()).Where(x => x > 0));
            if (ids.Count == 0) return new Dictionary<int, string>();

            if (BackendSettings.UseRemoteApi)
            {
                return RemoteApiClient.GetList<LRComment>("api/comments/lr/all")
                    .Where(x => x != null && ids.Contains(x.LREntryId))
                    .GroupBy(x => x.LREntryId)
                    .ToDictionary(g => g.Key, g => (g.OrderByDescending(x => x.CreatedAt).FirstOrDefault()?.Comment ?? string.Empty).Trim());
            }

            var result = new Dictionary<int, string>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT LREntryId, Comment, CreatedAt FROM LRComments ORDER BY CreatedAt DESC;", c))
            {
                c.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var id = Convert.ToInt32(r["LREntryId"]);
                        if (!ids.Contains(id) || result.ContainsKey(id)) continue;
                        result[id] = (r["Comment"] as string ?? string.Empty).Trim();
                    }
                }
            }
            return result;
        }

        public Dictionary<int, string> GetLatestBillCommentsByIds(IEnumerable<int> billIds)
        {
            var ids = new HashSet<int>((billIds ?? Enumerable.Empty<int>()).Where(x => x > 0));
            if (ids.Count == 0) return new Dictionary<int, string>();

            if (BackendSettings.UseRemoteApi)
            {
                return RemoteApiClient.GetList<BillComment>("api/comments/bill/all")
                    .Where(x => x != null && ids.Contains(x.BillId))
                    .GroupBy(x => x.BillId)
                    .ToDictionary(g => g.Key, g => (g.OrderByDescending(x => x.CreatedAt).FirstOrDefault()?.Comment ?? string.Empty).Trim());
            }

            var result = new Dictionary<int, string>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT BillId, Comment, CreatedAt FROM BillComments ORDER BY CreatedAt DESC;", c))
            {
                c.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var id = Convert.ToInt32(r["BillId"]);
                        if (!ids.Contains(id) || result.ContainsKey(id)) continue;
                        result[id] = (r["Comment"] as string ?? string.Empty).Trim();
                    }
                }
            }
            return result;
        }
    }
}
