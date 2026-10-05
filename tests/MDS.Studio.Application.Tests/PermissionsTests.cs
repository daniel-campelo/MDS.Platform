using MDS.Studio.Application;
namespace MDS.Studio.Application.Tests;
public sealed class PermissionsTests { [Fact] public void Foundation_permissions_are_stable() { Assert.Equal("studio.access", Permissions.StudioAccess); Assert.Equal("studio.admin.diagnostics", Permissions.AdminDiagnostics); Assert.Equal("studio.agent.manage", Permissions.AgentManage); } }
