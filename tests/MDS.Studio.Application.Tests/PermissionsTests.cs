using MDS.Studio.Application;
namespace MDS.Studio.Application.Tests;
public sealed class PermissionsTests { [Fact] public void Foundation_permissions_are_stable() { Assert.Equal("studio.access", Permissions.StudioAccess); Assert.Equal("studio.diagnostics", Permissions.AdminDiagnostics); Assert.Equal("agent.manage", Permissions.AgentManage); } }
