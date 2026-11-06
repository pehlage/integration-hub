using System.Collections.Concurrent;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace IntegrationHub.Api.Connectors;

public sealed class ErpHttpOptions
{
    /// <summary>Requisições simultâneas por host de ERP (muitos ERPs derrubam ou bloqueiam acima disso).</summary>
    public int MaxConcurrencyPerHost { get; set; } = 4;

    /// <summary>Intervalo mínimo entre o início de duas requisições ao mesmo host.</summary>
    public int MinIntervalMs { get; set; } = 50;
}

public static class ErpHttp
{
    public const string ClientName = "erp";

    /// <summary>
    /// Registra os conectores e o HttpClient compartilhado. Ordem do pipeline (de fora para dentro):
    /// resiliência (timeout total, retry com backoff + Retry-After, circuit breaker) → throttling por host → rede.
    /// Cada nova tentativa também passa pelo throttling, então o retry não fura o limite do ERP.
    /// </summary>
    public static IHttpClientBuilder AddErpConnectors(
        this IServiceCollection services, IConfiguration config, Action<HttpStandardResilienceOptions>? tuneResilience = null)
    {
        services.Configure<ErpHttpOptions>(config.GetSection("ErpHttp"));
        services.AddSingleton<ThrottlingHandlerState>();
        services.AddTransient<ThrottlingHandler>();
        services.AddScoped<IErpConnector, AcmeConnector>();
        services.AddScoped<IErpConnector, GlobexConnector>();

        var builder = services.AddHttpClient(ClientName);
        builder.AddStandardResilienceHandler(o =>
        {
            o.Retry.MaxRetryAttempts = 5;
            o.Retry.UseJitter = true;
            tuneResilience?.Invoke(o);
        });
        builder.AddHttpMessageHandler<ThrottlingHandler>();
        return builder;
    }
}

/// <summary>Um <see cref="RequestThrottler"/> por host, compartilhado entre todas as requisições.</summary>
public sealed class ThrottlingHandlerState(IOptions<ErpHttpOptions> options, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, RequestThrottler> _byHost = new(StringComparer.OrdinalIgnoreCase);

    public RequestThrottler For(string host) => _byHost.GetOrAdd(host, _ => new RequestThrottler(
        options.Value.MaxConcurrencyPerHost, TimeSpan.FromMilliseconds(options.Value.MinIntervalMs), clock));
}

public sealed class ThrottlingHandler(ThrottlingHandlerState state) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var host = request.RequestUri?.Authority ?? "unknown";
        using var _ = await state.For(host).AcquireAsync(ct);
        return await base.SendAsync(request, ct);
    }
}

/// <summary>Limita concorrência e cadência de requisições (semáforo + intervalo mínimo entre inícios).</summary>
public sealed class RequestThrottler(int maxConcurrency, TimeSpan minInterval, TimeProvider? clock = null)
{
    private readonly SemaphoreSlim _slots = new(maxConcurrency, maxConcurrency);
    private readonly SemaphoreSlim _cadence = new(1, 1);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private DateTimeOffset _nextStart = DateTimeOffset.MinValue;

    public int Available => _slots.CurrentCount;

    public async Task<IDisposable> AcquireAsync(CancellationToken ct)
    {
        await _slots.WaitAsync(ct);
        try
        {
            await _cadence.WaitAsync(ct);
            try
            {
                var wait = _nextStart - _clock.GetUtcNow();
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, _clock, ct);
                _nextStart = _clock.GetUtcNow() + minInterval;
            }
            finally
            {
                _cadence.Release();
            }
        }
        catch
        {
            _slots.Release();
            throw;
        }

        return new Slot(_slots);
    }

    private sealed class Slot(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                slots.Release();
        }
    }
}
