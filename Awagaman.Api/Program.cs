using Awagaman.Api.DataAccess;
using Awagaman.Api.Models;
using Dapper;
using Microsoft.AspNetCore.Http.Json;

DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(builder.Configuration["ApiUrls"] ?? "http://0.0.0.0:5088");

builder.Services.AddSingleton<IPgConnectionFactory, PgConnectionFactory>();
builder.Services.AddSingleton<PostgresSchemaInitializer>();
builder.Services.AddSingleton<AwagamanRepository>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
    options.SerializerOptions.DictionaryKeyPolicy = null;
    options.SerializerOptions.Converters.Add(new LegacyDateTimeJsonConverter());
    options.SerializerOptions.Converters.Add(new LegacyNullableDateTimeJsonConverter());
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

var parties = app.MapGroup("/api/parties");
parties.MapGet("/", async (AwagamanRepository repo) => Results.Ok(await repo.GetPartiesAsync()));
parties.MapGet("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    var item = await repo.GetPartyAsync(id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});
parties.MapGet("/search", async (string query, AwagamanRepository repo) => Results.Ok(await repo.SearchPartiesAsync(query)));
parties.MapPost("/", async (PartyEntry party, AwagamanRepository repo) =>
{
    var id = await repo.UpsertPartyAsync(party);
    return Results.Created($"/api/parties/{id}", new { id });
});
parties.MapPut("/{id:int}", async (int id, PartyEntry party, AwagamanRepository repo) =>
{
    party.Id = id;
    await repo.UpsertPartyAsync(party);
    return Results.NoContent();
});
parties.MapDelete("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    await repo.DeletePartyAsync(id);
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
vehicles.MapPost("/", async (VehicleEntry vehicle, AwagamanRepository repo) =>
{
    var id = await repo.UpsertVehicleAsync(vehicle);
    return Results.Created($"/api/vehicles/{id}", new { id });
});
vehicles.MapPut("/{id:int}", async (int id, VehicleEntry vehicle, AwagamanRepository repo) =>
{
    vehicle.Id = id;
    await repo.UpsertVehicleAsync(vehicle);
    return Results.NoContent();
});
vehicles.MapDelete("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    await repo.DeleteVehicleAsync(id);
    return Results.NoContent();
});

var challans = app.MapGroup("/api/challans");
challans.MapGet("/", async (AwagamanRepository repo) =>
{
    try
    {
        return Results.Ok(await repo.GetChallansAsync());
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
challans.MapGet("/max-sr", async (AwagamanRepository repo) =>
{
    try
    {
        return Results.Ok(new { maxSr = await repo.GetMaxChallanSrAsync() });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
challans.MapGet("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    try
    {
        var item = await repo.GetChallanAsync(id);
        return item is null ? Results.NotFound() : Results.Ok(item);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
challans.MapPost("/", async (ChallanEntry entry, AwagamanRepository repo) =>
{
    try
    {
        var id = await repo.UpsertChallanAsync(entry);
        return Results.Created($"/api/challans/{id}", new { id });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
challans.MapPut("/{id:int}", async (int id, ChallanEntry entry, AwagamanRepository repo) =>
{
    try
    {
        entry.Id = id;
        await repo.UpsertChallanAsync(entry);
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});
challans.MapDelete("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    try
    {
        await repo.DeleteChallanAsync(id);
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString(), statusCode: 500);
    }
});

var lrs = app.MapGroup("/api/lr");
lrs.MapGet("/", async (AwagamanRepository repo) => Results.Ok(await repo.GetLREntriesAsync()));
lrs.MapGet("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    var item = await repo.GetLREntryAsync(id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});
lrs.MapPost("/", async (LREntry entry, AwagamanRepository repo) =>
{
    var id = await repo.UpsertLREntryAsync(entry);
    return Results.Created($"/api/lr/{id}", new { id });
});
lrs.MapPut("/{id:int}", async (int id, LREntry entry, AwagamanRepository repo) =>
{
    entry.Id = id;
    await repo.UpsertLREntryAsync(entry);
    return Results.NoContent();
});
lrs.MapDelete("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    await repo.DeleteLREntryAsync(id);
    return Results.NoContent();
});

var bills = app.MapGroup("/api/bills");
bills.MapGet("/", async (AwagamanRepository repo) => Results.Ok(await repo.GetBillsAsync()));
bills.MapGet("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    var item = await repo.GetBillAsync(id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});
bills.MapPost("/", async (BillEntry entry, AwagamanRepository repo) =>
{
    var id = await repo.UpsertBillAsync(entry);
    return Results.Created($"/api/bills/{id}", new { id });
});
bills.MapPut("/{id:int}", async (int id, BillEntry entry, AwagamanRepository repo) =>
{
    entry.Id = id;
    await repo.UpsertBillAsync(entry);
    return Results.NoContent();
});
bills.MapDelete("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    await repo.DeleteBillAsync(id);
    return Results.NoContent();
});

var cbsAccounts = app.MapGroup("/api/cbs/accounts");
cbsAccounts.MapGet("/", async (AwagamanRepository repo) => Results.Ok(await repo.GetCBSAccountsAsync()));
cbsAccounts.MapGet("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    var item = await repo.GetCBSAccountAsync(id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});
cbsAccounts.MapPost("/", async (CBSAccountEntry entry, AwagamanRepository repo) =>
{
    var id = await repo.UpsertCBSAccountAsync(entry);
    return Results.Created($"/api/cbs/accounts/{id}", new { id });
});
cbsAccounts.MapPut("/{id:int}", async (int id, CBSAccountEntry entry, AwagamanRepository repo) =>
{
    entry.Id = id;
    await repo.UpsertCBSAccountAsync(entry);
    return Results.NoContent();
});

var cbsEntries = app.MapGroup("/api/cbs/statements");
cbsEntries.MapGet("/", async (AwagamanRepository repo) => Results.Ok(await repo.GetCashBankStatementsAsync()));
cbsEntries.MapGet("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    var item = await repo.GetCashBankStatementAsync(id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});
cbsEntries.MapPost("/", async (CashBankStatementEntry entry, AwagamanRepository repo) =>
{
    var id = await repo.UpsertCashBankStatementAsync(entry);
    return Results.Created($"/api/cbs/statements/{id}", new { id });
});
cbsEntries.MapPut("/{id:int}", async (int id, CashBankStatementEntry entry, AwagamanRepository repo) =>
{
    entry.Id = id;
    await repo.UpsertCashBankStatementAsync(entry);
    return Results.NoContent();
});

var receipts = app.MapGroup("/api/bill-receipts");
receipts.MapGet("/", async (AwagamanRepository repo) => Results.Ok(await repo.GetBillReceiptsAsync()));
receipts.MapPost("/", async (BillReceiptEntry entry, AwagamanRepository repo) =>
{
    var id = await repo.UpsertBillReceiptAsync(entry);
    return Results.Created($"/api/bill-receipts/{id}", new { id });
});
receipts.MapPut("/{id:int}", async (int id, BillReceiptEntry entry, AwagamanRepository repo) =>
{
    entry.Id = id;
    await repo.UpsertBillReceiptAsync(entry);
    return Results.NoContent();
});

var comments = app.MapGroup("/api/comments");
comments.MapGet("/challan/{challanId:int}", async (int challanId, AwagamanRepository repo) => Results.Ok(await repo.GetChallanCommentsAsync(challanId)));
comments.MapGet("/challan/all", async (AwagamanRepository repo) => Results.Ok(await repo.GetAllChallanCommentsAsync()));
comments.MapPost("/challan", async (ChallanComment comment, AwagamanRepository repo) =>
{
    var id = await repo.AddChallanCommentAsync(comment);
    return Results.Created($"/api/comments/challan/{comment.ChallanId}", new { id });
});
comments.MapDelete("/challan/{id:int}", async (int id, AwagamanRepository repo) =>
{
    await repo.DeleteChallanCommentAsync(id);
    return Results.NoContent();
});
comments.MapGet("/lr/{lrEntryId:int}", async (int lrEntryId, AwagamanRepository repo) => Results.Ok(await repo.GetLRCommentsAsync(lrEntryId)));
comments.MapGet("/lr/all", async (AwagamanRepository repo) => Results.Ok(await repo.GetAllLRCommentsAsync()));
comments.MapPost("/lr", async (LRComment comment, AwagamanRepository repo) =>
{
    var id = await repo.AddLRCommentAsync(comment);
    return Results.Created($"/api/comments/lr/{comment.LREntryId}", new { id });
});
comments.MapDelete("/lr/{id:int}", async (int id, AwagamanRepository repo) =>
{
    await repo.DeleteLRCommentAsync(id);
    return Results.NoContent();
});
comments.MapGet("/bill/{billId:int}", async (int billId, AwagamanRepository repo) => Results.Ok(await repo.GetBillCommentsAsync(billId)));
comments.MapGet("/bill/all", async (AwagamanRepository repo) => Results.Ok(await repo.GetAllBillCommentsAsync()));
comments.MapPost("/bill", async (BillComment comment, AwagamanRepository repo) =>
{
    var id = await repo.AddBillCommentAsync(comment);
    return Results.Created($"/api/comments/bill/{comment.BillId}", new { id });
});
comments.MapDelete("/bill/{id:int}", async (int id, AwagamanRepository repo) =>
{
    await repo.DeleteBillCommentAsync(id);
    return Results.NoContent();
});

var tracking = app.MapGroup("/api/tracking");
tracking.MapGet("/", async (AwagamanRepository repo) => Results.Ok(await repo.GetTrackingAsync()));
tracking.MapGet("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    var item = await repo.GetTrackingAsync(id);
    return item is null ? Results.NotFound() : Results.Ok(item);
});
tracking.MapPost("/", async (TrackingEntry entry, AwagamanRepository repo) =>
{
    var id = await repo.UpsertTrackingAsync(entry);
    return Results.Created($"/api/tracking/{id}", new { id });
});
tracking.MapPut("/{id:int}", async (int id, TrackingEntry entry, AwagamanRepository repo) =>
{
    entry.Id = id;
    await repo.UpsertTrackingAsync(entry);
    return Results.NoContent();
});
tracking.MapPost("/{trackingEntryId:int}/reports", async (int trackingEntryId, ReportingTrackEntry track, AwagamanRepository repo) =>
{
    track.TrackingEntryId = trackingEntryId;
    var id = await repo.AddReportingTrackAsync(track);
    return Results.Created($"/api/tracking/{trackingEntryId}/reports/{id}", new { id });
});
tracking.MapGet("/{trackingEntryId:int}/reports", async (int trackingEntryId, AwagamanRepository repo) => Results.Ok(await repo.GetReportingTracksAsync(trackingEntryId)));
tracking.MapDelete("/{id:int}", async (int id, AwagamanRepository repo) =>
{
    await repo.DeleteTrackingAsync(id);
    return Results.NoContent();
});

app.Run();
