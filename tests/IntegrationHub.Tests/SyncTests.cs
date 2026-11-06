using IntegrationHub.Api.Connectors;
using IntegrationHub.Api.Data;
using IntegrationHub.Api.Domain;
using IntegrationHub.Api.Sync;
using IntegrationHub.MockErp;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace IntegrationHub.Tests;

/// <summary>
/// Sobe o ERP simulado em memória (sem rede) e roda a sincronização real: conector, pipeline de
/// resiliência, throttling e gravação. Com falhas injetadas, prova que o retry entrega os dados completos.
/// </summary>
public sealed class SyncTests : IDisposable
{
    private const string BaseUrl = "http://mock-erp";
    private readonly WebApplicationFactory<MockErpMarker> _erp;
    private readonly ServiceProvider _services;

    public SyncTests()
    {
        _erp = new WebApplicationFactory<MockErpMarker>()
            .WithWebHostBuilder(b => b.UseSetting("MockErp:FailureRate", "0.25"));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ErpHttp:MinIntervalMs"] = "0" })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<HubDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<ISalesWriter, SalesWriter>();
        services.AddScoped<IntegrationSyncService>();
        services.AddErpConnectors(config, o =>
            {
                // Mesma política de produção, só que sem esperar segundos entre tentativas.
                o.Retry.Delay = TimeSpan.FromMilliseconds(1);
                o.Retry.MaxRetryAttempts = 8;
                o.Retry.ShouldRetryAfterHeader = false;
                o.CircuitBreaker.MinimumThroughput = 1000;
            })
            .ConfigurePrimaryHttpMessageHandler(() => _erp.Server.CreateHandler());
        _services = services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(ErpType.Acme)]
    [InlineData(ErpType.Globex)]
    public async Task Sincroniza_todas_as_paginas_apesar_de_falhas_transitorias(ErpType erp)
    {
        var (from, to) = (new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 7));
        var integrationId = await SeedAsync(erp);
        var expected = SalesGenerator.Range(erp.ToString().ToLowerInvariant(), from, to).Count;

        var run = await RunAsync(integrationId, from, to);

        Assert.Equal(RunStatus.Succeeded, run.Status);
        Assert.Equal(expected, run.Records);
        Assert.True(run.Pages > 1, "A janela deveria exigir mais de uma página");
        Assert.Equal(expected, await CountAsync(integrationId));
    }

    [Fact]
    public async Task Ressincronizar_a_mesma_janela_nao_duplica_registros()
    {
        var (from, to) = (new DateOnly(2025, 1, 10), new DateOnly(2025, 1, 12));
        var integrationId = await SeedAsync(ErpType.Acme);

        await RunAsync(integrationId, from, to);
        var afterFirst = await CountAsync(integrationId);
        await RunAsync(integrationId, from, to);

        Assert.Equal(afterFirst, await CountAsync(integrationId));
    }

    [Fact]
    public async Task Erp_fora_do_ar_marca_a_execucao_como_falha()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var integration = new Integration { Name = "Fora do ar", Erp = ErpType.Acme, BaseUrl = $"{BaseUrl}/rota-inexistente" };
        db.Integrations.Add(integration);
        await db.SaveChangesAsync();

        var run = await RunAsync(integration.Id, new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 1));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.NotNull(run.Error);
        Assert.NotNull(run.FinishedAt);
    }

    private async Task<Guid> SeedAsync(ErpType erp)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var integration = new Integration { Name = $"Teste {erp}", Erp = erp, BaseUrl = BaseUrl };
        db.Integrations.Add(integration);
        await db.SaveChangesAsync();
        return integration.Id;
    }

    private async Task<JobRun> RunAsync(Guid integrationId, DateOnly from, DateOnly to)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IntegrationSyncService>().RunAsync(integrationId, from, to, "test");
    }

    private async Task<int> CountAsync(Guid integrationId)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<HubDbContext>().SalesRecords.CountAsync(r => r.IntegrationId == integrationId);
    }

    public void Dispose()
    {
        _services.Dispose();
        _erp.Dispose();
    }
}
