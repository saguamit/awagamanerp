using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Web.Script.Serialization;
using Awagaman_ERP.Models;

namespace Awagaman_ERP.Data
{
    public sealed class DeletedLedgerRepository
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 128
        };

        public DeletedLedgerRepository()
        {
            AppDatabase.EnsureInitialized();
        }

        public void Add(string ledgerType, string entityKey, object snapshot)
        {
            if (string.IsNullOrWhiteSpace(ledgerType) || snapshot == null)
            {
                return;
            }

            var json = Serializer.Serialize(snapshot);
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = @"
INSERT INTO DeletedLedgerRecords (LedgerType, EntityKey, JsonData, DeletedUtc)
VALUES (@LedgerType, @EntityKey, @JsonData, @DeletedUtc);";
                command.Parameters.AddWithValue("@LedgerType", ledgerType.Trim());
                command.Parameters.AddWithValue("@EntityKey", (entityKey ?? string.Empty).Trim());
                command.Parameters.AddWithValue("@JsonData", json);
                command.Parameters.AddWithValue("@DeletedUtc", DateTime.UtcNow.ToString("o"));
                command.ExecuteNonQuery();

                command.Parameters.Clear();
                command.CommandText = @"
DELETE FROM DeletedLedgerRecords
WHERE LedgerType = @LedgerType
  AND Id NOT IN (
      SELECT Id
      FROM DeletedLedgerRecords
      WHERE LedgerType = @LedgerType
      ORDER BY DeletedUtc DESC, Id DESC
      LIMIT 100
  );";
                command.Parameters.AddWithValue("@LedgerType", ledgerType.Trim());
                command.ExecuteNonQuery();
            }
        }

        public List<DeletedLedgerRecord> GetRecent(string ledgerType, int take = 100)
        {
            var results = new List<DeletedLedgerRecord>();
            using (var connection = new SQLiteConnection(AppDatabase.ConnectionString))
            using (var command = connection.CreateCommand())
            {
                connection.Open();
                command.CommandText = @"
SELECT Id, LedgerType, EntityKey, JsonData, DeletedUtc
FROM DeletedLedgerRecords
WHERE LedgerType = @LedgerType
ORDER BY DeletedUtc DESC, Id DESC
LIMIT @Take;";
                command.Parameters.AddWithValue("@LedgerType", (ledgerType ?? string.Empty).Trim());
                command.Parameters.AddWithValue("@Take", Math.Max(1, Math.Min(100, take)));
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        results.Add(new DeletedLedgerRecord
                        {
                            Id = Convert.ToInt32(reader["Id"]),
                            LedgerType = reader["LedgerType"] as string,
                            EntityKey = reader["EntityKey"] as string,
                            JsonData = reader["JsonData"] as string,
                            DeletedUtc = ParseDate(reader["DeletedUtc"])
                        });
                    }
                }
            }

            return results;
        }

        private static DateTime ParseDate(object value)
        {
            if (value == null || value == DBNull.Value) return DateTime.UtcNow;
            DateTime parsed;
            return DateTime.TryParse(value.ToString(), out parsed) ? parsed : DateTime.UtcNow;
        }
    }
}
