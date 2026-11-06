using System.Net;
using System.Net.Http.Json;
using IntegrationHub.Api;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IntegrationHub.Tests;

/// <summary>Testa a API pelo HTTP, com banco e Hangfire em memória e sem worker processando jobs.</summary>
public sealed class ApiTests : IClassFixture<ApiTests.Factory>
{
    private readonly HttpClient _client;

    public ApiTests(Factory factory) => _client = factory.CreateClient();

    public sealed class Factory : WebApplicationFactory<ApiMarker>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Database:Provider", "InMemory");
            builder.UseSetting("Hangfire:Server", "false");
            builder.UseSetting("MockErp:BaseUrl", "");
            builder.UseSetting("Proxy:ApiKey", "chave-teste");
        }
    }

    [Fact]
    public async Task Cria_lista_e_desativa_integracao()
    {
        var created = await _client.PostAsJsonAsync("/api/integrations",
            new { name = "Loja Teste", erp = "Globex", baseUrl = "http://erp.local", cron = "0 * * * *" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<Created>())!.Id;

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync($"/api/integrations/{id}/disable", null)).StatusCode);

        var list = await _client.GetFromJsonAsync<List<Item>>("/api/integrations");
        var item = Assert.Single(list!, i => i.Id == id);
        Assert.False(item.Enabled);
        Assert.Equal("0 * * * *", item.Cron);
    }

    [Theory]
    [InlineData("", "http://erp.local", null)]
    [InlineData("Ok", "ftp://erp.local", null)]
    [InlineData("Ok", "http://erp.local", "isso não é cron")]
    public async Task Recusa_integracao_invalida(string name, string baseUrl, string? cron)
    {
        var response = await _client.PostAsJsonAsync("/api/integrations", new { name, erp = "Acme", baseUrl, cron });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Backfill_enfileira_uma_tarefa_por_janela_de_7_dias()
    {
        var created = await _client.PostAsJsonAsync("/api/integrations", new { name = "Backfill", erp = "Acme", baseUrl = "http://erp.local" });
        var id = (await created.Content.ReadFromJsonAsync<Created>())!.Id;

        var response = await _client.PostAsJsonAsync($"/api/integrations/{id}/backfill", new { from = "2025-01-01", to = "2025-01-31" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BackfillResult>();
        Assert.Equal(5, body!.Windows); // 31 dias / 7 = 5 janelas
    }

    [Fact]
    public async Task Backfill_maior_que_3_anos_e_recusado()
    {
        var created = await _client.PostAsJsonAsync("/api/integrations", new { name = "Longo", erp = "Acme", baseUrl = "http://erp.local" });
        var id = (await created.Content.ReadFromJsonAsync<Created>())!.Id;

        var response = await _client.PostAsJsonAsync($"/api/integrations/{id}/backfill", new { from = "2019-01-01", to = "2025-01-01" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null, "https://httpbin.org/get", HttpStatusCode.Unauthorized, "unauthorized")]
    [InlineData("chave-teste", "https://evil.com/", HttpStatusCode.Forbidden, "host-not-allowed")]
    [InlineData("chave-teste", "http://httpbin.org/get", HttpStatusCode.Forbidden, "https-required")]
    public async Task Proxy_bloqueia_antes_de_sair_para_a_rede(string? key, string url, HttpStatusCode status, string code)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/proxy") { Content = JsonContent.Create(new { url }) };
        if (key is not null) request.Headers.Add("X-Api-Key", key);

        var response = await _client.SendAsync(request);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, response.Headers.GetValues("X-Proxy-Error").Single());
    }

    [Fact]
    public async Task Health_check_responde()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record Created(Guid Id);
    private sealed record Item(Guid Id, string Name, bool Enabled, string Cron);
    private sealed record BackfillResult(int Windows, int WindowDays);
}
