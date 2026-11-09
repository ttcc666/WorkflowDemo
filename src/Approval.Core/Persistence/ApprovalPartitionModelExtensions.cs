using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Approval.Core.Persistence;

public static class ApprovalPartitionModelExtensions
{
    internal const string PartitionAnnotation = "Approval:PartitionProperty";

    /// <summary>按宿主已配置的分区属性扩宽审批索引、候选键和外键；宿主仍负责可信分区值、查询过滤和写入校验。</summary>
    public static ModelBuilder ConfigureApprovalPartition(this ModelBuilder modelBuilder, string propertyName, params Type[] additionalTypes)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        ArgumentNullException.ThrowIfNull(additionalTypes);
        if (modelBuilder.Model.FindAnnotation(PartitionAnnotation) is not null)
            throw new InvalidOperationException("审批分区模型只能配置一次");
        if (modelBuilder.Model.FindEntityType(typeof(ApprovalInstance)) is null)
            throw new InvalidOperationException("请先配置审批模型，再配置审批分区");
        foreach (var type in additionalTypes)
        {
            if (type is null || modelBuilder.Model.FindEntityType(type) is not { } entity || entity.IsOwned())
                throw new ArgumentException($"宿主分区类型 {type?.FullName ?? "<null>"} 必须已映射为非 Owned 实体", nameof(additionalTypes));
        }
        var entities = modelBuilder.Model.GetEntityTypes()
            .Where(entity => !entity.IsOwned() && (typeof(IApprovalEntity).IsAssignableFrom(entity.ClrType) || additionalTypes.Contains(entity.ClrType))).ToArray();
        foreach (var entity in entities)
        {
            var partition = entity.FindProperty(propertyName)
                ?? throw new InvalidOperationException($"审批实体 {entity.Name} 缺少宿主分区属性 {propertyName}");
            if (partition.ClrType != typeof(string) || partition.IsNullable)
                throw new InvalidOperationException("审批分区属性必须为非空字符串");
        }
        var partitioned = entities.ToHashSet();
        var keysToExpand = entities.SelectMany(e => e.GetKeys()).Distinct()
            .Where(k => !k.IsPrimaryKey() && k.Properties.All(p => p.Name != propertyName)).ToArray();
        foreach (var key in keysToExpand)
        {
            if (key.GetReferencingForeignKeys().Any(fk => !partitioned.Contains(fk.DeclaringEntityType)))
                throw new InvalidOperationException($"分区候选键 {key.DeclaringEntityType.Name} 被非分区实体引用，请先将引用方纳入分区模型");
            var existing = key.DeclaringEntityType.FindKey(new[] { key.DeclaringEntityType.FindProperty(propertyName)! }.Concat(key.Properties).ToArray());
            if (existing is null) continue;
            foreach (var annotation in key.GetAnnotations())
                if (existing.FindAnnotation(annotation.Name) is { } current
                    && !System.Collections.StructuralComparisons.StructuralEqualityComparer.Equals(current.Value, annotation.Value))
                    throw new InvalidOperationException($"分区候选键 {key.DeclaringEntityType.Name} 的 {annotation.Name} 配置冲突");
        }
        var foreignKeysToExpand = new List<IMutableForeignKey>();
        foreach (var foreignKey in entities.SelectMany(e => e.GetForeignKeys()).Distinct())
        {
            if (!partitioned.Contains(foreignKey.PrincipalEntityType)) continue;
            var dependentPosition = Array.FindIndex(foreignKey.Properties.ToArray(), p => p.Name == propertyName);
            var principalPosition = Array.FindIndex(foreignKey.PrincipalKey.Properties.ToArray(), p => p.Name == propertyName);
            if (dependentPosition != principalPosition)
                throw new InvalidOperationException($"分区外键 {foreignKey.DeclaringEntityType.Name} -> {foreignKey.PrincipalEntityType.Name} 必须在同一位置配对 {propertyName}");
            if (dependentPosition < 0) foreignKeysToExpand.Add(foreignKey);
        }
        foreach (var entity in entities)
        {
            var partition = entity.FindProperty(propertyName)!;
            foreach (var index in entity.GetIndexes().Where(i => i.Name is null && !i.Properties.Contains(partition)))
            {
                var existing = entity.FindIndex(new[] { partition }.Concat(index.Properties).ToArray());
                if (existing is null) continue;
                var descending = ExpandedDescending(index);
                if (descending is not null && existing.IsDescending is { } existingDescending
                    && !descending.SequenceEqual(existingDescending.Count == 0 ? Enumerable.Repeat(true, existing.Properties.Count) : existingDescending))
                    throw new InvalidOperationException($"分区索引 {entity.Name} 的排序配置冲突");
                foreach (var annotation in index.GetAnnotations())
                    if (existing.FindAnnotation(annotation.Name) is { } current
                        && !System.Collections.StructuralComparisons.StructuralEqualityComparer.Equals(current.Value, annotation.Value))
                        throw new InvalidOperationException($"分区索引 {entity.Name} 的 {annotation.Name} 配置冲突");
            }
        }
        // Validate the entire configuration before changing indexes, keys or annotations.
        foreach (var entity in entities)
        {
            var partition = entity.FindProperty(propertyName)!;
            entity.SetAnnotation(PartitionAnnotation, propertyName);
            foreach (var index in entity.GetIndexes().ToArray())
            {
                if (index.Properties.Contains(partition)) continue;
                var properties = new[] { partition }.Concat(index.Properties).ToArray();
                var unique = index.IsUnique;
                var descending = ExpandedDescending(index);
                var annotations = index.GetAnnotations().ToArray();
                var name = index.Name;
                entity.RemoveIndex(index);
                // EF may already have created the partitioned FK index by convention.
                var replacement = name is null ? entity.FindIndex(properties) ?? entity.AddIndex(properties) : entity.AddIndex(properties, name);
                replacement.IsUnique |= unique;
                foreach (var annotation in annotations) replacement.SetAnnotation(annotation.Name, annotation.Value);
                if (descending is not null) replacement.IsDescending = descending;
            }
        }
        foreach (var key in keysToExpand)
        {
            var entity = key.DeclaringEntityType;
            var properties = new[] { entity.FindProperty(propertyName)! }.Concat(key.Properties).ToArray();
            var replacement = entity.FindKey(properties) ?? entity.AddKey(properties);
            foreach (var annotation in key.GetAnnotations()) replacement.SetAnnotation(annotation.Name, annotation.Value);
        }
        foreach (var foreignKey in foreignKeysToExpand)
        {
            var principal = foreignKey.PrincipalEntityType;
            var principalProperties = new[] { principal.FindProperty(propertyName)! }.Concat(foreignKey.PrincipalKey.Properties).ToArray();
            var principalKey = principal.FindKey(principalProperties) ?? principal.AddKey(principalProperties);
            var properties = new[] { foreignKey.DeclaringEntityType.FindProperty(propertyName)! }.Concat(foreignKey.Properties).ToArray();
            foreignKey.SetProperties(properties, principalKey);
        }
        foreach (var key in keysToExpand) key.DeclaringEntityType.RemoveKey(key);
        modelBuilder.Model.SetAnnotation(PartitionAnnotation, propertyName);
        return modelBuilder;
    }

    private static bool[]? ExpandedDescending(IMutableIndex index) => index.IsDescending is null ? null : new[] { false }
        .Concat(index.IsDescending.Count == 0 ? Enumerable.Repeat(true, index.Properties.Count) : index.IsDescending).ToArray();
}
