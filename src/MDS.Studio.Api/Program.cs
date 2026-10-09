using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Encodings.Web;
using MDS.SharedKernel;
using MDS.Studio.Application;
using MDS.Studio.Infrastructure;
using MDS.Studio.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<StudioDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Studio")));
builder.Services.AddSingleton<IClock, MDS.SharedKernel.SystemClock>();
builder.Services.AddSingleton<IFileStorage>(_ => new LocalFileStorage(builder.Configuration["Storage:Root"] ?? "storage"));
builder.Services.AddScoped<IInbox, EfInbox>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("postgresql");

var identityOptions = builder.Configuration.GetSection(SharedIdentityOptions.SectionName).Get<SharedIdentityOptions>() ?? new SharedIdentityOptions();
builder.Services.AddSingleton(identityOptions);
if (identityOptions.Mode.Equals(SharedIdentityOptions.DevelopmentMode, StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddAuthentication(DevelopmentAuthHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevelopmentAuthHandler>(DevelopmentAuthHandler.SchemeName, _ => { });
}
else if (identityOptions.Mode.Equals(SharedIdentityOptions.CoreSessionMode, StringComparison.OrdinalIgnoreCase))
{
    if (!Uri.TryCreate(identityOptions.CoreBaseUrl, UriKind.Absolute, out var coreBaseUri))
    {
        throw new InvalidOperationException(
            "MDS-STUDIO-IDENTITY-001: Core session authentication requires a valid Authentication:CoreBaseUrl.");
    }

    if (identityOptions.RequireHttps && !coreBaseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException(
            "MDS-STUDIO-IDENTITY-001: Authentication:CoreBaseUrl must use HTTPS when Authentication:RequireHttps is true.");
    }

    var coreApiBaseUri = new Uri(coreBaseUri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    builder.Services.AddHttpClient(CoreSessionAuthHandler.HttpClientName, client => client.BaseAddress = coreApiBaseUri);
    builder.Services.AddAuthentication(CoreSessionAuthHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, CoreSessionAuthHandler>(CoreSessionAuthHandler.SchemeName, _ => { });
}
else
{
    throw new InvalidOperationException($"MDS-STUDIO-IDENTITY-001: Unsupported Authentication:Mode '{identityOptions.Mode}'.");
}
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(Permissions.StudioAccess, p => p.RequireClaim("permission", Permissions.StudioAccess));
    o.AddPolicy(Permissions.AdminDiagnostics, p => p.RequireClaim("permission", Permissions.AdminDiagnostics));
    o.AddPolicy(Permissions.AgentManage, p => p.RequireClaim("permission", Permissions.AgentManage));
});
builder.Services.AddHttpContextAccessor();
var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    using var migrationScope = app.Services.CreateScope();
    var database = migrationScope.ServiceProvider.GetRequiredService<StudioDbContext>();
    await database.Database.MigrateAsync();
}

app.Use(async (context, next) =>
{
    var correlation = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    if (!Guid.TryParse(correlation, out var id)) id = Guid.NewGuid();
    context.Response.Headers["X-Correlation-ID"] = id.ToString("D");
    using (app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id, ["Component"] = "MDS.Studio.Api" }))
    { await next(); }
});
app.UseAuthentication(); app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapGet("/version", (IHostEnvironment env) => Results.Ok(new { product = "MDS Studio API", version = "0.0.1", environment = env.EnvironmentName }));
app.MapGet("/me", (ClaimsPrincipal user) => Results.Ok(new { id = user.FindFirstValue(ClaimTypes.NameIdentifier), name = user.Identity?.Name, permissions = user.FindAll("permission").Select(x => x.Value) })).RequireAuthorization(Permissions.StudioAccess);
app.MapGet("/diagnostics", (IHostEnvironment env) => Results.Ok(new { environment = env.EnvironmentName, api = "0.0.1" })).RequireAuthorization(Permissions.AdminDiagnostics);
app.MapPost("/agents/enrollment-requests", async (EnrollmentRequest request, StudioDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.MachineName) || string.IsNullOrWhiteSpace(request.AgentVersion))
        return Results.BadRequest(new { code = "MDS-STUDIO-AGENT-001", message = "Machine name and agent version are required." });
    var agent = new LocalAgent { MachineName = request.MachineName, Version = request.AgentVersion, IsAuthorized = false };
    db.LocalAgents.Add(agent);
    db.AgentCapabilities.AddRange(request.Capabilities.Select(c => new AgentCapability { AgentId = agent.Id, Name = c.Name, Version = c.Version }));
    await db.SaveChangesAsync(ct);
    return Results.Accepted($"/agents/{agent.Id}", new { agentId = agent.Id, status = "PENDING_AUTHORIZATION" });
});
app.MapGet("/agents", async (StudioDbContext db, CancellationToken ct) =>
{
    var agents = await db.LocalAgents
        .OrderBy(x => x.MachineName)
        .Select(x => new { agentId = x.Id, x.MachineName, x.Version, x.IsAuthorized, x.LastHeartbeatAt })
        .ToListAsync(ct);
    return Results.Ok(agents);
}).RequireAuthorization(Permissions.AgentManage);
app.MapPost("/agents/{agentId:guid}/authorize", async (Guid agentId, StudioDbContext db, CancellationToken ct) =>
{
    var agent = await db.LocalAgents.FindAsync([agentId], ct);
    if (agent is null) return Results.NotFound(new { code = "MDS-STUDIO-AGENT-001", message = "Agent not found." });
    agent.IsAuthorized = true; await db.SaveChangesAsync(ct);
    return Results.Ok(new { agentId, status = "AUTHORIZED" });
}).RequireAuthorization(Permissions.AgentManage);
app.MapPost("/agents/{agentId:guid}/heartbeat", async (Guid agentId, HeartbeatRequest request, StudioDbContext db, CancellationToken ct) =>
{
    var agent = await db.LocalAgents.FindAsync([agentId], ct);
    if (agent is null || !agent.IsAuthorized) return Results.StatusCode(StatusCodes.Status403Forbidden);
    agent.Version = request.AgentVersion; agent.LastHeartbeatAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct); return Results.Ok(new { agentId, status = "ONLINE" });
});
app.MapPost("/agents/{agentId:guid}/jobs", async (Guid agentId, JobRequest request, HttpContext context, StudioDbContext db, CancellationToken ct) =>
{
    var agent = await db.LocalAgents.FindAsync([agentId], ct);
    if (agent is null || !agent.IsAuthorized) return Results.NotFound(new { code = "MDS-STUDIO-AGENT-001", message = "Authorized agent not found." });
    if (request.Type != "AgentPingJob") return Results.BadRequest(new { code = "MDS-STUDIO-AGENT-001", message = "Only AgentPingJob is available in M0." });
    var correlationId = context.Response.Headers["X-Correlation-ID"].ToString();
    var job = new BackgroundJob { TargetAgentId = agentId, Type = request.Type, CorrelationId = correlationId };
    db.BackgroundJobs.Add(job);
    db.OutboxEvents.Add(new OutboxEvent { Type = "AgentJobQueued", Payload = job.Id.ToString(), CorrelationId = correlationId, OccurredAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync(ct); return Results.Accepted($"/jobs/{job.Id}", new { jobId = job.Id, status = job.Status.ToString(), correlationId });
}).RequireAuthorization(Permissions.AgentManage);
app.MapGet("/jobs/{jobId:guid}", async (Guid jobId, StudioDbContext db, CancellationToken ct) =>
{
    var job = await db.BackgroundJobs.FindAsync([jobId], ct);
    return job is null
        ? Results.NotFound(new { code = "MDS-STUDIO-AGENT-001", message = "Job not found." })
        : Results.Ok(new { jobId = job.Id, job.TargetAgentId, job.Type, status = job.Status.ToString(), job.CorrelationId });
}).RequireAuthorization(Permissions.AgentManage);
app.MapGet("/agents/{agentId:guid}/jobs/next", async (Guid agentId, StudioDbContext db, CancellationToken ct) =>
{
    var agent = await db.LocalAgents.FindAsync([agentId], ct);
    if (agent is null || !agent.IsAuthorized) return Results.StatusCode(StatusCodes.Status403Forbidden);
    var job = await db.BackgroundJobs.OrderBy(x => x.Id)
        .FirstOrDefaultAsync(x => x.TargetAgentId == agentId && x.Status == JobStatus.Queued, ct);
    if (job is null) return Results.NoContent();
    job.TransitionTo(JobStatus.Running);
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { jobId = job.Id, job.Type, job.CorrelationId });
});
app.MapPost("/agents/{agentId:guid}/jobs/{jobId:guid}/complete", async (Guid agentId, Guid jobId, StudioDbContext db, CancellationToken ct) =>
{
    var job = await db.BackgroundJobs.FindAsync([jobId], ct);
    if (job is null || job.TargetAgentId != agentId) return Results.NotFound(new { code = "MDS-STUDIO-AGENT-001", message = "Job not found for this agent." });
    if (job.Status == JobStatus.Completed) return Results.Ok(new { jobId, status = "COMPLETED" });
    if (job.Status != JobStatus.Running) return Results.BadRequest(new { code = "MDS-STUDIO-AGENT-001", message = "Job is not running." });
    if (!await db.InboxEvents.AnyAsync(x => x.Id == jobId, ct))
        db.InboxEvents.Add(new InboxEvent { Id = jobId, ProcessedAt = DateTimeOffset.UtcNow });
    job.TransitionTo(JobStatus.Completed);
    db.OutboxEvents.Add(new OutboxEvent { Type = "AgentJobCompleted", Payload = jobId.ToString(), CorrelationId = job.CorrelationId, OccurredAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { jobId, status = "COMPLETED" });
});
app.Run();

public partial class Program { }
public sealed class SharedIdentityOptions
{
    public const string SectionName = "Authentication";
    public const string DevelopmentMode = "Development";
    public const string CoreSessionMode = "CoreSession";

    public string Mode { get; init; } = CoreSessionMode;
    public string? CoreBaseUrl { get; init; }
    public bool RequireHttps { get; init; } = true;
}
public sealed class DatabaseHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
 public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
 {
  try { using var scope = scopeFactory.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<StudioDbContext>(); return await db.Database.CanConnectAsync(cancellationToken) ? HealthCheckResult.Healthy("PostgreSQL is reachable.") : HealthCheckResult.Unhealthy("PostgreSQL is unavailable."); }
  catch (Exception exception) { return HealthCheckResult.Unhealthy("PostgreSQL is unavailable.", exception); }
 }
}
public sealed class DevelopmentAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
 public const string SchemeName = "development";
 protected override Task<AuthenticateResult> HandleAuthenticateAsync()
 {
  var claims = new[] { new Claim(ClaimTypes.NameIdentifier, Request.Headers["X-MDS-User"].FirstOrDefault() ?? "development-user"), new Claim(ClaimTypes.Name, "Development User"), new Claim("permission", Permissions.StudioAccess), new Claim("permission", Permissions.AdminDiagnostics), new Claim("permission", Permissions.AgentManage) };
  return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
 }
}
public sealed class CoreSessionAuthHandler(
 IOptionsMonitor<AuthenticationSchemeOptions> options,
 ILoggerFactory logger,
 UrlEncoder encoder,
 IHttpClientFactory httpClientFactory) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
 public const string SchemeName = "mds-core-session";
 public const string HttpClientName = "mds-core-identity";
 private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

 protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
 {
  var authorization = Request.Headers.Authorization.FirstOrDefault();
  if (string.IsNullOrWhiteSpace(authorization)) return AuthenticateResult.NoResult();
  if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.Fail("Unsupported authorization scheme.");

  using var request = new HttpRequestMessage(HttpMethod.Get, "auth/me");
  request.Headers.TryAddWithoutValidation("Authorization", authorization);
  using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, Context.RequestAborted);
  if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden) return AuthenticateResult.Fail("MDS Core session is invalid or expired.");
  if (!response.IsSuccessStatusCode) return AuthenticateResult.Fail("MDS Core identity service is unavailable.");

  var identity = await JsonSerializer.DeserializeAsync<CoreIdentityResponse>(await response.Content.ReadAsStreamAsync(Context.RequestAborted), JsonOptions, Context.RequestAborted);
  if (identity is null || string.IsNullOrWhiteSpace(identity.Id) || string.IsNullOrWhiteSpace(identity.Username)) return AuthenticateResult.Fail("MDS Core returned an invalid identity response.");

  var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, identity.Id), new(ClaimTypes.Name, identity.Username) };
  if (!string.IsNullOrWhiteSpace(identity.Email)) claims.Add(new Claim(ClaimTypes.Email, identity.Email));
  claims.AddRange((identity.Roles ?? []).Distinct(StringComparer.Ordinal).Select(role => new Claim(ClaimTypes.Role, role)));
  claims.AddRange((identity.Permissions ?? []).Distinct(StringComparer.Ordinal).Select(permission => new Claim("permission", permission)));
  return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
 }

 private sealed record CoreIdentityResponse(string? Id, string? Username, string? Email, string[]? Roles, string[]? Permissions);
}
