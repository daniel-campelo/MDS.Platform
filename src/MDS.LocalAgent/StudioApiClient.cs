using System.Net;
using System.Net.Http.Json;

namespace MDS.LocalAgent;

public sealed class StudioApiClient(HttpClient httpClient)
{
    private const string AgentVersion = "0.0.1";
    private static readonly object[] Capabilities =
    [
        new { name = "AgentPingJob", version = AgentVersion },
        new { name = "CreateTestFileJob", version = AgentVersion }
    ];

    public async Task<Guid> RequestEnrollmentAsync(string machineName, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync("/agents/enrollment-requests", new { machineName, agentVersion = AgentVersion, capabilities = Capabilities }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<EnrollmentResponse>(cancellationToken: cancellationToken);
        return result?.AgentId ?? throw new InvalidOperationException("MDS-STUDIO-AGENT-001: enrollment response did not contain an agent ID.");
    }

    public async Task<bool> SendHeartbeatAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync($"/agents/{agentId}/heartbeat", new { agentVersion = AgentVersion, capabilities = Capabilities }, cancellationToken);
        return response.StatusCode != HttpStatusCode.Forbidden && response.IsSuccessStatusCode;
    }

    public async Task<DeliveredJob?> GetNextJobAsync(Guid agentId, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync($"/agents/{agentId}/jobs/next", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DeliveredJob>(cancellationToken: cancellationToken);
    }

    public async Task CompleteJobAsync(Guid agentId, Guid jobId, CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsync($"/agents/{agentId}/jobs/{jobId}/complete", null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed record EnrollmentResponse(Guid AgentId, string Status);
}

public sealed record DeliveredJob(Guid JobId, string Type, string CorrelationId);
