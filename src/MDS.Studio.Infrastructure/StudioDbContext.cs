using MDS.Studio.Domain;
using Microsoft.EntityFrameworkCore;

namespace MDS.Studio.Infrastructure;

public sealed class StudioDbContext(DbContextOptions<StudioDbContext> options) : DbContext(options)
{
    public DbSet<FileAsset> FileAssets => Set<FileAsset>();
    public DbSet<StudioProject> Projects => Set<StudioProject>();
    public DbSet<ProjectOrganizationItem> ProjectOrganizationItems => Set<ProjectOrganizationItem>();
    public DbSet<ProjectGarmentBrief> ProjectGarmentBriefs => Set<ProjectGarmentBrief>();
    public DbSet<StudioPattern> Patterns => Set<StudioPattern>();
    public DbSet<PatternTechnicalRevision> PatternTechnicalRevisions => Set<PatternTechnicalRevision>();
    public DbSet<LocalAgent> LocalAgents => Set<LocalAgent>();
    public DbSet<AgentCapability> AgentCapabilities => Set<AgentCapability>();
    public DbSet<BackgroundJob> BackgroundJobs => Set<BackgroundJob>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<InboxEvent> InboxEvents => Set<InboxEvent>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<FileAsset>(e => { e.ToTable("file_assets"); e.HasKey(x => x.Id); e.Property(x => x.ContentType).HasMaxLength(80); e.Property(x => x.LocalPath).HasMaxLength(1024); e.HasIndex(x => x.ProjectId); e.Property(x => x.Status).HasConversion<string>(); });
        b.Entity<StudioProject>(e => { e.ToTable("studio_projects"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(160); e.Property(x => x.LocalPath).HasMaxLength(1024); e.Property(x => x.Description).HasMaxLength(2000); e.Property(x => x.Status).HasMaxLength(40); e.HasIndex(x => x.LocalPath).IsUnique(); });
        b.Entity<ProjectOrganizationItem>(e => { e.ToTable("project_organization_items"); e.HasKey(x => x.Id); e.Property(x => x.Title).HasMaxLength(160); e.Property(x => x.Notes).HasMaxLength(2000); e.Property(x => x.Status).HasMaxLength(40); e.HasIndex(x => x.ProjectId); });
        b.Entity<ProjectGarmentBrief>(e => { e.ToTable("project_garment_briefs"); e.HasKey(x => x.ProjectId); e.Property(x => x.GarmentType).HasMaxLength(100); e.Property(x => x.Model).HasMaxLength(160); e.Property(x => x.Fabric).HasMaxLength(160); e.Property(x => x.Colors).HasMaxLength(300); e.Property(x => x.FrontArtworkScale).HasMaxLength(20); e.Property(x => x.FrontArtworkPosition).HasMaxLength(30); e.Property(x => x.SizeRange).HasMaxLength(160); e.Property(x => x.Notes).HasMaxLength(2000); });
        b.Entity<StudioPattern>(e => { e.ToTable("studio_patterns"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(160); e.Property(x => x.Notes).HasMaxLength(2000); e.Property(x => x.TechnicalFilePath).HasMaxLength(1024); e.Property(x => x.TechnicalVersion).HasMaxLength(80); e.Property(x => x.Status).HasMaxLength(40); e.HasIndex(x => x.ProjectId); });
        b.Entity<PatternTechnicalRevision>(e => { e.ToTable("pattern_technical_revisions"); e.HasKey(x => x.Id); e.Property(x => x.FilePath).HasMaxLength(1024); e.Property(x => x.Version).HasMaxLength(80); e.HasIndex(x => new { x.PatternId, x.Version }).IsUnique(); });
        b.Entity<LocalAgent>(e => { e.ToTable("local_agents"); e.HasKey(x => x.Id); });
        b.Entity<AgentCapability>(e => { e.ToTable("agent_capabilities"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.AgentId, x.Name }).IsUnique(); });
        b.Entity<BackgroundJob>(e => { e.ToTable("background_jobs"); e.HasKey(x => x.Id); e.Property(x => x.Status).HasConversion<string>(); });
        b.Entity<OutboxEvent>(e => { e.ToTable("outbox_events"); e.HasKey(x => x.Id); });
        b.Entity<InboxEvent>(e => { e.ToTable("inbox_events"); e.HasKey(x => x.Id); });
        b.Entity<AuditEvent>(e => { e.ToTable("audit_events"); e.HasKey(x => x.Id); });
    }
}
