using Approval.Core.Domain;
using Approval.Core.Persistence;
using Approval.Forms;
using Approval.Forms.Attachments;
using Approval.Forms.Runtime;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Tests;

public sealed class ApprovalPartitionModelTests
{
    [Theory]
    [InlineData("unmapped")]
    [InlineData("owned")]
    [InlineData("null-type")]
    public void AdditionalTypes_InvalidEntryRejectsBeforeChangingModel(string kind)
    {
        var model = Model();
        if (kind == "owned") model.Entity<HostRow>().OwnsOne(e => e.Details);
        var additional = kind == "null-type" ? null! : typeof(HostDetails);
        Assert.ThrowsAny<ArgumentException>(() => model.ConfigureApprovalPartition("TenantId", additional));
        AssertUnchanged(model);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("nullable")]
    [InlineData("non-string")]
    public void PartitionProperty_InvalidConfigurationRejectsBeforeChangingModel(string kind)
    {
        var model = Model();
        var row = model.Entity<HostRow>();
        row.HasKey(e => e.Id);
        if (kind == "nullable") row.Property<string>("TenantId").IsRequired(false);
        if (kind == "non-string") row.Property<int>("TenantId");
        Assert.Throws<InvalidOperationException>(() => model.ConfigureApprovalPartition("TenantId", typeof(HostRow)));
        AssertUnchanged(model);
    }

    [Fact]
    public void MissingApprovalModel_RejectsConfiguration()
    {
        var model = new ModelBuilder();
        Assert.Throws<InvalidOperationException>(() => model.ConfigureApprovalPartition("TenantId"));
        Assert.Null(model.Model.FindAnnotation("Approval:PartitionProperty"));
    }

    [Fact]
    public void PartitionModel_PreservesGuidKeysAndScopesCoreFormsAndHostConstraints()
    {
        var model = Model();
        model.Entity<HostRow>(b =>
        {
            b.Ignore(e => e.Details); b.HasKey(e => e.Id);
            b.Property<string>("TenantId").IsRequired();
            b.HasIndex(e => e.Name).IsUnique();
            b.HasOne<ApprovalInstance>().WithMany().HasForeignKey(e => e.InstanceId);
        });
        model.ConfigureApprovalPartition("TenantId", typeof(HostRow));
        foreach (var type in new[] { typeof(ApprovalInstance), typeof(ApprovalDefinition), typeof(ApprovalDraft), typeof(ApprovalFile), typeof(HostRow) })
        {
            var entity = model.Model.FindEntityType(type)!;
            Assert.Equal(new[] { "Id" }, entity.FindPrimaryKey()!.Properties.Select(p => p.Name));
            Assert.Equal("TenantId", entity.FindAnnotation("Approval:PartitionProperty")!.Value);
            Assert.All(entity.GetIndexes(), i => Assert.Equal("TenantId", i.Properties[0].Name));
            Assert.All(entity.GetForeignKeys(), fk =>
            {
                Assert.Equal("TenantId", fk.Properties[0].Name);
                Assert.Equal("TenantId", fk.PrincipalKey.Properties[0].Name);
            });
        }
        Assert.True(model.Model.FindEntityType(typeof(HostRow))!.GetIndexes().Single().IsUnique);
        Assert.Throws<InvalidOperationException>(() => model.ConfigureApprovalPartition("TenantId"));
    }

    private static ModelBuilder Model()
    {
        var model = new ModelBuilder();
        model.ConfigureApproval().ConfigureApprovalForms();
        foreach (var entity in model.Model.GetEntityTypes().ToArray()) model.Entity(entity.ClrType).Property<string>("TenantId").IsRequired();
        return model;
    }

    [Fact]
    public void NamedIndexes_PreserveIdentityUniquenessAndOrdering()
    {
        var model = Model();
        model.Entity<HostRow>(b =>
        {
            b.Ignore(r => r.Details); b.HasKey(r => r.Id); b.Property<string>("TenantId").IsRequired();
            b.HasIndex(r => r.Name, "UniqueName").IsUnique().IsDescending().HasDatabaseName("IX_Host_Name");
            b.HasIndex(r => r.Name, "NameLookup").IsDescending(false);
            b.HasIndex(r => new { r.Name, r.InstanceId }, "NameInstance").IsDescending(false, true);
        });
        model.ConfigureApprovalPartition("TenantId", typeof(HostRow));
        var indexes = model.Model.FindEntityType(typeof(HostRow))!.GetIndexes().ToDictionary(i => i.Name!);
        Assert.Equal(3, indexes.Count);
        Assert.Equal(new[] { "TenantId", "Name" }, indexes["UniqueName"].Properties.Select(p => p.Name));
        Assert.True(indexes["UniqueName"].IsUnique);
        Assert.Equal("IX_Host_Name", indexes["UniqueName"].GetDatabaseName());
        Assert.Equal(new[] { false, true }, indexes["UniqueName"].IsDescending);
        Assert.False(indexes["NameLookup"].IsUnique);
        Assert.Null(indexes["NameLookup"].IsDescending); // EF represents all-ascending order as null.
        Assert.Equal(new[] { "TenantId", "Name", "InstanceId" }, indexes["NameInstance"].Properties.Select(p => p.Name));
        Assert.Equal(new[] { false, false, true }, indexes["NameInstance"].IsDescending);
        Assert.All(indexes.Values, i => Assert.Equal("TenantId", i.Properties[0].Name));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingPartitionIndex_MergesConstraintWithoutGlobalIndex(bool existingUnique)
    {
        var model = Model();
        model.Entity<HostRow>(b =>
        {
            b.Ignore(r => r.Details); b.HasKey(r => r.Id); b.Property<string>("TenantId").IsRequired();
            b.HasIndex(r => r.Name).IsUnique(!existingUnique);
            b.HasIndex("TenantId", "Name").IsUnique(existingUnique);
        });
        model.ConfigureApprovalPartition("TenantId", typeof(HostRow));
        var index = Assert.Single(model.Model.FindEntityType(typeof(HostRow))!.GetIndexes());
        Assert.Equal(new[] { "TenantId", "Name" }, index.Properties.Select(p => p.Name));
        Assert.True(index.IsUnique);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("sorting")]
    public void ConflictingPartitionIndexes_RejectBeforeChangingModel(string conflict)
    {
        var model = Model();
        model.Entity<HostRow>(b =>
        {
            b.Ignore(r => r.Details); b.HasKey(r => r.Id); b.Property<string>("TenantId").IsRequired();
            var original = b.HasIndex(r => r.Name);
            var partitioned = b.HasIndex("TenantId", "Name");
            if (conflict == "name") { original.HasDatabaseName("Original"); partitioned.HasDatabaseName("Partitioned"); }
            else { original.IsDescending(); partitioned.IsDescending(); }
        });
        var error = Assert.Throws<InvalidOperationException>(() => model.ConfigureApprovalPartition("TenantId", typeof(HostRow)));
        Assert.Contains("分区索引", error.Message);
        AssertUnchanged(model);
        Assert.Equal(2, model.Model.FindEntityType(typeof(HostRow))!.GetIndexes().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingPartitionForeignKey_PreservesPairedProperties(bool partitionLast)
    {
        var model = Model();
        var instance = model.Model.FindEntityType(typeof(ApprovalInstance))!;
        var definition = model.Model.FindEntityType(typeof(ApprovalDefinition))!;
        var foreign = partitionLast ? new[] { "DefinitionId", "TenantId" } : ["TenantId", "DefinitionId"];
        var principal = partitionLast ? new[] { "Id", "TenantId" } : ["TenantId", "Id"];
        var key = definition.AddKey(principal.Select(p => definition.FindProperty(p)!).ToArray());
        var fk = instance.GetForeignKeys().Single(f => f.PrincipalEntityType == definition);
        fk.SetProperties(foreign.Select(p => instance.FindProperty(p)!).ToArray(), key);
        model.ConfigureApprovalPartition("TenantId");
        Assert.Same(key, fk.PrincipalKey);
        Assert.Equal(foreign, fk.Properties.Select(p => p.Name));
        Assert.Equal(principal, fk.PrincipalKey.Properties.Select(p => p.Name));
        Assert.Single(instance.GetForeignKeys(), f => f.PrincipalEntityType == definition);
    }

    [Theory]
    [InlineData("dependent-only")]
    [InlineData("principal-only")]
    [InlineData("mismatched-position")]
    public void InvalidPartitionForeignKey_RejectsBeforeChangingModel(string shape)
    {
        var model = Model();
        model.Entity<ApprovalDefinition>().Property(d => d.Key).IsRequired();
        var instance = model.Model.FindEntityType(typeof(ApprovalInstance))!;
        var definition = model.Model.FindEntityType(typeof(ApprovalDefinition))!;
        var foreign = shape switch
        {
            "dependent-only" => new[] { "TenantId", "DefinitionId" },
            "principal-only" => ["BusinessType", "DefinitionId"],
            _ => ["TenantId", "BusinessType"]
        };
        var principal = shape switch
        {
            "dependent-only" => new[] { "Key", "Id" },
            "principal-only" => ["TenantId", "Id"],
            _ => ["Key", "TenantId"]
        };
        var key = definition.AddKey(principal.Select(p => definition.FindProperty(p)!).ToArray());
        var fk = instance.GetForeignKeys().Single(f => f.PrincipalEntityType == definition);
        fk.SetProperties(foreign.Select(p => instance.FindProperty(p)!).ToArray(), key);
        var error = Assert.Throws<InvalidOperationException>(() => model.ConfigureApprovalPartition("TenantId"));
        Assert.Contains("分区外键", error.Message);
        AssertUnchanged(model);
        Assert.Equal(foreign, fk.Properties.Select(p => p.Name));
    }

    private static void AssertUnchanged(ModelBuilder model)
    {
        Assert.Null(model.Model.FindAnnotation("Approval:PartitionProperty"));
        Assert.All(model.Model.GetEntityTypes(), e => Assert.Null(e.FindAnnotation("Approval:PartitionProperty")));
        Assert.Equal(new[] { "Key", "Version" }, model.Model.FindEntityType(typeof(ApprovalDefinition))!.GetIndexes().Single(i => i.IsUnique).Properties.Select(p => p.Name));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AlternateKeys_ScopeUniquenessAndRetargetReferences(bool referenced, bool existingComposite)
    {
        var model = Model();
        var row = model.Entity<HostRow>();
        row.Ignore(r => r.Details); row.HasKey(r => r.Id); row.Property<string>("TenantId").IsRequired();
        row.HasAlternateKey(r => r.Name).HasName("AK_Host_Name");
        if (existingComposite) row.HasAlternateKey("TenantId", "Name").HasName("AK_Host_Name");
        if (referenced)
        {
            model.Entity<HostReference>().HasKey(r => r.Id);
            model.Entity<HostReference>().Property<string>("TenantId").IsRequired();
            model.Entity<HostReference>().HasOne<HostRow>().WithMany().HasForeignKey(r => r.Name).HasPrincipalKey(r => r.Name);
        }
        model.ConfigureApprovalPartition("TenantId", referenced ? [typeof(HostRow), typeof(HostReference)] : [typeof(HostRow)]);
        var entity = model.Model.FindEntityType(typeof(HostRow))!;
        Assert.Equal(new[] { "Id" }, entity.FindPrimaryKey()!.Properties.Select(p => p.Name));
        var alternate = Assert.Single(entity.GetKeys(), k => !k.IsPrimaryKey());
        Assert.Equal(new[] { "TenantId", "Name" }, alternate.Properties.Select(p => p.Name));
        Assert.Equal("AK_Host_Name", alternate.GetName());
        if (referenced)
        {
            var fk = Assert.Single(model.Model.FindEntityType(typeof(HostReference))!.GetForeignKeys());
            Assert.Same(alternate, fk.PrincipalKey);
            Assert.Equal(new[] { "TenantId", "Name" }, fk.Properties.Select(p => p.Name));
        }
    }

    [Fact]
    public void AlternateKey_NonpartitionedReferenceRejectsBeforeChangingModel()
    {
        var model = Model();
        var row = model.Entity<HostRow>();
        row.Ignore(r => r.Details); row.HasKey(r => r.Id); row.Property<string>("TenantId").IsRequired();
        var key = row.HasAlternateKey(r => r.Name).Metadata;
        model.Entity<HostReference>().HasKey(r => r.Id);
        var fk = model.Entity<HostReference>().HasOne<HostRow>().WithMany().HasForeignKey(r => r.Name).HasPrincipalKey(r => r.Name).Metadata;
        var error = Assert.Throws<InvalidOperationException>(() => model.ConfigureApprovalPartition("TenantId", typeof(HostRow)));
        Assert.Contains("非分区实体引用", error.Message);
        AssertUnchanged(model);
        Assert.Same(key, fk.PrincipalKey);
        Assert.Equal(new[] { "Name" }, key.Properties.Select(p => p.Name));
    }

    [Fact]
    public void AlternateKey_ConflictingCompositeAnnotationsRejectBeforeChangingModel()
    {
        var model = Model();
        var row = model.Entity<HostRow>();
        row.Ignore(r => r.Details); row.HasKey(r => r.Id); row.Property<string>("TenantId").IsRequired();
        row.HasAlternateKey(r => r.Name).HasName("Original");
        row.HasAlternateKey("TenantId", "Name").HasName("Partitioned");
        var error = Assert.Throws<InvalidOperationException>(() => model.ConfigureApprovalPartition("TenantId", typeof(HostRow)));
        Assert.Contains("候选键", error.Message);
        AssertUnchanged(model);
        Assert.Equal(3, row.Metadata.GetKeys().Count());
        Assert.Equal("Original", row.Metadata.FindKey([row.Property(r => r.Name).Metadata])!.GetName());
    }

    public sealed class HostRow { public Guid Id { get; set; } public string Name { get; set; } = ""; public Guid InstanceId { get; set; } public HostDetails Details { get; set; } = new(); }
    public sealed class HostReference { public Guid Id { get; set; } public string Name { get; set; } = ""; }
    public sealed class HostDetails { public string Note { get; set; } = ""; }
}
