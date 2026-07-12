using Awagaman.Api.DataAccess;
using Awagaman.Api.Models;
using Dapper;
using Microsoft.AspNetCore.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;

DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(builder.Configuration["ApiUrls"] ?? "http://0.0.0.0:5088");

var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
ConfigureJsonOptions(jsonOptions);

builder.Services.AddSingleton<IPgConnectionFactory, PgConnectionFactory>();
builder.Services.AddSingleton<PostgresSchemaInitializer>();
builder.Services.AddSingleton<AwagamanRepository>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    ConfigureJsonOptions(options.SerializerOptions);
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<PostgresSchemaInitializer>();
    await initializer.EnsureCreatedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

var authSecret = builder.Configuration["Security:AuthSecret"];
if (string.IsNullOrWhiteSpace(authSecret))
{
    authSecret = "AwagamanERP-2026-Remote-Auth-Secret";
}

var passwordPreviewSecret = builder.Configuration["Security:PasswordPreviewSecret"];
if (string.IsNullOrWhiteSpace(passwordPreviewSecret))
{
    passwordPreviewSecret = authSecret;
}

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase) ||
        IsLocalRequest(context))
    {
        await next();
        return;
    }

    var authHeader = context.Request.Headers.Authorization.ToString();
    if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Authentication required." });
        return;
    }

    var token = authHeader.Substring("Bearer ".Length).Trim();
    if (!AuthSecurity.TryValidateToken(token, authSecret, out var user) || user == null || !user.IsActive)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Invalid or expired session." });
        return;
    }

    context.Items["AuthUser"] = user;
    await next();
});

app.MapGet("/", () => Results.Ok(new
{
    service = "Awagaman ERP API",
    status = "running",
    utc = DateTime.UtcNow
}));

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    utc = DateTime.UtcNow
}));

app.MapPost("/api/auth/login", async (LoginRequest request, AwagamanRepository repo) =>
{
    var username = (request?.Username ?? string.Empty).Trim();
    var password = request?.Password ?? string.Empty;
    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
    {
        return Results.BadRequest("Username and password are required.");
    }

    var user = await repo.GetUserByUsernameAsync(username);
    if (user == null || !user.IsActive || !AuthSecurity.VerifyPassword(password, user.PasswordHash, user.PasswordSalt))
    {
        return Results.Unauthorized();
    }

    await repo.UpdateUserLastLoginAsync(user.Id);

    var authUser = new AuthenticatedUser
    {
        Id = user.Id,
        Username = user.Username,
        FullName = user.FullName,
        Role = string.IsNullOrWhiteSpace(user.Role) ? "Operator" : user.Role,
        IsActive = user.IsActive
    };

    var token = AuthSecurity.CreateToken(authUser, authSecret, TimeSpan.FromHours(12));
    return Results.Ok(new LoginResponse
    {
        Token = token,
        User = new AppUserInfo
        {
            Id = user.Id,
            Username = user.Username,
            FullName = user.FullName,
            Role = string.IsNullOrWhiteSpace(user.Role) ? "Operator" : user.Role,
            IsActive = user.IsActive,
            LastLoginUtc = user.LastLoginUtc
        }
    });
});

app.MapGet("/api/auth/me", (HttpContext context) =>
{
    var user = GetAuthenticatedUser(context);
    return user == null
        ? Results.Unauthorized()
        : Results.Ok(ToUserInfo(user, null));
});

var users = app.MapGroup("/api/users");
users.MapGet("/", async (HttpContext context, AwagamanRepository repo) =>
{
    var guard = RequireAdmin(context);
    if (guard != null) return guard;
    return Results.Ok(await repo.GetUsersAsync());
});
users.MapPost("/", async (HttpContext context, CreateUserRequest request, AwagamanRepository repo) =>
{
    var guard = RequireAdmin(context);
    if (guard != null) return guard;

    var username = (request?.Username ?? string.Empty).Trim();
    var password = request?.Password ?? string.Empty;
    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
    {
        return Results.BadRequest("Username and password are required.");
    }

    var existing = await repo.GetUserByUsernameAsync(username);
    if (existing != null)
    {
        return Results.BadRequest("Username already exists.");
    }

    var id = await repo.CreateUserAsync(request, passwordPreviewSecret);
    await WriteAuditAsync(context, repo, "Users", "Create", username, $"Created user {username}.");
    return Results.Created($"/api/users/{id}", new { id });
});
users.MapPut("/{id:int}/status", async (HttpContext context, int id, UpdateUserStatusRequest request, AwagamanRepository repo) =>
{
    var guard = RequireAdmin(context);
    if (guard != null) return guard;

    await repo.UpdateUserStatusAsync(id, request.IsActive);
    await WriteAuditAsync(context, repo, "Users", "Update", id.ToString(), request.IsActive ? "Enabled user." : "Disabled user.");
    return Results.NoContent();
});
users.MapPut("/{id:int}/password", async (HttpContext context, int id, ResetPasswordRequest request, AwagamanRepository repo) =>
{
    var guard = RequireAdmin(context);
    if (guard != null) return guard;

    if (string.IsNullOrWhiteSpace(request?.Password))
    {
        return Results.BadRequest("Password is required.");
    }

    await repo.ResetUserPasswordAsync(id, request.Password, passwordPreviewSecret);
    await WriteAuditAsync(context, repo, "Users", "Update", id.ToString(), "Reset user password.");
    return Results.NoContent();
});
users.MapGet("/{id:int}/password", async (HttpContext context, int id, AwagamanRepository repo) =>
{
    var guard = RequireAdmin(context);
    if (guard != null) return guard;
    return Results.Ok(await repo.GetUserPasswordPreviewAsync(id, passwordPreviewSecret));
});
users.MapDelete("/{id:int}", async (HttpContext context, int id, AwagamanRepository repo) =>
{
    var guard = RequireAdmin(context);
    if (guard != null) return guard;

    await repo.DeleteUserAsync(id);
    await WriteAuditAsync(context, repo, "Users", "Delete", id.ToString(), "Deleted user.");
    return Results.NoContent();
});

var audit = app.MapGroup("/api/audit");
audit.MapGet("/summary", async (HttpContext context, AwagamanRepository repo) =>
{
    var guard = RequireAdmin(context);
    if (guard != null) return guard;
    return Results.Ok(await repo.GetAuditUserSummaryAsync());
});
audit.MapGet("/recent", async (HttpContext context, int? take, AwagamanRepository repo) =>
{
    var guard = RequireAdmin(context);
    if (guard != null) return guard;
    return Results.Ok(await repo.GetRecentAuditAsync(Math.Clamp(take ?? 200, 1, 500)));
});

app.MapGet("/api/dashboard/summary", async (AwagamanRepository repo) =>
    Results.Ok(await repo.GetDashboardSummaryAsync()));

var parties = app.MapGroup("/api/parties");
parties.MapGet("/", async (AwagamanRepository repo) => Results.Ok(await repo.GetPartiesAsync()));
parties.MapGet("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    var item = await repo.GetPartyAsync(id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});
parties.MapGet("/search", async (string query, AwagamanRepository repo) => Results.Ok(await repo.SearchPartiesAsync(query)));
parties.MapPost("/", async (HttpContext context, PartyEntry party, AwagamanRepository repo) =>
{
    var id = await repo.UpsertPartyAsync(party);
    await WriteAuditAsync(context, repo, "Party Ledger", "Create", party.PartyName, $"Created party {party.PartyName}.");
    return Results.Created($"/api/parties/{id}", new { id });
});
parties.MapPut("/{id:int}", async (HttpContext context, int id, PartyEntry party, AwagamanRepository repo) =>
{
    party.Id = id;
    await repo.UpsertPartyAsync(party);
    await WriteAuditAsync(context, repo, "Party Ledger", "Update", party.PartyName, $"Updated party {party.PartyName}.");
    return Results.NoContent();
});
parties.MapDelete("/{id:int}", async (HttpContext context, int id, AwagamanRepository repo) =>
{
    await repo.DeletePartyAsync(id);
    await WriteAuditAsync(context, repo, "Party Ledger", "Delete", id.ToString(), "Deleted party.");
    return Results.NoContent();
});

var vehicles = app.MapGroup("/api/vehicles");
vehicles.MapGet("/", async (AwagamanRepository repo) => Results.Ok(await repo.GetVehiclesAsync()));
vehicles.MapGet("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    var item = await repo.GetVehicleAsync(id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});
vehicles.MapGet("/search", async (string query, AwagamanRepository repo) => Results.Ok(await repo.SearchVehiclesAsync(query)));
vehicles.MapPost("/", async (HttpContext context, VehicleEntry vehicle, AwagamanRepository repo) =>
{
    var id = await repo.UpsertVehicleAsync(vehicle);
    await WriteAuditAsync(context, repo, "Vehicle Ledger", "Create", vehicle.VehicleNumber, $"Created vehicle {vehicle.VehicleNumber}.");
    return Results.Created($"/api/vehicles/{id}", new { id });
});
vehicles.MapPut("/{id:int}", async (HttpContext context, int id, VehicleEntry vehicle, AwagamanRepository repo) =>
{
    vehicle.Id = id;
    await repo.UpsertVehicleAsync(vehicle);
    await WriteAuditAsync(context, repo, "Vehicle Ledger", "Update", vehicle.VehicleNumber, $"Updated vehicle {vehicle.VehicleNumber}.");
    return Results.NoContent();
});
vehicles.MapDelete("/{id:int}", async (HttpContext context, int id, AwagamanRepository repo) =>
{
    await repo.DeleteVehicleAsync(id);
    await WriteAuditAsync(context, repo, "Vehicle Ledger", "Delete", id.ToString(), "Deleted vehicle.");
    return Results.NoContent();
});

var challans = app.MapGroup("/api/challans");
challans.MapGet("/", async (string? ledgerKind, AwagamanRepository repo) =>
{
    try
    {
        return Results.Ok(await repo.GetChallansAsync(ledgerKind));
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
challans.MapPost("/by-numbers", async (string? ledgerKind, List<string> challanNumbers, AwagamanRepository repo) =>
{
    try
    {
        return Results.Ok(await repo.GetChallansByNumbersAsync(challanNumbers ?? new List<string>(), ledgerKind));
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
challans.MapGet("/max-sr", async (string? ledgerKind, AwagamanRepository repo) =>
{
    try
    {
        return Results.Ok(new { maxSr = await repo.GetMaxChallanSrAsync(ledgerKind) });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
challans.MapGet("/page", async (
    int page,
    int pageSize,
    string? search,
    string? sort,
    bool? asc,
    string? challanNo,
    string? lrNo,
    string? from,
    string? to,
    bool? useLhsDerived,
    string? ledgerKind,
    AwagamanRepository repo) =>
{
    try
    {
        return Results.Ok(await repo.GetChallansPageAsync(page, pageSize, search, sort, asc ?? true, challanNo, lrNo, from, to, useLhsDerived ?? false, ledgerKind));
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
challans.MapGet("/summary", async (
    string? search,
    string? challanNo,
    string? lrNo,
    string? from,
    string? to,
    bool? useLhsDerived,
    string? ledgerKind,
    AwagamanRepository repo) =>
{
    try
    {
        return Results.Ok(await repo.GetChallansSummaryAsync(search, challanNo, lrNo, from, to, useLhsDerived ?? false, ledgerKind));
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
challans.MapGet("/{id:int}", async (int id, string? ledgerKind, AwagamanRepository repo) =>
{
    try
    {
        var item = await repo.GetChallanAsync(id, ledgerKind);
        return item is null ? Results.NotFound() : Results.Ok(item);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
challans.MapPost("/", async (HttpContext context, string? ledgerKind, JsonElement body, AwagamanRepository repo) =>
{
    try
    {
        var entry = DeserializeBody<ChallanEntry>(body, jsonOptions);
        if (entry == null)
        {
            return Results.BadRequest("Invalid challan payload.");
        }

        var id = await repo.UpsertChallanAsync(entry, ledgerKind);
        await WriteAuditAsync(
            context,
            repo,
            string.Equals(ledgerKind, "challan", StringComparison.OrdinalIgnoreCase) ? "Challan Ledger" : "Purchase Ledger",
            "Create",
            entry.ChallanNumber,
            $"Saved challan {entry.ChallanNumber}.");
        return Results.Created($"/api/challans/{id}", new { id });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
challans.MapPut("/{id:int}", async (HttpContext context, int id, string? ledgerKind, JsonElement body, AwagamanRepository repo) =>
{
    try
    {
        var entry = DeserializeBody<ChallanEntry>(body, jsonOptions);
        if (entry == null)
        {
            return Results.BadRequest("Invalid challan payload.");
        }

        entry.Id = id;
        await repo.UpsertChallanAsync(entry, ledgerKind);
        await WriteAuditAsync(
            context,
            repo,
            string.Equals(ledgerKind, "challan", StringComparison.OrdinalIgnoreCase) ? "Challan Ledger" : "Purchase Ledger",
            "Update",
            entry.ChallanNumber,
            $"Updated challan {entry.ChallanNumber}.");
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
challans.MapDelete("/{id:int}", async (HttpContext context, int id, string? ledgerKind, AwagamanRepository repo) =>
{
    try
    {
        await repo.DeleteChallanAsync(id, ledgerKind);
        await WriteAuditAsync(
            context,
            repo,
            string.Equals(ledgerKind, "challan", StringComparison.OrdinalIgnoreCase) ? "Challan Ledger" : "Purchase Ledger",
            "Delete",
            id.ToString(),
            "Deleted challan.");
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});

var lrs = app.MapGroup("/api/lr");
lrs.MapGet("/", async (AwagamanRepository repo) => Results.Ok(await repo.GetLREntriesAsync()));
lrs.MapGet("/page", async (int page, int pageSize, string? search, string? sort, bool? asc, AwagamanRepository repo) =>
{
    try
    {
        return Results.Ok(await repo.GetLREntriesPageAsync(page, pageSize, search, sort, asc ?? true));
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
lrs.MapGet("/summary", async (string? search, AwagamanRepository repo) =>
{
    try
    {
        return Results.Ok(await repo.GetLREntriesSummaryAsync(search));
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
lrs.MapGet("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    var item = await repo.GetLREntryAsync(id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});
lrs.MapPost("/", async (HttpContext context, LREntry entry, AwagamanRepository repo) =>
{
    var id = await repo.UpsertLREntryAsync(entry);
    await WriteAuditAsync(context, repo, "LR Ledger", "Create", entry.LRNo, $"Saved LR {entry.LRNo}.");
    return Results.Created($"/api/lr/{id}", new { id });
});
lrs.MapPut("/{id:int}", async (HttpContext context, int id, LREntry entry, AwagamanRepository repo) =>
{
    entry.Id = id;
    await repo.UpsertLREntryAsync(entry);
    await WriteAuditAsync(context, repo, "LR Ledger", "Update", entry.LRNo, $"Updated LR {entry.LRNo}.");
    return Results.NoContent();
});
lrs.MapDelete("/{id:int}", async (HttpContext context, int id, AwagamanRepository repo) =>
{
    await repo.DeleteLREntryAsync(id);
    await WriteAuditAsync(context, repo, "LR Ledger", "Delete", id.ToString(), "Deleted LR.");
    return Results.NoContent();
});
lrs.MapPost("/reset-all", async (HttpContext context, AwagamanRepository repo) =>
{
    await repo.ResetLRDataAsync();
    await WriteAuditAsync(context, repo, "LR Ledger", "Reset", "All LR Data", "Deleted all LR ledger data.");
    return Results.NoContent();
});

var bills = app.MapGroup("/api/bills");
bills.MapGet("/", async (AwagamanRepository repo) =>
{
    try
    {
        return Results.Ok(await repo.GetBillsAsync());
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
bills.MapGet("/summary", async (string? search, string? party, bool? dueOnly, AwagamanRepository repo) =>
{
    try
    {
        return Results.Ok(await repo.GetBillsSummaryAsync(search, party, dueOnly ?? false));
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
bills.MapGet("/page", async (int page, int pageSize, string? search, string? sort, bool? asc, string? party, bool? dueOnly, AwagamanRepository repo) =>
{
    try
    {
        return Results.Ok(await repo.GetBillsPageAsync(page, pageSize, search, sort, asc ?? true, party, dueOnly ?? false));
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
bills.MapGet("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    try
    {
        var item = await repo.GetBillAsync(id);
        return item is null ? Results.NotFound() : Results.Ok(item);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
bills.MapPost("/", async (HttpContext context, BillEntry entry, AwagamanRepository repo) =>
{
    try
    {
        var id = await repo.UpsertBillAsync(entry);
        await WriteAuditAsync(context, repo, "Bill Ledger", "Create", entry.BillNo, $"Saved bill {entry.BillNo}.");
        return Results.Created($"/api/bills/{id}", new { id });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
bills.MapPost("/reset-all", async (HttpContext context, AwagamanRepository repo) =>
{
    try
    {
        await repo.ResetBillDataAsync();
        await WriteAuditAsync(context, repo, "Bill Ledger", "Reset", null, "Reset bill ledger data.");
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
bills.MapPut("/{id:int}", async (HttpContext context, int id, BillEntry entry, AwagamanRepository repo) =>
{
    try
    {
        entry.Id = id;
        await repo.UpsertBillAsync(entry);
        await WriteAuditAsync(context, repo, "Bill Ledger", "Update", entry.BillNo, $"Updated bill {entry.BillNo}.");
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
bills.MapDelete("/{id:int}", async (HttpContext context, int id, AwagamanRepository repo) =>
{
    try
    {
        await repo.DeleteBillAsync(id);
        await WriteAuditAsync(context, repo, "Bill Ledger", "Delete", id.ToString(), "Deleted bill.");
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});

var cbsAccounts = app.MapGroup("/api/cbs/accounts");
cbsAccounts.MapGet("/", async (AwagamanRepository repo) => Results.Ok(await repo.GetCBSAccountsAsync()));
cbsAccounts.MapGet("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    var item = await repo.GetCBSAccountAsync(id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});
cbsAccounts.MapPost("/", async (HttpContext context, CBSAccountEntry entry, AwagamanRepository repo) =>
{
    var id = await repo.UpsertCBSAccountAsync(entry);
    await WriteAuditAsync(context, repo, "CBS Accounts", "Create", entry.AccountName, $"Saved CBS account {entry.AccountName}.");
    return Results.Created($"/api/cbs/accounts/{id}", new { id });
});
cbsAccounts.MapPut("/{id:int}", async (HttpContext context, int id, CBSAccountEntry entry, AwagamanRepository repo) =>
{
    entry.Id = id;
    await repo.UpsertCBSAccountAsync(entry);
    await WriteAuditAsync(context, repo, "CBS Accounts", "Update", entry.AccountName, $"Updated CBS account {entry.AccountName}.");
    return Results.NoContent();
});
cbsAccounts.MapDelete("/{id:int}", async (HttpContext context, int id, AwagamanRepository repo) =>
{
    await repo.DeleteCBSAccountAsync(id);
    await WriteAuditAsync(context, repo, "CBS Accounts", "Delete", id.ToString(), "Deleted CBS account.");
    return Results.NoContent();
});

var cbsEntries = app.MapGroup("/api/cbs/statements");
cbsEntries.MapGet("/", async (string? account, DateTime? fromDate, DateTime? toDate, AwagamanRepository repo) =>
    Results.Ok(await repo.GetCashBankStatementsAsync(account, fromDate, toDate)));
cbsEntries.MapGet("/lhs-summary", async (string? account, DateTime? fromDate, DateTime? toDate, AwagamanRepository repo) =>
    Results.Ok(await repo.GetLhsSummaryAsync(fromDate, toDate, account)));
cbsEntries.MapGet("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    var item = await repo.GetCashBankStatementAsync(id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});
cbsEntries.MapPost("/", async (HttpContext context, CashBankStatementEntry entry, AwagamanRepository repo) =>
{
    var id = await repo.UpsertCashBankStatementAsync(entry);
    await WriteAuditAsync(context, repo, "CBS Ledger", "Create", entry.AccountName, $"Saved CBS entry for {entry.AccountName}.");
    return Results.Created($"/api/cbs/statements/{id}", new { id });
});
cbsEntries.MapPut("/{id:int}", async (HttpContext context, int id, CashBankStatementEntry entry, AwagamanRepository repo) =>
{
    entry.Id = id;
    await repo.UpsertCashBankStatementAsync(entry);
    await WriteAuditAsync(context, repo, "CBS Ledger", "Update", entry.AccountName, $"Updated CBS entry for {entry.AccountName}.");
    return Results.NoContent();
});
cbsEntries.MapDelete("/{id:int}", async (HttpContext context, int id, AwagamanRepository repo) =>
{
    await repo.DeleteCashBankStatementAsync(id);
    await WriteAuditAsync(context, repo, "CBS Ledger", "Delete", id.ToString(), "Deleted CBS entry.");
    return Results.NoContent();
});

var receipts = app.MapGroup("/api/bill-receipts");
receipts.MapGet("/", async (string? billNo, AwagamanRepository repo) => Results.Ok(await repo.GetBillReceiptsAsync(billNo)));
receipts.MapPost("/", async (HttpContext context, BillReceiptEntry entry, AwagamanRepository repo) =>
{
    var id = await repo.UpsertBillReceiptAsync(entry);
    await WriteAuditAsync(context, repo, "Bill Receipts", "Create", entry.BillNo, $"Saved bill receipt for {entry.BillNo}.");
    return Results.Created($"/api/bill-receipts/{id}", new { id });
});
receipts.MapPut("/{id:int}", async (HttpContext context, int id, BillReceiptEntry entry, AwagamanRepository repo) =>
{
    entry.Id = id;
    await repo.UpsertBillReceiptAsync(entry);
    await WriteAuditAsync(context, repo, "Bill Receipts", "Update", entry.BillNo, $"Updated bill receipt for {entry.BillNo}.");
    return Results.NoContent();
});
receipts.MapDelete("/{id:int}", async (HttpContext context, int id, AwagamanRepository repo) =>
{
    await repo.DeleteBillReceiptAsync(id);
    await WriteAuditAsync(context, repo, "Bill Receipts", "Delete", id.ToString(), "Deleted bill receipt.");
    return Results.NoContent();
});

var comments = app.MapGroup("/api/comments");
comments.MapGet("/challan/{challanId:int}", async (int challanId, AwagamanRepository repo) => Results.Ok(await repo.GetChallanCommentsAsync(challanId)));
comments.MapGet("/challan/all", async (AwagamanRepository repo) => Results.Ok(await repo.GetAllChallanCommentsAsync()));
comments.MapPost("/challan", async (HttpContext context, ChallanComment comment, AwagamanRepository repo) =>
{
    var id = await repo.AddChallanCommentAsync(comment);
    await WriteAuditAsync(context, repo, "Challan Comments", "Create", comment.ChallanId.ToString(), "Added challan comment.");
    return Results.Created($"/api/comments/challan/{comment.ChallanId}", new { id });
});
comments.MapDelete("/challan/{id:int}", async (HttpContext context, int id, AwagamanRepository repo) =>
{
    await repo.DeleteChallanCommentAsync(id);
    await WriteAuditAsync(context, repo, "Challan Comments", "Delete", id.ToString(), "Deleted challan comment.");
    return Results.NoContent();
});
comments.MapGet("/lr/{lrEntryId:int}", async (int lrEntryId, AwagamanRepository repo) => Results.Ok(await repo.GetLRCommentsAsync(lrEntryId)));
comments.MapGet("/lr/all", async (AwagamanRepository repo) => Results.Ok(await repo.GetAllLRCommentsAsync()));
comments.MapPost("/lr", async (HttpContext context, LRComment comment, AwagamanRepository repo) =>
{
    var id = await repo.AddLRCommentAsync(comment);
    await WriteAuditAsync(context, repo, "LR Comments", "Create", comment.LREntryId.ToString(), "Added LR comment.");
    return Results.Created($"/api/comments/lr/{comment.LREntryId}", new { id });
});
comments.MapDelete("/lr/{id:int}", async (HttpContext context, int id, AwagamanRepository repo) =>
{
    await repo.DeleteLRCommentAsync(id);
    await WriteAuditAsync(context, repo, "LR Comments", "Delete", id.ToString(), "Deleted LR comment.");
    return Results.NoContent();
});
comments.MapGet("/bill/{billId:int}", async (int billId, AwagamanRepository repo) => Results.Ok(await repo.GetBillCommentsAsync(billId)));
comments.MapGet("/bill/all", async (AwagamanRepository repo) => Results.Ok(await repo.GetAllBillCommentsAsync()));
comments.MapPost("/bill", async (HttpContext context, BillComment comment, AwagamanRepository repo) =>
{
    var id = await repo.AddBillCommentAsync(comment);
    await WriteAuditAsync(context, repo, "Bill Comments", "Create", comment.BillId.ToString(), "Added bill comment.");
    return Results.Created($"/api/comments/bill/{comment.BillId}", new { id });
});
comments.MapDelete("/bill/{id:int}", async (HttpContext context, int id, AwagamanRepository repo) =>
{
    await repo.DeleteBillCommentAsync(id);
    await WriteAuditAsync(context, repo, "Bill Comments", "Delete", id.ToString(), "Deleted bill comment.");
    return Results.NoContent();
});

app.MapPost("/api/admin/reset-all", async (HttpContext context, AwagamanRepository repo) =>
{
    await repo.ResetAllDataAsync();
    await WriteAuditAsync(context, repo, "System", "Reset", "All Data", "Deleted all application data.");
    return Results.NoContent();
});

var tracking = app.MapGroup("/api/tracking");
tracking.MapGet("/", async (AwagamanRepository repo) => Results.Ok(await repo.GetTrackingAsync()));
tracking.MapGet("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    var item = await repo.GetTrackingAsync(id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});
tracking.MapPost("/", async (HttpContext context, TrackingEntry entry, AwagamanRepository repo) =>
{
    var id = await repo.UpsertTrackingAsync(entry);
    await WriteAuditAsync(context, repo, "Tracking Ledger", "Create", entry.ChallanNo, $"Saved tracking row for {entry.ChallanNo}.");
    return Results.Created($"/api/tracking/{id}", new { id });
});
tracking.MapPut("/{id:int}", async (HttpContext context, int id, TrackingEntry entry, AwagamanRepository repo) =>
{
    entry.Id = id;
    await repo.UpsertTrackingAsync(entry);
    await WriteAuditAsync(context, repo, "Tracking Ledger", "Update", entry.ChallanNo, $"Updated tracking row for {entry.ChallanNo}.");
    return Results.NoContent();
});
tracking.MapPost("/{trackingEntryId:int}/reports", async (HttpContext context, int trackingEntryId, ReportingTrackEntry track, AwagamanRepository repo) =>
{
    track.TrackingEntryId = trackingEntryId;
    var id = await repo.AddReportingTrackAsync(track);
    await WriteAuditAsync(context, repo, "Tracking Reports", "Create", trackingEntryId.ToString(), "Added tracking report.");
    return Results.Created($"/api/tracking/{trackingEntryId}/reports/{id}", new { id });
});
tracking.MapGet("/{trackingEntryId:int}/reports", async (int trackingEntryId, AwagamanRepository repo) => Results.Ok(await repo.GetReportingTracksAsync(trackingEntryId)));
tracking.MapDelete("/{id:int}", async (HttpContext context, int id, AwagamanRepository repo) =>
{
    await repo.DeleteTrackingAsync(id);
    await WriteAuditAsync(context, repo, "Tracking Ledger", "Delete", id.ToString(), "Deleted tracking row.");
    return Results.NoContent();
});

app.Run();

void ConfigureJsonOptions(JsonSerializerOptions options)
{
    options.PropertyNamingPolicy = null;
    options.DictionaryKeyPolicy = null;
    options.Converters.Add(new LegacyDateTimeJsonConverter());
    options.Converters.Add(new LegacyNullableDateTimeJsonConverter());
}

static T? DeserializeBody<T>(JsonElement body, JsonSerializerOptions options)
{
    var raw = body.GetRawText();
    if (string.IsNullOrWhiteSpace(raw))
    {
        return default;
    }

    return JsonSerializer.Deserialize<T>(raw, options);
}

static bool IsLocalRequest(HttpContext context)
{
    var remoteIp = context.Connection.RemoteIpAddress;
    if (remoteIp == null)
    {
        return true;
    }

    return IPAddress.IsLoopback(remoteIp)
        || remoteIp.Equals(context.Connection.LocalIpAddress)
        || remoteIp.ToString() == "::1";
}

static AuthenticatedUser? GetAuthenticatedUser(HttpContext context)
{
    return context.Items.TryGetValue("AuthUser", out var value) ? value as AuthenticatedUser : null;
}

static IResult? RequireAdmin(HttpContext context)
{
    var user = GetAuthenticatedUser(context);
    if (user == null)
    {
        return Results.Unauthorized();
    }

    return string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase)
        ? null
        : Results.StatusCode(StatusCodes.Status403Forbidden);
}

static AppUserInfo ToUserInfo(AuthenticatedUser user, DateTime? lastLoginUtc)
{
    return new AppUserInfo
    {
        Id = user.Id,
        Username = user.Username,
        FullName = user.FullName,
        Role = user.Role,
        IsActive = user.IsActive,
        LastLoginUtc = lastLoginUtc
    };
}

static async Task WriteAuditAsync(HttpContext context, AwagamanRepository repo, string area, string actionType, string entityKey, string details)
{
    var user = GetAuthenticatedUser(context);
    if (user == null)
    {
        return;
    }

    await repo.AddAuditLogAsync(new AuditLogEntry
    {
        UserId = user.Id,
        Username = user.Username,
        FullName = user.FullName,
        Role = user.Role,
        ActionArea = area,
        ActionType = actionType,
        EntityKey = entityKey,
        Details = details,
        CreatedUtc = DateTime.UtcNow
    });
}
