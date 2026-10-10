using MDS.SharedKernel;

namespace MDS.Studio.Domain;

public enum FileAssetStatus { Pending, Available, Archived }
public enum JobStatus { Queued, Running, Completed, Failed, Cancelled }

public sealed class StudioProject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string LocalPath { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "Ativo";
    public required string CreatedByUserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class ProjectOrganizationItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }
    public required string Title { get; init; }
    public string? Notes { get; init; }
    public string Status { get; set; } = "Pendente";
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class FileAsset
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string OriginalName { get; init; }
    public required string ContentType { get; init; }
    public string? LocalPath { get; init; }
    public Guid? ProjectId { get; init; }
    public long SizeBytes { get; init; }
    public string? Sha256 { get; set; }
    public FileAssetStatus Status { get; set; } = FileAssetStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; }
}
public sealed class LocalAgent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string MachineName { get; init; }
    public required string Version { get; set; }
    public bool IsAuthorized { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
}
public sealed class AgentCapability
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid AgentId { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
}
public sealed class BackgroundJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TargetAgentId { get; init; }
    public required string Type { get; init; }
    public required string CorrelationId { get; init; }
    public JobStatus Status { get; private set; } = JobStatus.Queued;
    public Result TransitionTo(JobStatus next) => (Status, next) switch
    {
        (JobStatus.Queued, JobStatus.Running) or (JobStatus.Queued, JobStatus.Cancelled) or
        (JobStatus.Running, JobStatus.Completed) or (JobStatus.Running, JobStatus.Failed) or (JobStatus.Running, JobStatus.Cancelled)
            => Set(next),
        _ => Result.Failure(new Error("MDS-STUDIO-AGENT-001", "Invalid job state transition."))
    };
    private Result Set(JobStatus next) { Status = next; return Result.Success(); }
}
public sealed class OutboxEvent { public Guid Id { get; init; } = Guid.NewGuid(); public required string Type { get; init; } public required string Payload { get; init; } public required string CorrelationId { get; init; } public DateTimeOffset OccurredAt { get; init; } public DateTimeOffset? ProcessedAt { get; set; } }
public sealed class InboxEvent { public Guid Id { get; init; } public DateTimeOffset ProcessedAt { get; init; } }
public sealed class AuditEvent { public Guid Id { get; init; } = Guid.NewGuid(); public required string Operation { get; init; } public required string CorrelationId { get; init; } public DateTimeOffset OccurredAt { get; init; } }
