using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace IntegrationHub.Api.Proxy;

public sealed class ProxyOptions
{
    public string ApiKey { get; set; } = "";

    /// <summary>Hosts de destino permitidos. Aceita curinga de subdomínio: "*.exemplo.com".</summary>
    public string[] AllowedHosts { get; set; } = [];

    public int MaxTimeoutSeconds { get; set; } = 120;
}

public sealed record ProxyRequest(string Url, string? Method, Dictionary<string, string>? Headers, string? Body, int? TimeoutSeconds);

public sealed class ProxyBlockedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Regras de segurança do forward proxy. Um proxy aberto vira porta de entrada para a rede interna (SSRF),
/// então tudo é negado por padrão: HTTPS, host na allowlist, IP de destino público e sem seguir redirects.
/// </summary>
public static class ProxyGuard
{
    /// <summary>Compara a API key em tempo constante (hash antes, para não vazar nem o tamanho).</summary>
    public static bool IsValidApiKey(string? provided, string expected)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(provided)) return false;
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
    }

    public static Uri ValidateTarget(string url, IReadOnlyCollection<string> allowedHosts)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ProxyBlockedException("invalid-url", "URL de destino inválida.");
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new ProxyBlockedException("https-required", "Somente destinos HTTPS são aceitos.");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new ProxyBlockedException("invalid-url", "Credenciais embutidas na URL não são aceitas.");
        if (!IsHostAllowed(uri.IdnHost, allowedHosts))
            throw new ProxyBlockedException("host-not-allowed", $"Host '{uri.IdnHost}' fora da allowlist.");
        return uri;
    }

    public static bool IsHostAllowed(string host, IReadOnlyCollection<string> allowedHosts) =>
        allowedHosts.Any(rule => rule.StartsWith("*.", StringComparison.Ordinal)
            ? host.EndsWith(rule[1..], StringComparison.OrdinalIgnoreCase) && host.Length > rule.Length - 1
            : host.Equals(rule, StringComparison.OrdinalIgnoreCase));

    /// <summary>Bloqueia loopback, redes privadas, link-local (inclui metadata de cloud 169.254.169.254), CGNAT e multicast.</summary>
    public static bool IsBlockedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                0 or 10 or 127 => true,
                100 => b[1] is >= 64 and <= 127,          // 100.64.0.0/10 (CGNAT)
                169 => b[1] == 254,                        // link-local / metadata
                172 => b[1] is >= 16 and <= 31,
                192 => b[1] == 168 || (b[1] == 0 && b[2] == 0),
                >= 224 => true,                            // multicast e reservados
                _ => false,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.Equals(IPAddress.IPv6Any) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                return true;
            var first = address.GetAddressBytes()[0];
            return (first & 0xFE) == 0xFC;                 // fc00::/7 (unique local)
        }

        return true;
    }

    /// <summary>
    /// Resolve o DNS e conecta só em IP público. A checagem é feita no momento da conexão,
    /// o que fecha a brecha de DNS rebinding (validar o nome e depois conectar em outro IP).
    /// </summary>
    public static async ValueTask<Stream> ConnectSafelyAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        var target = addresses.FirstOrDefault(a => !IsBlockedAddress(a))
                     ?? throw new ProxyBlockedException("blocked-address", "O destino resolve para um endereço interno.");

        var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}

public static class ProxyEndpoints
{
    public const string ClientName = "proxy";
    public const string ErrorHeader = "X-Proxy-Error";

    private static readonly HashSet<string> SkippedRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Connection", "Keep-Alive", "Transfer-Encoding", "Upgrade", "Proxy-Authorization", "Content-Length",
    };

    public static IServiceCollection AddSecureProxy(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<ProxyOptions>(config.GetSection("Proxy"));
        services.AddHttpClient(ClientName, c => c.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,            // redirect poderia levar a um host fora da allowlist
                UseCookies = false,
                ConnectCallback = ProxyGuard.ConnectSafelyAsync,
            });
        return services;
    }

    public static void MapSecureProxy(this IEndpointRouteBuilder app) =>
        app.MapPost("/proxy", HandleAsync).WithTags("Proxy").DisableAntiforgery();

    private static async Task<IResult> HandleAsync(
        HttpContext ctx, ProxyRequest request, IOptions<ProxyOptions> options, IHttpClientFactory httpFactory, ILoggerFactory logs)
    {
        var opts = options.Value;
        if (!ProxyGuard.IsValidApiKey(ctx.Request.Headers["X-Api-Key"], opts.ApiKey))
            return Blocked(ctx, StatusCodes.Status401Unauthorized, "unauthorized", "API key inválida.");

        Uri target;
        try
        {
            target = ProxyGuard.ValidateTarget(request.Url, opts.AllowedHosts);
        }
        catch (ProxyBlockedException ex)
        {
            return Blocked(ctx, StatusCodes.Status403Forbidden, ex.Code, ex.Message);
        }

        var message = new HttpRequestMessage(new HttpMethod(request.Method ?? "GET"), target);
        if (request.Body is not null)
            message.Content = new StringContent(request.Body, Encoding.UTF8, request.Headers?.GetValueOrDefault("Content-Type") ?? "application/json");
        foreach (var (name, value) in request.Headers ?? [])
        {
            if (SkippedRequestHeaders.Contains(name) || name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            message.Headers.TryAddWithoutValidation(name, value);
        }

        var timeout = TimeSpan.FromSeconds(Math.Clamp(request.TimeoutSeconds ?? 30, 1, opts.MaxTimeoutSeconds));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
        cts.CancelAfter(timeout);

        try
        {
            using var response = await httpFactory.CreateClient(ClientName).SendAsync(message, cts.Token);
            var body = await response.Content.ReadAsByteArrayAsync(cts.Token);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
            // Status do destino é repassado sem X-Proxy-Error: quem chama sabe que o erro foi do ERP, não do proxy.
            ctx.Response.StatusCode = (int)response.StatusCode;
            ctx.Response.ContentType = contentType;
            await ctx.Response.Body.WriteAsync(body, ctx.RequestAborted);
            return Results.Empty;
        }
        catch (HttpRequestException ex) when (ex.InnerException is ProxyBlockedException blocked)
        {
            return Blocked(ctx, StatusCodes.Status403Forbidden, blocked.Code, blocked.Message);
        }
        catch (OperationCanceledException) when (!ctx.RequestAborted.IsCancellationRequested)
        {
            return Blocked(ctx, StatusCodes.Status504GatewayTimeout, "timeout", $"Destino não respondeu em {timeout.TotalSeconds:0}s.");
        }
        catch (HttpRequestException ex)
        {
            logs.CreateLogger("Proxy").LogWarning(ex, "Falha ao chamar {Host}", target.Host);
            return Blocked(ctx, StatusCodes.Status502BadGateway, "upstream-unreachable", "Não foi possível conectar ao destino.");
        }
    }

    private static IResult Blocked(HttpContext ctx, int status, string code, string detail)
    {
        ctx.Response.Headers[ErrorHeader] = code;
        return Results.Problem(detail, statusCode: status, title: code);
    }
}
