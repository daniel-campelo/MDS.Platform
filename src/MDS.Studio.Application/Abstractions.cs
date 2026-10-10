using MDS.SharedKernel;

namespace MDS.Studio.Application;

public static class Permissions
{
    public const string StudioAccess = "studio.access";
    public const string AdminDiagnostics = "studio.diagnostics";
    public const string AgentManage = "agent.manage";
}
public sealed record FileStorageMetadata(string Key, long SizeBytes, FileHash Hash);
public interface IFileStorage
{
    Task UploadAsync(string key, Stream content, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
    Task<FileStorageMetadata> GetMetadataAsync(string key, CancellationToken cancellationToken = default);
    Task ArchiveAsync(string key, CancellationToken cancellationToken = default);
}
public interface IInbox { Task<bool> TryRegisterAsync(Guid eventId, CancellationToken cancellationToken = default); }
public interface IJobDispatcher { Task EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default); }
