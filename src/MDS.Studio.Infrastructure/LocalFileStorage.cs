using System.Security.Cryptography;
using MDS.SharedKernel;
using MDS.Studio.Application;

namespace MDS.Studio.Infrastructure;

public sealed class LocalFileStorage(string root) : IFileStorage
{
    private readonly string _root = Path.GetFullPath(root);
    private string Resolve(string key)
    {
        var path = Path.GetFullPath(Path.Combine(_root, key));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MDS-STUDIO-FILE-001: unsafe storage key.");
        return path;
    }
    public async Task UploadAsync(string key, Stream content, CancellationToken ct = default)
    {
        var path = Resolve(key); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var target = File.Create(path); await content.CopyToAsync(target, ct);
    }
    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream>(File.OpenRead(Resolve(key)));
    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(File.Exists(Resolve(key)));
    public async Task<FileStorageMetadata> GetMetadataAsync(string key, CancellationToken ct = default)
    {
        var path = Resolve(key); var info = new FileInfo(path);
        await using var stream = File.OpenRead(path); var hash = await SHA256.HashDataAsync(stream, ct);
        return new FileStorageMetadata(key, info.Length, new FileHash(Convert.ToHexString(hash).ToLowerInvariant()));
    }
    public Task ArchiveAsync(string key, CancellationToken ct = default)
    {
        var source = Resolve(key); var archive = Resolve(Path.Combine("archive", key));
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!); File.Move(source, archive, true); return Task.CompletedTask;
    }
}
