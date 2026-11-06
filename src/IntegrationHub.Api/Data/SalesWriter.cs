using IntegrationHub.Api.Connectors;
using IntegrationHub.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace IntegrationHub.Api.Data;

/// <summary>Grava vendas de forma idempotente (upsert pela chave IntegrationId + ExternalId).</summary>
public interface ISalesWriter
{
    Task<int> UpsertAsync(Guid integrationId, IReadOnlyCollection<ErpSale> sales, CancellationToken ct);
}

/// <summary>
/// Escolhe a estratégia pelo provedor: no PostgreSQL usa um único INSERT ... ON CONFLICT com UNNEST
/// (1 round-trip por lote); em outros provedores (testes) cai no caminho genérico do EF.
/// </summary>
public sealed class SalesWriter(HubDbContext db, TimeProvider clock) : ISalesWriter
{
    public Task<int> UpsertAsync(Guid integrationId, IReadOnlyCollection<ErpSale> sales, CancellationToken ct) =>
        sales.Count == 0 ? Task.FromResult(0)
        : db.Database.IsNpgsql() ? UpsertPostgresAsync(integrationId, sales, ct)
        : UpsertGenericAsync(integrationId, sales, ct);

    private async Task<int> UpsertPostgresAsync(Guid integrationId, IReadOnlyCollection<ErpSale> sales, CancellationToken ct)
    {
        var ids = sales.Select(s => s.ExternalId).ToArray();
        var stores = sales.Select(s => s.Store).ToArray();
        var skus = sales.Select(s => s.Sku).ToArray();
        var dates = sales.Select(s => s.Date).ToArray();
        var quantities = sales.Select(s => s.Quantity).ToArray();
        var amounts = sales.Select(s => s.Amount).ToArray();
        var now = clock.GetUtcNow();

        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "SalesRecords" ("IntegrationId", "ExternalId", "Store", "Sku", "Date", "Quantity", "Amount", "SyncedAt")
            SELECT {integrationId}, u.e, u.s, u.k, u.d, u.q, u.a, {now}
            FROM UNNEST({ids}, {stores}, {skus}, {dates}, {quantities}, {amounts}) AS u(e, s, k, d, q, a)
            ON CONFLICT ("IntegrationId", "ExternalId") DO UPDATE SET
                "Store" = EXCLUDED."Store", "Sku" = EXCLUDED."Sku", "Date" = EXCLUDED."Date",
                "Quantity" = EXCLUDED."Quantity", "Amount" = EXCLUDED."Amount", "SyncedAt" = EXCLUDED."SyncedAt"
            """, ct);
        return sales.Count;
    }

    private async Task<int> UpsertGenericAsync(Guid integrationId, IReadOnlyCollection<ErpSale> sales, CancellationToken ct)
    {
        var ids = sales.Select(s => s.ExternalId).ToList();
        var existing = await db.SalesRecords
            .Where(r => r.IntegrationId == integrationId && ids.Contains(r.ExternalId))
            .ToDictionaryAsync(r => r.ExternalId, ct);
        var now = clock.GetUtcNow();

        foreach (var sale in sales)
        {
            if (!existing.TryGetValue(sale.ExternalId, out var record))
            {
                record = new SalesRecord { IntegrationId = integrationId, ExternalId = sale.ExternalId, Sku = sale.Sku };
                db.SalesRecords.Add(record);
                existing[sale.ExternalId] = record;
            }
            record.Store = sale.Store;
            record.Sku = sale.Sku;
            record.Date = sale.Date;
            record.Quantity = sale.Quantity;
            record.Amount = sale.Amount;
            record.SyncedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return sales.Count;
    }
}
