using Awagaman.Api.Models;
using Awagaman.Api.Helpers;
using Dapper;
using System.Globalization;
using System.Threading;

namespace Awagaman.Api.DataAccess;

public sealed class AwagamanRepository
{
    private readonly IPgConnectionFactory _factory;
    private readonly SemaphoreSlim _purchaseLhsSyncLock = new(1, 1);
    private readonly SemaphoreSlim _challanLhsSyncLock = new(1, 1);
    private volatile bool _purchaseLhsDirty = true;
    private volatile bool _challanLhsDirty = true;

    public AwagamanRepository(IPgConnectionFactory factory)
    {
        _factory = factory;
    }

    private async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        return await conn.QueryAsync<T>(sql, param);
    }

    private async Task<T?> QuerySingleOrDefaultAsync<T>(string sql, object? param = null)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        return await conn.QuerySingleOrDefaultAsync<T>(sql, param);
    }

    private async Task<int> ExecuteScalarIntAsync(string sql, object param)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(sql, param);
    }

    private async Task<int> ExecuteAsync(string sql, object param)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        return await conn.ExecuteAsync(sql, param);
    }

    public async Task ResetAllDataAsync()
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await conn.ExecuteAsync("DELETE FROM reporting_tracks;", transaction: tx);
        await conn.ExecuteAsync("DELETE FROM tracking_entries;", transaction: tx);
        await conn.ExecuteAsync("DELETE FROM bill_comments;", transaction: tx);
        await conn.ExecuteAsync("DELETE FROM lr_comments;", transaction: tx);
        await conn.ExecuteAsync("DELETE FROM challan_comments;", transaction: tx);
        await conn.ExecuteAsync("DELETE FROM bill_receipts;", transaction: tx);
        await conn.ExecuteAsync("DELETE FROM bills;", transaction: tx);
        await conn.ExecuteAsync("DELETE FROM challan_ledger_entries;", transaction: tx);
        await conn.ExecuteAsync("DELETE FROM challans;", transaction: tx);
        await conn.ExecuteAsync("DELETE FROM lr_entries;", transaction: tx);
        await conn.ExecuteAsync("DELETE FROM cash_bank_statements;", transaction: tx);
        await conn.ExecuteAsync("DELETE FROM vehicle_ledger;", transaction: tx);
        await conn.ExecuteAsync("DELETE FROM parties;", transaction: tx);

        await tx.CommitAsync();

        MarkLhsDirty("Purchase LHS");
        MarkLhsDirty("Challan LHS");
    }

    public async Task ResetLRDataAsync()
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await conn.ExecuteAsync("DELETE FROM lr_comments;", transaction: tx);
        await conn.ExecuteAsync("DELETE FROM lr_entries;", transaction: tx);

        await tx.CommitAsync();
    }

    public async Task ResetBillDataAsync()
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await conn.ExecuteAsync("DELETE FROM bill_comments;", transaction: tx);
        await conn.ExecuteAsync("DELETE FROM bill_receipts;", transaction: tx);
        await conn.ExecuteAsync("DELETE FROM bills;", transaction: tx);

        await tx.CommitAsync();
    }

    public async Task<DashboardSummary> GetDashboardSummaryAsync()
    {
        const string sql = @"
WITH normalized_lr AS (
    SELECT lower(regexp_replace(trim(lrno), '\s+', '', 'g')) AS lr_no
    FROM lr_entries
    WHERE COALESCE(trim(lrno), '') <> ''
),
lr_challans AS (
    SELECT lower(trim(chno)) AS challan_no
    FROM lr_entries
    WHERE COALESCE(trim(chno), '') <> ''
),
challan_lr_parts AS (
    SELECT
        c.id,
        c.challan_number,
        COALESCE(trim(c.lr_number), '') AS raw_lr,
        lower(regexp_replace(trim(part.lr_no), '\s+', '', 'g')) AS lr_no
    FROM challans c
    LEFT JOIN LATERAL (
        SELECT value AS lr_no
        FROM regexp_split_to_table(COALESCE(c.lr_number, ''), '[,;\s]+') AS value
        WHERE COALESCE(trim(value), '') <> ''
    ) part ON TRUE
),
pending_bookings AS (
    SELECT id
    FROM challan_lr_parts p
    WHERE (p.raw_lr = '' AND NOT EXISTS (
        SELECT 1
        FROM lr_challans lc
        WHERE lc.challan_no = lower(trim(p.challan_number))
    ))
    OR (p.raw_lr <> '' AND p.lr_no IS NOT NULL AND NOT EXISTS (
        SELECT 1
        FROM normalized_lr lr
        WHERE lr.lr_no = p.lr_no
    ))
),
billed_lrs AS (
    SELECT lower(regexp_replace(trim(part.lr_no), '\s+', '', 'g')) AS lr_no
    FROM bills b
    CROSS JOIN LATERAL (
        SELECT value AS lr_no
        FROM regexp_split_to_table(COALESCE(b.lr_no, ''), '[,;\s]+') AS value
        WHERE COALESCE(trim(value), '') <> ''
    ) part
)
SELECT
    (SELECT COUNT(*) FROM challan_ledger_entries) AS ChallanCount,
    (SELECT COUNT(*) FROM challan_ledger_entries
        WHERE (lorry_hire - less_tds + detention + hamali - deduction - advance_amount - balance_paid_neft - balance_paid_cash) > 0) AS DueChallanCount,
    COALESCE((SELECT SUM(lorry_hire - less_tds + detention + hamali - deduction - advance_amount - balance_paid_neft - balance_paid_cash) FROM challan_ledger_entries), 0) AS ChallanDueAmount,
    COALESCE((SELECT SUM(freight + detention + hml + othr + st_charge - rcvd - tds - ded) FROM bills), 0) AS BillDueAmount,
    COALESCE((SELECT SUM(bank_dr - bank_cr) FROM cash_bank_statements), 0) AS CBSBankNet,
    COALESCE((SELECT SUM(cash_dr - cash_cr) FROM cash_bank_statements), 0) AS CBSCashNet,
    (SELECT COUNT(*) FROM lr_entries lr
        WHERE COALESCE(trim(lr.lrno), '') <> ''
          AND COALESCE(trim(lr.bill_no), '') = ''
          AND NOT EXISTS (
              SELECT 1
              FROM billed_lrs b
              WHERE b.lr_no = lower(regexp_replace(trim(lr.lrno), '\s+', '', 'g'))
          )) AS PendingBillCount,
    (SELECT COUNT(*) FROM pending_bookings) AS NewBookingCount;";

        return await QuerySingleOrDefaultAsync<DashboardSummary>(sql) ?? new DashboardSummary();
    }

    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private static int NormalizePageSize(int pageSize)
    {
        if (pageSize < 1) return 100;
        return pageSize > 500 ? 500 : pageSize;
    }

    private static void AddLikeFilter(List<string> whereParts, DynamicParameters parameters, string columnName, string parameterName, string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        whereParts.Add($"{columnName} ILIKE @{parameterName}");
        parameters.Add(parameterName, $"%{text}%");
    }

    private static string SortDirection(bool ascending) => ascending ? "ASC" : "DESC";

    private static string ChallanFinancialYearSql() =>
        @"COALESCE(
            CASE
                WHEN POSITION('/' IN COALESCE(challan_number, '')) > 0 THEN
                    CASE
                        WHEN COALESCE(NULLIF(regexp_replace(split_part(split_part(COALESCE(challan_number, ''), '/', 2), '-', 1), '[^0-9]', '', 'g'), ''), '0')::int >= 50
                            THEN 1900 + COALESCE(NULLIF(regexp_replace(split_part(split_part(COALESCE(challan_number, ''), '/', 2), '-', 1), '[^0-9]', '', 'g'), ''), '0')::int
                        ELSE 2000 + COALESCE(NULLIF(regexp_replace(split_part(split_part(COALESCE(challan_number, ''), '/', 2), '-', 1), '[^0-9]', '', 'g'), ''), '0')::int
                    END
            END,
            CASE WHEN EXTRACT(MONTH FROM date) >= 4 THEN EXTRACT(YEAR FROM date)::int ELSE EXTRACT(YEAR FROM date)::int - 1 END
        )";

    private static string ChallanSequenceSql() =>
        "COALESCE(NULLIF(regexp_replace(split_part(COALESCE(challan_number, ''), '/', 1), '[^0-9]', '', 'g'), '')::int, 0)";

    private static string BillFinancialYearSql() =>
        @"CASE
            WHEN EXTRACT(MONTH FROM bill_date) >= 4 THEN EXTRACT(YEAR FROM bill_date)::int
            ELSE EXTRACT(YEAR FROM bill_date)::int - 1
        END";

    private static string BillSequenceSql() =>
        @"CASE
            WHEN POSITION('/' IN COALESCE(bill_no, '')) > 0 THEN
                CASE
                    WHEN COALESCE(NULLIF(regexp_replace(split_part(COALESCE(bill_no, ''), '/', 2), '[^0-9]', '', 'g'), ''), '0')::int > 0
                         OR trim(split_part(COALESCE(bill_no, ''), '/', 2)) = '0'
                        THEN COALESCE(NULLIF(regexp_replace(split_part(COALESCE(bill_no, ''), '/', 2), '[^0-9]', '', 'g'), ''), '0')::int
                    ELSE COALESCE(NULLIF(regexp_replace(split_part(COALESCE(bill_no, ''), '/', 1), '[^0-9]', '', 'g'), ''), '0')::int
                END
            ELSE COALESCE(NULLIF(regexp_replace(COALESCE(bill_no, ''), '[^0-9]', '', 'g'), ''), '0')::int
        END";

    private static string BuildChallanOrderBy(string? sortColumn, bool ascending)
    {
        var dir = SortDirection(ascending);
        return (sortColumn ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "challannumber" => $"{ChallanFinancialYearSql()} {dir}, {ChallanSequenceSql()} {dir}, sr {dir}, id {dir}",
            "date" => $"date {dir} NULLS LAST, sr {dir}, id {dir}",
            "lrnumber" => $"lr_number {dir}, sr {dir}, id {dir}",
            "brokername" => $"broker_name {dir}, sr {dir}, id {dir}",
            "from" or "fromlocation" => $"from_location {dir}, sr {dir}, id {dir}",
            "to" or "tolocation" => $"to_location {dir}, sr {dir}, id {dir}",
            "vehiclenumber" => $"vehicle_number {dir}, sr {dir}, id {dir}",
            "vehicletype" => $"vehicle_type {dir}, sr {dir}, id {dir}",
            "drivername" => $"driver_name {dir}, sr {dir}, id {dir}",
            "drivermobile" => $"driver_mobile {dir}, sr {dir}, id {dir}",
            "ownername" => $"owner_name {dir}, sr {dir}, id {dir}",
            "lorryhire" => $"lorry_hire {dir}, sr {dir}, id {dir}",
            "other" => $"other_amount {dir}, sr {dir}, id {dir}",
            "lhs" => $"(lorry_hire + other_amount) {dir}, sr {dir}, id {dir}",
            "detention" => $"detention {dir}, sr {dir}, id {dir}",
            "hamali" => $"hamali {dir}, sr {dir}, id {dir}",
            "balance" => $"(lorry_hire - less_tds - advance_amount) {dir}, sr {dir}, id {dir}",
            "due" => $"((lorry_hire - less_tds - advance_amount) + detention + hamali + deduction - balance_paid_neft - balance_paid_cash) {dir}, sr {dir}, id {dir}",
            "billamount" => $"bill_amount {dir}, sr {dir}, id {dir}",
            "margin" => $"margin {dir}, sr {dir}, id {dir}",
            "sr" => $"sr {dir}, id {dir}",
            _ => $"sr {dir}, id {dir}"
        };
    }

    private static string BuildChallanOrderBy(string? sortColumn, bool ascending, bool useLhsDerived)
    {
        if (!useLhsDerived)
        {
            return BuildChallanOrderBy(sortColumn, ascending);
        }

        var dir = SortDirection(ascending);
        return (sortColumn ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "balance" => $"((lorry_hire + other_amount) - less_tds - advance_amount) {dir}, sr {dir}, id {dir}",
            "due" => $"(((lorry_hire + other_amount) - less_tds - advance_amount) + detention + hamali + deduction - balance_paid_neft - balance_paid_cash) {dir}, sr {dir}, id {dir}",
            "margin" => $"(CASE WHEN bill_amount = 0 THEN 0 ELSE bill_amount - ((lorry_hire + other_amount) + detention + hamali) END) {dir}, sr {dir}, id {dir}",
            _ => BuildChallanOrderBy(sortColumn, ascending)
        };
    }

    private static string BuildLROrderBy(string? sortColumn, bool ascending)
    {
        var dir = SortDirection(ascending);
        return (sortColumn ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "lrno" => $@"CASE
                            WHEN POSITION('/' IN COALESCE(lrno, '')) > 0 THEN split_part(COALESCE(lrno, ''), '/', 2)
                            ELSE ''
                         END {dir},
                         COALESCE(NULLIF(regexp_replace(split_part(COALESCE(lrno, ''), '/', 1), '[^0-9]', '', 'g'), '')::int, 0) {dir},
                         lrno {dir}, sr {dir}, id {dir}",
            "date" => $"date {dir} NULLS LAST, sr {dir}, id {dir}",
            "consignorname" => $"consignor_name {dir}, sr {dir}, id {dir}",
            "consigneename" => $"consignee_name {dir}, sr {dir}, id {dir}",
            "from" or "fromlocation" => $"from_location {dir}, sr {dir}, id {dir}",
            "to" or "tolocation" => $"to_location {dir}, sr {dir}, id {dir}",
            "vehicleno" => $"vehicle_no {dir}, sr {dir}, id {dir}",
            "vehicletype" => $"vehicle_type {dir}, sr {dir}, id {dir}",
            "chno" => $"chno {dir}, sr {dir}, id {dir}",
            "totalfreight" => $"total_freight {dir}, sr {dir}, id {dir}",
            "hamali" => $"hamali {dir}, sr {dir}, id {dir}",
            "detention" => $"detention {dir}, sr {dir}, id {dir}",
            "others" => $"others {dir}, sr {dir}, id {dir}",
            "stcharge" => $"st_charge {dir}, sr {dir}, id {dir}",
            "billno" => $"bill_no {dir}, sr {dir}, id {dir}",
            "billparty" => $"bill_party {dir}, sr {dir}, id {dir}",
            "broker" => $"broker {dir}, sr {dir}, id {dir}",
            "sr" => $"sr {dir}, id {dir}",
            _ => $"sr {dir}, id {dir}"
        };
    }

    private static string BuildBillOrderBy(string? sortColumn, bool ascending)
    {
        var dir = SortDirection(ascending);
        return (sortColumn ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "billno" => $"{BillFinancialYearSql()} {dir}, {BillSequenceSql()} {dir}, bill_no {dir}, sr {dir}, id {dir}",
            "billdate" => $"bill_date {dir} NULLS LAST, sr {dir}, id {dir}",
            "party" => $"party {dir}, sr {dir}, id {dir}",
            "lrno" => $"lr_no {dir}, sr {dir}, id {dir}",
            "lrdate" => $"lr_date {dir} NULLS LAST, sr {dir}, id {dir}",
            "from" or "fromloc" => $"from_loc {dir}, sr {dir}, id {dir}",
            "to" or "toloc" => $"to_loc {dir}, sr {dir}, id {dir}",
            "vehicletype" => $"vehicle_type {dir}, sr {dir}, id {dir}",
            "freight" => $"freight {dir}, sr {dir}, id {dir}",
            "detention" => $"detention {dir}, sr {dir}, id {dir}",
            "hml" => $"hml {dir}, sr {dir}, id {dir}",
            "othr" => $"othr {dir}, sr {dir}, id {dir}",
            "stcharge" => $"st_charge {dir}, sr {dir}, id {dir}",
            "total" => $"(freight + detention + hml + othr + st_charge) {dir}, sr {dir}, id {dir}",
            "rcvd" => $"rcvd {dir}, sr {dir}, id {dir}",
            "tds" => $"tds {dir}, sr {dir}, id {dir}",
            "ded" => $"ded {dir}, sr {dir}, id {dir}",
            "due" => $"(freight + detention + hml + othr + st_charge - rcvd - tds - ded) {dir}, sr {dir}, id {dir}",
            "mop" => $"mop {dir}, sr {dir}, id {dir}",
            "mr" => $"mr {dir}, sr {dir}, id {dir}",
            "remarks" => $"remarks {dir}, sr {dir}, id {dir}",
            "date" => $"date {dir} NULLS LAST, sr {dir}, id {dir}",
            "sr" => $"sr {dir}, id {dir}",
            _ => $"bill_date DESC NULLS LAST, sr DESC, id DESC"
        };
    }

    public Task<IEnumerable<PartyEntry>> GetPartiesAsync() =>
        QueryAsync<PartyEntry>("SELECT id, sr, party_name AS PartyName, address AS Address, gst_no AS GSTNo FROM parties ORDER BY sr, id;");

    public Task<PartyEntry?> GetPartyAsync(int id) =>
        QuerySingleOrDefaultAsync<PartyEntry>("SELECT id, sr, party_name AS PartyName, address AS Address, gst_no AS GSTNo FROM parties WHERE id = @id;", new { id });

    public Task<IEnumerable<PartyEntry>> SearchPartiesAsync(string query) =>
        QueryAsync<PartyEntry>(
            "SELECT id, sr, party_name AS PartyName, address AS Address, gst_no AS GSTNo FROM parties WHERE party_name ILIKE @q ORDER BY party_name LIMIT 20;",
            new { q = $"%{query ?? string.Empty}%" });

    public async Task<int> UpsertPartyAsync(PartyEntry entry)
    {
        var sql = entry.Id <= 0
            ? @"INSERT INTO parties (sr, party_name, address, gst_no)
                VALUES (@Sr, @PartyName, @Address, @GSTNo)
                RETURNING id;"
            : @"UPDATE parties SET sr = @Sr, party_name = @PartyName, address = @Address, gst_no = @GSTNo
                WHERE id = @Id;
               SELECT @Id;";
        return await ExecuteScalarIntAsync(sql, entry);
    }

    public Task<int> DeletePartyAsync(int id) =>
        ExecuteAsync("DELETE FROM parties WHERE id = @id;", new { id });

    public Task<IEnumerable<VehicleEntry>> GetVehiclesAsync() =>
        QueryAsync<VehicleEntry>("SELECT id, sr, vehicle_number AS VehicleNumber, owner_name AS OwnerName, pan_number AS PANNumber, engine_number AS EngineNumber, chassis_number AS ChassisNumber, vehicle_type AS VehicleType, driver_name AS DriverName, driver_mobile AS DriverMobile, licence_number AS LicenceNumber, policy_number AS PolicyNumber FROM vehicle_ledger ORDER BY sr, id;");

    public Task<VehicleEntry?> GetVehicleAsync(int id) =>
        QuerySingleOrDefaultAsync<VehicleEntry>("SELECT id, sr, vehicle_number AS VehicleNumber, owner_name AS OwnerName, pan_number AS PANNumber, engine_number AS EngineNumber, chassis_number AS ChassisNumber, vehicle_type AS VehicleType, driver_name AS DriverName, driver_mobile AS DriverMobile, licence_number AS LicenceNumber, policy_number AS PolicyNumber FROM vehicle_ledger WHERE id = @id;", new { id });

    public Task<IEnumerable<VehicleEntry>> SearchVehiclesAsync(string query) =>
        QueryAsync<VehicleEntry>(
            "SELECT id, sr, vehicle_number AS VehicleNumber, owner_name AS OwnerName, pan_number AS PANNumber, engine_number AS EngineNumber, chassis_number AS ChassisNumber, vehicle_type AS VehicleType, driver_name AS DriverName, driver_mobile AS DriverMobile, licence_number AS LicenceNumber, policy_number AS PolicyNumber FROM vehicle_ledger WHERE vehicle_number ILIKE @q ORDER BY vehicle_number LIMIT 20;",
            new { q = $"%{query ?? string.Empty}%" });

    public async Task<int> UpsertVehicleAsync(VehicleEntry entry)
    {
        var sql = entry.Id <= 0
            ? @"INSERT INTO vehicle_ledger (sr, vehicle_number, owner_name, pan_number, engine_number, chassis_number, vehicle_type, driver_name, driver_mobile, licence_number, policy_number)
                VALUES (@Sr, @VehicleNumber, @OwnerName, @PANNumber, @EngineNumber, @ChassisNumber, @VehicleType, @DriverName, @DriverMobile, @LicenceNumber, @PolicyNumber)
                RETURNING id;"
            : @"UPDATE vehicle_ledger SET sr = @Sr, vehicle_number = @VehicleNumber, owner_name = @OwnerName, pan_number = @PANNumber, engine_number = @EngineNumber, chassis_number = @ChassisNumber, vehicle_type = @VehicleType, driver_name = @DriverName, driver_mobile = @DriverMobile, licence_number = @LicenceNumber, policy_number = @PolicyNumber
                WHERE id = @Id;
               SELECT @Id;";
        return await ExecuteScalarIntAsync(sql, entry);
    }

    private async Task SyncVehicleLedgerFromChallanAsync(ChallanEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.VehicleNumber))
        {
            return;
        }

        var vehicleNumber = entry.VehicleNumber.Trim().ToUpperInvariant();
        var existing = (await SearchVehiclesAsync(vehicleNumber))
            .FirstOrDefault(x => string.Equals((x.VehicleNumber ?? string.Empty).Trim(), vehicleNumber, StringComparison.OrdinalIgnoreCase));

        await UpsertVehicleAsync(new VehicleEntry
        {
            Id = existing?.Id ?? 0,
            Sr = existing?.Sr ?? (await GetVehiclesAsync()).Count() + 1,
            VehicleNumber = vehicleNumber,
            OwnerName = string.IsNullOrWhiteSpace(entry.OwnerName) ? existing?.OwnerName : entry.OwnerName,
            PANNumber = string.IsNullOrWhiteSpace(entry.PAN) ? existing?.PANNumber : entry.PAN,
            EngineNumber = string.IsNullOrWhiteSpace(entry.EngineNo) ? existing?.EngineNumber : entry.EngineNo,
            ChassisNumber = string.IsNullOrWhiteSpace(entry.ChassisNo) ? existing?.ChassisNumber : entry.ChassisNo,
            VehicleType = string.IsNullOrWhiteSpace(entry.VehicleType) ? existing?.VehicleType : entry.VehicleType,
            DriverName = string.IsNullOrWhiteSpace(entry.DriverName) ? existing?.DriverName : entry.DriverName,
            DriverMobile = string.IsNullOrWhiteSpace(entry.DriverMobile) ? existing?.DriverMobile : entry.DriverMobile,
            LicenceNumber = string.IsNullOrWhiteSpace(entry.LicenceNo) ? existing?.LicenceNumber : entry.LicenceNo,
            PolicyNumber = string.IsNullOrWhiteSpace(entry.PolicyNo) ? existing?.PolicyNumber : entry.PolicyNo
        });
    }

    private async Task SyncChallanCBSFromChallanAsync(ChallanEntry entry, string accountName)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.ChallanNumber))
        {
            return;
        }

        accountName = string.IsNullOrWhiteSpace(accountName) ? "Purchase LHS" : accountName.Trim();
        await EnsureCBSAccountAsync(accountName);

        var challanNo = entry.ChallanNumber.Trim();
        var advanceParticulars = $"Challan {challanNo} - Advance Paid";
        var balanceParticulars = $"Challan {challanNo} - Balance Paid";

        var advanceNeft = entry.AdvanceNEFT;
        var advanceCash = entry.AdvanceCash;
        if (advanceNeft == 0m && advanceCash == 0m && entry.AdvanceAmount != 0m)
        {
            advanceCash = entry.AdvanceAmount;
        }

        if (advanceNeft != 0m || advanceCash != 0m)
        {
            var advanceDate = (entry.AdvanceDate ?? entry.Date).Date;
            await UpsertAutoCBSRowAsync(new CashBankStatementEntry
            {
                Date = advanceDate,
                CBS = ToCBSMonth(advanceDate),
                AccountName = accountName,
                Particulars = advanceParticulars,
                Remarks = "Auto from Challan",
                BankDr = 0m,
                BankCr = advanceNeft,
                CashDr = 0m,
                CashCr = advanceCash
            });
        }
        else
        {
            await DeleteAutoCBSRowsAsync(accountName, advanceParticulars, "Auto from Challan");
        }

        if (entry.BalancePaidNEFT != 0m || entry.BalancePaidCash != 0m)
        {
            var balanceDate = (entry.BalancePaidDate ?? entry.Date).Date;
            await UpsertAutoCBSRowAsync(new CashBankStatementEntry
            {
                Date = balanceDate,
                CBS = ToCBSMonth(balanceDate),
                AccountName = accountName,
                Particulars = balanceParticulars,
                Remarks = "Auto from Challan",
                BankDr = 0m,
                BankCr = entry.BalancePaidNEFT,
                CashDr = 0m,
                CashCr = entry.BalancePaidCash
            });
        }
        else
        {
            await DeleteAutoCBSRowsAsync(accountName, balanceParticulars, "Auto from Challan");
        }
    }

    private async Task SyncBfrsCBSFromBillReceiptAsync(BillReceiptEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.BillNo))
        {
            return;
        }

        await EnsureCBSAccountAsync("BFRS");

        var billNo = entry.BillNo.Trim();
        var particulars = $"Bill {billNo} - Receipt {entry.Id}";
        var remarks = "Auto from Bill Receipt";

        if (entry.RCVD == 0m)
        {
            await DeleteAutoCBSRowsAsync("BFRS", particulars, remarks);
            return;
        }

        var isCash = IsCashMode(entry.MOP);
        await UpsertAutoCBSRowAsync(new CashBankStatementEntry
        {
            Date = (entry.ReceiptDate == default ? DateTime.Today : entry.ReceiptDate).Date,
            CBS = ToCBSMonth((entry.ReceiptDate == default ? DateTime.Today : entry.ReceiptDate).Date),
            AccountName = "BFRS",
            Particulars = particulars,
            Remarks = remarks,
            BankDr = isCash ? 0m : entry.RCVD,
            BankCr = 0m,
            CashDr = isCash ? entry.RCVD : 0m,
            CashCr = 0m
        });
    }

    private async Task EnsureCBSAccountAsync(string accountName)
    {
        var name = (accountName ?? string.Empty).Trim();
        if (string.Equals(name, "LHS", StringComparison.OrdinalIgnoreCase))
        {
            name = "Purchase LHS";
        }
        if (name.Length == 0)
        {
            return;
        }

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
WITH existing AS (
    SELECT id FROM cbs_accounts
    WHERE CASE
              WHEN lower(trim(account_name)) = 'lhs' THEN 'purchase lhs'
              ELSE lower(trim(account_name))
          END = CASE
                    WHEN lower(trim(@AccountName)) = 'lhs' THEN 'purchase lhs'
                    ELSE lower(trim(@AccountName))
                END
    LIMIT 1
),
updated AS (
    UPDATE cbs_accounts
    SET is_active = true, account_name = @AccountName
    WHERE id IN (SELECT id FROM existing)
    RETURNING id
)
INSERT INTO cbs_accounts (sr, account_name, is_active)
SELECT COALESCE((SELECT MAX(sr) FROM cbs_accounts), 0) + 1, @AccountName, true
WHERE NOT EXISTS (SELECT 1 FROM existing);", new { AccountName = name });
    }

    private async Task UpsertAutoCBSRowAsync(CashBankStatementEntry row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.AccountName) || string.IsNullOrWhiteSpace(row.Particulars))
        {
            return;
        }

        var accountName = row.AccountName.Trim();
        if (string.Equals(accountName, "LHS", StringComparison.OrdinalIgnoreCase))
        {
            accountName = "Purchase LHS";
        }
        var particulars = row.Particulars.Trim();
        var remarks = (row.Remarks ?? string.Empty).Trim();
        row.Date = row.Date.Date;
        row.CBS = string.IsNullOrWhiteSpace(row.CBS) ? ToCBSMonth(row.Date) : row.CBS;

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var existingIds = (await conn.QueryAsync<int>(@"
SELECT id
FROM cash_bank_statements
WHERE CASE
          WHEN lower(trim(account_name)) = 'lhs' THEN 'purchase lhs'
          ELSE lower(trim(account_name))
      END = CASE
                WHEN lower(trim(@AccountName)) = 'lhs' THEN 'purchase lhs'
                ELSE lower(trim(@AccountName))
            END
  AND lower(trim(particulars)) = lower(trim(@Particulars))
  AND lower(trim(COALESCE(remarks, ''))) = lower(trim(@Remarks))
ORDER BY id;", new { AccountName = accountName, Particulars = particulars, Remarks = remarks })).ToList();

        if (existingIds.Count > 0)
        {
            var id = existingIds[0];
            await conn.ExecuteAsync(@"
UPDATE cash_bank_statements
SET cbs = @CBS, date = @Date, account_name = @AccountName, particulars = @Particulars, remarks = @Remarks,
    bank_dr = @BankDr, bank_cr = @BankCr, cash_dr = @CashDr, cash_cr = @CashCr
WHERE id = @Id;", new
            {
                Id = id,
                row.CBS,
                row.Date,
                AccountName = accountName,
                Particulars = particulars,
                Remarks = remarks,
                row.BankDr,
                row.BankCr,
                row.CashDr,
                row.CashCr
            });

            if (existingIds.Count > 1)
            {
                await conn.ExecuteAsync("DELETE FROM cash_bank_statements WHERE id = ANY(@Ids);", new { Ids = existingIds.Skip(1).ToArray() });
            }

            return;
        }

        await conn.ExecuteAsync(@"
INSERT INTO cash_bank_statements (sr, cbs, date, account_name, particulars, remarks, bank_dr, bank_cr, cash_dr, cash_cr)
VALUES (COALESCE((SELECT MAX(sr) FROM cash_bank_statements), 0) + 1, @CBS, @Date, @AccountName, @Particulars, @Remarks, @BankDr, @BankCr, @CashDr, @CashCr);", new
        {
            row.CBS,
            row.Date,
            AccountName = accountName,
            Particulars = particulars,
            Remarks = remarks,
            row.BankDr,
            row.BankCr,
            row.CashDr,
            row.CashCr
        });
    }

    private async Task DeleteAutoCBSRowsAsync(string accountName, string particulars, string remarks)
    {
        await ExecuteAsync(@"
DELETE FROM cash_bank_statements
WHERE CASE
          WHEN lower(trim(account_name)) = 'lhs' THEN 'purchase lhs'
          ELSE lower(trim(account_name))
      END = CASE
                WHEN lower(trim(@AccountName)) = 'lhs' THEN 'purchase lhs'
                ELSE lower(trim(@AccountName))
            END
  AND lower(trim(particulars)) = lower(trim(@Particulars))
  AND lower(trim(COALESCE(remarks, ''))) = lower(trim(@Remarks));", new
        {
            AccountName = (accountName ?? string.Empty).Trim(),
            Particulars = (particulars ?? string.Empty).Trim(),
            Remarks = (remarks ?? string.Empty).Trim()
        });
    }

    private static string NormalizeLhsAccountName(string? accountName)
    {
        var normalized = string.IsNullOrWhiteSpace(accountName) ? "Purchase LHS" : accountName.Trim();
        if (string.Equals(normalized, "LHS", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "Purchase LHS";
        }

        return normalized;
    }

    private static string GetAutoAdvanceParticular(string challanNo) => $"Challan {challanNo} - Advance Paid";

    private static string GetAutoBalanceParticular(string challanNo) => $"Challan {challanNo} - Balance Paid";

    private bool IsLhsDirty(string accountName)
    {
        return string.Equals(accountName, "Challan LHS", StringComparison.OrdinalIgnoreCase)
            ? _challanLhsDirty
            : _purchaseLhsDirty;
    }

    private void MarkLhsDirty(string accountName)
    {
        if (string.Equals(accountName, "Challan LHS", StringComparison.OrdinalIgnoreCase))
        {
            _challanLhsDirty = true;
        }
        else
        {
            _purchaseLhsDirty = true;
        }
    }

    private void MarkLhsClean(string accountName)
    {
        if (string.Equals(accountName, "Challan LHS", StringComparison.OrdinalIgnoreCase))
        {
            _challanLhsDirty = false;
        }
        else
        {
            _purchaseLhsDirty = false;
        }
    }

    private async Task EnsureLhsRowsSynchronizedAsync(string? accountName)
    {
        var normalizedAccount = NormalizeLhsAccountName(accountName);
        if (!IsLhsDirty(normalizedAccount))
        {
            return;
        }

        var isChallanLhs = string.Equals(normalizedAccount, "Challan LHS", StringComparison.OrdinalIgnoreCase);
        var syncLock = isChallanLhs ? _challanLhsSyncLock : _purchaseLhsSyncLock;

        await syncLock.WaitAsync();
        try
        {
            if (!IsLhsDirty(normalizedAccount))
            {
                return;
            }

            var rows = (await QueryAsync<ChallanEntry>(
                GetChallanSelectSql(isChallanLhs) + " ORDER BY sr, id;"))
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.ChallanNumber))
                .ToList();

            var expectedParticulars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                await SyncChallanCBSFromChallanAsync(row, normalizedAccount);

                var challanNo = row.ChallanNumber!.Trim();
                var advanceNeft = row.AdvanceNEFT;
                var advanceCash = row.AdvanceCash;
                if (advanceNeft == 0m && advanceCash == 0m && row.AdvanceAmount != 0m)
                {
                    advanceCash = row.AdvanceAmount;
                }

                if (advanceNeft != 0m || advanceCash != 0m)
                {
                    expectedParticulars.Add(GetAutoAdvanceParticular(challanNo));
                }

                if (row.BalancePaidNEFT != 0m || row.BalancePaidCash != 0m)
                {
                    expectedParticulars.Add(GetAutoBalanceParticular(challanNo));
                }
            }

            await using var conn = _factory.Create();
            await conn.OpenAsync();
            var staleRows = await conn.QueryAsync<(int Id, string Particulars)>(@"
SELECT id AS Id, particulars AS Particulars
FROM cash_bank_statements
WHERE CASE
          WHEN LOWER(TRIM(account_name)) = 'lhs' THEN 'purchase lhs'
          ELSE LOWER(TRIM(account_name))
      END = CASE
                WHEN LOWER(TRIM(@accountName)) = 'lhs' THEN 'purchase lhs'
                ELSE LOWER(TRIM(@accountName))
            END
  AND LOWER(TRIM(COALESCE(remarks, ''))) = 'auto from challan'
  AND particulars ILIKE 'Challan % - %';", new { accountName = normalizedAccount });

            var staleIds = staleRows
                .Where(x => string.IsNullOrWhiteSpace(x.Particulars) || !expectedParticulars.Contains(x.Particulars.Trim()))
                .Select(x => x.Id)
                .Distinct()
                .ToArray();

            if (staleIds.Length > 0)
            {
                await conn.ExecuteAsync("DELETE FROM cash_bank_statements WHERE id = ANY(@Ids);", new { Ids = staleIds });
            }

            MarkLhsClean(normalizedAccount);
        }
        finally
        {
            syncLock.Release();
        }
    }

    private static string ToCBSMonth(DateTime date)
    {
        return date.ToString("MMM-yy", CultureInfo.InvariantCulture);
    }

    private static bool IsCashMode(string? mop)
    {
        return string.Equals((mop ?? string.Empty).Trim(), "CASH", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChallanLedgerKind(string? ledgerKind) =>
        string.Equals(ledgerKind, "challan", StringComparison.OrdinalIgnoreCase);

    private static string GetChallanTableName(bool challanLedgerMode) =>
        challanLedgerMode ? "challan_ledger_entries" : "challans";

    private static string GetChallanSelectSql(bool challanLedgerMode)
    {
        return @"SELECT
            " + (challanLedgerMode ? "source_purchase_id AS SourcePurchaseId," : "NULL::integer AS SourcePurchaseId,") + @"
            id, sr, challan_number AS ChallanNumber, date, lr_number AS LRNumber, broker_name AS BrokerName,
            from_location AS ""From"", to_location AS ""To"", vehicle_number AS VehicleNumber, vehicle_type AS VehicleType,
            driver_name AS DriverName, driver_mobile AS DriverMobile, engine_no AS EngineNo, licence_no AS LicenceNo,
            policy_no AS PolicyNo, chassis_no AS ChassisNo, owner_name AS OwnerName, pan AS PAN, lorry_hire AS LorryHire,
            less_tds AS LessTDS, advance_amount AS AdvanceAmount, advance_neft AS AdvanceNEFT, advance_cash AS AdvanceCash,
            advance_date AS AdvanceDate, detention AS Detention, hamali AS Hamali, other_amount AS Other, deduction AS Deduction,
            balance_paid_neft AS BalancePaidNEFT, balance_paid_cash AS BalancePaidCash, balance_paid_date AS BalancePaidDate,
            paid_to AS PaidTo, remarks AS Remarks, bill_amount AS BillAmount, margin AS Margin,
            imported_balance AS ImportedBalance, imported_due AS ImportedDue, preserve_imported_billing AS PreserveImportedBilling
            FROM " + GetChallanTableName(challanLedgerMode);
    }

    public Task<int> DeleteVehicleAsync(int id) =>
        ExecuteAsync("DELETE FROM vehicle_ledger WHERE id = @id;", new { id });

    public Task<IEnumerable<ChallanEntry>> GetChallansAsync(string? ledgerKind = null)
    {
        var challanLedgerMode = IsChallanLedgerKind(ledgerKind);
        return QueryAsync<ChallanEntry>(GetChallanSelectSql(challanLedgerMode) + " ORDER BY " + BuildChallanOrderBy("challannumber", ascending: false) + ";");
    }

    public async Task<IEnumerable<ChallanEntry>> GetPendingBookingItemsAsync(int limit = 0, string? ledgerKind = null)
    {
        var challanLedgerMode = IsChallanLedgerKind(ledgerKind);
        var tableName = GetChallanTableName(challanLedgerMode);
        var limitClause = limit > 0 ? "LIMIT @limit" : string.Empty;
        var sql = $@"
WITH split_pending AS (
    SELECT
        c.id,
        c.challan_number AS ChallanNumber,
        c.date AS date_sort,
        c.from_location AS ""From"",
        c.to_location AS ""To"",
        c.vehicle_number AS VehicleNumber,
        c.broker_name AS BrokerName,
        c.lorry_hire AS LorryHire,
        btrim(token.lr_no) AS LRNumber
    FROM {tableName} c
    CROSS JOIN LATERAL regexp_split_to_table(COALESCE(c.lr_number, ''), E'[,;\\s]+') AS token(lr_no)
    WHERE btrim(token.lr_no) <> ''
      AND NOT EXISTS (
          SELECT 1
          FROM lr_entries l
          WHERE lower(btrim(COALESCE(l.lrno, ''))) = lower(btrim(token.lr_no))
      )
),
blank_pending AS (
    SELECT
        c.id,
        c.challan_number AS ChallanNumber,
        c.date AS date_sort,
        c.from_location AS ""From"",
        c.to_location AS ""To"",
        c.vehicle_number AS VehicleNumber,
        c.broker_name AS BrokerName,
        c.lorry_hire AS LorryHire,
        ''::text AS LRNumber
    FROM {tableName} c
    WHERE btrim(COALESCE(c.lr_number, '')) = ''
      AND NOT EXISTS (
          SELECT 1
          FROM lr_entries l
          WHERE lower(btrim(COALESCE(l.chno, ''))) = lower(btrim(COALESCE(c.challan_number, '')))
      )
)
SELECT
    id,
    0 AS sr,
    ChallanNumber,
    COALESCE(date_sort, CURRENT_DATE) AS date,
    LRNumber,
    BrokerName,
    ""From"",
    ""To"",
    VehicleNumber,
    NULL::text AS VehicleType,
    NULL::text AS DriverName,
    NULL::text AS DriverMobile,
    NULL::text AS EngineNo,
    NULL::text AS LicenceNo,
    NULL::text AS PolicyNo,
    NULL::text AS ChassisNo,
    NULL::text AS OwnerName,
    NULL::text AS PAN,
    LorryHire,
    0::numeric AS LessTDS,
    0::numeric AS AdvanceAmount,
    0::numeric AS AdvanceNEFT,
    0::numeric AS AdvanceCash,
    NULL::date AS AdvanceDate,
    0::numeric AS Detention,
    0::numeric AS Hamali,
    0::numeric AS Other,
    0::numeric AS Deduction,
    0::numeric AS BalancePaidNEFT,
    0::numeric AS BalancePaidCash,
    NULL::date AS BalancePaidDate,
    NULL::text AS PaidTo,
    NULL::text AS Remarks,
    0::numeric AS BillAmount,
    0::numeric AS Margin,
    NULL::numeric AS ImportedBalance,
    NULL::numeric AS ImportedDue,
    false AS PreserveImportedBilling,
    {(challanLedgerMode ? "NULL::integer AS SourcePurchaseId" : "NULL::integer AS SourcePurchaseId")}
FROM (
    SELECT * FROM split_pending
    UNION ALL
    SELECT * FROM blank_pending
) pending
ORDER BY id DESC, LRNumber DESC
{limitClause};";

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        return await conn.QueryAsync<ChallanEntry>(sql, new { limit });
    }

    public async Task<PagedResult<ChallanEntry>> GetPendingBookingPageAsync(
        int page,
        int pageSize,
        string? search = null,
        string? ledgerKind = null)
    {
        var challanLedgerMode = IsChallanLedgerKind(ledgerKind);
        var tableName = GetChallanTableName(challanLedgerMode);
        page = NormalizePage(page);
        pageSize = NormalizePageSize(pageSize);
        var offset = (page - 1) * pageSize;
        var searchValue = (search ?? string.Empty).Trim();
        var hasSearch = !string.IsNullOrWhiteSpace(searchValue);
        var searchPattern = hasSearch ? $"%{searchValue}%" : string.Empty;

        var sql = $@"
WITH split_pending AS (
    SELECT
        c.id,
        c.challan_number AS ChallanNumber,
        c.date AS date_sort,
        c.from_location AS ""From"",
        c.to_location AS ""To"",
        c.vehicle_number AS VehicleNumber,
        c.broker_name AS BrokerName,
        c.lorry_hire AS LorryHire,
        btrim(token.lr_no) AS LRNumber
    FROM {tableName} c
    CROSS JOIN LATERAL regexp_split_to_table(COALESCE(c.lr_number, ''), E'[,;\\s]+') AS token(lr_no)
    WHERE btrim(token.lr_no) <> ''
      AND NOT EXISTS (
          SELECT 1
          FROM lr_entries l
          WHERE lower(btrim(COALESCE(l.lrno, ''))) = lower(btrim(token.lr_no))
      )
),
blank_pending AS (
    SELECT
        c.id,
        c.challan_number AS ChallanNumber,
        c.date AS date_sort,
        c.from_location AS ""From"",
        c.to_location AS ""To"",
        c.vehicle_number AS VehicleNumber,
        c.broker_name AS BrokerName,
        c.lorry_hire AS LorryHire,
        ''::text AS LRNumber
    FROM {tableName} c
    WHERE btrim(COALESCE(c.lr_number, '')) = ''
      AND NOT EXISTS (
          SELECT 1
          FROM lr_entries l
          WHERE lower(btrim(COALESCE(l.chno, ''))) = lower(btrim(COALESCE(c.challan_number, '')))
      )
),
pending AS (
    SELECT * FROM split_pending
    UNION ALL
    SELECT * FROM blank_pending
),
filtered AS (
    SELECT *
    FROM pending
    WHERE @hasSearch = FALSE
       OR ChallanNumber ILIKE @searchPattern
       OR LRNumber ILIKE @searchPattern
       OR COALESCE(BrokerName, '') ILIKE @searchPattern
       OR COALESCE(""From"", '') ILIKE @searchPattern
       OR COALESCE(""To"", '') ILIKE @searchPattern
       OR COALESCE(VehicleNumber, '') ILIKE @searchPattern
       OR to_char(COALESCE(date_sort, CURRENT_DATE), 'DD-Mon-YYYY') ILIKE @searchPattern
)
SELECT
    id,
    0 AS sr,
    ChallanNumber,
    COALESCE(date_sort, CURRENT_DATE) AS date,
    LRNumber,
    BrokerName,
    ""From"",
    ""To"",
    VehicleNumber,
    NULL::text AS VehicleType,
    NULL::text AS DriverName,
    NULL::text AS DriverMobile,
    NULL::text AS EngineNo,
    NULL::text AS LicenceNo,
    NULL::text AS PolicyNo,
    NULL::text AS ChassisNo,
    NULL::text AS OwnerName,
    NULL::text AS PAN,
    LorryHire,
    0::numeric AS LessTDS,
    0::numeric AS AdvanceAmount,
    0::numeric AS AdvanceNEFT,
    0::numeric AS AdvanceCash,
    NULL::date AS AdvanceDate,
    0::numeric AS Detention,
    0::numeric AS Hamali,
    0::numeric AS Other,
    0::numeric AS Deduction,
    0::numeric AS BalancePaidNEFT,
    0::numeric AS BalancePaidCash,
    NULL::date AS BalancePaidDate,
    NULL::text AS PaidTo,
    NULL::text AS Remarks,
    0::numeric AS BillAmount,
    0::numeric AS Margin,
    NULL::numeric AS ImportedBalance,
    NULL::numeric AS ImportedDue,
    false AS PreserveImportedBilling,
    NULL::integer AS SourcePurchaseId
FROM filtered
ORDER BY id DESC, LRNumber DESC
LIMIT @pageSize OFFSET @offset;

SELECT COUNT(*) FROM filtered;";

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await using var multi = await conn.QueryMultipleAsync(sql, new
        {
            hasSearch,
            searchPattern,
            pageSize,
            offset
        });

        var items = (await multi.ReadAsync<ChallanEntry>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<ChallanEntry>
        {
            Items = items,
            TotalCount = total
        };
    }

    public async Task<PagedResult<ChallanEntry>> GetChallansPageAsync(
        int page,
        int pageSize,
        string? search,
        string? sortColumn,
        bool sortAscending,
        string? challanNo = null,
        string? lrNo = null,
        string? from = null,
        string? to = null,
        bool useLhsDerived = false,
        string? ledgerKind = null)
    {
        var challanLedgerMode = IsChallanLedgerKind(ledgerKind);
        var tableName = GetChallanTableName(challanLedgerMode);
        page = NormalizePage(page);
        pageSize = NormalizePageSize(pageSize);
        var offset = (page - 1) * pageSize;
        var whereParts = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("limit", pageSize);
        parameters.Add("offset", offset);

        var q = (search ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(q))
        {
            whereParts.Add(@"(
                challan_number ILIKE @search OR lr_number ILIKE @search OR vehicle_number ILIKE @search OR
                vehicle_type ILIKE @search OR driver_name ILIKE @search OR driver_mobile ILIKE @search OR broker_name ILIKE @search OR
                from_location ILIKE @search OR to_location ILIKE @search OR owner_name ILIKE @search OR
                engine_no ILIKE @search OR licence_no ILIKE @search OR policy_no ILIKE @search OR chassis_no ILIKE @search OR
                pan ILIKE @search OR paid_to ILIKE @search OR remarks ILIKE @search)");
            parameters.Add("search", $"%{q}%");
        }

        AddLikeFilter(whereParts, parameters, "challan_number", "challanNo", challanNo);
        AddLikeFilter(whereParts, parameters, "lr_number", "lrNo", lrNo);
        AddLikeFilter(whereParts, parameters, "from_location", "from", from);
        AddLikeFilter(whereParts, parameters, "to_location", "to", to);

        var where = whereParts.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", whereParts);
        var orderBy = BuildChallanOrderBy(sortColumn, sortAscending, useLhsDerived);
        var select = GetChallanSelectSql(challanLedgerMode);

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {tableName} {where};", parameters);
        var items = (await conn.QueryAsync<ChallanEntry>($"{select} {where} ORDER BY {orderBy} LIMIT @limit OFFSET @offset;", parameters)).ToList();
        return new PagedResult<ChallanEntry> { Items = items, TotalCount = total };
    }

    public async Task<ChallanLedgerPageResult> GetChallanLedgerPageAsync(
        int page,
        int pageSize,
        string? search,
        string? sortColumn,
        bool sortAscending,
        string? challanNo = null,
        string? lrNo = null,
        string? from = null,
        string? to = null,
        bool useLhsDerived = false,
        string? ledgerKind = null)
    {
        var challanLedgerMode = IsChallanLedgerKind(ledgerKind);
        var tableName = GetChallanTableName(challanLedgerMode);
        page = NormalizePage(page);
        pageSize = NormalizePageSize(pageSize);
        var offset = (page - 1) * pageSize;
        var whereParts = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("limit", pageSize);
        parameters.Add("offset", offset);

        var q = (search ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(q))
        {
            whereParts.Add(@"(
                challan_number ILIKE @search OR lr_number ILIKE @search OR vehicle_number ILIKE @search OR
                vehicle_type ILIKE @search OR driver_name ILIKE @search OR driver_mobile ILIKE @search OR broker_name ILIKE @search OR
                from_location ILIKE @search OR to_location ILIKE @search OR owner_name ILIKE @search OR
                engine_no ILIKE @search OR licence_no ILIKE @search OR policy_no ILIKE @search OR chassis_no ILIKE @search OR
                pan ILIKE @search OR paid_to ILIKE @search OR remarks ILIKE @search)");
            parameters.Add("search", $"%{q}%");
        }

        AddLikeFilter(whereParts, parameters, "challan_number", "challanNo", challanNo);
        AddLikeFilter(whereParts, parameters, "lr_number", "lrNo", lrNo);
        AddLikeFilter(whereParts, parameters, "from_location", "from", from);
        AddLikeFilter(whereParts, parameters, "to_location", "to", to);

        var where = whereParts.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", whereParts);
        var orderBy = BuildChallanOrderBy(sortColumn, sortAscending, useLhsDerived);
        var select = GetChallanSelectSql(challanLedgerMode);
        var dueSql = useLhsDerived
            ? $@"SELECT COALESCE(SUM(
                ((lorry_hire + other_amount) - less_tds - advance_amount + detention + hamali + deduction) - balance_paid_neft - balance_paid_cash
            ), 0) FROM {tableName} {where};"
            : $@"SELECT COALESCE(SUM(
                (lorry_hire - less_tds - advance_amount + detention + hamali + deduction) - balance_paid_neft - balance_paid_cash
            ), 0) FROM {tableName} {where};";

        await using var conn = _factory.Create();
        await conn.OpenAsync();

        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {tableName} {where};", parameters);
        var due = await conn.ExecuteScalarAsync<decimal>(dueSql, parameters);
        var items = (await conn.QueryAsync<ChallanEntry>($"{select} {where} ORDER BY {orderBy} LIMIT @limit OFFSET @offset;", parameters)).ToList();

        var commentIds = new List<int>();
        var pageIds = items.Select(x => x.Id).Distinct().ToArray();
        if (pageIds.Length > 0)
        {
            commentIds = (await conn.QueryAsync<int>(
                "SELECT DISTINCT challan_id FROM challan_comments WHERE challan_id = ANY(@ids);",
                new { ids = pageIds })).ToList();
        }

        return new ChallanLedgerPageResult
        {
            TotalCount = total,
            TotalDue = due,
            CommentIds = commentIds,
            Items = items
        };
    }

    public async Task<ChallanSummaryResult> GetChallansSummaryAsync(
        string? search,
        string? challanNo = null,
        string? lrNo = null,
        string? from = null,
        string? to = null,
        bool useLhsDerived = false,
        string? ledgerKind = null)
    {
        var challanLedgerMode = IsChallanLedgerKind(ledgerKind);
        var tableName = GetChallanTableName(challanLedgerMode);
        var whereParts = new List<string>();
        var parameters = new DynamicParameters();

        var q = (search ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(q))
        {
            whereParts.Add(@"(
                challan_number ILIKE @search OR lr_number ILIKE @search OR vehicle_number ILIKE @search OR
                vehicle_type ILIKE @search OR driver_name ILIKE @search OR driver_mobile ILIKE @search OR broker_name ILIKE @search OR
                from_location ILIKE @search OR to_location ILIKE @search OR owner_name ILIKE @search OR
                engine_no ILIKE @search OR licence_no ILIKE @search OR policy_no ILIKE @search OR chassis_no ILIKE @search OR
                pan ILIKE @search OR paid_to ILIKE @search OR remarks ILIKE @search)");
            parameters.Add("search", $"%{q}%");
        }

        AddLikeFilter(whereParts, parameters, "challan_number", "challanNo", challanNo);
        AddLikeFilter(whereParts, parameters, "lr_number", "lrNo", lrNo);
        AddLikeFilter(whereParts, parameters, "from_location", "from", from);
        AddLikeFilter(whereParts, parameters, "to_location", "to", to);

        var where = whereParts.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", whereParts);

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {tableName} {where};", parameters);
        var due = await conn.ExecuteScalarAsync<decimal>(useLhsDerived
            ? $@"SELECT COALESCE(SUM(
                ((lorry_hire + other_amount) - less_tds - advance_amount + detention + hamali + deduction) - balance_paid_neft - balance_paid_cash
            ), 0) FROM {tableName} {where};"
            : $@"SELECT COALESCE(SUM(
                (lorry_hire - less_tds - advance_amount + detention + hamali + deduction) - balance_paid_neft - balance_paid_cash
            ), 0) FROM {tableName} {where};", parameters);
        return new ChallanSummaryResult { TotalCount = total, TotalDue = due };
    }

    public Task<int> GetMaxChallanSrAsync(string? ledgerKind = null) =>
        ExecuteScalarIntAsync($"SELECT COALESCE(MAX(sr), 0) FROM {GetChallanTableName(IsChallanLedgerKind(ledgerKind))};", new { });

    public Task<ChallanEntry?> GetChallanAsync(int id, string? ledgerKind = null)
    {
        var challanLedgerMode = IsChallanLedgerKind(ledgerKind);
        return QuerySingleOrDefaultAsync<ChallanEntry>($"{GetChallanSelectSql(challanLedgerMode)} WHERE id = @id;", new { id });
    }

    public async Task<int> UpsertChallanAsync(ChallanEntry entry, string? ledgerKind = null, bool skipCbsSync = false)
    {
        entry.ChallanNumber = ChallanNumberFormatter.Normalize(entry.ChallanNumber, entry.Date);
        var challanLedgerMode = IsChallanLedgerKind(ledgerKind);
        var tableName = GetChallanTableName(challanLedgerMode);
        NormalizeChallanImportedBilling(entry, challanLedgerMode);
        var sql = entry.Id <= 0
            ? challanLedgerMode
                ? $@"INSERT INTO {tableName} (
                        source_purchase_id, sr, challan_number, date, lr_number, broker_name, from_location, to_location, vehicle_number, vehicle_type,
                        driver_name, driver_mobile, engine_no, licence_no, policy_no, chassis_no, owner_name, pan,
                        lorry_hire, less_tds, advance_amount, advance_neft, advance_cash, advance_date, detention, hamali,
                        other_amount, deduction, balance_paid_neft, balance_paid_cash, balance_paid_date, paid_to, remarks, bill_amount, margin,
                        imported_balance, imported_due, preserve_imported_billing)
                    VALUES (
                        @SourcePurchaseId, @Sr, @ChallanNumber, @Date, @LRNumber, @BrokerName, @From, @To, @VehicleNumber, @VehicleType,
                        @DriverName, @DriverMobile, @EngineNo, @LicenceNo, @PolicyNo, @ChassisNo, @OwnerName, @PAN,
                        @LorryHire, @LessTDS, @AdvanceAmount, @AdvanceNEFT, @AdvanceCash, @AdvanceDate, @Detention, @Hamali,
                        @Other, @Deduction, @BalancePaidNEFT, @BalancePaidCash, @BalancePaidDate, @PaidTo, @Remarks, @BillAmount, @Margin,
                        @ImportedBalance, @ImportedDue, @PreserveImportedBilling)
                    RETURNING id;"
                : $@"INSERT INTO {tableName} (
                        sr, challan_number, date, lr_number, broker_name, from_location, to_location, vehicle_number, vehicle_type,
                        driver_name, driver_mobile, engine_no, licence_no, policy_no, chassis_no, owner_name, pan,
                        lorry_hire, less_tds, advance_amount, advance_neft, advance_cash, advance_date, detention, hamali,
                        other_amount, deduction, balance_paid_neft, balance_paid_cash, balance_paid_date, paid_to, remarks, bill_amount, margin,
                        imported_balance, imported_due, preserve_imported_billing)
                    VALUES (
                        @Sr, @ChallanNumber, @Date, @LRNumber, @BrokerName, @From, @To, @VehicleNumber, @VehicleType,
                        @DriverName, @DriverMobile, @EngineNo, @LicenceNo, @PolicyNo, @ChassisNo, @OwnerName, @PAN,
                        @LorryHire, @LessTDS, @AdvanceAmount, @AdvanceNEFT, @AdvanceCash, @AdvanceDate, @Detention, @Hamali,
                        @Other, @Deduction, @BalancePaidNEFT, @BalancePaidCash, @BalancePaidDate, @PaidTo, @Remarks, @BillAmount, @Margin,
                        @ImportedBalance, @ImportedDue, @PreserveImportedBilling)
                    RETURNING id;"
            : challanLedgerMode
                ? $@"UPDATE {tableName} SET
                        source_purchase_id = @SourcePurchaseId, sr = @Sr, challan_number = @ChallanNumber, date = @Date, lr_number = @LRNumber, broker_name = @BrokerName,
                        from_location = @From, to_location = @To, vehicle_number = @VehicleNumber, vehicle_type = @VehicleType,
                        driver_name = @DriverName, driver_mobile = @DriverMobile, engine_no = @EngineNo, licence_no = @LicenceNo,
                        policy_no = @PolicyNo, chassis_no = @ChassisNo, owner_name = @OwnerName, pan = @PAN, lorry_hire = @LorryHire,
                        less_tds = @LessTDS, advance_amount = @AdvanceAmount, advance_neft = @AdvanceNEFT, advance_cash = @AdvanceCash,
                        advance_date = @AdvanceDate, detention = @Detention, hamali = @Hamali, other_amount = @Other, deduction = @Deduction,
                        balance_paid_neft = @BalancePaidNEFT, balance_paid_cash = @BalancePaidCash, balance_paid_date = @BalancePaidDate,
                        paid_to = @PaidTo, remarks = @Remarks, bill_amount = @BillAmount, margin = @Margin,
                        imported_balance = @ImportedBalance, imported_due = @ImportedDue, preserve_imported_billing = @PreserveImportedBilling
                    WHERE id = @Id;
                   SELECT @Id;"
                : $@"UPDATE {tableName} SET
                        sr = @Sr, challan_number = @ChallanNumber, date = @Date, lr_number = @LRNumber, broker_name = @BrokerName,
                        from_location = @From, to_location = @To, vehicle_number = @VehicleNumber, vehicle_type = @VehicleType,
                        driver_name = @DriverName, driver_mobile = @DriverMobile, engine_no = @EngineNo, licence_no = @LicenceNo,
                        policy_no = @PolicyNo, chassis_no = @ChassisNo, owner_name = @OwnerName, pan = @PAN, lorry_hire = @LorryHire,
                        less_tds = @LessTDS, advance_amount = @AdvanceAmount, advance_neft = @AdvanceNEFT, advance_cash = @AdvanceCash,
                        advance_date = @AdvanceDate, detention = @Detention, hamali = @Hamali, other_amount = @Other, deduction = @Deduction,
                        balance_paid_neft = @BalancePaidNEFT, balance_paid_cash = @BalancePaidCash, balance_paid_date = @BalancePaidDate,
                        paid_to = @PaidTo, remarks = @Remarks, bill_amount = @BillAmount, margin = @Margin,
                        imported_balance = @ImportedBalance, imported_due = @ImportedDue, preserve_imported_billing = @PreserveImportedBilling
                    WHERE id = @Id;
                   SELECT @Id;";
        var id = await ExecuteScalarIntAsync(sql, entry);
        entry.Id = id;
        if (challanLedgerMode)
        {
            if (!skipCbsSync)
            {
                await SyncChallanCBSFromChallanAsync(entry, "Challan LHS");
            }
            MarkLhsDirty("Challan LHS");
        }
        else
        {
            if (!skipCbsSync)
            {
                await SyncChallanCBSFromChallanAsync(entry, "Purchase LHS");
            }
            await SyncRemoteChallanMirrorAsync(entry);
            MarkLhsDirty("Purchase LHS");
        }
        await SyncVehicleLedgerFromChallanAsync(entry);
        return id;
    }

    private static void NormalizeChallanImportedBilling(ChallanEntry entry, bool challanLedgerMode)
    {
        if (entry == null)
        {
            return;
        }

        var computedBalance = challanLedgerMode
            ? (entry.LHS - entry.LessTDS - entry.AdvanceAmount)
            : (entry.LorryHire - entry.LessTDS - entry.AdvanceAmount);
        var computedDue = (computedBalance + entry.Detention + entry.Hamali + entry.Deduction)
            - entry.BalancePaidNEFT
            - entry.BalancePaidCash;

        if (entry.PreserveImportedBilling)
        {
            entry.ImportedBalance ??= computedBalance;
            entry.ImportedDue ??= computedDue;
            return;
        }

        entry.ImportedBalance = computedBalance;
        entry.ImportedDue = computedDue;
    }

    public async Task<int> DeleteChallanAsync(int id, string? ledgerKind = null)
    {
        var challanLedgerMode = IsChallanLedgerKind(ledgerKind);
        var tableName = GetChallanTableName(challanLedgerMode);
        var challan = await GetChallanAsync(id, ledgerKind);
        var affected = await ExecuteAsync($"DELETE FROM {tableName} WHERE id = @id;", new { id });
        if (challanLedgerMode && challan != null && !string.IsNullOrWhiteSpace(challan.ChallanNumber))
        {
            var challanNo = challan.ChallanNumber.Trim();
            await DeleteAutoCBSRowsAsync("Challan LHS", $"Challan {challanNo} - Advance Paid", "Auto from Challan");
            await DeleteAutoCBSRowsAsync("Challan LHS", $"Challan {challanNo} - Balance Paid", "Auto from Challan");
            MarkLhsDirty("Challan LHS");
        }
        else if (!challanLedgerMode && challan != null)
        {
            if (!string.IsNullOrWhiteSpace(challan.ChallanNumber))
            {
                var challanNo = challan.ChallanNumber.Trim();
                await DeleteAutoCBSRowsAsync("Purchase LHS", $"Challan {challanNo} - Advance Paid", "Auto from Challan");
                await DeleteAutoCBSRowsAsync("Purchase LHS", $"Challan {challanNo} - Balance Paid", "Auto from Challan");
            }
            await ExecuteAsync(@"DELETE FROM challan_ledger_entries
WHERE source_purchase_id = @SourcePurchaseId
   OR lower(trim(challan_number)) = lower(trim(@ChallanNumber));",
                new { SourcePurchaseId = challan.Id, ChallanNumber = challan.ChallanNumber ?? string.Empty });
            MarkLhsDirty("Purchase LHS");
        }

        return affected;
    }

    private async Task SyncRemoteChallanMirrorAsync(ChallanEntry purchaseEntry)
    {
        if (purchaseEntry == null)
        {
            return;
        }

        var existingMirror = await QuerySingleOrDefaultAsync<ChallanEntry>(
            GetChallanSelectSql(true) + @"
 WHERE (source_purchase_id IS NOT NULL AND source_purchase_id = @SourcePurchaseId)
    OR lower(trim(challan_number)) = lower(trim(@ChallanNumber))
 ORDER BY CASE WHEN source_purchase_id = @SourcePurchaseId THEN 0 ELSE 1 END, id
 LIMIT 1;",
            new
            {
                SourcePurchaseId = purchaseEntry.Id,
                ChallanNumber = purchaseEntry.ChallanNumber ?? string.Empty
            });

        var mirror = CreateRemoteMirrorFromPurchase(purchaseEntry, existingMirror);

        mirror.SourcePurchaseId = purchaseEntry.Id;
        await UpsertChallanAsync(mirror, "challan", skipCbsSync: true);
    }

    private static ChallanEntry CreateRemoteMirrorFromPurchase(ChallanEntry purchaseEntry, ChallanEntry? existingMirror)
    {
        return new ChallanEntry
        {
            Id = existingMirror?.Id ?? 0,
            SourcePurchaseId = purchaseEntry.Id,
            Sr = purchaseEntry.Sr,
            ChallanNumber = purchaseEntry.ChallanNumber,
            Date = purchaseEntry.Date,
            LRNumber = purchaseEntry.LRNumber,
            BrokerName = purchaseEntry.BrokerName,
            From = purchaseEntry.From,
            To = purchaseEntry.To,
            VehicleNumber = purchaseEntry.VehicleNumber,
            VehicleType = purchaseEntry.VehicleType,
            DriverName = purchaseEntry.DriverName,
            DriverMobile = purchaseEntry.DriverMobile,
            EngineNo = purchaseEntry.EngineNo,
            LicenceNo = purchaseEntry.LicenceNo,
            PolicyNo = purchaseEntry.PolicyNo,
            ChassisNo = purchaseEntry.ChassisNo,
            OwnerName = purchaseEntry.OwnerName,
            PAN = purchaseEntry.PAN,
            LorryHire = purchaseEntry.LorryHire,
            LessTDS = existingMirror?.LessTDS ?? purchaseEntry.LessTDS,
            AdvanceAmount = existingMirror?.AdvanceAmount ?? purchaseEntry.AdvanceAmount,
            AdvanceNEFT = existingMirror?.AdvanceNEFT ?? purchaseEntry.AdvanceNEFT,
            AdvanceCash = existingMirror?.AdvanceCash ?? purchaseEntry.AdvanceCash,
            AdvanceDate = existingMirror?.AdvanceDate ?? purchaseEntry.AdvanceDate,
            Detention = existingMirror?.Detention ?? purchaseEntry.Detention,
            Hamali = existingMirror?.Hamali ?? purchaseEntry.Hamali,
            Other = existingMirror?.Other ?? purchaseEntry.Other,
            Deduction = existingMirror?.Deduction ?? purchaseEntry.Deduction,
            BalancePaidNEFT = existingMirror?.BalancePaidNEFT ?? purchaseEntry.BalancePaidNEFT,
            BalancePaidCash = existingMirror?.BalancePaidCash ?? purchaseEntry.BalancePaidCash,
            BalancePaidDate = existingMirror?.BalancePaidDate ?? purchaseEntry.BalancePaidDate,
            PaidTo = existingMirror?.PaidTo ?? purchaseEntry.PaidTo,
            Remarks = existingMirror?.Remarks ?? purchaseEntry.Remarks,
            BillAmount = purchaseEntry.BillAmount,
            Margin = purchaseEntry.Margin,
            ImportedBalance = existingMirror?.ImportedBalance ?? purchaseEntry.ImportedBalance,
            ImportedDue = existingMirror?.ImportedDue ?? purchaseEntry.ImportedDue,
            PreserveImportedBilling = existingMirror?.PreserveImportedBilling ?? purchaseEntry.PreserveImportedBilling
        };
    }

    public Task<IEnumerable<LREntry>> GetLREntriesAsync() =>
        QueryAsync<LREntry>(@"SELECT
            id, sr, lrno AS LRNo, date, consignor_name AS ConsignorName, consignor_address AS ConsignorAddress, consignor_gst AS ConsignorGST,
            consignee_name AS ConsigneeName, consignee_address AS ConsigneeAddress, consignee_gst AS ConsigneeGST,
            from_location AS ""From"", to_location AS ""To"", vehicle_no AS VehicleNo, vehicle_type AS VehicleType,
            weight AS Weight, size_l AS SizeL, size_w AS SizeW, size_h AS SizeH, actual_weight AS ActualWeight, charged_weight AS ChargedWeight,
            pkg AS PKG, pkg_type AS PkgType, description AS Description, invoice AS Invoice, value AS Value, chno AS CHNo,
            total_freight AS TotalFreight, hamali AS Hamali, detention AS Detention, others AS Others, st_charge AS StCharge,
            neft AS NEFT, cash AS CASH, tds AS TDS, ded AS Ded, bill_no AS BillNo, bill_date AS BillDate, bill AS BILL,
            challan_lorry_hire AS ChallanLorryHire,
            bill_party AS BillParty, broker AS Broker, frt_type AS FrtType, pay_type AS PayType, comm AS Comm, paid AS Paid,
            preserve_imported_billing AS PreserveImportedBilling
            FROM lr_entries ORDER BY sr, id;");

    public async Task<PagedResult<LREntry>> GetLREntriesPageAsync(int page, int pageSize, string? search, string? sortColumn, bool sortAscending)
    {
        page = NormalizePage(page);
        pageSize = NormalizePageSize(pageSize);
        var offset = (page - 1) * pageSize;
        var parameters = new DynamicParameters();
        parameters.Add("limit", pageSize);
        parameters.Add("offset", offset);
        var where = string.Empty;
        var q = (search ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(q))
        {
            where = @"WHERE lrno ILIKE @search OR consignor_name ILIKE @search OR consignor_address ILIKE @search OR consignor_gst ILIKE @search OR
                consignee_name ILIKE @search OR consignee_address ILIKE @search OR consignee_gst ILIKE @search OR
                from_location ILIKE @search OR to_location ILIKE @search OR
                vehicle_no ILIKE @search OR vehicle_type ILIKE @search OR
                pkg_type ILIKE @search OR description ILIKE @search OR invoice ILIKE @search OR value ILIKE @search OR
                bill_no ILIKE @search OR bill_party ILIKE @search OR broker ILIKE @search OR frt_type ILIKE @search OR pay_type ILIKE @search OR paid ILIKE @search OR
                chno ILIKE @search";
            parameters.Add("search", $"%{q}%");
        }

        var orderBy = BuildLROrderBy(sortColumn, sortAscending);
        var select = @"SELECT
            id, sr, lrno AS LRNo, date, consignor_name AS ConsignorName, consignor_address AS ConsignorAddress, consignor_gst AS ConsignorGST,
            consignee_name AS ConsigneeName, consignee_address AS ConsigneeAddress, consignee_gst AS ConsigneeGST,
            from_location AS ""From"", to_location AS ""To"", vehicle_no AS VehicleNo, vehicle_type AS VehicleType,
            weight AS Weight, size_l AS SizeL, size_w AS SizeW, size_h AS SizeH, actual_weight AS ActualWeight, charged_weight AS ChargedWeight,
            pkg AS PKG, pkg_type AS PkgType, description AS Description, invoice AS Invoice, value AS Value, chno AS CHNo,
            total_freight AS TotalFreight, hamali AS Hamali, detention AS Detention, others AS Others, st_charge AS StCharge,
            neft AS NEFT, cash AS CASH, tds AS TDS, ded AS Ded, bill_no AS BillNo, bill_date AS BillDate, bill AS BILL,
            challan_lorry_hire AS ChallanLorryHire,
            bill_party AS BillParty, broker AS Broker, frt_type AS FrtType, pay_type AS PayType, comm AS Comm, paid AS Paid,
            preserve_imported_billing AS PreserveImportedBilling
            FROM lr_entries";

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM lr_entries {where};", parameters);
        var items = (await conn.QueryAsync<LREntry>($"{select} {where} ORDER BY {orderBy} LIMIT @limit OFFSET @offset;", parameters)).ToList();
        return new PagedResult<LREntry> { Items = items, TotalCount = total };
    }

    public async Task<PagedResult<LREntry>> GetPendingBillLREntriesPageAsync(int page, int pageSize, string? search)
    {
        page = NormalizePage(page);
        pageSize = NormalizePageSize(pageSize);
        var offset = (page - 1) * pageSize;
        var parameters = new DynamicParameters();
        parameters.Add("limit", pageSize);
        parameters.Add("offset", offset);
        var q = (search ?? string.Empty).Trim();

        var pendingWhere = @"
WHERE NULLIF(TRIM(COALESCE(lr.lrno, '')), '') IS NOT NULL
  AND NULLIF(TRIM(COALESCE(lr.bill_no, '')), '') IS NULL
  AND NOT EXISTS (
      SELECT 1
      FROM bills b
      WHERE lower(trim(COALESCE(b.lr_no, ''))) = lower(trim(COALESCE(lr.lrno, '')))
        AND NULLIF(TRIM(COALESCE(b.bill_no, '')), '') IS NOT NULL
  )";

        if (!string.IsNullOrWhiteSpace(q))
        {
            pendingWhere += @"
  AND (
      lr.lrno ILIKE @search OR
      lr.consignor_name ILIKE @search OR
      lr.bill_party ILIKE @search OR
      lr.from_location ILIKE @search OR
      lr.to_location ILIKE @search OR
      lr.vehicle_no ILIKE @search OR
      lr.chno ILIKE @search
  )";
            parameters.Add("search", $"%{q}%");
        }

        const string select = @"SELECT
            lr.id, lr.sr, lr.lrno AS LRNo, lr.date, lr.consignor_name AS ConsignorName, lr.consignor_address AS ConsignorAddress, lr.consignor_gst AS ConsignorGST,
            lr.consignee_name AS ConsigneeName, lr.consignee_address AS ConsigneeAddress, lr.consignee_gst AS ConsigneeGST,
            lr.from_location AS ""From"", lr.to_location AS ""To"", lr.vehicle_no AS VehicleNo, lr.vehicle_type AS VehicleType,
            lr.weight AS Weight, lr.size_l AS SizeL, lr.size_w AS SizeW, lr.size_h AS SizeH, lr.actual_weight AS ActualWeight, lr.charged_weight AS ChargedWeight,
            lr.pkg AS PKG, lr.pkg_type AS PkgType, lr.description AS Description, lr.invoice AS Invoice, lr.value AS Value, lr.chno AS CHNo,
            lr.total_freight AS TotalFreight, lr.hamali AS Hamali, lr.detention AS Detention, lr.others AS Others, lr.st_charge AS StCharge,
            lr.neft AS NEFT, lr.cash AS CASH, lr.tds AS TDS, lr.ded AS Ded, lr.bill_no AS BillNo, lr.bill_date AS BillDate, lr.bill AS BILL,
            lr.challan_lorry_hire AS ChallanLorryHire,
            lr.bill_party AS BillParty, lr.broker AS Broker, lr.frt_type AS FrtType, lr.pay_type AS PayType, lr.comm AS Comm, lr.paid AS Paid,
            lr.preserve_imported_billing AS PreserveImportedBilling
            FROM lr_entries lr";

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM lr_entries lr {pendingWhere};", parameters);
        var items = (await conn.QueryAsync<LREntry>($"{select} {pendingWhere} ORDER BY lr.id DESC LIMIT @limit OFFSET @offset;", parameters)).ToList();
        return new PagedResult<LREntry> { Items = items, TotalCount = total };
    }

    public async Task<LRSummaryResult> GetLREntriesSummaryAsync(string? search)
    {
        var parameters = new DynamicParameters();
        var where = string.Empty;
        var q = (search ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(q))
        {
            where = @"WHERE lrno ILIKE @search OR consignor_name ILIKE @search OR consignor_address ILIKE @search OR consignor_gst ILIKE @search OR
                consignee_name ILIKE @search OR consignee_address ILIKE @search OR consignee_gst ILIKE @search OR
                from_location ILIKE @search OR to_location ILIKE @search OR
                vehicle_no ILIKE @search OR vehicle_type ILIKE @search OR
                pkg_type ILIKE @search OR description ILIKE @search OR invoice ILIKE @search OR value ILIKE @search OR
                bill_no ILIKE @search OR bill_party ILIKE @search OR broker ILIKE @search OR frt_type ILIKE @search OR pay_type ILIKE @search OR paid ILIKE @search OR
                chno ILIKE @search";
            parameters.Add("search", $"%{q}%");
        }

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM lr_entries {where};", parameters);
        var freight = await conn.ExecuteScalarAsync<decimal>($"SELECT COALESCE(SUM(total_freight), 0) FROM lr_entries {where};", parameters);
        var balance = await conn.ExecuteScalarAsync<decimal>($"SELECT COALESCE(SUM((neft + cash - tds + ded)), 0) FROM lr_entries {where};", parameters);
        return new LRSummaryResult { TotalCount = total, TotalFreight = freight, TotalBalance = balance };
    }

    public Task<LREntry?> GetLREntryAsync(int id) =>
        QuerySingleOrDefaultAsync<LREntry>(@"SELECT
            id, sr, lrno AS LRNo, date, consignor_name AS ConsignorName, consignor_address AS ConsignorAddress, consignor_gst AS ConsignorGST,
            consignee_name AS ConsigneeName, consignee_address AS ConsigneeAddress, consignee_gst AS ConsigneeGST,
            from_location AS ""From"", to_location AS ""To"", vehicle_no AS VehicleNo, vehicle_type AS VehicleType,
            weight AS Weight, size_l AS SizeL, size_w AS SizeW, size_h AS SizeH, actual_weight AS ActualWeight, charged_weight AS ChargedWeight,
            pkg AS PKG, pkg_type AS PkgType, description AS Description, invoice AS Invoice, value AS Value, chno AS CHNo,
            total_freight AS TotalFreight, hamali AS Hamali, detention AS Detention, others AS Others, st_charge AS StCharge,
            neft AS NEFT, cash AS CASH, tds AS TDS, ded AS Ded, bill_no AS BillNo, bill_date AS BillDate, bill AS BILL,
            challan_lorry_hire AS ChallanLorryHire,
            bill_party AS BillParty, broker AS Broker, frt_type AS FrtType, pay_type AS PayType, comm AS Comm, paid AS Paid,
            preserve_imported_billing AS PreserveImportedBilling
            FROM lr_entries WHERE id = @id;", new { id });

    public async Task<CreateLrFromChallanResponse> CreateLREntryFromChallanAsync(int challanId, LREntry entry)
    {
        if (entry == null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        var savedId = await UpsertLREntryAsync(entry);
        entry.Id = savedId;

        await EnsurePartyExistsAsync(entry.ConsignorName, entry.ConsignorAddress, entry.ConsignorGST);
        await EnsurePartyExistsAsync(entry.ConsigneeName, entry.ConsigneeAddress, entry.ConsigneeGST);
        await EnsurePartyExistsAsync(entry.BillParty, string.Empty, string.Empty);

        ChallanEntry? linkedChallan = null;
        if (challanId > 0)
        {
            linkedChallan = await GetChallanAsync(challanId, "purchase");
        }

        if (linkedChallan == null && !string.IsNullOrWhiteSpace(entry.CHNo))
        {
            linkedChallan = await QuerySingleOrDefaultAsync<ChallanEntry>(
                GetChallanSelectSql(false) + @"
WHERE lower(trim(challan_number)) = lower(trim(@challanNumber))
LIMIT 1;",
                new { challanNumber = entry.CHNo.Trim() });
        }

        if (linkedChallan != null && !string.IsNullOrWhiteSpace(entry.LRNo))
        {
            var existing = SplitLrNumbers(linkedChallan.LRNumber).ToList();
            var lrNo = entry.LRNo.Trim();
            if (!existing.Contains(lrNo, StringComparer.OrdinalIgnoreCase))
            {
                existing.Add(lrNo);
                linkedChallan.LRNumber = string.Join(", ", existing);
                await UpsertChallanAsync(linkedChallan, "purchase", skipCbsSync: true);
            }
        }

        return new CreateLrFromChallanResponse
        {
            Entry = entry,
            LinkedChallan = linkedChallan
        };
    }

    private static IEnumerable<string> SplitLrNumbers(string? raw)
    {
        return (raw ?? string.Empty)
            .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IEnumerable<LREntry>> GetLREntriesByNumbersAsync(IEnumerable<string>? lrNumbers)
    {
        var keys = (lrNumbers ?? Enumerable.Empty<string>())
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (keys.Length == 0)
        {
            return Array.Empty<LREntry>();
        }

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        return await conn.QueryAsync<LREntry>(@"SELECT
            id, sr, lrno AS LRNo, date, consignor_name AS ConsignorName, consignor_address AS ConsignorAddress, consignor_gst AS ConsignorGST,
            consignee_name AS ConsigneeName, consignee_address AS ConsigneeAddress, consignee_gst AS ConsigneeGST,
            from_location AS ""From"", to_location AS ""To"", vehicle_no AS VehicleNo, vehicle_type AS VehicleType,
            weight AS Weight, size_l AS SizeL, size_w AS SizeW, size_h AS SizeH, actual_weight AS ActualWeight, charged_weight AS ChargedWeight,
            pkg AS PKG, pkg_type AS PkgType, description AS Description, invoice AS Invoice, value AS Value, chno AS CHNo,
            total_freight AS TotalFreight, hamali AS Hamali, detention AS Detention, others AS Others, st_charge AS StCharge,
            neft AS NEFT, cash AS CASH, tds AS TDS, ded AS Ded, bill_no AS BillNo, bill_date AS BillDate, bill AS BILL,
            challan_lorry_hire AS ChallanLorryHire,
            bill_party AS BillParty, broker AS Broker, frt_type AS FrtType, pay_type AS PayType, comm AS Comm, paid AS Paid,
            preserve_imported_billing AS PreserveImportedBilling
            FROM lr_entries
            WHERE lower(btrim(COALESCE(lrno, ''))) = ANY(@keys)
            ORDER BY id;", new
        {
            keys = keys.Select(x => x.ToLowerInvariant()).ToArray()
        });
    }

    public async Task<bool> LRNumberExistsAsync(string? lrNo, int excludeId = 0)
    {
        var normalized = (lrNo ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(@"
SELECT COUNT(*)
FROM lr_entries
WHERE lower(btrim(COALESCE(lrno, ''))) = lower(btrim(@lrNo))
  AND (@excludeId <= 0 OR id <> @excludeId);", new { lrNo = normalized, excludeId }) > 0;
    }

    public async Task<int> UpsertLREntryAsync(LREntry entry)
    {
        var sql = entry.Id <= 0
            ? @"INSERT INTO lr_entries (
                    sr, lrno, date, consignor_name, consignor_address, consignor_gst, consignee_name, consignee_address, consignee_gst,
                    from_location, to_location, vehicle_no, vehicle_type, weight, size_l, size_w, size_h, actual_weight, charged_weight,
                    pkg, pkg_type, description, invoice, value, chno, total_freight, hamali, detention, others, st_charge, neft, cash,
                    tds, ded, bill_no, bill_date, bill, challan_lorry_hire, bill_party, broker, frt_type, pay_type, comm, paid, preserve_imported_billing)
                VALUES (
                    @Sr, @LRNo, @Date, @ConsignorName, @ConsignorAddress, @ConsignorGST, @ConsigneeName, @ConsigneeAddress, @ConsigneeGST,
                    @From, @To, @VehicleNo, @VehicleType, @Weight, @SizeL, @SizeW, @SizeH, @ActualWeight, @ChargedWeight,
                    @PKG, @PkgType, @Description, @Invoice, @Value, @CHNo, @TotalFreight, @Hamali, @Detention, @Others, @StCharge, @NEFT, @CASH,
                    @TDS, @Ded, @BillNo, @BillDate, @BILL, @ChallanLorryHire, @BillParty, @Broker, @FrtType, @PayType, @Comm, @Paid, @PreserveImportedBilling)
                RETURNING id;"
            : @"UPDATE lr_entries SET
                    sr = @Sr, lrno = @LRNo, date = @Date, consignor_name = @ConsignorName, consignor_address = @ConsignorAddress, consignor_gst = @ConsignorGST,
                    consignee_name = @ConsigneeName, consignee_address = @ConsigneeAddress, consignee_gst = @ConsigneeGST,
                    from_location = @From, to_location = @To, vehicle_no = @VehicleNo, vehicle_type = @VehicleType, weight = @Weight,
                    size_l = @SizeL, size_w = @SizeW, size_h = @SizeH, actual_weight = @ActualWeight, charged_weight = @ChargedWeight,
                    pkg = @PKG, pkg_type = @PkgType, description = @Description, invoice = @Invoice, value = @Value, chno = @CHNo,
                    total_freight = @TotalFreight, hamali = @Hamali, detention = @Detention, others = @Others, st_charge = @StCharge,
                    neft = @NEFT, cash = @CASH, tds = @TDS, ded = @Ded, bill_no = @BillNo, bill_date = @BillDate, bill = @BILL,
                    challan_lorry_hire = @ChallanLorryHire,
                    bill_party = @BillParty, broker = @Broker, frt_type = @FrtType, pay_type = @PayType, comm = @Comm, paid = @Paid,
                    preserve_imported_billing = @PreserveImportedBilling
                WHERE id = @Id;
               SELECT @Id;";
        return await ExecuteScalarIntAsync(sql, entry);
    }

    private async Task EnsurePartyExistsAsync(string? name, string? address, string? gstNo)
    {
        var partyName = (name ?? string.Empty).Trim();
        if (partyName.Length == 0)
        {
            return;
        }

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var existing = await conn.QuerySingleOrDefaultAsync<int?>(
            "SELECT id FROM parties WHERE lower(trim(party_name)) = lower(trim(@name)) LIMIT 1;",
            new { name = partyName });
        if (existing.HasValue)
        {
            return;
        }

        var nextSr = await conn.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(sr), 0) + 1 FROM parties;");
        await conn.ExecuteAsync(
            @"INSERT INTO parties (sr, party_name, address, gst_no)
              VALUES (@Sr, @PartyName, @Address, @GSTNo);",
            new
            {
                Sr = nextSr,
                PartyName = partyName,
                Address = (address ?? string.Empty).Trim(),
                GSTNo = (gstNo ?? string.Empty).Trim()
            });
    }

    public Task<int> DeleteLREntryAsync(int id) =>
        ExecuteAsync("DELETE FROM lr_entries WHERE id = @id;", new { id });

    public Task<IEnumerable<BillEntry>> GetBillsAsync() =>
        QueryAsync<BillEntry>(@"SELECT
            id, sr, bill_no AS BillNo, bill_date AS BillDate, party AS Party, lr_no AS LRNo, lr_date AS LRDate,
            from_loc AS ""From"", to_loc AS ""To"", vehicle_type AS VehicleType, freight AS Freight, detention AS Detention,
            hml AS HML, othr AS OTHR, st_charge AS StCharge, rcvd AS RCVD, tds AS TDS, ded AS DED, mop AS MOP, mr AS MR,
            remarks AS Remarks, date AS Date
            FROM bills ORDER BY bill_date DESC NULLS LAST, sr, id;");

    public async Task<PagedResult<BillEntry>> GetBillsPageAsync(int page, int pageSize, string? search, string? sortColumn, bool sortAscending, string? party = null, bool dueOnly = false)
    {
        page = NormalizePage(page);
        pageSize = NormalizePageSize(pageSize);
        var offset = (page - 1) * pageSize;
        var parameters = new DynamicParameters();
        parameters.Add("limit", pageSize);
        parameters.Add("offset", offset);
        var whereParts = new List<string>();
        var q = (search ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(q))
        {
            whereParts.Add(@"(
                bill_no ILIKE @search OR party ILIKE @search OR lr_no ILIKE @search OR
                from_loc ILIKE @search OR to_loc ILIKE @search OR vehicle_type ILIKE @search OR
                mop ILIKE @search OR mr ILIKE @search OR remarks ILIKE @search)");
            parameters.Add("search", $"%{q}%");
        }

        AddLikeFilter(whereParts, parameters, "party", "party", party);
        if (dueOnly)
        {
            whereParts.Add("(freight + detention + hml + othr + st_charge - rcvd - tds - ded) > 0");
        }

        var where = whereParts.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", whereParts);
        var orderBy = BuildBillOrderBy(sortColumn, sortAscending);
        var select = @"SELECT
            id, sr, bill_no AS BillNo, bill_date AS BillDate, party AS Party, lr_no AS LRNo, lr_date AS LRDate,
            from_loc AS ""From"", to_loc AS ""To"", vehicle_type AS VehicleType, freight AS Freight, detention AS Detention,
            hml AS HML, othr AS OTHR, st_charge AS StCharge, rcvd AS RCVD, tds AS TDS, ded AS DED, mop AS MOP, mr AS MR,
            remarks AS Remarks, date AS Date
            FROM bills";

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM bills {where};", parameters);
        var items = (await conn.QueryAsync<BillEntry>($"{select} {where} ORDER BY {orderBy} LIMIT @limit OFFSET @offset;", parameters)).ToList();
        return new PagedResult<BillEntry> { Items = items, TotalCount = total };
    }

    public async Task<BillSummaryResult> GetBillsSummaryAsync(string? search, string? party = null, bool dueOnly = false)
    {
        var parameters = new DynamicParameters();
        var whereParts = new List<string>();
        var q = (search ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(q))
        {
            whereParts.Add(@"(
                bill_no ILIKE @search OR party ILIKE @search OR lr_no ILIKE @search OR
                from_loc ILIKE @search OR to_loc ILIKE @search OR vehicle_type ILIKE @search OR
                mop ILIKE @search OR mr ILIKE @search OR remarks ILIKE @search)");
            parameters.Add("search", $"%{q}%");
        }

        AddLikeFilter(whereParts, parameters, "party", "party", party);
        if (dueOnly)
        {
            whereParts.Add("(freight + detention + hml + othr + st_charge - rcvd - tds - ded) > 0");
        }

        var where = whereParts.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", whereParts);
        await using var conn = _factory.Create();
        await conn.OpenAsync();
        const string dueExpr = "(freight + detention + hml + othr + st_charge - rcvd - tds - ded)";
        return await conn.QuerySingleAsync<BillSummaryResult>(
            $@"SELECT
                    COUNT(*)::int AS TotalCount,
                    COALESCE(SUM({dueExpr}), 0)::numeric AS TotalDue
               FROM bills
               {where};",
            parameters);
    }

    public async Task<BillPreviewResult?> GetBillPreviewAsync(string? billNo)
    {
        var key = (billNo ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            return null;
        }

        const string sql = @"
SELECT
    b.id,
    b.sr,
    b.bill_no AS BillNo,
    b.bill_date AS BillDate,
    b.party AS Party,
    b.lr_no AS LRNo,
    b.lr_date AS LRDate,
    b.from_loc AS ""From"",
    b.to_loc AS ""To"",
    b.vehicle_type AS VehicleType,
    b.freight AS Freight,
    b.detention AS Detention,
    b.hml AS HML,
    b.othr AS OTHR,
    b.st_charge AS StCharge,
    b.rcvd AS RCVD,
    b.tds AS TDS,
    b.ded AS DED,
    b.mop AS MOP,
    b.mr AS MR,
    b.remarks AS Remarks,
    b.date AS Date,
    COALESCE(l.invoice, '') AS Invoice,
    COALESCE(l.vehicle_no, '') AS VehicleNo,
    COALESCE(p.address, '') AS PartyAddress,
    COALESCE(p.gst_no, '') AS PartyGST
FROM bills b
LEFT JOIN lr_entries l
    ON lower(trim(COALESCE(l.lrno, ''))) = lower(trim(COALESCE(b.lr_no, '')))
LEFT JOIN parties p
    ON lower(trim(COALESCE(p.party_name, ''))) = lower(trim(COALESCE(b.party, '')))
WHERE lower(trim(COALESCE(b.bill_no, ''))) = lower(trim(@billNo))
ORDER BY b.sr ASC, b.id ASC;";

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var rows = (await conn.QueryAsync<BillPreviewSourceRow>(sql, new { billNo = key })).ToList();
        if (rows.Count == 0)
        {
            return null;
        }

        var first = rows[0];
        var billEntries = rows.Select(r => new BillEntry
        {
            Id = r.Id,
            Sr = r.Sr,
            BillNo = r.BillNo,
            BillDate = r.BillDate,
            Party = r.Party,
            LRNo = r.LRNo,
            LRDate = r.LRDate,
            From = r.From,
            To = r.To,
            VehicleType = r.VehicleType,
            Freight = r.Freight,
            Detention = r.Detention,
            HML = r.HML,
            OTHR = r.OTHR,
            StCharge = r.StCharge,
            RCVD = r.RCVD,
            TDS = r.TDS,
            DED = r.DED,
            MOP = r.MOP,
            MR = r.MR,
            Remarks = r.Remarks,
            Date = r.Date
        }).ToList();

        var invoiceByLr = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.LRNo))
            .GroupBy(r => r.LRNo.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Invoice ?? string.Empty).FirstOrDefault() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var vehicleByLr = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.LRNo))
            .GroupBy(r => r.LRNo.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.VehicleNo ?? string.Empty).FirstOrDefault() ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        var totalAmount = billEntries.Sum(x => x.Freight + x.Detention + x.HML + x.OTHR + x.StCharge);
        var partyGst = (first.PartyGST ?? string.Empty).Trim();
        var preview = new BillPreviewResult
        {
            Party = (first.Party ?? string.Empty).Trim(),
            PartyAddress = (first.PartyAddress ?? string.Empty).Trim(),
            PartyGST = partyGst,
            PartyStateCode = partyGst.Length >= 2 ? partyGst.Substring(0, 2) : string.Empty,
            BillNo = (first.BillNo ?? string.Empty).Trim(),
            BillDate = first.BillDate == default ? string.Empty : first.BillDate.ToString("dd-MMM-yyyy"),
            TotalAmount = totalAmount,
            Lines = BuildBillPreviewLines(billEntries, invoiceByLr, vehicleByLr)
        };

        return preview;
    }

    public async Task<IEnumerable<BillPartyDueSummaryItem>> GetBillPartyDueSummaryAsync()
    {
        const string dueExpr = "(b.freight + b.detention + b.hml + b.othr + b.st_charge - b.rcvd - b.tds - b.ded)";
        const string sql = $@"
SELECT
    TRIM(COALESCE(NULLIF(b.party, ''), NULLIF(l.bill_party, ''), NULLIF(l.consignor_name, ''), '')) AS Party,
    COUNT(DISTINCT NULLIF(TRIM(COALESCE(b.bill_no, '')), ''))::int AS Bills,
    COALESCE(SUM({dueExpr}), 0)::numeric AS Due
FROM bills b
LEFT JOIN lr_entries l
    ON lower(trim(COALESCE(l.lrno, ''))) = lower(trim(COALESCE(b.lr_no, '')))
WHERE NULLIF(TRIM(COALESCE(b.bill_no, '')), '') IS NOT NULL
  AND {dueExpr} > 0
GROUP BY TRIM(COALESCE(NULLIF(b.party, ''), NULLIF(l.bill_party, ''), NULLIF(l.consignor_name, ''), ''))
ORDER BY Due DESC, Party ASC;";
        return await QueryAsync<BillPartyDueSummaryItem>(sql);
    }

    private sealed class BillPreviewSourceRow
    {
        public int Id { get; set; }
        public int Sr { get; set; }
        public string BillNo { get; set; } = string.Empty;
        public DateTime BillDate { get; set; }
        public string Party { get; set; } = string.Empty;
        public string LRNo { get; set; } = string.Empty;
        public DateTime? LRDate { get; set; }
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string VehicleType { get; set; } = string.Empty;
        public decimal Freight { get; set; }
        public decimal Detention { get; set; }
        public decimal HML { get; set; }
        public decimal OTHR { get; set; }
        public decimal StCharge { get; set; }
        public decimal RCVD { get; set; }
        public decimal TDS { get; set; }
        public decimal DED { get; set; }
        public string MOP { get; set; } = string.Empty;
        public string MR { get; set; } = string.Empty;
        public string Remarks { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Invoice { get; set; } = string.Empty;
        public string VehicleNo { get; set; } = string.Empty;
        public string PartyAddress { get; set; } = string.Empty;
        public string PartyGST { get; set; } = string.Empty;
    }

    private static List<BillPreviewLineItem> BuildBillPreviewLines(List<BillEntry> billRows, IDictionary<string, string> invoiceByLr, IDictionary<string, string> vehicleByLr)
    {
        var output = new List<BillPreviewLineItem>();
        var rows = (billRows ?? new List<BillEntry>()).Where(x => (x.Freight + x.Detention + x.HML + x.OTHR + x.StCharge) != 0m).ToList();
        var totalStChargeForBill = rows.Sum(x => x.StCharge);
        foreach (var x in rows)
        {
            var lrNo = (x.LRNo ?? string.Empty).Trim();
            var lrDate = x.LRDate.HasValue ? x.LRDate.Value.ToString("dd.MM.yy") : string.Empty;
            var invoiceValues = SplitInvoiceValues(invoiceByLr.TryGetValue(lrNo, out var invoice) ? invoice : string.Empty);
            var vehicle = vehicleByLr.TryGetValue(lrNo, out var vehicleNo) ? vehicleNo : string.Empty;
            var from = (x.From ?? string.Empty).Trim();
            var to = (x.To ?? string.Empty).Trim();
            var weight = (x.VehicleType ?? string.Empty).Trim();
            var blockRows = new List<BillPreviewLineItem>();

            blockRows.Add(new BillPreviewLineItem
            {
                LRNo = lrNo,
                LRDate = lrDate,
                Invoice = string.Empty,
                Vehicle = vehicle,
                From = from,
                To = to,
                ChargesBreakdown = string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to) ? string.Empty : ((from ?? string.Empty) + "        " + (to ?? string.Empty)).Trim(),
                WeightOrType = weight,
                Rate = string.Empty,
                Amount = x.Freight != 0m ? x.Freight.ToString("0.##") : string.Empty
            });

            if (x.HML != 0m)
            {
                blockRows.Add(new BillPreviewLineItem { From = "Hamali", ChargesBreakdown = "Hamali", Amount = x.HML.ToString("0.##") });
            }
            if (x.Detention != 0m)
            {
                blockRows.Add(new BillPreviewLineItem { From = "Detention", ChargesBreakdown = "Detention", Amount = x.Detention.ToString("0.##") });
            }
            if (x.OTHR != 0m)
            {
                blockRows.Add(new BillPreviewLineItem { From = "Other", ChargesBreakdown = "Other", Amount = x.OTHR.ToString("0.##") });
            }

            var invoiceCellValues = PackInvoiceValuesForCells(invoiceValues);
            for (var invIndex = 0; invIndex < invoiceCellValues.Count; invIndex++)
            {
                if (invIndex < blockRows.Count)
                {
                    blockRows[invIndex].Invoice = invoiceCellValues[invIndex];
                }
                else
                {
                    blockRows.Add(new BillPreviewLineItem { Invoice = invoiceCellValues[invIndex] });
                }
            }

            output.AddRange(blockRows);
        }

        EnsureStChargeSummaryRow(output, totalStChargeForBill);
        NormalizeInvoiceCellsInPreviewLines(output);
        return output;
    }

    private static List<string> SplitInvoiceValues(string rawInvoice)
    {
        var raw = (rawInvoice ?? string.Empty).Trim();
        var result = new List<string>();
        if (raw.Length == 0) return result;
        var normalized = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
        {
            var parts = line.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                var single = (line ?? string.Empty).Trim();
                if (single.Length > 0) result.Add(single);
                continue;
            }

            for (var i = 0; i < parts.Length; i++)
            {
                var value = (parts[i] ?? string.Empty).Trim();
                if (value.Length > 0) result.Add(value);
            }
        }
        return result;
    }

    private static List<string> PackInvoiceValuesForCells(List<string> invoiceValues)
    {
        var values = (invoiceValues ?? new List<string>())
            .Select(v => (v ?? string.Empty).Trim())
            .Where(v => v.Length > 0)
            .ToList();
        var packed = new List<string>();
        if (values.Count == 0) return packed;

        const int maxCharsPerCell = 24;
        var current = string.Empty;
        for (var i = 0; i < values.Count; i++)
        {
            var next = values[i];
            var candidate = string.IsNullOrEmpty(current) ? next : (current + ", " + next);
            if (candidate.Length <= maxCharsPerCell || string.IsNullOrEmpty(current))
            {
                current = candidate;
                continue;
            }

            packed.Add(current);
            current = next;
        }

        if (!string.IsNullOrEmpty(current))
        {
            packed.Add(current);
        }

        return packed;
    }

    private static void EnsureStChargeSummaryRow(List<BillPreviewLineItem> lines, decimal totalStChargeForBill)
    {
        if (lines == null) return;

        lines.RemoveAll(x =>
            string.Equals((x?.ChargesBreakdown ?? string.Empty).Trim(), "St. Charge", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(x?.LRNo) &&
            string.IsNullOrWhiteSpace(x?.Vehicle));

        if (totalStChargeForBill == 0m) return;

        lines.Add(new BillPreviewLineItem
        {
            From = "St. Charge",
            ChargesBreakdown = "St. Charge",
            Amount = totalStChargeForBill.ToString("0.##")
        });
    }

    private static void NormalizeInvoiceCellsInPreviewLines(List<BillPreviewLineItem> lines)
    {
        if (lines == null || lines.Count == 0) return;

        var i = 0;
        while (i < lines.Count)
        {
            var lrNo = ((lines[i]?.LRNo) ?? string.Empty).Trim();
            if (lrNo.Length == 0)
            {
                i++;
                continue;
            }

            var groupStart = i;
            var groupEnd = lines.Count - 1;
            for (var j = i + 1; j < lines.Count; j++)
            {
                var nextLr = ((lines[j]?.LRNo) ?? string.Empty).Trim();
                if (nextLr.Length > 0)
                {
                    groupEnd = j - 1;
                    break;
                }
            }

            var values = new List<string>();
            for (var j = groupStart; j <= groupEnd; j++)
            {
                values.AddRange(SplitInvoiceValues(lines[j]?.Invoice));
            }

            var packed = PackInvoiceValuesForCells(values);
            for (var j = groupStart; j <= groupEnd; j++)
            {
                lines[j].Invoice = string.Empty;
            }

            var capacity = Math.Max(0, groupEnd - groupStart + 1);
            for (var j = 0; j < Math.Min(capacity, packed.Count); j++)
            {
                lines[groupStart + j].Invoice = packed[j];
            }

            if (packed.Count > capacity)
            {
                var insertAt = groupEnd + 1;
                for (var j = capacity; j < packed.Count; j++)
                {
                    lines.Insert(insertAt, new BillPreviewLineItem { Invoice = packed[j] });
                    insertAt++;
                }
                groupEnd = insertAt - 1;
            }

            i = groupEnd + 1;
        }
    }

    public async Task<IEnumerable<BillDueDetailItem>> GetBillDueDetailsForPartyAsync(string? party)
    {
        const string dueExpr = "(b.freight + b.detention + b.hml + b.othr + b.st_charge - b.rcvd - b.tds - b.ded)";
        const string resolvedPartyExpr = "TRIM(COALESCE(NULLIF(b.party, ''), NULLIF(l.bill_party, ''), NULLIF(l.consignor_name, ''), ''))";
        const string sql = $@"
SELECT
    TRIM(COALESCE(b.bill_no, '')) AS BillNo,
    COALESCE(string_agg(DISTINCT NULLIF(TRIM(COALESCE(b.lr_no, '')), ''), ', '), '') AS LRNos,
    COALESCE(string_agg(DISTINCT NULLIF(TRIM(COALESCE(NULLIF(b.from_loc, ''), l.from_location, '')), ''), ', '), '') AS ""From"",
    COALESCE(string_agg(DISTINCT NULLIF(TRIM(COALESCE(NULLIF(b.to_loc, ''), l.to_location, '')), ''), ', '), '') AS ""To"",
    COALESCE(SUM({dueExpr}), 0)::numeric AS Due
FROM bills b
LEFT JOIN lr_entries l
    ON lower(trim(COALESCE(l.lrno, ''))) = lower(trim(COALESCE(b.lr_no, '')))
WHERE NULLIF(TRIM(COALESCE(b.bill_no, '')), '') IS NOT NULL
  AND {dueExpr} > 0
  AND {resolvedPartyExpr} = @party
GROUP BY TRIM(COALESCE(b.bill_no, ''))
ORDER BY Due DESC, BillNo DESC;";

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        return await conn.QueryAsync<BillDueDetailItem>(sql, new { party = (party ?? string.Empty).Trim() });
    }

    public async Task<IEnumerable<string>> GetPendingBillPartiesAsync(string? partyFilter)
    {
        const string resolvedPartyExpr = "TRIM(COALESCE(NULLIF(b.party, ''), NULLIF(l.bill_party, ''), NULLIF(l.consignor_name, ''), ''))";
        const string dueExpr = "(b.freight + b.detention + b.hml + b.othr + b.st_charge - b.rcvd - b.tds - b.ded)";
        const string sql = $@"
SELECT DISTINCT {resolvedPartyExpr} AS Party
FROM bills b
LEFT JOIN lr_entries l
    ON lower(trim(COALESCE(l.lrno, ''))) = lower(trim(COALESCE(b.lr_no, '')))
WHERE NULLIF(TRIM(COALESCE(b.bill_no, '')), '') IS NOT NULL
  AND {dueExpr} > 0
  AND {resolvedPartyExpr} <> ''
  AND (@partyFilter = '' OR {resolvedPartyExpr} ILIKE @partySearch)
ORDER BY Party ASC;";

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<string>(sql, new
        {
            partyFilter = (partyFilter ?? string.Empty).Trim(),
            partySearch = "%" + (partyFilter ?? string.Empty).Trim() + "%"
        });
        return rows;
    }

    public async Task<IEnumerable<BillPendingOptionItem>> GetPendingBillOptionsAsync(string? partyFilter, string? billNoFilter)
    {
        const string resolvedPartyExpr = "TRIM(COALESCE(NULLIF(b.party, ''), NULLIF(l.bill_party, ''), NULLIF(l.consignor_name, ''), ''))";
        const string dueExpr = "(b.freight + b.detention + b.hml + b.othr + b.st_charge - b.rcvd - b.tds - b.ded)";
        const string totalExpr = "(b.freight + b.detention + b.hml + b.othr + b.st_charge)";
        const string sql = $@"
SELECT
    TRIM(COALESCE(b.bill_no, '')) AS BillNo,
    MAX({resolvedPartyExpr}) AS Party,
    COALESCE(string_agg(DISTINCT NULLIF(TRIM(COALESCE(b.lr_no, '')), ''), ', '), '') AS LRNos,
    COALESCE(SUM({totalExpr}), 0)::numeric AS Total,
    COALESCE(SUM(b.rcvd), 0)::numeric AS RCVD,
    COALESCE(SUM(b.tds), 0)::numeric AS TDS,
    COALESCE(SUM(b.ded), 0)::numeric AS DED
FROM bills b
LEFT JOIN lr_entries l
    ON lower(trim(COALESCE(l.lrno, ''))) = lower(trim(COALESCE(b.lr_no, '')))
WHERE NULLIF(TRIM(COALESCE(b.bill_no, '')), '') IS NOT NULL
  AND {dueExpr} > 0
  AND (@partyFilter = '' OR {resolvedPartyExpr} ILIKE @partySearch)
  AND (@billNoFilter = '' OR TRIM(COALESCE(b.bill_no, '')) ILIKE @billSearch)
GROUP BY TRIM(COALESCE(b.bill_no, ''))
ORDER BY BillNo DESC;";

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        return await conn.QueryAsync<BillPendingOptionItem>(sql, new
        {
            partyFilter = (partyFilter ?? string.Empty).Trim(),
            partySearch = "%" + (partyFilter ?? string.Empty).Trim() + "%",
            billNoFilter = (billNoFilter ?? string.Empty).Trim(),
            billSearch = "%" + (billNoFilter ?? string.Empty).Trim() + "%"
        });
    }

    public Task<BillEntry?> GetBillAsync(int id) =>
        QuerySingleOrDefaultAsync<BillEntry>(@"SELECT
            id, sr, bill_no AS BillNo, bill_date AS BillDate, party AS Party, lr_no AS LRNo, lr_date AS LRDate,
            from_loc AS ""From"", to_loc AS ""To"", vehicle_type AS VehicleType, freight AS Freight, detention AS Detention,
            hml AS HML, othr AS OTHR, st_charge AS StCharge, rcvd AS RCVD, tds AS TDS, ded AS DED, mop AS MOP, mr AS MR,
            remarks AS Remarks, date AS Date
            FROM bills WHERE id = @id;", new { id });

    public async Task<int> UpsertBillAsync(BillEntry entry)
    {
        var sql = entry.Id <= 0
            ? @"INSERT INTO bills (
                    sr, bill_no, bill_date, party, lr_no, lr_date, from_loc, to_loc, vehicle_type, freight, detention,
                    hml, othr, st_charge, rcvd, tds, ded, mop, mr, remarks, date)
                VALUES (
                    @Sr, @BillNo, @BillDate, @Party, @LRNo, @LRDate, @From, @To, @VehicleType, @Freight, @Detention,
                    @HML, @OTHR, @StCharge, @RCVD, @TDS, @DED, @MOP, @MR, @Remarks, @Date)
                RETURNING id;"
            : @"UPDATE bills SET
                    sr = @Sr, bill_no = @BillNo, bill_date = @BillDate, party = @Party, lr_no = @LRNo, lr_date = @LRDate,
                    from_loc = @From, to_loc = @To, vehicle_type = @VehicleType, freight = @Freight, detention = @Detention,
                    hml = @HML, othr = @OTHR, st_charge = @StCharge, rcvd = @RCVD, tds = @TDS, ded = @DED, mop = @MOP, mr = @MR,
                    remarks = @Remarks, date = @Date
                WHERE id = @Id;
               SELECT @Id;";
        return await ExecuteScalarIntAsync(sql, entry);
    }

    public Task<int> DeleteBillAsync(int id) =>
        ExecuteAsync("DELETE FROM bills WHERE id = @id;", new { id });

    public Task<IEnumerable<CBSAccountEntry>> GetCBSAccountsAsync() =>
        QueryAsync<CBSAccountEntry>(@"
SELECT id, sr, account_name AS AccountName, is_active AS IsActive
FROM (
    SELECT id,
           sr,
           CASE
               WHEN lower(trim(account_name)) = 'lhs' THEN 'Purchase LHS'
               ELSE account_name
           END AS account_name,
           is_active,
           row_number() OVER (
               PARTITION BY CASE
                                WHEN lower(trim(account_name)) = 'lhs' THEN 'purchase lhs'
                                ELSE lower(trim(account_name))
                            END
               ORDER BY is_active DESC, sr, id
           ) AS rn
    FROM cbs_accounts
) q
WHERE rn = 1
ORDER BY sr, id;");

    public Task<CBSAccountEntry?> GetCBSAccountAsync(int id) =>
        QuerySingleOrDefaultAsync<CBSAccountEntry>("SELECT id, sr, account_name AS AccountName, is_active AS IsActive FROM cbs_accounts WHERE id = @id;", new { id });

    public async Task<int> UpsertCBSAccountAsync(CBSAccountEntry entry)
    {
        entry.AccountName = (entry.AccountName ?? string.Empty).Trim();
        if (string.Equals(entry.AccountName, "LHS", StringComparison.OrdinalIgnoreCase))
        {
            entry.AccountName = "Purchase LHS";
        }
        var sql = entry.Id <= 0
            ? @"INSERT INTO cbs_accounts (sr, account_name, is_active)
                VALUES (@Sr, @AccountName, @IsActive)
                RETURNING id;"
            : @"UPDATE cbs_accounts SET sr = @Sr, account_name = @AccountName, is_active = @IsActive
                WHERE id = @Id;
               SELECT @Id;";
        return await ExecuteScalarIntAsync(sql, entry);
    }

    public Task<int> DeleteCBSAccountAsync(int id) =>
        ExecuteAsync("DELETE FROM cbs_accounts WHERE id = @id;", new { id });

    public Task<IEnumerable<CashBankStatementEntry>> GetCashBankStatementsAsync(string? accountName = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
        var normalizedAccount = string.IsNullOrWhiteSpace(accountName) ? null : NormalizeLhsAccountName(accountName);
        if (normalizedAccount != null &&
            (string.Equals(normalizedAccount, "Purchase LHS", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(normalizedAccount, "Challan LHS", StringComparison.OrdinalIgnoreCase)))
        {
            return GetCashBankStatementsWithLhsSyncAsync(normalizedAccount, fromDate, toDate);
        }

        var sql = @"SELECT
            id, sr, cbs AS CBS, date, account_name AS AccountName, particulars AS Particulars, remarks AS Remarks,
            bank_dr AS BankDr, bank_cr AS BankCr, cash_dr AS CashDr, cash_cr AS CashCr
            FROM cash_bank_statements";

        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(accountName))
        {
            conditions.Add("LOWER(TRIM(account_name)) = LOWER(TRIM(@accountName))");
            parameters.Add("accountName", accountName.Trim());
        }
        if (fromDate.HasValue)
        {
            conditions.Add("date::date >= @fromDate");
            parameters.Add("fromDate", fromDate.Value.Date);
        }
        if (toDate.HasValue)
        {
            conditions.Add("date::date <= @toDate");
            parameters.Add("toDate", toDate.Value.Date);
        }

        if (conditions.Count > 0)
        {
            sql += " WHERE " + string.Join(" AND ", conditions);
        }

        sql += " ORDER BY date DESC, sr, id;";
        return QueryAsync<CashBankStatementEntry>(sql, parameters);
    }

    private async Task<IEnumerable<CashBankStatementEntry>> GetCashBankStatementsWithLhsSyncAsync(string accountName, DateTime? fromDate, DateTime? toDate)
    {
        await EnsureLhsRowsSynchronizedAsync(accountName);

        var sql = @"SELECT
            id, sr, cbs AS CBS, date, account_name AS AccountName, particulars AS Particulars, remarks AS Remarks,
            bank_dr AS BankDr, bank_cr AS BankCr, cash_dr AS CashDr, cash_cr AS CashCr
            FROM cash_bank_statements
            WHERE CASE
                      WHEN LOWER(TRIM(account_name)) = 'lhs' THEN 'purchase lhs'
                      ELSE LOWER(TRIM(account_name))
                  END = CASE
                            WHEN LOWER(TRIM(@accountName)) = 'lhs' THEN 'purchase lhs'
                            ELSE LOWER(TRIM(@accountName))
                        END";

        var parameters = new DynamicParameters(new { accountName });
        if (fromDate.HasValue)
        {
            sql += " AND date::date >= @fromDate";
            parameters.Add("fromDate", fromDate.Value.Date);
        }
        if (toDate.HasValue)
        {
            sql += " AND date::date <= @toDate";
            parameters.Add("toDate", toDate.Value.Date);
        }

        sql += " ORDER BY date DESC, sr, id;";
        return await QueryAsync<CashBankStatementEntry>(sql, parameters);
    }

    public async Task<IEnumerable<LhsSummaryEntry>> GetLhsSummaryAsync(DateTime? fromDate = null, DateTime? toDate = null, string? accountName = "Purchase LHS")
    {
        var normalizedAccount = NormalizeLhsAccountName(accountName);
        await EnsureLhsRowsSynchronizedAsync(normalizedAccount);
        var sourceTable = string.Equals(normalizedAccount, "Challan LHS", StringComparison.OrdinalIgnoreCase)
            ? "challan_ledger_entries"
            : "challans";
        var sql = @"
SELECT
    cbs.date::date AS Date,
    COALESCE(parsed.challan_number, '') AS ChallanNumber,
    COALESCE(ch.broker_name, '') AS BrokerName,
    COALESCE(ch.from_location, '') AS ""From"",
    COALESCE(ch.to_location, '') AS ""To"",
    COALESCE(ch.vehicle_number, '') AS VehicleNumber,
    SUM(CASE WHEN LOWER(COALESCE(cbs.particulars, '')) LIKE '%advance paid%' THEN cbs.bank_cr ELSE 0 END) AS AdvanceNeft,
    SUM(CASE WHEN LOWER(COALESCE(cbs.particulars, '')) LIKE '%advance paid%' THEN cbs.cash_cr ELSE 0 END) AS AdvanceCash,
    SUM(CASE WHEN LOWER(COALESCE(cbs.particulars, '')) LIKE '%balance paid%' THEN cbs.bank_cr ELSE 0 END) AS BalanceNeft,
    SUM(CASE WHEN LOWER(COALESCE(cbs.particulars, '')) LIKE '%balance paid%' THEN cbs.cash_cr ELSE 0 END) AS BalanceCash,
    SUM(cbs.bank_dr) AS BankDr,
    SUM(cbs.bank_cr) AS BankCr,
    SUM(cbs.cash_dr) AS CashDr,
    SUM(cbs.cash_cr) AS CashCr
FROM cash_bank_statements cbs
LEFT JOIN LATERAL (
    SELECT (regexp_match(COALESCE(cbs.particulars, ''), 'Challan\s+([^\s\-\|\t\r\n]+)', 'i'))[1] AS challan_number
) parsed ON TRUE
LEFT JOIN " + sourceTable + @" ch
    ON LOWER(TRIM(ch.challan_number)) = LOWER(TRIM(COALESCE(parsed.challan_number, '')))
WHERE CASE
          WHEN LOWER(TRIM(cbs.account_name)) = 'lhs' THEN 'purchase lhs'
          ELSE LOWER(TRIM(cbs.account_name))
      END = CASE
                WHEN LOWER(TRIM(@accountName)) = 'lhs' THEN 'purchase lhs'
                ELSE LOWER(TRIM(@accountName))
            END";

        var conditions = new List<string>();
        var parameters = new DynamicParameters(new { accountName = normalizedAccount });
        if (fromDate.HasValue)
        {
            conditions.Add("cbs.date::date >= @fromDate");
            parameters.Add("fromDate", fromDate.Value.Date);
        }
        if (toDate.HasValue)
        {
            conditions.Add("cbs.date::date <= @toDate");
            parameters.Add("toDate", toDate.Value.Date);
        }
        if (conditions.Count > 0)
        {
            sql += " AND " + string.Join(" AND ", conditions);
        }

        sql += @"
GROUP BY
    cbs.date::date,
    COALESCE(parsed.challan_number, ''),
    COALESCE(ch.broker_name, ''),
    COALESCE(ch.from_location, ''),
    COALESCE(ch.to_location, ''),
    COALESCE(ch.vehicle_number, '')
ORDER BY cbs.date::date DESC, COALESCE(parsed.challan_number, '') DESC;";
        return await QueryAsync<LhsSummaryEntry>(sql, parameters);
    }

    public Task<IEnumerable<ChallanEntry>> GetChallansByNumbersAsync(IEnumerable<string> challanNumbers, string? ledgerKind = null)
    {
        var challanLedgerMode = IsChallanLedgerKind(ledgerKind);
        var keys = (challanNumbers ?? Enumerable.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (keys.Length == 0)
        {
            return Task.FromResult<IEnumerable<ChallanEntry>>(Array.Empty<ChallanEntry>());
        }

        return QueryAsync<ChallanEntry>(GetChallanSelectSql(challanLedgerMode) + @"
WHERE challan_number = ANY(@challanNumbers)
ORDER BY date DESC, sr, id;", new { challanNumbers = keys });
    }

    public Task<CashBankStatementEntry?> GetCashBankStatementAsync(int id) =>
        QuerySingleOrDefaultAsync<CashBankStatementEntry>(@"SELECT
            id, sr, cbs AS CBS, date, account_name AS AccountName, particulars AS Particulars, remarks AS Remarks,
            bank_dr AS BankDr, bank_cr AS BankCr, cash_dr AS CashDr, cash_cr AS CashCr
            FROM cash_bank_statements WHERE id = @id;", new { id });

    public async Task<int> UpsertCashBankStatementAsync(CashBankStatementEntry entry)
    {
        entry.Date = entry.Date.Date;
        entry.CBS = string.IsNullOrWhiteSpace(entry.CBS) ? ToCBSMonth(entry.Date) : entry.CBS;
        var sql = entry.Id <= 0
            ? @"INSERT INTO cash_bank_statements (sr, cbs, date, account_name, particulars, remarks, bank_dr, bank_cr, cash_dr, cash_cr)
                VALUES (@Sr, @CBS, @Date, @AccountName, @Particulars, @Remarks, @BankDr, @BankCr, @CashDr, @CashCr)
                RETURNING id;"
            : @"UPDATE cash_bank_statements SET sr = @Sr, cbs = @CBS, date = @Date, account_name = @AccountName, particulars = @Particulars, remarks = @Remarks,
                    bank_dr = @BankDr, bank_cr = @BankCr, cash_dr = @CashDr, cash_cr = @CashCr
                WHERE id = @Id;
               SELECT @Id;";
        return await ExecuteScalarIntAsync(sql, entry);
    }

    public Task<int> DeleteCashBankStatementAsync(int id) =>
        ExecuteAsync("DELETE FROM cash_bank_statements WHERE id = @id;", new { id });

    public Task<IEnumerable<BillReceiptEntry>> GetBillReceiptsAsync(string? billNo = null)
    {
        var key = (billNo ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            return QueryAsync<BillReceiptEntry>(@"SELECT
                id, bill_no AS BillNo, lr_no AS LRNo, party AS Party, bill_total AS BillTotal, bill_date AS BillDate, receipt_date AS ReceiptDate,
                rcvd AS RCVD, tds AS TDS, ded AS DED, mop AS MOP, mr AS MR, remarks AS Remarks, due_after AS DueAfter, created_at AS CreatedAt
                FROM bill_receipts ORDER BY receipt_date DESC, id DESC;");
        }

        return QueryAsync<BillReceiptEntry>(@"SELECT
                id, bill_no AS BillNo, lr_no AS LRNo, party AS Party, bill_total AS BillTotal, bill_date AS BillDate, receipt_date AS ReceiptDate,
                rcvd AS RCVD, tds AS TDS, ded AS DED, mop AS MOP, mr AS MR, remarks AS Remarks, due_after AS DueAfter, created_at AS CreatedAt
                FROM bill_receipts
                WHERE trim(COALESCE(bill_no, '')) = @billNo
                ORDER BY receipt_date DESC, id DESC;", new { billNo = key });
    }

    public async Task<int> UpsertBillReceiptAsync(BillReceiptEntry entry)
    {
        var sql = entry.Id <= 0
            ? @"INSERT INTO bill_receipts (bill_no, lr_no, party, bill_total, bill_date, receipt_date, rcvd, tds, ded, mop, mr, remarks, due_after, created_at)
                VALUES (@BillNo, @LRNo, @Party, @BillTotal, @BillDate, @ReceiptDate, @RCVD, @TDS, @DED, @MOP, @MR, @Remarks, @DueAfter, @CreatedAt)
                RETURNING id;"
            : @"UPDATE bill_receipts SET bill_no = @BillNo, lr_no = @LRNo, party = @Party, bill_total = @BillTotal, bill_date = @BillDate,
                    receipt_date = @ReceiptDate, rcvd = @RCVD, tds = @TDS, ded = @DED, mop = @MOP, mr = @MR, remarks = @Remarks, due_after = @DueAfter, created_at = @CreatedAt
                WHERE id = @Id;
               SELECT @Id;";
        var receiptId = await ExecuteScalarIntAsync(sql, entry);
        entry.Id = receiptId;
        await SyncBfrsCBSFromBillReceiptAsync(entry);
        return receiptId;
    }

    public async Task<int> DeleteBillReceiptAsync(int id)
    {
        var receipt = (await QueryAsync<BillReceiptEntry>(
            "SELECT id, bill_no AS BillNo FROM bill_receipts WHERE id = @id;",
            new { id })).FirstOrDefault();
        var affected = await ExecuteAsync("DELETE FROM bill_receipts WHERE id = @id;", new { id });
        if (receipt != null && !string.IsNullOrWhiteSpace(receipt.BillNo))
        {
            await DeleteAutoCBSRowsAsync("BFRS", $"Bill {receipt.BillNo.Trim()} - Receipt {receipt.Id}", "Auto from Bill Receipt");
        }

        return affected;
    }

    public Task<IEnumerable<ChallanComment>> GetChallanCommentsAsync(int challanId) =>
        QueryAsync<ChallanComment>("SELECT id AS Id, challan_id AS ChallanId, comment AS Comment, created_at AS CreatedAt FROM challan_comments WHERE challan_id = @challanId ORDER BY created_at DESC, id DESC;", new { challanId });

    public Task<IEnumerable<ChallanComment>> GetAllChallanCommentsAsync() =>
        QueryAsync<ChallanComment>("SELECT id AS Id, challan_id AS ChallanId, comment AS Comment, created_at AS CreatedAt FROM challan_comments ORDER BY created_at DESC, id DESC;");

    public Task<int> AddChallanCommentAsync(ChallanComment comment) =>
        ExecuteScalarIntAsync(
            @"INSERT INTO challan_comments (challan_id, comment, created_at)
              VALUES (@ChallanId, @Comment, @CreatedAt)
              RETURNING id;",
            new { comment.ChallanId, comment.Comment, CreatedAt = comment.CreatedAt == default ? DateTime.Now : comment.CreatedAt });

    public Task<int> DeleteChallanCommentAsync(int id) =>
        ExecuteAsync("DELETE FROM challan_comments WHERE id = @id;", new { id });

    public Task<IEnumerable<LRComment>> GetLRCommentsAsync(int lrEntryId) =>
        QueryAsync<LRComment>("SELECT id AS Id, lr_entry_id AS LREntryId, comment AS Comment, created_at AS CreatedAt FROM lr_comments WHERE lr_entry_id = @lrEntryId ORDER BY created_at DESC, id DESC;", new { lrEntryId });

    public Task<IEnumerable<LRComment>> GetAllLRCommentsAsync() =>
        QueryAsync<LRComment>("SELECT id AS Id, lr_entry_id AS LREntryId, comment AS Comment, created_at AS CreatedAt FROM lr_comments ORDER BY created_at DESC, id DESC;");

    public Task<int> AddLRCommentAsync(LRComment comment) =>
        ExecuteScalarIntAsync(
            @"INSERT INTO lr_comments (lr_entry_id, comment, created_at)
              VALUES (@LREntryId, @Comment, @CreatedAt)
              RETURNING id;",
            new { comment.LREntryId, comment.Comment, CreatedAt = comment.CreatedAt == default ? DateTime.Now : comment.CreatedAt });

    public Task<int> DeleteLRCommentAsync(int id) =>
        ExecuteAsync("DELETE FROM lr_comments WHERE id = @id;", new { id });

    public Task<IEnumerable<BillComment>> GetBillCommentsAsync(int billId) =>
        QueryAsync<BillComment>("SELECT id AS Id, bill_id AS BillId, comment AS Comment, created_at AS CreatedAt FROM bill_comments WHERE bill_id = @billId ORDER BY created_at DESC, id DESC;", new { billId });

    public Task<IEnumerable<BillComment>> GetAllBillCommentsAsync() =>
        QueryAsync<BillComment>("SELECT id AS Id, bill_id AS BillId, comment AS Comment, created_at AS CreatedAt FROM bill_comments ORDER BY created_at DESC, id DESC;");

    public Task<int> AddBillCommentAsync(BillComment comment) =>
        ExecuteScalarIntAsync(
            @"INSERT INTO bill_comments (bill_id, comment, created_at)
              VALUES (@BillId, @Comment, @CreatedAt)
              RETURNING id;",
            new { comment.BillId, comment.Comment, CreatedAt = comment.CreatedAt == default ? DateTime.Now : comment.CreatedAt });

    public Task<int> DeleteBillCommentAsync(int id) =>
        ExecuteAsync("DELETE FROM bill_comments WHERE id = @id;", new { id });

    public Task<IEnumerable<TrackingEntry>> GetTrackingAsync() =>
        QueryAsync<TrackingEntry>("SELECT id AS Id, sr AS Sr, challan_no AS ChallanNo, challan_date AS ChallanDate, from_location AS From, to_location AS To, vehicle_no AS VehicleNo, driver_mobile AS DriverMobile, eway_bill_till_date AS EwayBillTillDate, dispatch_date AS DispatchDate, dispatch_time AS DispatchTime, delivered_date AS DeliveredDate, delivered_time AS DeliveredTime FROM tracking_entries ORDER BY sr, id;");

    public Task<TrackingEntry?> GetTrackingAsync(int id) =>
        QuerySingleOrDefaultAsync<TrackingEntry>("SELECT id AS Id, sr AS Sr, challan_no AS ChallanNo, challan_date AS ChallanDate, from_location AS From, to_location AS To, vehicle_no AS VehicleNo, driver_mobile AS DriverMobile, eway_bill_till_date AS EwayBillTillDate, dispatch_date AS DispatchDate, dispatch_time AS DispatchTime, delivered_date AS DeliveredDate, delivered_time AS DeliveredTime FROM tracking_entries WHERE id = @id;", new { id });

    public async Task<int> UpsertTrackingAsync(TrackingEntry entry)
    {
        var sql = entry.Id <= 0
            ? @"INSERT INTO tracking_entries (sr, challan_no, challan_date, from_location, to_location, vehicle_no, driver_mobile, eway_bill_till_date, dispatch_date, dispatch_time, delivered_date, delivered_time)
                VALUES (@Sr, @ChallanNo, @ChallanDate, @From, @To, @VehicleNo, @DriverMobile, @EwayBillTillDate, @DispatchDate, @DispatchTime, @DeliveredDate, @DeliveredTime)
                RETURNING id;"
            : @"UPDATE tracking_entries SET sr=@Sr, challan_no=@ChallanNo, challan_date=@ChallanDate, from_location=@From, to_location=@To, vehicle_no=@VehicleNo, driver_mobile=@DriverMobile, eway_bill_till_date=@EwayBillTillDate, dispatch_date=@DispatchDate, dispatch_time=@DispatchTime, delivered_date=@DeliveredDate, delivered_time=@DeliveredTime
                WHERE id=@Id;
               SELECT @Id;";
        return await ExecuteScalarIntAsync(sql, entry);
    }

    public Task<int> DeleteTrackingAsync(int id) =>
        ExecuteAsync("DELETE FROM tracking_entries WHERE id = @id;", new { id });

    public Task<int> AddReportingTrackAsync(ReportingTrackEntry entry) =>
        ExecuteScalarIntAsync(
            @"INSERT INTO reporting_tracks (tracking_entry_id, report_date_time, remarks)
              VALUES (@TrackingEntryId, @ReportDateTime, @Remarks)
              RETURNING id;",
            new { entry.TrackingEntryId, entry.ReportDateTime, entry.Remarks });

    public Task<IEnumerable<ReportingTrackEntry>> GetReportingTracksAsync(int trackingEntryId) =>
        QueryAsync<ReportingTrackEntry>("SELECT id AS Id, tracking_entry_id AS TrackingEntryId, report_date_time AS ReportDateTime, remarks AS Remarks FROM reporting_tracks WHERE tracking_entry_id = @trackingEntryId ORDER BY report_date_time;", new { trackingEntryId });

    public Task<IEnumerable<TrackingLatestReportItem>> GetLatestTrackingReportsAsync() =>
        QueryAsync<TrackingLatestReportItem>(@"
SELECT
    t.id AS TrackingEntryId,
    r.report_date_time AS ReportDateTime,
    COALESCE(r.remarks, '') AS Remarks
FROM tracking_entries t
LEFT JOIN LATERAL (
    SELECT report_date_time, remarks
    FROM reporting_tracks
    WHERE tracking_entry_id = t.id
    ORDER BY report_date_time DESC, id DESC
    LIMIT 1
) r ON TRUE
ORDER BY t.id;");

    public Task<IEnumerable<AppUserInfo>> GetUsersAsync() =>
        QueryAsync<AppUserInfo>(@"
SELECT
    id,
    username,
    full_name AS FullName,
    role,
    is_active AS IsActive,
    last_login_utc AS LastLoginUtc
FROM app_users
ORDER BY username, id;");

    public Task<AppUserEntry?> GetUserByUsernameAsync(string username) =>
        QuerySingleOrDefaultAsync<AppUserEntry>(@"
SELECT
    id,
    username,
    full_name AS FullName,
    role,
    is_active AS IsActive,
    created_utc AS CreatedUtc,
    last_login_utc AS LastLoginUtc,
    password_hash AS PasswordHash,
    password_salt AS PasswordSalt
FROM app_users
WHERE lower(trim(username)) = lower(trim(@username))
LIMIT 1;", new { username });

    public Task<AppUserInfo?> GetUserInfoAsync(int id) =>
        QuerySingleOrDefaultAsync<AppUserInfo>(@"
SELECT
    id,
    username,
    full_name AS FullName,
    role,
    is_active AS IsActive,
    last_login_utc AS LastLoginUtc
FROM app_users
WHERE id = @id;", new { id });

    public async Task<int> CreateUserAsync(CreateUserRequest request)
    {
        return await CreateUserAsync(request, string.Empty);
    }

    public async Task<int> CreateUserAsync(CreateUserRequest request, string passwordPreviewSecret)
    {
        var password = AuthSecurity.HashPassword(request.Password);
        var preview = string.IsNullOrWhiteSpace(request.Password)
            ? string.Empty
            : AuthSecurity.EncryptPasswordPreview(request.Password, passwordPreviewSecret);
        return await ExecuteScalarIntAsync(@"
INSERT INTO app_users (username, full_name, password_hash, password_salt, password_preview, role, is_active, created_utc)
VALUES (@Username, @FullName, @PasswordHash, @PasswordSalt, @PasswordPreview, @Role, TRUE, @CreatedUtc)
RETURNING id;", new
        {
            Username = (request.Username ?? string.Empty).Trim(),
            FullName = (request.FullName ?? string.Empty).Trim(),
            PasswordHash = password.Hash,
            PasswordSalt = password.Salt,
            PasswordPreview = preview,
            Role = string.IsNullOrWhiteSpace(request.Role) ? "Operator" : request.Role.Trim(),
            CreatedUtc = DateTime.UtcNow
        });
    }

    public Task<int> UpdateUserStatusAsync(int id, bool isActive) =>
        ExecuteAsync("UPDATE app_users SET is_active = @isActive WHERE id = @id;", new { id, isActive });

    public async Task<int> ResetUserPasswordAsync(int id, string password, string passwordPreviewSecret)
    {
        var hashed = AuthSecurity.HashPassword(password);
        var preview = string.IsNullOrWhiteSpace(password)
            ? string.Empty
            : AuthSecurity.EncryptPasswordPreview(password, passwordPreviewSecret);
        return await ExecuteAsync(
            "UPDATE app_users SET password_hash = @PasswordHash, password_salt = @PasswordSalt, password_preview = @PasswordPreview WHERE id = @id;",
            new { id, PasswordHash = hashed.Hash, PasswordSalt = hashed.Salt, PasswordPreview = preview });
    }

    public async Task<UserPasswordPreviewResponse> GetUserPasswordPreviewAsync(int id, string passwordPreviewSecret)
    {
        var encrypted = await QuerySingleOrDefaultAsync<string>(
            "SELECT COALESCE(password_preview, '') FROM app_users WHERE id = @id;",
            new { id });

        if (string.IsNullOrWhiteSpace(encrypted))
        {
            return new UserPasswordPreviewResponse
            {
                Password = string.Empty,
                Available = false
            };
        }

        try
        {
            return new UserPasswordPreviewResponse
            {
                Password = AuthSecurity.DecryptPasswordPreview(encrypted, passwordPreviewSecret),
                Available = true
            };
        }
        catch
        {
            return new UserPasswordPreviewResponse
            {
                Password = string.Empty,
                Available = false
            };
        }
    }

    public Task<int> DeleteUserAsync(int id) =>
        ExecuteAsync("DELETE FROM app_users WHERE id = @id AND lower(username) <> 'admin';", new { id });

    public Task<int> UpdateUserLastLoginAsync(int id) =>
        ExecuteAsync("UPDATE app_users SET last_login_utc = @LastLoginUtc WHERE id = @id;", new { id, LastLoginUtc = DateTime.UtcNow });

    public Task<int> AddAuditLogAsync(AuditLogEntry entry) =>
        ExecuteScalarIntAsync(@"
INSERT INTO audit_logs (user_id, username, full_name, role, action_area, action_type, entity_key, details, created_utc)
VALUES (@UserId, @Username, @FullName, @Role, @ActionArea, @ActionType, @EntityKey, @Details, @CreatedUtc)
RETURNING id;", new
        {
            entry.UserId,
            Username = (entry.Username ?? string.Empty).Trim(),
            FullName = (entry.FullName ?? string.Empty).Trim(),
            Role = (entry.Role ?? string.Empty).Trim(),
            ActionArea = (entry.ActionArea ?? string.Empty).Trim(),
            ActionType = (entry.ActionType ?? string.Empty).Trim(),
            EntityKey = (entry.EntityKey ?? string.Empty).Trim(),
            Details = entry.Details ?? string.Empty,
            CreatedUtc = entry.CreatedUtc == default ? DateTime.UtcNow : entry.CreatedUtc
        });

    public Task<IEnumerable<AuditLogEntry>> GetRecentAuditAsync(int take = 200) =>
        QueryAsync<AuditLogEntry>(@"
SELECT
    id,
    user_id AS UserId,
    username,
    full_name AS FullName,
    role,
    action_area AS ActionArea,
    action_type AS ActionType,
    entity_key AS EntityKey,
    details,
    created_utc AS CreatedUtc
FROM audit_logs
ORDER BY created_utc DESC, id DESC
LIMIT @take;", new { take });

    public Task<IEnumerable<AuditUserSummaryEntry>> GetAuditUserSummaryAsync() =>
        QueryAsync<AuditUserSummaryEntry>(@"
SELECT
    username,
    max(full_name) AS FullName,
    max(role) AS Role,
    COUNT(*) FILTER (WHERE lower(action_type) = 'create') AS AddedCount,
    COUNT(*) FILTER (WHERE lower(action_type) = 'update') AS UpdatedCount,
    COUNT(*) FILTER (WHERE lower(action_type) = 'delete') AS DeletedCount,
    MAX(created_utc) AS LastActivityUtc
FROM audit_logs
GROUP BY username
ORDER BY MAX(created_utc) DESC NULLS LAST, username;");
}
