using System;
using System.Data.SQLite;
using System.IO;

namespace Awagaman_ERP.Data
{
    internal static class AppDatabase
    {
        private static readonly object SyncRoot = new object();
        private static bool _initialized;

        public static string DatabasePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Awagaman ERP", "awagaman_erp.db");

        public static string ConnectionString =>
            $"Data Source={DatabasePath};Version=3;Pooling=True;";

        public static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (_initialized)
                {
                    return;
                }

                // Migrate database from old location (EXE folder) to new location (AppData)
                var oldPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "awagaman_erp.db");
                var newDir = Path.GetDirectoryName(DatabasePath);
                if (!Directory.Exists(newDir))
                {
                    Directory.CreateDirectory(newDir);
                }
                if (File.Exists(oldPath) && !File.Exists(DatabasePath))
                {
                    try { File.Copy(oldPath, DatabasePath); }
                    catch { /* Migration failed, new DB will be created */ }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath));

                using (var connection = new SQLiteConnection(ConnectionString))
                {
                    connection.Open();

                    ExecuteNonQuery(connection, "PRAGMA foreign_keys = ON;");
                    ExecuteNonQuery(connection, "PRAGMA journal_mode = WAL;");
                    ExecuteNonQuery(connection, "PRAGMA synchronous = NORMAL;");

                    ExecuteNonQuery(connection, @"
CREATE TABLE IF NOT EXISTS Challans (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Sr INTEGER NOT NULL,
    ChallanNumber TEXT NOT NULL UNIQUE,
    Date TEXT NOT NULL,
    LRNumber TEXT,
    BrokerName TEXT,
    FromLocation TEXT,
    ToLocation TEXT,
    VehicleNumber TEXT,
    VehicleType TEXT,
    DriverName TEXT,
    DriverMobile TEXT,
    EngineNo TEXT,
    LicenceNo TEXT,
    PolicyNo TEXT,
    ChassisNo TEXT,
    OwnerName TEXT,
    PAN TEXT,
    LorryHire REAL NOT NULL,
    LessTDS REAL NOT NULL,
    AdvanceAmount REAL NOT NULL,
    AdvanceNEFT REAL NOT NULL,
    AdvanceCash REAL NOT NULL,
    AdvanceDate TEXT NULL,
    Detention REAL NOT NULL,
    Hamali REAL NOT NULL,
    Deduction REAL NOT NULL,
    BalancePaidNEFT REAL NOT NULL,
    BalancePaidCash REAL NOT NULL,
    BalancePaidDate TEXT NULL,
    PaidTo TEXT,
    Remarks TEXT,
    BillAmount REAL NOT NULL,
    Margin REAL NOT NULL
);");

                    // Add Balance/Due columns for existing databases (safe if already exist)
                    try { ExecuteNonQuery(connection, "ALTER TABLE Challans ADD COLUMN ImportedBalance REAL;"); } catch { }
                    try { ExecuteNonQuery(connection, "ALTER TABLE Challans ADD COLUMN ImportedDue REAL;"); } catch { }

                    ExecuteNonQuery(connection, @"
CREATE TABLE IF NOT EXISTS LREntries (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Sr INTEGER NOT NULL,
    LRNo TEXT NOT NULL UNIQUE,
    Date TEXT NOT NULL,
    ConsignorName TEXT,
    ConsignorAddress TEXT,
    ConsignorGST TEXT,
    ConsigneeName TEXT,
    ConsigneeAddress TEXT,
    ConsigneeGST TEXT,
    FromLocation TEXT,
    ToLocation TEXT,
    VehicleNo TEXT,
    VehicleType TEXT,
    Weight REAL NOT NULL DEFAULT 0,
    SizeL REAL NOT NULL DEFAULT 0,
    SizeW REAL NOT NULL DEFAULT 0,
    SizeH REAL NOT NULL DEFAULT 0,
    ActualWeight REAL NOT NULL DEFAULT 0,
    ChargedWeight REAL NOT NULL DEFAULT 0,
    PKG INTEGER NOT NULL,
    PkgType TEXT,
    Description TEXT,
    Invoice TEXT,
    Value TEXT,
    CHNo TEXT,
    TotalFreight REAL NOT NULL,
    Hamali REAL NOT NULL DEFAULT 0,
    Detention REAL NOT NULL DEFAULT 0,
    Others REAL NOT NULL DEFAULT 0,
    StCharge REAL NOT NULL DEFAULT 0,
    NEFT REAL NOT NULL,
    CASH REAL NOT NULL,
    TDS REAL NOT NULL DEFAULT 0,
    Ded REAL NOT NULL DEFAULT 0,
    BillNo TEXT,
    BillDate TEXT NULL,
    BILL REAL NOT NULL,
    BillParty TEXT,
    Broker TEXT,
    FrtType TEXT,
    PayType TEXT,
    Comm REAL NOT NULL,
    Paid TEXT
);");

                    ExecuteNonQuery(connection, @"
CREATE TABLE IF NOT EXISTS TrackingEntries (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Sr INTEGER NOT NULL,
    ChallanNo TEXT,
    ChallanDate TEXT,
    FromLocation TEXT,
    ToLocation TEXT,
    VehicleNo TEXT,
    DriverMobile TEXT,
    EwayBillTillDate TEXT NULL,
    DispatchDate TEXT NULL,
    DispatchTime TEXT,
    DeliveredDate TEXT NULL,
    DeliveredTime TEXT
);");

                    try { ExecuteNonQuery(connection, "ALTER TABLE LREntries ADD COLUMN Invoice TEXT;"); } catch { }
                    try { ExecuteNonQuery(connection, "ALTER TABLE LREntries ADD COLUMN Value TEXT;"); } catch { }
                    try { ExecuteNonQuery(connection, "ALTER TABLE LREntries ADD COLUMN Weight REAL NOT NULL DEFAULT 0;"); } catch { }
                    try { ExecuteNonQuery(connection, "ALTER TABLE LREntries ADD COLUMN Hamali REAL NOT NULL DEFAULT 0;"); } catch { }
                    try { ExecuteNonQuery(connection, "ALTER TABLE LREntries ADD COLUMN Detention REAL NOT NULL DEFAULT 0;"); } catch { }
                    try { ExecuteNonQuery(connection, "ALTER TABLE LREntries ADD COLUMN Others REAL NOT NULL DEFAULT 0;"); } catch { }
                    try { ExecuteNonQuery(connection, "ALTER TABLE LREntries ADD COLUMN StCharge REAL NOT NULL DEFAULT 0;"); } catch { }
                    try { ExecuteNonQuery(connection, "ALTER TABLE LREntries ADD COLUMN TDS REAL NOT NULL DEFAULT 0;"); } catch { }
                    try { ExecuteNonQuery(connection, "ALTER TABLE LREntries ADD COLUMN Ded REAL NOT NULL DEFAULT 0;"); } catch { }
                    try { ExecuteNonQuery(connection, "ALTER TABLE LREntries ADD COLUMN SizeL REAL NOT NULL DEFAULT 0;"); } catch { }
                    try { ExecuteNonQuery(connection, "ALTER TABLE LREntries ADD COLUMN SizeW REAL NOT NULL DEFAULT 0;"); } catch { }
                    try { ExecuteNonQuery(connection, "ALTER TABLE LREntries ADD COLUMN SizeH REAL NOT NULL DEFAULT 0;"); } catch { }
                    try { ExecuteNonQuery(connection, "ALTER TABLE LREntries ADD COLUMN ActualWeight REAL NOT NULL DEFAULT 0;"); } catch { }
                    try { ExecuteNonQuery(connection, "ALTER TABLE LREntries ADD COLUMN ChargedWeight REAL NOT NULL DEFAULT 0;"); } catch { }
                    try { ExecuteNonQuery(connection, "ALTER TABLE LREntries ADD COLUMN PkgType TEXT;"); } catch { }
                    try { ExecuteNonQuery(connection, "ALTER TABLE LREntries ADD COLUMN PayType TEXT;"); } catch { }

                    ExecuteNonQuery(connection, @"
CREATE TABLE IF NOT EXISTS ReportingTracks (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TrackingEntryId INTEGER NOT NULL,
    ReportDateTime TEXT NOT NULL,
    Remarks TEXT,
    FOREIGN KEY (TrackingEntryId) REFERENCES TrackingEntries(Id) ON DELETE CASCADE
);");

                    ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_Challans_Date ON Challans(Date);");
                    ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_Challans_Sr ON Challans(Sr);");
                    ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_Challans_Sr_Id ON Challans(Sr, Id);");
                    ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_Challans_BrokerName ON Challans(BrokerName);");
                    ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_Challans_LRNumber ON Challans(LRNumber);");
                    ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_Challans_VehicleNumber ON Challans(VehicleNumber);");
                    ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_LREntries_Date ON LREntries(Date);");
                    ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_LREntries_Broker ON LREntries(Broker);");
                    ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_LREntries_CHNo ON LREntries(CHNo);");
                    ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_LREntries_VehicleNo ON LREntries(VehicleNo);");
                    ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_ReportingTracks_TrackingEntryId ON ReportingTracks(TrackingEntryId);");

                    ExecuteNonQuery(connection, @"
CREATE TABLE IF NOT EXISTS Parties (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Sr INTEGER NOT NULL,
    PartyName TEXT NOT NULL UNIQUE,
    Address TEXT,
    GSTNo TEXT
);");

                    ExecuteNonQuery(connection, @"CREATE TABLE IF NOT EXISTS ChallanComments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ChallanId INTEGER NOT NULL,
    Comment TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);");

                    ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_ChallanComments_ChallanId ON ChallanComments(ChallanId);");

                    ExecuteNonQuery(connection, @"CREATE TABLE IF NOT EXISTS LRComments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    LREntryId INTEGER NOT NULL,
    Comment TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);");

                    ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_LRComments_LREntryId ON LRComments(LREntryId);");

                    ExecuteNonQuery(connection, @"CREATE TABLE IF NOT EXISTS Bills (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Sr INTEGER NOT NULL,
    BillNo TEXT,
    BillDate TEXT,
    Party TEXT,
    LRNo TEXT,
    LRDate TEXT,
    FromLoc TEXT,
    ToLoc TEXT,
    VehicleType TEXT,
    Freight REAL DEFAULT 0,
    Detention REAL DEFAULT 0,
    HML REAL DEFAULT 0,
    OTHR REAL DEFAULT 0,
    StCharge REAL DEFAULT 0,
    RCVD REAL DEFAULT 0,
    TDS REAL DEFAULT 0,
    DED REAL DEFAULT 0,
    MOP TEXT,
    MR TEXT,
    Remarks TEXT,
    Date TEXT
);");
                    try { ExecuteNonQuery(connection, "ALTER TABLE Bills ADD COLUMN Remarks TEXT;"); } catch { }
                    try { ExecuteNonQuery(connection, "ALTER TABLE Bills ADD COLUMN StCharge REAL DEFAULT 0;"); } catch { }

                    ExecuteNonQuery(connection, @"
CREATE TABLE IF NOT EXISTS CBSAccounts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Sr INTEGER NOT NULL,
    AccountName TEXT NOT NULL UNIQUE,
    IsActive INTEGER NOT NULL DEFAULT 1
);");
                    ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_CBSAccounts_AccountName ON CBSAccounts(AccountName);");

                    ExecuteNonQuery(connection, @"
CREATE TABLE IF NOT EXISTS CashBankStatements (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Sr INTEGER NOT NULL,
    CBS TEXT,
    Date TEXT NOT NULL,
    AccountName TEXT,
    Particulars TEXT,
    Remarks TEXT,
    BankDr REAL NOT NULL DEFAULT 0,
    BankCr REAL NOT NULL DEFAULT 0,
    CashDr REAL NOT NULL DEFAULT 0,
    CashCr REAL NOT NULL DEFAULT 0
);");
                    ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_CashBankStatements_Date ON CashBankStatements(Date);");
                    ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_CashBankStatements_AccountName ON CashBankStatements(AccountName);");

                    ExecuteNonQuery(connection, @"
CREATE TABLE IF NOT EXISTS VehicleLedger (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Sr INTEGER NOT NULL,
    VehicleNumber TEXT NOT NULL UNIQUE,
    OwnerName TEXT,
    PANNumber TEXT,
    EngineNumber TEXT,
    ChassisNumber TEXT,
    VehicleType TEXT
);");
                    ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_VehicleLedger_VehicleNumber ON VehicleLedger(VehicleNumber);");

                    ExecuteNonQuery(connection, @"CREATE TABLE IF NOT EXISTS BillComments (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    BillId INTEGER NOT NULL,
    Comment TEXT NOT NULL,
    CreatedAt TEXT NOT NULL
);");

                    ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS IX_BillComments_BillId ON BillComments(BillId);");
                }

                _initialized = true;
            }
        }

        private static void ExecuteNonQuery(SQLiteConnection connection, string sql)
        {
            using (var command = new SQLiteCommand(sql, connection))
            {
                command.ExecuteNonQuery();
            }
        }

        public static void EnsureBillTablesExist()
        {
            try
            {
                using (var c = new SQLiteConnection(ConnectionString))
                {
                    c.Open();
                    ExecuteNonQuery(c, @"CREATE TABLE IF NOT EXISTS Bills (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT, Sr INTEGER NOT NULL, BillNo TEXT, BillDate TEXT,
                        Party TEXT, LRNo TEXT, LRDate TEXT, FromLoc TEXT, ToLoc TEXT, VehicleType TEXT,
                        Freight REAL DEFAULT 0, Detention REAL DEFAULT 0, HML REAL DEFAULT 0, OTHR REAL DEFAULT 0,
                        StCharge REAL DEFAULT 0,
                        RCVD REAL DEFAULT 0, TDS REAL DEFAULT 0, DED REAL DEFAULT 0, MOP TEXT, MR TEXT, Remarks TEXT, Date TEXT);");
                    try { ExecuteNonQuery(c, "ALTER TABLE Bills ADD COLUMN Remarks TEXT;"); } catch { }
                    try { ExecuteNonQuery(c, "ALTER TABLE Bills ADD COLUMN StCharge REAL DEFAULT 0;"); } catch { }
                    ExecuteNonQuery(c, @"CREATE TABLE IF NOT EXISTS BillComments (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT, BillId INTEGER NOT NULL,
                        Comment TEXT NOT NULL, CreatedAt TEXT NOT NULL);");
                    ExecuteNonQuery(c, "CREATE INDEX IF NOT EXISTS IX_BillComments_BillId ON BillComments(BillId);");
                    ExecuteNonQuery(c, @"CREATE TABLE IF NOT EXISTS BillReceipts (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        BillNo TEXT NOT NULL,
                        Party TEXT,
                        BillTotal REAL NOT NULL DEFAULT 0,
                        BillDate TEXT,
                        ReceiptDate TEXT NOT NULL,
                        RCVD REAL NOT NULL DEFAULT 0,
                        TDS REAL NOT NULL DEFAULT 0,
                        DED REAL NOT NULL DEFAULT 0,
                        MOP TEXT,
                        MR TEXT,
                        Remarks TEXT,
                        DueAfter REAL NOT NULL DEFAULT 0,
                        CreatedAt TEXT NOT NULL);");
                    try { ExecuteNonQuery(c, "ALTER TABLE BillReceipts ADD COLUMN BillDate TEXT;"); } catch { }
                    ExecuteNonQuery(c, "CREATE INDEX IF NOT EXISTS IX_BillReceipts_BillNo ON BillReceipts(BillNo);");
                }
            }
            catch { }
        }
    }
}
