using MDS.Studio.Infrastructure;
namespace MDS.Studio.IntegrationTests;
public sealed class StorageTests
{
 [Fact] public async Task Storage_uploads_and_returns_hash() { var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); try { var storage = new LocalFileStorage(root); await storage.UploadAsync("test/hello.txt", new MemoryStream("hello"u8.ToArray())); var meta = await storage.GetMetadataAsync("test/hello.txt"); Assert.Equal(5, meta.SizeBytes); Assert.True(await storage.ExistsAsync("test/hello.txt")); } finally { if (Directory.Exists(root)) Directory.Delete(root, true); } }
 [Fact] public async Task Storage_rejects_path_escape() { var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); var storage = new LocalFileStorage(root); await Assert.ThrowsAsync<InvalidOperationException>(() => storage.UploadAsync("../../escape", new MemoryStream())); }
}
