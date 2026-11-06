namespace IntegrationHub.MockErp;

/// <summary>Marcador para WebApplicationFactory nos testes.</summary>
public sealed class MockErpMarker;

public sealed record MockSale(string ExternalId, int Store, string Sku, DateOnly Date, int Quantity, decimal Amount);

/// <summary>Gera vendas determinísticas: o mesmo ERP e dia sempre devolvem os mesmos dados.</summary>
public static class SalesGenerator
{
    private const int Stores = 3;

    public static List<MockSale> Range(string erp, DateOnly from, DateOnly to)
    {
        var result = new List<MockSale>();
        for (var day = from; day <= to; day = day.AddDays(1))
            result.AddRange(ForDay(erp, day));
        return result;
    }

    public static IEnumerable<MockSale> ForDay(string erp, DateOnly day)
    {
        for (var store = 1; store <= Stores; store++)
        {
            // Seed estável entre processos (HashCode.Combine é aleatorizado por execução).
            var random = new Random(day.DayNumber * 31 + store * 7919 + erp.Sum(ch => ch));
            var count = random.Next(20, 60);
            for (var i = 0; i < count; i++)
            {
                var qty = random.Next(1, 12);
                var price = Math.Round((decimal)(random.NextDouble() * 90 + 5), 2);
                yield return new MockSale($"{erp}-{day:yyyyMMdd}-{store}-{i}", store, $"SKU-{random.Next(1000, 1100)}", day, qty, qty * price);
            }
        }
    }
}

public enum Fault { None, Unavailable, RateLimited }

/// <summary>Injeta falhas transitórias (503/429) numa taxa configurável.</summary>
public sealed class FaultInjector(double failureRate)
{
    private readonly Random _random = new();
    private readonly Lock _lock = new();

    public Fault Next()
    {
        if (failureRate <= 0) return Fault.None;
        lock (_lock)
        {
            var roll = _random.NextDouble();
            if (roll >= failureRate) return Fault.None;
            return roll < failureRate / 2 ? Fault.Unavailable : Fault.RateLimited;
        }
    }
}
