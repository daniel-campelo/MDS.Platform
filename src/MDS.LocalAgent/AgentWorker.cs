using Microsoft.EntityFrameworkCore;

namespace MDS.LocalAgent;
public sealed class AgentWorker(IServiceScopeFactory scopeFactory, StudioApiClient studioApi, ILogger<AgentWorker> logger) : BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken stoppingToken)
 {
  using (var scope = scopeFactory.CreateScope())
  {
   var database = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
   await database.Database.EnsureCreatedAsync(stoppingToken);
  }
  logger.LogInformation("MDS Local Agent 0.0.1 starting; station enrollment is required before executing jobs.");
  while (!stoppingToken.IsCancellationRequested)
  {
   try
   {
    using var scope = scopeFactory.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
    var identity = await database.Identity.SingleOrDefaultAsync(stoppingToken);
    if (identity is null)
    {
     var agentId = await studioApi.RequestEnrollmentAsync(Environment.MachineName, stoppingToken);
     identity = new AgentIdentity { AgentId = agentId, MachineName = Environment.MachineName, EnrolledAt = DateTimeOffset.UtcNow };
     database.Identity.Add(identity); await database.SaveChangesAsync(stoppingToken);
     logger.LogInformation("Enrollment requested for Agent {AgentId}; awaiting administrator authorization.", agentId);
    }
    else if (await studioApi.SendHeartbeatAsync(identity.AgentId, stoppingToken))
    {
      logger.LogInformation("Heartbeat accepted for Agent {AgentId}.", identity.AgentId);
      var job = await studioApi.GetNextJobAsync(identity.AgentId, stoppingToken);
      if (job is not null)
      {
       if (job.Type != "AgentPingJob") throw new InvalidOperationException($"MDS-STUDIO-AGENT-001: Unsupported M0 job '{job.Type}'.");
       var executed = await new AgentJobExecutor(database).ExecutePingAsync(job.JobId, job.CorrelationId, stoppingToken);
       await studioApi.CompleteJobAsync(identity.AgentId, job.JobId, stoppingToken);
       logger.LogInformation("Job {JobId} completed; executed locally: {Executed}.", job.JobId, executed);
      }
    }
    else logger.LogInformation("Agent {AgentId} is awaiting administrator authorization.", identity.AgentId);
   }
   catch (Exception exception) { logger.LogWarning(exception, "MDS-STUDIO-AGENT-001: Agent heartbeat cycle failed."); }
   await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
  }
 }
}
