using Awagaman.Api.Models;
using Dapper;
using System.Globalization;

namespace Awagaman.Api.DataAccess;

public sealed class AwagamanRepository
{
    private readonly IPgConnectionFactory _factory;

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
    (SELECT COUNT(*) FROM challans) AS ChallanCount,
    (SELECT COUNT(*) FROM challans
        WHERE (lorry_hire - less_tds + detention + hamali - deduction - advance_amount - balance_paid_neft - balance_paid_cash) > 0) AS DueChallanCount,
    COALESCE((SELECT SUM(lorry_hire - less_tds + detention + hamali - deduction - advance_amount - balance_paid_neft - balance_paid_cash) FROM challans), 0) AS ChallanDueAmount,
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

    private static string BuildChallanOrderBy(string? sortColumn, bool ascending)
    {
        var dir = SortDirection(ascending);
        return (sortColumn ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "challannumber" => $"challan_number {dir}, sr {dir}, id {dir}",
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
            "lrno" => $"lrno {dir}, sr {dir}, id {dir}",
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
            "billno" => $"bill_no {dir}, sr {dir}, id {dir}",
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
        QueryAsync<VehicleEntry>("SELECT id, sr, vehicle_number AS VehicleNumber, owner_name AS OwnerName, pan_number AS PANNumber, engine_number AS EngineNumber, chassis_number AS ChassisNumber, vehicle_type AS VehicleType FROM vehicle_ledger ORDER BY sr, id;");

    public Task<VehicleEntry?> GetVehicleAsync(int id) =>
        QuerySingleOrDefaultAsync<VehicleEntry>("SELECT id, sr, vehicle_number AS VehicleNumber, owner_name AS OwnerName, pan_number AS PANNumber, engine_number AS EngineNumber, chassis_number AS ChassisNumber, vehicle_type AS VehicleType FROM vehicle_ledger WHERE id = @id;", new { id });

    public Task<IEnumerable<VehicleEntry>> SearchVehiclesAsync(string query) =>
        QueryAsync<VehicleEntry>(
            "SELECT id, sr, vehicle_number AS VehicleNumber, owner_name AS OwnerName, pan_number AS PANNumber, engine_number AS EngineNumber, chassis_number AS ChassisNumber, vehicle_type AS VehicleType FROM vehicle_ledger WHERE vehicle_number ILIKE @q ORDER BY vehicle_number LIMIT 20;",
            new { q = $"%{query ?? string.Empty}%" });

    public async Task<int> UpsertVehicleAsync(VehicleEntry entry)
    {
        var sql = entry.Id <= 0
            ? @"INSERT INTO vehicle_ledger (sr, vehicle_number, owner_name, pan_number, engine_number, chassis_number, vehicle_type)
                VALUES (@Sr, @VehicleNumber, @OwnerName, @PANNumber, @EngineNumber, @ChassisNumber, @VehicleType)
                RETURNING id;"
            : @"UPDATE vehicle_ledger SET sr = @Sr, vehicle_number = @VehicleNumber, owner_name = @OwnerName, pan_number = @PANNumber, engine_number = @EngineNumber, chassis_number = @ChassisNumber, vehicle_type = @VehicleType
                WHERE id = @Id;
               SELECT @Id;";
        return await ExecuteScalarIntAsync(sql, entry);
    }

    private async Task SyncLhsCBSFromChallanAsync(ChallanEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.ChallanNumber))
        {
            return;
        }

        await EnsureCBSAccountAsync("LHS");

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
            var advanceDate = entry.AdvanceDate ?? entry.Date;
            await UpsertAutoCBSRowAsync(new CashBankStatementEntry
            {
                Date = advanceDate,
                CBS = ToCBSMonth(advanceDate),
                AccountName = "LHS",
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
            await DeleteAutoCBSRowsAsync("LHS", advanceParticulars, "Auto from Challan");
        }

        if (entry.BalancePaidNEFT != 0m || entry.BalancePaidCash != 0m)
        {
            var balanceDate = entry.BalancePaidDate ?? entry.Date;
            await UpsertAutoCBSRowAsync(new CashBankStatementEntry
            {
                Date = balanceDate,
                CBS = ToCBSMonth(balanceDate),
                AccountName = "LHS",
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
            await DeleteAutoCBSRowsAsync("LHS", balanceParticulars, "Auto from Challan");
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
            Date = entry.ReceiptDate == default ? DateTime.Today : entry.ReceiptDate,
            CBS = ToCBSMonth(entry.ReceiptDate == default ? DateTime.Today : entry.ReceiptDate),
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
        if (name.Length == 0)
        {
            return;
        }

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        await conn.ExecuteAsync(@"
WITH existing AS (
    SELECT id FROM cbs_accounts
    WHERE lower(trim(account_name)) = lower(trim(@AccountName))
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
        var particulars = row.Particulars.Trim();
        var remarks = (row.Remarks ?? string.Empty).Trim();

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var existingIds = (await conn.QueryAsync<int>(@"
SELECT id
FROM cash_bank_statements
WHERE lower(trim(account_name)) = lower(trim(@AccountName))
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
WHERE lower(trim(account_name)) = lower(trim(@AccountName))
  AND lower(trim(particulars)) = lower(trim(@Particulars))
  AND lower(trim(COALESCE(remarks, ''))) = lower(trim(@Remarks));", new
        {
            AccountName = (accountName ?? string.Empty).Trim(),
            Particulars = (particulars ?? string.Empty).Trim(),
            Remarks = (remarks ?? string.Empty).Trim()
        });
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
            imported_balance AS ImportedBalance, imported_due AS ImportedDue
            FROM " + GetChallanTableName(challanLedgerMode);
    }

    public Task<int> DeleteVehicleAsync(int id) =>
        ExecuteAsync("DELETE FROM vehicle_ledger WHERE id = @id;", new { id });

    public Task<IEnumerable<ChallanEntry>> GetChallansAsync(string? ledgerKind = null)
    {
        var challanLedgerMode = IsChallanLedgerKind(ledgerKind);
        return QueryAsync<ChallanEntry>(GetChallanSelectSql(challanLedgerMode) + " ORDER BY sr, id;");
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
                vehicle_type ILIKE @search OR driver_name ILIKE @search OR broker_name ILIKE @search OR
                from_location ILIKE @search OR to_location ILIKE @search OR owner_name ILIKE @search)");
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
                vehicle_type ILIKE @search OR driver_name ILIKE @search OR broker_name ILIKE @search OR
                from_location ILIKE @search OR to_location ILIKE @search OR owner_name ILIKE @search)");
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

    public async Task<int> UpsertChallanAsync(ChallanEntry entry, string? ledgerKind = null)
    {
        var challanLedgerMode = IsChallanLedgerKind(ledgerKind);
        var tableName = GetChallanTableName(challanLedgerMode);
        var sql = entry.Id <= 0
            ? challanLedgerMode
                ? $@"INSERT INTO {tableName} (
                        source_purchase_id, sr, challan_number, date, lr_number, broker_name, from_location, to_location, vehicle_number, vehicle_type,
                        driver_name, driver_mobile, engine_no, licence_no, policy_no, chassis_no, owner_name, pan,
                        lorry_hire, less_tds, advance_amount, advance_neft, advance_cash, advance_date, detention, hamali,
                        other_amount, deduction, balance_paid_neft, balance_paid_cash, balance_paid_date, paid_to, remarks, bill_amount, margin,
                        imported_balance, imported_due)
                    VALUES (
                        @SourcePurchaseId, @Sr, @ChallanNumber, @Date, @LRNumber, @BrokerName, @From, @To, @VehicleNumber, @VehicleType,
                        @DriverName, @DriverMobile, @EngineNo, @LicenceNo, @PolicyNo, @ChassisNo, @OwnerName, @PAN,
                        @LorryHire, @LessTDS, @AdvanceAmount, @AdvanceNEFT, @AdvanceCash, @AdvanceDate, @Detention, @Hamali,
                        @Other, @Deduction, @BalancePaidNEFT, @BalancePaidCash, @BalancePaidDate, @PaidTo, @Remarks, @BillAmount, @Margin,
                        @ImportedBalance, @ImportedDue)
                    RETURNING id;"
                : $@"INSERT INTO {tableName} (
                        sr, challan_number, date, lr_number, broker_name, from_location, to_location, vehicle_number, vehicle_type,
                        driver_name, driver_mobile, engine_no, licence_no, policy_no, chassis_no, owner_name, pan,
                        lorry_hire, less_tds, advance_amount, advance_neft, advance_cash, advance_date, detention, hamali,
                        other_amount, deduction, balance_paid_neft, balance_paid_cash, balance_paid_date, paid_to, remarks, bill_amount, margin,
                        imported_balance, imported_due)
                    VALUES (
                        @Sr, @ChallanNumber, @Date, @LRNumber, @BrokerName, @From, @To, @VehicleNumber, @VehicleType,
                        @DriverName, @DriverMobile, @EngineNo, @LicenceNo, @PolicyNo, @ChassisNo, @OwnerName, @PAN,
                        @LorryHire, @LessTDS, @AdvanceAmount, @AdvanceNEFT, @AdvanceCash, @AdvanceDate, @Detention, @Hamali,
                        @Other, @Deduction, @BalancePaidNEFT, @BalancePaidCash, @BalancePaidDate, @PaidTo, @Remarks, @BillAmount, @Margin,
                        @ImportedBalance, @ImportedDue)
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
                        imported_balance = @ImportedBalance, imported_due = @ImportedDue
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
                        imported_balance = @ImportedBalance, imported_due = @ImportedDue
                    WHERE id = @Id;
                   SELECT @Id;";
        var id = await ExecuteScalarIntAsync(sql, entry);
        entry.Id = id;
        if (challanLedgerMode)
        {
            await SyncLhsCBSFromChallanAsync(entry);
        }
        else
        {
            await SyncRemoteChallanMirrorAsync(entry);
        }
        return id;
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
            await DeleteAutoCBSRowsAsync("LHS", $"Challan {challanNo} - Advance Paid", "Auto from Challan");
            await DeleteAutoCBSRowsAsync("LHS", $"Challan {challanNo} - Balance Paid", "Auto from Challan");
        }
        else if (!challanLedgerMode && challan != null)
        {
            await ExecuteAsync(@"DELETE FROM challan_ledger_entries
WHERE source_purchase_id = @SourcePurchaseId
   OR lower(trim(challan_number)) = lower(trim(@ChallanNumber));",
                new { SourcePurchaseId = challan.Id, ChallanNumber = challan.ChallanNumber ?? string.Empty });
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
        await UpsertChallanAsync(mirror, "challan");
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
            BillAmount = existingMirror?.BillAmount ?? purchaseEntry.BillAmount,
            Margin = existingMirror?.Margin ?? purchaseEntry.Margin,
            ImportedBalance = existingMirror?.ImportedBalance ?? purchaseEntry.ImportedBalance,
            ImportedDue = existingMirror?.ImportedDue ?? purchaseEntry.ImportedDue
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
            bill_party AS BillParty, broker AS Broker, frt_type AS FrtType, pay_type AS PayType, comm AS Comm, paid AS Paid
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
            where = @"WHERE lrno ILIKE @search OR consignor_name ILIKE @search OR consignee_name ILIKE @search OR
                vehicle_no ILIKE @search OR bill_no ILIKE @search OR chno ILIKE @search";
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
            bill_party AS BillParty, broker AS Broker, frt_type AS FrtType, pay_type AS PayType, comm AS Comm, paid AS Paid
            FROM lr_entries";

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM lr_entries {where};", parameters);
        var items = (await conn.QueryAsync<LREntry>($"{select} {where} ORDER BY {orderBy} LIMIT @limit OFFSET @offset;", parameters)).ToList();
        return new PagedResult<LREntry> { Items = items, TotalCount = total };
    }

    public async Task<LRSummaryResult> GetLREntriesSummaryAsync(string? search)
    {
        var parameters = new DynamicParameters();
        var where = string.Empty;
        var q = (search ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(q))
        {
            where = @"WHERE lrno ILIKE @search OR consignor_name ILIKE @search OR consignee_name ILIKE @search OR
                vehicle_no ILIKE @search OR bill_no ILIKE @search OR chno ILIKE @search";
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
            bill_party AS BillParty, broker AS Broker, frt_type AS FrtType, pay_type AS PayType, comm AS Comm, paid AS Paid
            FROM lr_entries WHERE id = @id;", new { id });

    public async Task<int> UpsertLREntryAsync(LREntry entry)
    {
        var sql = entry.Id <= 0
            ? @"INSERT INTO lr_entries (
                    sr, lrno, date, consignor_name, consignor_address, consignor_gst, consignee_name, consignee_address, consignee_gst,
                    from_location, to_location, vehicle_no, vehicle_type, weight, size_l, size_w, size_h, actual_weight, charged_weight,
                    pkg, pkg_type, description, invoice, value, chno, total_freight, hamali, detention, others, st_charge, neft, cash,
                    tds, ded, bill_no, bill_date, bill, bill_party, broker, frt_type, pay_type, comm, paid)
                VALUES (
                    @Sr, @LRNo, @Date, @ConsignorName, @ConsignorAddress, @ConsignorGST, @ConsigneeName, @ConsigneeAddress, @ConsigneeGST,
                    @From, @To, @VehicleNo, @VehicleType, @Weight, @SizeL, @SizeW, @SizeH, @ActualWeight, @ChargedWeight,
                    @PKG, @PkgType, @Description, @Invoice, @Value, @CHNo, @TotalFreight, @Hamali, @Detention, @Others, @StCharge, @NEFT, @CASH,
                    @TDS, @Ded, @BillNo, @BillDate, @BILL, @BillParty, @Broker, @FrtType, @PayType, @Comm, @Paid)
                RETURNING id;"
            : @"UPDATE lr_entries SET
                    sr = @Sr, lrno = @LRNo, date = @Date, consignor_name = @ConsignorName, consignor_address = @ConsignorAddress, consignor_gst = @ConsignorGST,
                    consignee_name = @ConsigneeName, consignee_address = @ConsigneeAddress, consignee_gst = @ConsigneeGST,
                    from_location = @From, to_location = @To, vehicle_no = @VehicleNo, vehicle_type = @VehicleType, weight = @Weight,
                    size_l = @SizeL, size_w = @SizeW, size_h = @SizeH, actual_weight = @ActualWeight, charged_weight = @ChargedWeight,
                    pkg = @PKG, pkg_type = @PkgType, description = @Description, invoice = @Invoice, value = @Value, chno = @CHNo,
                    total_freight = @TotalFreight, hamali = @Hamali, detention = @Detention, others = @Others, st_charge = @StCharge,
                    neft = @NEFT, cash = @CASH, tds = @TDS, ded = @Ded, bill_no = @BillNo, bill_date = @BillDate, bill = @BILL,
                    bill_party = @BillParty, broker = @Broker, frt_type = @FrtType, pay_type = @PayType, comm = @Comm, paid = @Paid
                WHERE id = @Id;
               SELECT @Id;";
        return await ExecuteScalarIntAsync(sql, entry);
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
                from_loc ILIKE @search OR to_loc ILIKE @search OR mr ILIKE @search OR remarks ILIKE @search)");
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
                from_loc ILIKE @search OR to_loc ILIKE @search OR mr ILIKE @search OR remarks ILIKE @search)");
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
        QueryAsync<CBSAccountEntry>("SELECT id, sr, account_name AS AccountName, is_active AS IsActive FROM cbs_accounts ORDER BY sr, id;");

    public Task<CBSAccountEntry?> GetCBSAccountAsync(int id) =>
        QuerySingleOrDefaultAsync<CBSAccountEntry>("SELECT id, sr, account_name AS AccountName, is_active AS IsActive FROM cbs_accounts WHERE id = @id;", new { id });

    public async Task<int> UpsertCBSAccountAsync(CBSAccountEntry entry)
    {
        var sql = entry.Id <= 0
            ? @"INSERT INTO cbs_accounts (sr, account_name, is_active)
                VALUES (@Sr, @AccountName, @IsActive)
                RETURNING id;"
            : @"UPDATE cbs_accounts SET sr = @Sr, account_name = @AccountName, is_active = @IsActive
                WHERE id = @Id;
               SELECT @Id;";
        return await ExecuteScalarIntAsync(sql, entry);
    }

    public Task<IEnumerable<CashBankStatementEntry>> GetCashBankStatementsAsync(string? accountName = null, DateTime? fromDate = null, DateTime? toDate = null)
    {
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

    public Task<IEnumerable<LhsSummaryEntry>> GetLhsSummaryAsync(DateTime? fromDate = null, DateTime? toDate = null)
    {
        var sql = @"
SELECT
    cbs.date AS Date,
    COALESCE(ch.broker_name, '') AS BrokerName,
    COALESCE(ch.from_location, '') AS ""From"",
    COALESCE(ch.to_location, '') AS ""To"",
    COALESCE(ch.vehicle_number, '') AS VehicleNumber,
    cbs.bank_dr AS BankDr,
    cbs.bank_cr AS BankCr,
    cbs.cash_dr AS CashDr,
    cbs.cash_cr AS CashCr
FROM cash_bank_statements cbs
LEFT JOIN LATERAL (
    SELECT (regexp_match(COALESCE(cbs.particulars, ''), 'Challan\s+([^\s\-\|\t\r\n]+)', 'i'))[1] AS challan_number
) parsed ON TRUE
LEFT JOIN challans ch
    ON LOWER(TRIM(ch.challan_number)) = LOWER(TRIM(COALESCE(parsed.challan_number, '')))
WHERE LOWER(TRIM(cbs.account_name)) = 'lhs'";

        var conditions = new List<string>();
        var parameters = new DynamicParameters();
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

        sql += " ORDER BY cbs.date DESC, cbs.sr, cbs.id;";
        return QueryAsync<LhsSummaryEntry>(sql, parameters);
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

    public Task<IEnumerable<BillReceiptEntry>> GetBillReceiptsAsync() =>
        QueryAsync<BillReceiptEntry>(@"SELECT
            id, bill_no AS BillNo, party AS Party, bill_total AS BillTotal, bill_date AS BillDate, receipt_date AS ReceiptDate,
            rcvd AS RCVD, tds AS TDS, ded AS DED, mop AS MOP, mr AS MR, remarks AS Remarks, due_after AS DueAfter, created_at AS CreatedAt
            FROM bill_receipts ORDER BY receipt_date DESC, id DESC;");

    public async Task<int> UpsertBillReceiptAsync(BillReceiptEntry entry)
    {
        var sql = entry.Id <= 0
            ? @"INSERT INTO bill_receipts (bill_no, party, bill_total, bill_date, receipt_date, rcvd, tds, ded, mop, mr, remarks, due_after, created_at)
                VALUES (@BillNo, @Party, @BillTotal, @BillDate, @ReceiptDate, @RCVD, @TDS, @DED, @MOP, @MR, @Remarks, @DueAfter, @CreatedAt)
                RETURNING id;"
            : @"UPDATE bill_receipts SET bill_no = @BillNo, party = @Party, bill_total = @BillTotal, bill_date = @BillDate,
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
