using System.Diagnostics;
using System.Security.Claims;
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
builder.Services.AddAuthentication("development").AddScheme<AuthenticationSchemeOptions, DevelopmentAuthHandler>("development", _ => { });
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
 protected override Task<AuthenticateResult> HandleAuthenticateAsync()
 {
  var claims = new[] { new Claim(ClaimTypes.NameIdentifier, Request.Headers["X-MDS-User"].FirstOrDefault() ?? "development-user"), new Claim(ClaimTypes.Name, "Development User"), new Claim("permission", Permissions.StudioAccess), new Claim("permission", Permissions.AdminDiagnostics), new Claim("permission", Permissions.AgentManage) };
  return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
 }
}
