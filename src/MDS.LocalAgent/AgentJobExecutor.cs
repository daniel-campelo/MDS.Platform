using Microsoft.EntityFrameworkCore;

namespace MDS.LocalAgent;

public sealed class AgentJobExecutor(AgentDbContext db)
{
    public async Task<bool> ExecutePingAsync(Guid jobId, string correlationId, CancellationToken ct = default)
    {
        if (await db.ProcessedEvents.AnyAsync(x => x.Id == jobId, ct)) return false;
        db.Jobs.Add(new AgentJob { Id = jobId, Type = "AgentPingJob", CorrelationId = correlationId, Status = "COMPLETED" });
        db.ProcessedEvents.Add(new AgentProcessedEvent { Id = jobId, ProcessedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
