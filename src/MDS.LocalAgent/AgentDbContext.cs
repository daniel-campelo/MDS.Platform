using Microsoft.EntityFrameworkCore;

namespace MDS.LocalAgent;
public sealed class AgentDbContext(DbContextOptions<AgentDbContext> options) : DbContext(options)
{
 public DbSet<AgentJob> Jobs => Set<AgentJob>();
 public DbSet<AgentProcessedEvent> ProcessedEvents => Set<AgentProcessedEvent>();
 public DbSet<AgentIdentity> Identity => Set<AgentIdentity>();
 protected override void OnModelCreating(ModelBuilder b) { b.Entity<AgentJob>().HasKey(x => x.Id); b.Entity<AgentProcessedEvent>().HasKey(x => x.Id); b.Entity<AgentIdentity>().HasKey(x => x.AgentId); }
}
public sealed class AgentJob { public Guid Id { get; init; } public required string Type { get; init; } public required string CorrelationId { get; init; } public string Status { get; set; } = "QUEUED"; }
public sealed class AgentProcessedEvent { public Guid Id { get; init; } public DateTimeOffset ProcessedAt { get; init; } }
public sealed class AgentIdentity { public Guid AgentId { get; init; } public required string MachineName { get; init; } public DateTimeOffset EnrolledAt { get; init; } }
