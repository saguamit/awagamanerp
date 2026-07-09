using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    public class CBSAccountRepository
    {
        public CBSAccountRepository()
        {
            AppDatabase.EnsureInitialized();
            if (!BackendSettings.UseRemoteApi)
            {
                EnsureDefaults();
            }
        }

        public List<CBSAccountEntry> GetAll()
        {
            if (BackendSettings.UseRemoteApi)
            {
                var accounts = MasterDataCache.GetCBSAccounts(() => RemoteApiClient.GetList<CBSAccountEntry>("api/cbs/accounts"));
                if (EnsureRemoteDefaults(accounts))
                {
                    MasterDataCache.InvalidateCBSAccounts();
                    accounts = MasterDataCache.GetCBSAccounts(() => RemoteApiClient.GetList<CBSAccountEntry>("api/cbs/accounts"));
                }
                return NormalizeAndDeduplicate(accounts);
            }
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
            return NormalizeAndDeduplicate(list);
        }

        public List<string> GetActiveAccountNames()
        {
            if (BackendSettings.UseRemoteApi)
            {
                return GetAll()
                    .FindAll(x => x.IsActive)
                    .ConvertAll(x => (x.AccountName ?? string.Empty).Trim())
                    .FindAll(x => x.Length > 0);
            }
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
            if (BackendSettings.UseRemoteApi)
            {
                return GetAll().Select(x => x.Sr).DefaultIfEmpty(0).Max();
            }
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
            if (BackendSettings.UseRemoteApi)
            {
                return GetAll()
                    .Find(x => string.Equals((x.AccountName ?? string.Empty).Trim(), key, StringComparison.OrdinalIgnoreCase));
            }
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
            if (string.Equals(entry.AccountName, "LHS", StringComparison.OrdinalIgnoreCase))
            {
                entry.AccountName = "Purchase LHS";
            }
            if (entry.AccountName.Length == 0) return;

            if (BackendSettings.UseRemoteApi)
            {
                if (entry.Id <= 0)
                {
                    entry.Id = RemoteApiClient.PostAndReadInt("api/cbs/accounts", entry);
                }
                else
                {
                    RemoteApiClient.Put($"api/cbs/accounts/{entry.Id}", entry);
                }
                MasterDataCache.InvalidateCBSAccounts();
                return;
            }

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
            if (BackendSettings.UseRemoteApi)
            {
                RemoteApiClient.Delete($"api/cbs/accounts/{entry.Id}");
                MasterDataCache.InvalidateCBSAccounts();
                return;
            }
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
                var legacyLhs = FindByName("LHS");
                if (legacyLhs != null)
                {
                    legacyLhs.AccountName = "Purchase LHS";
                    legacyLhs.IsActive = true;
                    Upsert(legacyLhs);
                }
                if (FindByName("Purchase LHS") == null)
                {
                    Upsert(new CBSAccountEntry { Sr = GetMaxSr() + 1, AccountName = "Purchase LHS", IsActive = true });
                }
                if (FindByName("Challan LHS") == null)
                {
                    Upsert(new CBSAccountEntry { Sr = GetMaxSr() + 1, AccountName = "Challan LHS", IsActive = true });
                }
                if (FindByName("BFRS") == null)
                {
                    Upsert(new CBSAccountEntry { Sr = GetMaxSr() + 1, AccountName = "BFRS", IsActive = true });
                }

                DeduplicateLocalSpecialAccounts();
            }
            catch { }
        }

        private bool EnsureRemoteDefaults(List<CBSAccountEntry> accounts = null)
        {
            try
            {
                accounts = accounts ?? MasterDataCache.GetCBSAccounts(() => RemoteApiClient.GetList<CBSAccountEntry>("api/cbs/accounts"));
                var maxSr = accounts.Select(x => x.Sr).DefaultIfEmpty(0).Max();
                var changed = false;

                changed |= EnsureRemoteDefaultAccount(accounts, "Cash A/c", ref maxSr);
                changed |= EnsureRemoteDefaultAccount(accounts, "Bank A/c", ref maxSr);
                changed |= EnsureRemoteDefaultAccount(accounts, "Purchase LHS", ref maxSr);
                changed |= EnsureRemoteDefaultAccount(accounts, "Challan LHS", ref maxSr);
                changed |= EnsureRemoteDefaultAccount(accounts, "BFRS", ref maxSr);

                return changed;
            }
            catch
            {
                return false;
            }
        }

        private static bool EnsureRemoteDefaultAccount(List<CBSAccountEntry> accounts, string accountName, ref int maxSr)
        {
            if (AccountExists(accounts, accountName)) return false;

            maxSr++;
            var entry = new CBSAccountEntry
            {
                Sr = maxSr,
                AccountName = accountName,
                IsActive = true
            };
            entry.Id = RemoteApiClient.PostAndReadInt("api/cbs/accounts", entry);
            accounts.Add(entry);
            return true;
        }

        private static bool AccountExists(IEnumerable<CBSAccountEntry> accounts, string accountName)
        {
            var key = (accountName ?? string.Empty).Trim();
            return accounts != null && accounts.Any(x => string.Equals((x.AccountName ?? string.Empty).Trim(), key, StringComparison.OrdinalIgnoreCase));
        }

        private List<CBSAccountEntry> NormalizeAndDeduplicate(List<CBSAccountEntry> accounts)
        {
            accounts = accounts ?? new List<CBSAccountEntry>();
            foreach (var account in accounts)
            {
                if (account != null && string.Equals((account.AccountName ?? string.Empty).Trim(), "LHS", StringComparison.OrdinalIgnoreCase))
                {
                    account.AccountName = "Purchase LHS";
                }
            }

            return accounts
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.AccountName))
                .GroupBy(x => (x.AccountName ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(x => x.IsActive).ThenBy(x => x.Sr).ThenBy(x => x.Id).First())
                .OrderBy(x => x.Sr)
                .ThenBy(x => x.Id)
                .ToList();
        }

        private void DeduplicateLocalSpecialAccounts()
        {
            var accounts = new List<CBSAccountEntry>();
            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var cmd = new SQLiteCommand("SELECT Id, Sr, AccountName, IsActive FROM CBSAccounts ORDER BY Sr, Id;", c))
            {
                c.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        accounts.Add(new CBSAccountEntry
                        {
                            Id = Convert.ToInt32(r["Id"]),
                            Sr = Convert.ToInt32(r["Sr"]),
                            AccountName = r["AccountName"] as string,
                            IsActive = Convert.ToInt32(r["IsActive"]) == 1
                        });
                    }
                }
            }

            var duplicateIds = accounts
                .Where(x => x != null && (
                    string.Equals((x.AccountName ?? string.Empty).Trim(), "Purchase LHS", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals((x.AccountName ?? string.Empty).Trim(), "Challan LHS", StringComparison.OrdinalIgnoreCase)))
                .GroupBy(x => (x.AccountName ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .SelectMany(g => g.OrderByDescending(x => x.IsActive).ThenBy(x => x.Sr).ThenBy(x => x.Id).Skip(1))
                .Select(x => x.Id)
                .ToList();

            if (duplicateIds.Count == 0) return;

            using (var c = new SQLiteConnection(AppDatabase.ConnectionString))
            {
                c.Open();
                foreach (var id in duplicateIds)
                {
                    using (var cmd = new SQLiteCommand("DELETE FROM CBSAccounts WHERE Id = @id;", c))
                    {
                        cmd.Parameters.AddWithValue("@id", id);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }
    }
}
