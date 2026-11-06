using Hangfire;
using IntegrationHub.Api.Data;
using IntegrationHub.Api.Domain;
using IntegrationHub.Api.Sync;
using Microsoft.EntityFrameworkCore;

namespace IntegrationHub.Api.Endpoints;

public sealed record CreateIntegration(string Name, ErpType Erp, string BaseUrl, string? Cron);
public sealed record BackfillRequest(DateOnly From, DateOnly To);

public sealed record IntegrationView(
    Guid Id, string Name, ErpType Erp, string BaseUrl, bool Enabled, string Cron, long Records, RunView? LastRun);

public sealed record RunView(
    Guid Id, RunStatus Status, string Trigger, DateOnly From, DateOnly To, int Pages, int Records,
    string? Error, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, double? DurationSeconds);

public sealed record DashboardView(int Integrations, int Enabled, long Records, int RunsLast24h, int FailedLast24h, double? AvgDurationSeconds);

public static class IntegrationEndpoints
{
    public static void MapIntegrationEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("Integrações");

        api.MapGet("/integrations", async (HubDbContext db, CancellationToken ct) =>
        {
            var integrations = await db.Integrations.AsNoTracking().OrderBy(i => i.Name).ToListAsync(ct);
            var counts = await db.SalesRecords.GroupBy(r => r.IntegrationId)
                .Select(g => new { g.Key, Count = g.LongCount() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            var lastRuns = await db.JobRuns.AsNoTracking()
                .GroupBy(r => r.IntegrationId)
                .Select(g => g.OrderByDescending(r => r.StartedAt).First())
                .ToDictionaryAsync(r => r.IntegrationId, ct);

            return integrations.Select(i => new IntegrationView(
                i.Id, i.Name, i.Erp, i.BaseUrl, i.Enabled, i.Cron,
                counts.GetValueOrDefault(i.Id),
                lastRuns.TryGetValue(i.Id, out var run) ? ToView(run) : null));
        });

        api.MapPost("/integrations", async (CreateIntegration body, HubDbContext db, IntegrationScheduler scheduler, TimeProvider clock, CancellationToken ct) =>
        {
            var errors = Validate(body);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var integration = new Integration
            {
                Name = body.Name.Trim(),
                Erp = body.Erp,
                BaseUrl = body.BaseUrl.TrimEnd('/'),
                Cron = string.IsNullOrWhiteSpace(body.Cron) ? "*/15 * * * *" : body.Cron.Trim(),
                CreatedAt = clock.GetUtcNow(),
            };
            db.Integrations.Add(integration);
            await db.SaveChangesAsync(ct);
            scheduler.Apply(integration);
            return Results.Created($"/api/integrations/{integration.Id}", new { integration.Id });
        });

        api.MapPost("/integrations/{id:guid}/enable", (Guid id, HubDbContext db, IntegrationScheduler s, CancellationToken ct) => SetEnabled(id, true, db, s, ct));
        api.MapPost("/integrations/{id:guid}/disable", (Guid id, HubDbContext db, IntegrationScheduler s, CancellationToken ct) => SetEnabled(id, false, db, s, ct));

        api.MapPost("/integrations/{id:guid}/run", async (Guid id, HubDbContext db, IntegrationScheduler scheduler, CancellationToken ct) =>
            await db.Integrations.AnyAsync(i => i.Id == id, ct)
                ? Results.Accepted(value: new { JobId = scheduler.RunNow(id) })
                : Results.NotFound());

        api.MapPost("/integrations/{id:guid}/backfill", async (Guid id, BackfillRequest body, HubDbContext db, IntegrationScheduler scheduler, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await db.Integrations.AnyAsync(i => i.Id == id, ct)) return Results.NotFound();

            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            if (body.To < body.From || body.To > today)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["to"] = ["Período inválido: 'to' deve ser >= 'from' e não pode ser futuro."] });
            if (body.To.DayNumber - body.From.DayNumber > IntegrationScheduler.MaxBackfillDays)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["from"] = ["O período máximo de carga retroativa é de 3 anos."] });

            var jobs = scheduler.Backfill(id, body.From, body.To);
            return Results.Accepted(value: new { Windows = jobs.Count, WindowDays = IntegrationScheduler.BackfillWindowDays });
        });

        api.MapGet("/integrations/{id:guid}/runs", async (Guid id, HubDbContext db, int take = 20, CancellationToken ct = default) =>
            (await db.JobRuns.AsNoTracking().Where(r => r.IntegrationId == id)
                .OrderByDescending(r => r.StartedAt).Take(Math.Clamp(take, 1, 100)).ToListAsync(ct))
                .Select(ToView));

        api.MapGet("/dashboard", async (HubDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var since = clock.GetUtcNow().AddHours(-24);
            var recent = await db.JobRuns.AsNoTracking().Where(r => r.StartedAt >= since).ToListAsync(ct);
            var finished = recent.Where(r => r.FinishedAt is not null).ToList();
            return new DashboardView(
                await db.Integrations.CountAsync(ct),
                await db.Integrations.CountAsync(i => i.Enabled, ct),
                await db.SalesRecords.LongCountAsync(ct),
                recent.Count,
                recent.Count(r => r.Status == RunStatus.Failed),
                finished.Count == 0 ? null : finished.Average(r => (r.FinishedAt!.Value - r.StartedAt).TotalSeconds));
        });
    }

    private static async Task<IResult> SetEnabled(Guid id, bool enabled, HubDbContext db, IntegrationScheduler scheduler, CancellationToken ct)
    {
        var integration = await db.Integrations.FindAsync([id], ct);
        if (integration is null) return Results.NotFound();
        integration.Enabled = enabled;
        await db.SaveChangesAsync(ct);
        scheduler.Apply(integration);
        return Results.NoContent();
    }

    private static Dictionary<string, string[]> Validate(CreateIntegration body)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 120)
            errors["name"] = ["Informe um nome de até 120 caracteres."];
        if (!Uri.TryCreate(body.BaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            errors["baseUrl"] = ["Informe uma URL http(s) válida."];
        if (!string.IsNullOrWhiteSpace(body.Cron) && Cronos.CronExpression.TryParse(body.Cron, out _) is false)
            errors["cron"] = ["Expressão cron inválida."];
        return errors;
    }

    private static RunView ToView(JobRun r) => new(
        r.Id, r.Status, r.Trigger, r.From, r.To, r.Pages, r.Records, r.Error, r.StartedAt, r.FinishedAt,
        r.FinishedAt is null ? null : Math.Round((r.FinishedAt.Value - r.StartedAt).TotalSeconds, 2));
}
