using Hangfire.Dashboard;
using IntegrationHub.Api.Data;
using IntegrationHub.Api.Domain;
using IntegrationHub.Api.Sync;
using Microsoft.EntityFrameworkCore;

namespace IntegrationHub.Api;

/// <summary>Marcador para WebApplicationFactory nos testes.</summary>
public sealed class ApiMarker;

public sealed class AllowAllDashboardFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}

public static class DatabaseSeeder
{
    /// <summary>Cria o schema, semeia duas integrações de exemplo e registra os recurring jobs das ativas.</summary>
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HubDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        // Demo: EnsureCreated mantém o "docker compose up" sem passos extras.
        // Em produção, o schema seria versionado com migrations do EF Core.
        await db.Database.EnsureCreatedAsync();

        var mockUrl = config["MockErp:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(mockUrl) && !await db.Integrations.AnyAsync())
        {
            db.Integrations.AddRange(
                new Integration { Name = "Rede Acme (matriz)", Erp = ErpType.Acme, BaseUrl = mockUrl, CreatedAt = clock.GetUtcNow() },
                new Integration { Name = "Globex Supermercados", Erp = ErpType.Globex, BaseUrl = mockUrl, CreatedAt = clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }

        var scheduler = scope.ServiceProvider.GetRequiredService<IntegrationScheduler>();
        foreach (var integration in await db.Integrations.AsNoTracking().ToListAsync())
            scheduler.Apply(integration);
    }
}
