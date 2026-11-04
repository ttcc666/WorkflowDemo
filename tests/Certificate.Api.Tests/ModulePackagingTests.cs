using Approval.Certificate;
using Approval.Core.Definitions;
using Certificate.Api.Certificates;
using Certificate.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Tests;

public sealed class ModulePackagingTests
{
    [Fact]
    public void Module_embeds_all_default_certificate_workflows()
    {
        var templates = CertificateFlowTemplates.ReadDefaults();
        Assert.Equal(new[] { "certificate.create", "certificate.revoke", "certificate.update" }, templates.Keys.Order());
        var create = FlowDefinition.Parse(templates["certificate.create"]);
        var revoke = FlowDefinition.Parse(templates["certificate.revoke"]);
        var update = FlowDefinition.Parse(templates["certificate.update"]);
        Assert.Equal("dept", create.GetNode("start").Next);
        Assert.Equal("dept", update.GetNode("start").Next);
        Assert.Equal("fork", revoke.GetNode("start").Next);
        Assert.Equal(NodeType.Parallel, revoke.GetNode("fork").Type);
        Assert.Equal(NodeType.Join, revoke.GetNode("join").Type);
    }

    [Fact]
    public void Module_contains_only_current_initial_migration_and_model()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options);
        Assert.Equal(new[] { "20261103051646_InitialApproval" }, db.Database.GetMigrations());
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
