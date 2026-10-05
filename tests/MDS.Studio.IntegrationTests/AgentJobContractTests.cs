using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MDS.Studio.Domain;
using MDS.Studio.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MDS.Studio.IntegrationTests;

public sealed class AgentJobContractTests : IClassFixture<AgentJobApiFactory>
{
    private readonly AgentJobApiFactory _factory;
    public AgentJobContractTests(AgentJobApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Authorized_agent_job_moves_from_queued_to_completed()
    {
        var agentId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StudioDbContext>();
            db.LocalAgents.Add(new LocalAgent { Id = agentId, MachineName = "CONTRACT-AGENT", Version = "0.0.1", IsAuthorized = true });
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        var queued = await client.PostAsJsonAsync($"/agents/{agentId}/jobs", new { type = "AgentPingJob" });
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        var jobId = JsonDocument.Parse(await queued.Content.ReadAsStringAsync()).RootElement.GetProperty("jobId").GetGuid();

        var next = await client.GetAsync($"/agents/{agentId}/jobs/next");
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Equal(jobId, JsonDocument.Parse(await next.Content.ReadAsStringAsync()).RootElement.GetProperty("jobId").GetGuid());

        var completed = await client.PostAsync($"/agents/{agentId}/jobs/{jobId}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);

        var result = await client.GetStringAsync($"/jobs/{jobId}");
        Assert.Equal("Completed", JsonDocument.Parse(result).RootElement.GetProperty("status").GetString());
    }
}

public sealed class AgentJobApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"agent-contract-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            var existing = services.Single(x => x.ServiceType == typeof(DbContextOptions<StudioDbContext>));
            services.Remove(existing);
            services.AddDbContext<StudioDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }
}
