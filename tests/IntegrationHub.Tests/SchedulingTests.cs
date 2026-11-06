using System.Diagnostics;
using IntegrationHub.Api.Connectors;
using IntegrationHub.Api.Sync;

namespace IntegrationHub.Tests;

public class DateWindowsTests
{
    [Fact]
    public void Quebra_periodo_em_janelas_sem_buracos_nem_sobreposicao()
    {
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 1, 20);

        var windows = DateWindows.Split(from, to, 7);

        Assert.Equal(
            [(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 7)),
             (new DateOnly(2024, 1, 8), new DateOnly(2024, 1, 14)),
             (new DateOnly(2024, 1, 15), new DateOnly(2024, 1, 20))],
            windows);
    }

    [Fact]
    public void Periodo_de_um_dia_gera_uma_janela()
    {
        var day = new DateOnly(2025, 6, 1);
        Assert.Single(DateWindows.Split(day, day, 7));
    }

    [Fact]
    public void Tres_anos_cobrem_todos_os_dias()
    {
        var from = new DateOnly(2023, 1, 1);
        var to = new DateOnly(2025, 12, 31);
        var days = DateWindows.Split(from, to, 7).Sum(w => w.To.DayNumber - w.From.DayNumber + 1);
        Assert.Equal(to.DayNumber - from.DayNumber + 1, days);
    }

    [Fact]
    public void Recusa_periodo_invertido() =>
        Assert.Throws<ArgumentException>(() => DateWindows.Split(new DateOnly(2025, 2, 1), new DateOnly(2025, 1, 1), 7));
}

public class RequestThrottlerTests
{
    [Fact]
    public async Task Nunca_ultrapassa_a_concorrencia_maxima()
    {
        var throttler = new RequestThrottler(maxConcurrency: 3, minInterval: TimeSpan.Zero);
        int inFlight = 0, peak = 0;

        await Task.WhenAll(Enumerable.Range(0, 30).Select(async _ =>
        {
            using var slot = await throttler.AcquireAsync(CancellationToken.None);
            var now = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref peak, now);
            await Task.Delay(5);
            Interlocked.Decrement(ref inFlight);
        }));

        Assert.Equal(3, peak);
        Assert.Equal(3, throttler.Available);
    }

    [Fact]
    public async Task Respeita_intervalo_minimo_entre_inicios()
    {
        var throttler = new RequestThrottler(maxConcurrency: 10, minInterval: TimeSpan.FromMilliseconds(40));
        var watch = Stopwatch.StartNew();

        for (var i = 0; i < 4; i++)
            (await throttler.AcquireAsync(CancellationToken.None)).Dispose();

        // 4 inícios = pelo menos 3 intervalos de 40 ms (margem para o timer do SO).
        Assert.True(watch.ElapsedMilliseconds >= 100, $"Levou só {watch.ElapsedMilliseconds} ms");
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)) &&
               Interlocked.CompareExchange(ref target, value, current) != current) { }
    }
}
