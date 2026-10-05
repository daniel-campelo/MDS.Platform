namespace MDS.LocalAgent;
public sealed class SandboxFileWriter(string root)
{
 private readonly string _root = Path.GetFullPath(root);
 public string CreateTestFile(Guid agentId, Guid jobId, string environment)
 {
  var path = Path.GetFullPath(Path.Combine(_root, "MDS_AGENT_TEST.txt"));
  if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("MDS-STUDIO-AGENT-001: unsafe path.");
  Directory.CreateDirectory(_root);
  File.WriteAllText(path, $"Agent ID: {agentId}\nJob ID: {jobId}\nTimestamp: {DateTimeOffset.UtcNow:O}\nEnvironment: {environment}\n"); return path;
 }
}
