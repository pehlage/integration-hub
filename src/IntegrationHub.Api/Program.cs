using System.Text.Json.Serialization;
using Hangfire;
using Hangfire.PostgreSql;
using IntegrationHub.Api;
using IntegrationHub.Api.Connectors;
using IntegrationHub.Api.Data;
using IntegrationHub.Api.Endpoints;
using IntegrationHub.Api.Proxy;
using IntegrationHub.Api.Sync;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var usePostgres = !string.Equals(config["Database:Provider"], "InMemory", StringComparison.OrdinalIgnoreCase);
var connectionString = config.GetConnectionString("Hub");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(config.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddDbContext<HubDbContext>(o =>
{
    if (usePostgres) o.UseNpgsql(connectionString);
    else o.UseInMemoryDatabase("integration-hub");
});

builder.Services.AddHangfire(h =>
{
    h.UseSimpleAssemblyNameTypeSerializer().UseRecommendedSerializerSettings();
    if (usePostgres) h.UsePostgreSqlStorage(o => o.UseNpgsqlConnection(connectionString));
    else h.UseInMemoryStorage();
});
if (config.GetValue("Hangfire:Server", true))
    builder.Services.AddHangfireServer(o => o.WorkerCount = config.GetValue("Hangfire:Workers", 4));

builder.Services.AddErpConnectors(config);
builder.Services.AddScoped<ISalesWriter, SalesWriter>();
builder.Services.AddScoped<IntegrationSyncService>();
builder.Services.AddScoped<SyncJobs>();
builder.Services.AddScoped<IntegrationScheduler>();
builder.Services.AddSecureProxy(config);

var healthChecks = builder.Services.AddHealthChecks().AddDbContextCheck<HubDbContext>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.MapOpenApi();
app.MapHealthChecks("/health");
// Painel do Hangfire aberto apenas para a demo; em produção, protegido por autenticação.
app.MapHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [new AllowAllDashboardFilter()] });
app.MapIntegrationEndpoints();
app.MapSecureProxy();

await DatabaseSeeder.InitializeAsync(app.Services, config);

app.Run();
