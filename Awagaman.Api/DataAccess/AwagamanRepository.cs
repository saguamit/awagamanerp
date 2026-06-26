using Awagaman.Api.Models;
using Dapper;

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

    public Task<int> DeleteVehicleAsync(int id) =>
        ExecuteAsync("DELETE FROM vehicle_ledger WHERE id = @id;", new { id });

    public Task<IEnumerable<ChallanEntry>> GetChallansAsync() =>
        QueryAsync<ChallanEntry>(@"SELECT
            id, sr, challan_number AS ChallanNumber, date, lr_number AS LRNumber, broker_name AS BrokerName,
            from_location AS ""From"", to_location AS ""To"", vehicle_number AS VehicleNumber, vehicle_type AS VehicleType,
            driver_name AS DriverName, driver_mobile AS DriverMobile, engine_no AS EngineNo, licence_no AS LicenceNo,
            policy_no AS PolicyNo, chassis_no AS ChassisNo, owner_name AS OwnerName, pan AS PAN, lorry_hire AS LorryHire,
            less_tds AS LessTDS, advance_amount AS AdvanceAmount, advance_neft AS AdvanceNEFT, advance_cash AS AdvanceCash,
            advance_date AS AdvanceDate, detention AS Detention, hamali AS Hamali, deduction AS Deduction,
            balance_paid_neft AS BalancePaidNEFT, balance_paid_cash AS BalancePaidCash, balance_paid_date AS BalancePaidDate,
            paid_to AS PaidTo, remarks AS Remarks, bill_amount AS BillAmount, margin AS Margin,
            imported_balance AS ImportedBalance, imported_due AS ImportedDue
            FROM challans ORDER BY sr, id;");

    public async Task<PagedResult<ChallanEntry>> GetChallansPageAsync(
        int page,
        int pageSize,
        string? search,
        string? sortColumn,
        bool sortAscending,
        string? challanNo = null,
        string? lrNo = null,
        string? from = null,
        string? to = null)
    {
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
        var orderBy = BuildChallanOrderBy(sortColumn, sortAscending);
        var select = @"SELECT
            id, sr, challan_number AS ChallanNumber, date, lr_number AS LRNumber, broker_name AS BrokerName,
            from_location AS ""From"", to_location AS ""To"", vehicle_number AS VehicleNumber, vehicle_type AS VehicleType,
            driver_name AS DriverName, driver_mobile AS DriverMobile, engine_no AS EngineNo, licence_no AS LicenceNo,
            policy_no AS PolicyNo, chassis_no AS ChassisNo, owner_name AS OwnerName, pan AS PAN, lorry_hire AS LorryHire,
            less_tds AS LessTDS, advance_amount AS AdvanceAmount, advance_neft AS AdvanceNEFT, advance_cash AS AdvanceCash,
            advance_date AS AdvanceDate, detention AS Detention, hamali AS Hamali, deduction AS Deduction,
            balance_paid_neft AS BalancePaidNEFT, balance_paid_cash AS BalancePaidCash, balance_paid_date AS BalancePaidDate,
            paid_to AS PaidTo, remarks AS Remarks, bill_amount AS BillAmount, margin AS Margin,
            imported_balance AS ImportedBalance, imported_due AS ImportedDue
            FROM challans";

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM challans {where};", parameters);
        var items = (await conn.QueryAsync<ChallanEntry>($"{select} {where} ORDER BY {orderBy} LIMIT @limit OFFSET @offset;", parameters)).ToList();
        return new PagedResult<ChallanEntry> { Items = items, TotalCount = total };
    }

    public Task<int> GetMaxChallanSrAsync() =>
        ExecuteScalarIntAsync("SELECT COALESCE(MAX(sr), 0) FROM challans;", new { });

    public Task<ChallanEntry?> GetChallanAsync(int id) =>
        QuerySingleOrDefaultAsync<ChallanEntry>(@"SELECT
            id, sr, challan_number AS ChallanNumber, date, lr_number AS LRNumber, broker_name AS BrokerName,
            from_location AS ""From"", to_location AS ""To"", vehicle_number AS VehicleNumber, vehicle_type AS VehicleType,
            driver_name AS DriverName, driver_mobile AS DriverMobile, engine_no AS EngineNo, licence_no AS LicenceNo,
            policy_no AS PolicyNo, chassis_no AS ChassisNo, owner_name AS OwnerName, pan AS PAN, lorry_hire AS LorryHire,
            less_tds AS LessTDS, advance_amount AS AdvanceAmount, advance_neft AS AdvanceNEFT, advance_cash AS AdvanceCash,
            advance_date AS AdvanceDate, detention AS Detention, hamali AS Hamali, deduction AS Deduction,
            balance_paid_neft AS BalancePaidNEFT, balance_paid_cash AS BalancePaidCash, balance_paid_date AS BalancePaidDate,
            paid_to AS PaidTo, remarks AS Remarks, bill_amount AS BillAmount, margin AS Margin,
            imported_balance AS ImportedBalance, imported_due AS ImportedDue
            FROM challans WHERE id = @id;", new { id });

    public async Task<int> UpsertChallanAsync(ChallanEntry entry)
    {
        var sql = entry.Id <= 0
            ? @"INSERT INTO challans (
                    sr, challan_number, date, lr_number, broker_name, from_location, to_location, vehicle_number, vehicle_type,
                    driver_name, driver_mobile, engine_no, licence_no, policy_no, chassis_no, owner_name, pan,
                    lorry_hire, less_tds, advance_amount, advance_neft, advance_cash, advance_date, detention, hamali,
                    deduction, balance_paid_neft, balance_paid_cash, balance_paid_date, paid_to, remarks, bill_amount, margin,
                    imported_balance, imported_due)
                VALUES (
                    @Sr, @ChallanNumber, @Date, @LRNumber, @BrokerName, @From, @To, @VehicleNumber, @VehicleType,
                    @DriverName, @DriverMobile, @EngineNo, @LicenceNo, @PolicyNo, @ChassisNo, @OwnerName, @PAN,
                    @LorryHire, @LessTDS, @AdvanceAmount, @AdvanceNEFT, @AdvanceCash, @AdvanceDate, @Detention, @Hamali,
                    @Deduction, @BalancePaidNEFT, @BalancePaidCash, @BalancePaidDate, @PaidTo, @Remarks, @BillAmount, @Margin,
                    @ImportedBalance, @ImportedDue)
                RETURNING id;"
            : @"UPDATE challans SET
                    sr = @Sr, challan_number = @ChallanNumber, date = @Date, lr_number = @LRNumber, broker_name = @BrokerName,
                    from_location = @From, to_location = @To, vehicle_number = @VehicleNumber, vehicle_type = @VehicleType,
                    driver_name = @DriverName, driver_mobile = @DriverMobile, engine_no = @EngineNo, licence_no = @LicenceNo,
                    policy_no = @PolicyNo, chassis_no = @ChassisNo, owner_name = @OwnerName, pan = @PAN, lorry_hire = @LorryHire,
                    less_tds = @LessTDS, advance_amount = @AdvanceAmount, advance_neft = @AdvanceNEFT, advance_cash = @AdvanceCash,
                    advance_date = @AdvanceDate, detention = @Detention, hamali = @Hamali, deduction = @Deduction,
                    balance_paid_neft = @BalancePaidNEFT, balance_paid_cash = @BalancePaidCash, balance_paid_date = @BalancePaidDate,
                    paid_to = @PaidTo, remarks = @Remarks, bill_amount = @BillAmount, margin = @Margin,
                    imported_balance = @ImportedBalance, imported_due = @ImportedDue
                WHERE id = @Id;
               SELECT @Id;";
        return await ExecuteScalarIntAsync(sql, entry);
    }

    public Task<int> DeleteChallanAsync(int id) =>
        ExecuteAsync("DELETE FROM challans WHERE id = @id;", new { id });

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
            from_loc AS FromLoc, to_loc AS ToLoc, vehicle_type AS VehicleType, freight AS Freight, detention AS Detention,
            hml AS HML, othr AS OTHR, st_charge AS StCharge, rcvd AS RCVD, tds AS TDS, ded AS DED, mop AS MOP, mr AS MR,
            remarks AS Remarks, date AS Date
            FROM bills ORDER BY bill_date DESC NULLS LAST, sr, id;");

    public async Task<PagedResult<BillEntry>> GetBillsPageAsync(int page, int pageSize, string? search, string? sortColumn, bool sortAscending)
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
            where = @"WHERE bill_no ILIKE @search OR party ILIKE @search OR lr_no ILIKE @search OR
                from_loc ILIKE @search OR to_loc ILIKE @search OR mr ILIKE @search OR remarks ILIKE @search";
            parameters.Add("search", $"%{q}%");
        }

        var orderBy = BuildBillOrderBy(sortColumn, sortAscending);
        var select = @"SELECT
            id, sr, bill_no AS BillNo, bill_date AS BillDate, party AS Party, lr_no AS LRNo, lr_date AS LRDate,
            from_loc AS FromLoc, to_loc AS ToLoc, vehicle_type AS VehicleType, freight AS Freight, detention AS Detention,
            hml AS HML, othr AS OTHR, st_charge AS StCharge, rcvd AS RCVD, tds AS TDS, ded AS DED, mop AS MOP, mr AS MR,
            remarks AS Remarks, date AS Date
            FROM bills";

        await using var conn = _factory.Create();
        await conn.OpenAsync();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM bills {where};", parameters);
        var items = (await conn.QueryAsync<BillEntry>($"{select} {where} ORDER BY {orderBy} LIMIT @limit OFFSET @offset;", parameters)).ToList();
        return new PagedResult<BillEntry> { Items = items, TotalCount = total };
    }

    public Task<BillEntry?> GetBillAsync(int id) =>
        QuerySingleOrDefaultAsync<BillEntry>(@"SELECT
            id, sr, bill_no AS BillNo, bill_date AS BillDate, party AS Party, lr_no AS LRNo, lr_date AS LRDate,
            from_loc AS FromLoc, to_loc AS ToLoc, vehicle_type AS VehicleType, freight AS Freight, detention AS Detention,
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
                    @Sr, @BillNo, @BillDate, @Party, @LRNo, @LRDate, @FromLoc, @ToLoc, @VehicleType, @Freight, @Detention,
                    @HML, @OTHR, @StCharge, @RCVD, @TDS, @DED, @MOP, @MR, @Remarks, @Date)
                RETURNING id;"
            : @"UPDATE bills SET
                    sr = @Sr, bill_no = @BillNo, bill_date = @BillDate, party = @Party, lr_no = @LRNo, lr_date = @LRDate,
                    from_loc = @FromLoc, to_loc = @ToLoc, vehicle_type = @VehicleType, freight = @Freight, detention = @Detention,
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

    public Task<IEnumerable<CashBankStatementEntry>> GetCashBankStatementsAsync() =>
        QueryAsync<CashBankStatementEntry>(@"SELECT
            id, sr, cbs AS CBS, date, account_name AS AccountName, particulars AS Particulars, remarks AS Remarks,
            bank_dr AS BankDr, bank_cr AS BankCr, cash_dr AS CashDr, cash_cr AS CashCr
            FROM cash_bank_statements ORDER BY date DESC, sr, id;");

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
        return await ExecuteScalarIntAsync(sql, entry);
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
}
