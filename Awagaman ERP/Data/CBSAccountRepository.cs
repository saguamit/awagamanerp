using System;
using System.Collections.Generic;
using System.Data.SQLite;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    public class CBSAccountRepository
    {
        public CBSAccountRepository()
        {
            AppDatabase.EnsureInitialized();
            EnsureDefaults();
        }

        public List<CBSAccountEntry> GetAll()
        {
            EnsureDefaults();
            var list = new List<CBSAccountEntry>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT Id, Sr, AccountName, IsActive FROM CBSAccounts ORDER BY Sr, Id;", c))
            {
                c.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        list.Add(new CBSAccountEntry
                        {
                            Id = Convert.ToInt32(r["Id"]),
                            Sr = Convert.ToInt32(r["Sr"]),
                            AccountName = r["AccountName"] as string,
                            IsActive = Convert.ToInt32(r["IsActive"]) == 1
                        });
                    }
                }
            }
            return list;
        }

        public List<string> GetActiveAccountNames()
        {
            EnsureDefaults();
            var list = new List<string>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT AccountName FROM CBSAccounts WHERE IsActive = 1 ORDER BY AccountName;", c))
            {
                c.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var name = (r["AccountName"] as string ?? string.Empty).Trim();
                        if (name.Length > 0) list.Add(name);
                    }
                }
            }
            return list;
        }

        public int GetMaxSr()
        {
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT COALESCE(MAX(Sr), 0) FROM CBSAccounts;", c))
            {
                c.Open();
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public CBSAccountEntry FindByName(string accountName)
        {
            var key = (accountName ?? string.Empty).Trim();
            if (key.Length == 0) return null;
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT Id, Sr, AccountName, IsActive FROM CBSAccounts WHERE LOWER(TRIM(AccountName)) = LOWER(TRIM(@name)) LIMIT 1;", c))
            {
                cmd.Parameters.AddWithValue("@name", key);
                c.Open();
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return null;
                    return new CBSAccountEntry
                    {
                        Id = Convert.ToInt32(r["Id"]),
                        Sr = Convert.ToInt32(r["Sr"]),
                        AccountName = r["AccountName"] as string,
                        IsActive = Convert.ToInt32(r["IsActive"]) == 1
                    };
                }
            }
        }

        public void Upsert(CBSAccountEntry entry)
        {
            if (entry == null) return;
            entry.AccountName = (entry.AccountName ?? string.Empty).Trim();
            if (entry.AccountName.Length == 0) return;

            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = c.CreateCommand())
            {
                c.Open();
                if (entry.Id <= 0)
                {
                    cmd.CommandText = @"INSERT INTO CBSAccounts (Sr, AccountName, IsActive)
                                        VALUES (@Sr, @AccountName, @IsActive);
                                        SELECT last_insert_rowid();";
                }
                else
                {
                    cmd.CommandText = @"UPDATE CBSAccounts
                                        SET Sr = @Sr, AccountName = @AccountName, IsActive = @IsActive
                                        WHERE Id = @Id;";
                    cmd.Parameters.AddWithValue("@Id", entry.Id);
                }

                cmd.Parameters.AddWithValue("@Sr", entry.Sr <= 0 ? GetMaxSr() + 1 : entry.Sr);
                cmd.Parameters.AddWithValue("@AccountName", entry.AccountName);
                cmd.Parameters.AddWithValue("@IsActive", entry.IsActive ? 1 : 0);

                if (entry.Id <= 0)
                    entry.Id = Convert.ToInt32((long)cmd.ExecuteScalar());
                else
                    cmd.ExecuteNonQuery();
            }
        }

        public void Delete(CBSAccountEntry entry)
        {
            if (entry == null || entry.Id <= 0) return;
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("DELETE FROM CBSAccounts WHERE Id = @id;", c))
            {
                cmd.Parameters.AddWithValue("@id", entry.Id);
                c.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private void EnsureDefaults()
        {
            try
            {
                if (FindByName("Cash A/c") == null)
                {
                    Upsert(new CBSAccountEntry { Sr = GetMaxSr() + 1, AccountName = "Cash A/c", IsActive = true });
                }
                if (FindByName("Bank A/c") == null)
                {
                    Upsert(new CBSAccountEntry { Sr = GetMaxSr() + 1, AccountName = "Bank A/c", IsActive = true });
                }
            }
            catch { }
        }
    }
}
