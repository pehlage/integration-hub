using Hangfire;
using IntegrationHub.Api.Connectors;
using IntegrationHub.Api.Data;
using IntegrationHub.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace IntegrationHub.Api.Sync;

public static class DateWindows
{
    /// <summary>Quebra um intervalo em janelas de até <paramref name="maxDays"/> dias (inclusivas).</summary>
    public static IReadOnlyList<(DateOnly From, DateOnly To)> Split(DateOnly from, DateOnly to, int maxDays)
    {
        if (to < from) throw new ArgumentException("A data final deve ser maior ou igual à inicial.", nameof(to));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDays, 1);

        var windows = new List<(DateOnly, DateOnly)>();
        for (var start = from; start <= to; start = start.AddDays(maxDays))
        {
            var end = start.AddDays(maxDays - 1);
            windows.Add((start, end > to ? to : end));
        }
        return windows;
    }
}

/// <summary>Executa uma sincronização: lê o ERP página a página e grava em lotes, registrando a execução.</summary>
public sealed class IntegrationSyncService(
    HubDbContext db,
    IEnumerable<IErpConnector> connectors,
    ISalesWriter writer,
    TimeProvider clock,
    ILogger<IntegrationSyncService> log)
{
    public const int BatchSize = 500;

    public async Task<JobRun> RunAsync(Guid integrationId, DateOnly from, DateOnly to, string trigger, CancellationToken ct = default)
    {
        var integration = await db.Integrations.AsNoTracking().FirstOrDefaultAsync(i => i.Id == integrationId, ct)
                          ?? throw new KeyNotFoundException($"Integração {integrationId} não encontrada.");
        var connector = connectors.Single(c => c.Erp == integration.Erp);

        var run = new JobRun { IntegrationId = integrationId, From = from, To = to, Trigger = trigger, StartedAt = clock.GetUtcNow() };
        db.JobRuns.Add(run);
        await db.SaveChangesAsync(ct);

        try
        {
            await foreach (var page in connector.FetchPagesAsync(integration, from, to, ct))
            {
                foreach (var batch in page.Chunk(BatchSize))
                    run.Records += await writer.UpsertAsync(integrationId, batch, ct);
                run.Pages++;
            }
            run.Status = RunStatus.Succeeded;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Status = RunStatus.Failed;
            run.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            log.LogError(ex, "Falha ao sincronizar {Integration} ({From}..{To})", integration.Name, from, to);
        }
        finally
        {
            run.FinishedAt = clock.GetUtcNow();
            db.ChangeTracker.Clear();
            db.JobRuns.Update(run);
            await db.SaveChangesAsync(CancellationToken.None);
        }

        log.LogInformation("Sincronização {Integration} {Status}: {Records} registros em {Pages} páginas",
            integration.Name, run.Status, run.Records, run.Pages);
        return run;
    }
}

/// <summary>Pontos de entrada chamados pelo Hangfire.</summary>
public sealed class SyncJobs(IntegrationSyncService sync, TimeProvider clock)
{
    /// <summary>Janela recorrente: ontem e hoje, para pegar vendas lançadas com atraso no ERP.</summary>
    [AutomaticRetry(Attempts = 2)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunRecentAsync(Guid integrationId, string trigger, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        await RunWindowAsync(integrationId, today.AddDays(-1), today, trigger, ct);
    }

    [AutomaticRetry(Attempts = 2)]
    public async Task RunWindowAsync(Guid integrationId, DateOnly from, DateOnly to, string trigger, CancellationToken ct)
    {
        var run = await sync.RunAsync(integrationId, from, to, trigger, ct);
        // Falha vira exceção para o Hangfire aplicar o retry automático da janela.
        if (run.Status == RunStatus.Failed)
            throw new InvalidOperationException($"Sincronização falhou: {run.Error}");
    }
}

/// <summary>Mantém os recurring jobs do Hangfire em linha com as integrações ativas.</summary>
public sealed class IntegrationScheduler(IRecurringJobManager recurring, IBackgroundJobClient jobs)
{
    public const int BackfillWindowDays = 7;
    public const int MaxBackfillDays = 3 * 366;

    public static string RecurringId(Guid id) => $"sync-{id:N}";

    public void Apply(Integration integration)
    {
        if (integration.Enabled)
            recurring.AddOrUpdate<SyncJobs>(RecurringId(integration.Id), j => j.RunRecentAsync(integration.Id, "recurring", CancellationToken.None), integration.Cron);
        else
            recurring.RemoveIfExists(RecurringId(integration.Id));
    }

    public string RunNow(Guid integrationId) =>
        jobs.Enqueue<SyncJobs>(j => j.RunRecentAsync(integrationId, "manual", CancellationToken.None));

    /// <summary>Carga retroativa: uma tarefa por janela, para retry e paralelismo por pedaço, não pelo período inteiro.</summary>
    public IReadOnlyList<string> Backfill(Guid integrationId, DateOnly from, DateOnly to) =>
        DateWindows.Split(from, to, BackfillWindowDays)
            .Select(w => jobs.Enqueue<SyncJobs>(j => j.RunWindowAsync(integrationId, w.From, w.To, "backfill", CancellationToken.None)))
            .ToList();
}
