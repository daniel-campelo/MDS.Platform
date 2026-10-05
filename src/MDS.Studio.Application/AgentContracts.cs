namespace MDS.Studio.Application;

public sealed record EnrollmentRequest(string MachineName, string AgentVersion, IReadOnlyCollection<AgentCapabilityRequest> Capabilities);
public sealed record AgentCapabilityRequest(string Name, string Version);
public sealed record HeartbeatRequest(string AgentVersion, IReadOnlyCollection<AgentCapabilityRequest> Capabilities);
public sealed record JobRequest(string Type);
