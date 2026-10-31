using Approval.Core.Abstractions;
using Approval.Core.Domain;

namespace Approval.Core.Resolvers;

/// <summary>指定用户，Value 为逗号分隔的用户 ID。</summary>
public sealed class UserApproverResolver : IApproverResolver
{
    public string Type => "User";

    public ApproverTypeDescriptor Descriptor { get; } = new("User", "指定用户", ApproverValueKind.User);

    public Task<IReadOnlyList<Assignee>> ResolveAsync(string value, ApproverResolveContext context, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Assignee>>(SplitIds(value).Select(id => new Assignee(AssigneeKind.User, id)).ToList());

    internal static IEnumerable<string> SplitIds(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>角色，Value 为角色名（可逗号分隔多个）。生成角色任务，是否展开为具体用户由引擎按审批方式决定。</summary>
public sealed class RoleApproverResolver : IApproverResolver
{
    public string Type => "Role";

    public ApproverTypeDescriptor Descriptor { get; } = new("Role", "角色", ApproverValueKind.Role);

    public Task<IReadOnlyList<Assignee>> ResolveAsync(string value, ApproverResolveContext context, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Assignee>>(
            UserApproverResolver.SplitIds(value).Select(role => new Assignee(AssigneeKind.Role, role)).ToList());
}

/// <summary>取自表单字段，Value 为字段名，字段值可以是单个用户 ID 或用户 ID 数组。</summary>
public sealed class FormFieldApproverResolver : IApproverResolver
{
    public string Type => "FormField";

    public ApproverTypeDescriptor Descriptor { get; } = new("FormField", "表单人员字段", ApproverValueKind.FormUserField);

    public Task<IReadOnlyList<Assignee>> ResolveAsync(string value, ApproverResolveContext context, CancellationToken ct)
    {
        IReadOnlyList<Assignee> result = context.FormData.GetValueOrDefault(value) switch
        {
            string id when !string.IsNullOrWhiteSpace(id) => [new Assignee(AssigneeKind.User, id)],
            object?[] ids => ids.OfType<string>().Where(id => id.Length > 0)
                .Select(id => new Assignee(AssigneeKind.User, id)).ToList(),
            _ => []
        };
        return Task.FromResult(result);
    }
}

/// <summary>发起人本人（常用于抄送节点）。</summary>
public sealed class InitiatorApproverResolver : IApproverResolver
{
    public string Type => "Initiator";

    public ApproverTypeDescriptor Descriptor { get; } = new("Initiator", "发起人", ApproverValueKind.None);

    public Task<IReadOnlyList<Assignee>> ResolveAsync(string value, ApproverResolveContext context, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Assignee>>([new Assignee(AssigneeKind.User, context.Instance.InitiatorId)]);
}
