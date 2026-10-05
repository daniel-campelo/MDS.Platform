using MDS.LocalAgent;
using Microsoft.EntityFrameworkCore;
namespace MDS.LocalAgent.Tests;
public sealed class AgentTests
{
 [Fact] public void Test_file_is_created_inside_sandbox() { var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); try { var path = new SandboxFileWriter(root).CreateTestFile(Guid.NewGuid(), Guid.NewGuid(), "Staging"); Assert.StartsWith(Path.GetFullPath(root), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase); Assert.Contains("Environment: Staging", File.ReadAllText(path)); } finally { if (Directory.Exists(root)) Directory.Delete(root, true); } }
 [Fact] public async Task Ping_job_is_idempotent() { var options = new DbContextOptionsBuilder<AgentDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options; await using var db = new AgentDbContext(options); var executor = new AgentJobExecutor(db); var id = Guid.NewGuid(); Assert.True(await executor.ExecutePingAsync(id, "correlation")); Assert.False(await executor.ExecutePingAsync(id, "correlation")); Assert.Single(db.Jobs); }
}
