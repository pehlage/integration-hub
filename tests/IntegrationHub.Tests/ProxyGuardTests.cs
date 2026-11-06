using System.Net;
using IntegrationHub.Api.Proxy;

namespace IntegrationHub.Tests;

public class ProxyGuardTests
{
    private static readonly string[] Allowed = ["api.parceiro.com", "*.erp-exemplo.com.br"];

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.10")]
    [InlineData("169.254.169.254")] // metadata de cloud (AWS/Azure/GCP)
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:127.0.0.1")] // IPv4 mapeado em IPv6
    public void Bloqueia_enderecos_internos(string ip) =>
        Assert.True(ProxyGuard.IsBlockedAddress(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700:4700::1111")]
    public void Permite_enderecos_publicos(string ip) =>
        Assert.False(ProxyGuard.IsBlockedAddress(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("https://api.parceiro.com/v1/vendas")]
    [InlineData("https://loja1.erp-exemplo.com.br/api")]
    public void Aceita_https_em_host_permitido(string url) =>
        Assert.NotNull(ProxyGuard.ValidateTarget(url, Allowed));

    [Theory]
    [InlineData("http://api.parceiro.com/v1", "https-required")]
    [InlineData("https://outro.com/v1", "host-not-allowed")]
    [InlineData("https://erp-exemplo.com.br/api", "host-not-allowed")] // curinga não cobre o domínio raiz
    [InlineData("https://api.parceiro.com.evil.com/", "host-not-allowed")]
    [InlineData("https://user:pass@api.parceiro.com/", "invalid-url")]
    [InlineData("nao-e-url", "invalid-url")]
    public void Recusa_destinos_fora_da_regra(string url, string expectedCode)
    {
        var ex = Assert.Throws<ProxyBlockedException>(() => ProxyGuard.ValidateTarget(url, Allowed));
        Assert.Equal(expectedCode, ex.Code);
    }

    [Theory]
    [InlineData("segredo", "segredo", true)]
    [InlineData("segredo2", "segredo", false)]
    [InlineData("", "segredo", false)]
    [InlineData(null, "segredo", false)]
    [InlineData("qualquer", "", false)] // proxy sem chave configurada fica fechado
    public void Valida_api_key(string? provided, string expected, bool valid) =>
        Assert.Equal(valid, ProxyGuard.IsValidApiKey(provided, expected));
}
