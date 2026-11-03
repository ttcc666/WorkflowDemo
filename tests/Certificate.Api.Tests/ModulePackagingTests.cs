using Approval.Certificate;
using Approval.Core.Definitions;
using Certificate.Api.Certificates;
using Certificate.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace Certificate.Api.Tests;

public sealed class ModulePackagingTests
{
    [Theory]
    [InlineData("Certificate.Api.Certificates.AppDbContext, Certificate.Api", typeof(AppDbContext))]
    [InlineData("Certificate.Api.Certificates.CertificateApprovalHandler, Certificate.Api", typeof(CertificateApprovalHandler))]
    [InlineData("Certificate.Api.Endpoints.CertificateEndpoints, Certificate.Api", typeof(CertificateEndpoints))]
    public void Legacy_assembly_names_forward_to_the_certificate_module(string qualifiedName, Type expected)
    {
        var actual = Type.GetType(qualifiedName, throwOnError: true)!;
        Assert.Same(expected, actual);
        Assert.Equal("Approval.Certificate", actual.Assembly.GetName().Name);
    }

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
    public void Module_preserves_migration_history_and_model()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options);
        Assert.Equal(new[] { "20261101034706_CertificateBaseline", "20261101040302_CertificatePilotHardening", "20261101174625_ExpandApprovalTenants", "20261101174626_TenantIsolation", "20261102014813_AtomicOperations", "20261102043317_DefinitionGovernance", "20261102054547_LifecycleMaintenance" }, db.Database.GetMigrations());
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
