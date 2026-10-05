using MDS.LocalAgent;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDbContext<AgentDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("Agent") ?? "Data Source=mds-local-agent.db"));
builder.Services.AddSingleton(_ => new StudioApiClient(new HttpClient { BaseAddress = new Uri(builder.Configuration["StudioApi:BaseUrl"] ?? "http://localhost:5000") }));
builder.Services.AddHostedService<AgentWorker>();
await builder.Build().RunAsync();
