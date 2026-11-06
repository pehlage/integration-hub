using IntegrationHub.MockErp;

// Simula dois ERPs de varejo com contratos de API diferentes e instabilidade controlada,
// para exercitar paginação, retry, circuit breaker e throttling no Integration Hub.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(new FaultInjector(builder.Configuration.GetValue("MockErp:FailureRate", 0.1)));

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    var faults = ctx.RequestServices.GetRequiredService<FaultInjector>();
    switch (faults.Next())
    {
        case Fault.Unavailable:
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        case Fault.RateLimited:
            ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ctx.Response.Headers.RetryAfter = "1";
            return;
        default:
            await next();
            break;
    }
});

app.MapGet("/health", () => Results.Ok("ok"));

// ERP "Acme": paginação por número de página.
app.MapGet("/acme/sales", (DateOnly from, DateOnly to, int page = 1, int pageSize = 100) =>
{
    pageSize = Math.Clamp(pageSize, 1, 500);
    var all = SalesGenerator.Range("acme", from, to);
    var totalPages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)pageSize));
    var items = all.Skip((page - 1) * pageSize).Take(pageSize)
        .Select(s => new { id = s.ExternalId, store = s.Store, sku = s.Sku, date = s.Date, qty = s.Quantity, total = s.Amount });
    return Results.Ok(new { page, totalPages, items });
});

// ERP "Globex": paginação por cursor e nomes de campo diferentes.
app.MapGet("/globex/v2/sales", (DateOnly date_from, DateOnly date_to, string? cursor, int limit = 100) =>
{
    limit = Math.Clamp(limit, 1, 500);
    var offset = int.TryParse(cursor, out var c) ? c : 0;
    var all = SalesGenerator.Range("globex", date_from, date_to);
    var data = all.Skip(offset).Take(limit)
        .Select(s => new { code = s.ExternalId, branch_id = s.Store, product = s.Sku, sold_at = s.Date, units = s.Quantity, gross_value = s.Amount });
    var next = offset + limit < all.Count ? (offset + limit).ToString() : null;
    return Results.Ok(new { data, next_cursor = next });
});

app.Run();
