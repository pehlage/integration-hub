using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using IntegrationHub.Api.Domain;

namespace IntegrationHub.Api.Connectors;

/// <summary>Formato canônico de venda: cada conector traduz o contrato do seu ERP para cá.</summary>
public sealed record ErpSale(string ExternalId, int Store, string Sku, DateOnly Date, int Quantity, decimal Amount);

public interface IErpConnector
{
    ErpType Erp { get; }

    /// <summary>Lê a janela página a página, sem carregar tudo em memória.</summary>
    IAsyncEnumerable<IReadOnlyList<ErpSale>> FetchPagesAsync(Integration integration, DateOnly from, DateOnly to, CancellationToken ct);
}

internal static class Query
{
    public static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>ERP com paginação por número de página.</summary>
public sealed class AcmeConnector(IHttpClientFactory httpFactory) : IErpConnector
{
    private const int PageSize = 200;

    public ErpType Erp => ErpType.Acme;

    public async IAsyncEnumerable<IReadOnlyList<ErpSale>> FetchPagesAsync(
        Integration integration, DateOnly from, DateOnly to, [EnumeratorCancellation] CancellationToken ct)
    {
        var http = httpFactory.CreateClient(ErpHttp.ClientName);
        var baseUrl = integration.BaseUrl.TrimEnd('/');
        int page = 1, totalPages;

        do
        {
            var url = $"{baseUrl}/acme/sales?from={Query.Date(from)}&to={Query.Date(to)}&page={page}&pageSize={PageSize}";
            var body = await http.GetFromJsonAsync<AcmePage>(url, ct)
                       ?? throw new InvalidDataException($"Resposta vazia do ERP Acme na página {page}.");
            totalPages = body.TotalPages;
            yield return body.Items.Select(i => new ErpSale(i.Id, i.Store, i.Sku, i.Date, i.Qty, i.Total)).ToList();
            page++;
        } while (page <= totalPages);
    }

    private sealed record AcmePage(int Page, int TotalPages, List<AcmeItem> Items);
    private sealed record AcmeItem(string Id, int Store, string Sku, DateOnly Date, int Qty, decimal Total);
}

/// <summary>ERP com paginação por cursor e payload em snake_case.</summary>
public sealed class GlobexConnector(IHttpClientFactory httpFactory) : IErpConnector
{
    private const int Limit = 200;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public ErpType Erp => ErpType.Globex;

    public async IAsyncEnumerable<IReadOnlyList<ErpSale>> FetchPagesAsync(
        Integration integration, DateOnly from, DateOnly to, [EnumeratorCancellation] CancellationToken ct)
    {
        var http = httpFactory.CreateClient(ErpHttp.ClientName);
        var baseUrl = integration.BaseUrl.TrimEnd('/');
        string? cursor = null;

        do
        {
            var url = $"{baseUrl}/globex/v2/sales?date_from={Query.Date(from)}&date_to={Query.Date(to)}&limit={Limit}"
                      + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var body = await http.GetFromJsonAsync<GlobexPage>(url, Json, ct)
                       ?? throw new InvalidDataException("Resposta vazia do ERP Globex.");
            yield return body.Data.Select(d => new ErpSale(d.Code, d.BranchId, d.Product, d.SoldAt, d.Units, d.GrossValue)).ToList();
            cursor = body.NextCursor;
        } while (cursor is not null);
    }

    private sealed record GlobexPage(List<GlobexItem> Data, string? NextCursor);
    private sealed record GlobexItem(string Code, int BranchId, string Product, DateOnly SoldAt, int Units, decimal GrossValue);
}
