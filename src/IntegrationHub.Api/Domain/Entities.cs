namespace IntegrationHub.Api.Domain;

public enum ErpType { Acme, Globex }

public enum RunStatus { Running, Succeeded, Failed }

public sealed class Integration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public ErpType Erp { get; set; }
    public required string BaseUrl { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Cron da sincronização recorrente (padrão: a cada 15 minutos).</summary>
    public string Cron { get; set; } = "*/15 * * * *";

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class JobRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IntegrationId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Running;
    public required string Trigger { get; set; }
    public int Pages { get; set; }
    public int Records { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public sealed class SalesRecord
{
    public long Id { get; set; }
    public Guid IntegrationId { get; set; }
    public required string ExternalId { get; set; }
    public int Store { get; set; }
    public required string Sku { get; set; }
    public DateOnly Date { get; set; }
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}
