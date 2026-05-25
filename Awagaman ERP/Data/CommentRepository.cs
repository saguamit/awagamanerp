using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    public class CommentRepository
    {
        public CommentRepository() { AppDatabase.EnsureInitialized(); AppDatabase.EnsureBillTablesExist(); }

        public List<ChallanComment> GetByChallanId(int challanId)
        {
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
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("DELETE FROM BillComments WHERE Id = @id;", c))
            {
                cmd.Parameters.AddWithValue("@id", commentId);
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
    }
}
