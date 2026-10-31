using Approval.Core.Definitions;
using Approval.Core.Domain;

namespace Approval.Core.Templates;

public sealed record TemplateVariableInfo(string Name, string Alias, string Description);

/// <summary>消息模板与动作参数可用的变量。中文名与英文名等价，如 <c>{标题}</c> 与 <c>{title}</c>。</summary>
public static class TemplateVariables
{
    public static IReadOnlyList<TemplateVariableInfo> Catalog { get; } =
    [
        new("标题", "title", "审批标题"),
        new("申请人", "initiator", "发起人 ID"),
        new("节点", "node", "当前节点名称"),
        new("操作人", "operator", "执行同意 / 驳回 / 转交的人"),
        new("意见", "comment", "审批意见"),
        new("结果", "result", "流程结果：已通过 / 已驳回 / 已撤回"),
        new("当前审批人", "assignees", "当前节点的审批人"),
        new("转交给", "transferTo", "转交对象"),
        new("截止时间", "dueAt", "待办处理截止时间"),
        new("退回到", "returnTo", "驳回时退回到的节点（驳回结束时为空）"),
        new("发起时间", "createdAt", "流程发起时间"),
        new("流程", "flow", "流程标识"),
        new("业务类型", "businessType", "业务类型"),
        new("业务单号", "businessKey", "业务单据主键"),
        new("实例ID", "instanceId", "审批实例 ID"),
        new("链接", "link", "审批详情页地址（需配置 InstanceUrlFormat）"),
        new("表单.字段", "form.字段", "表单数据，如 {form.amount}")
    ];

    /// <summary>事件相关的变量，由引擎按事件填充。</summary>
    public sealed class EventValues
    {
        public string? OperatorId { get; init; }
        public string? Comment { get; init; }
        public IReadOnlyList<string> Assignees { get; init; } = [];
        public string? TransferTo { get; init; }
        public DateTime? DueAt { get; init; }
        public string? ReturnTo { get; init; }
    }

    public static Dictionary<string, object?> Build(ApprovalInstance instance, IReadOnlyDictionary<string, object?> form,
        FlowNode? node, ApprovalOptions options, EventValues? values = null)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in options.GlobalVariables) result[key] = value;

        void Add(string name, string alias, object? value)
        {
            result[name] = value;
            result[alias] = value;
        }

        Add("标题", "title", instance.Title ?? "");
        Add("申请人", "initiator", instance.InitiatorId);
        Add("节点", "node", node?.Name ?? "");
        Add("操作人", "operator", values?.OperatorId ?? "");
        Add("意见", "comment", values?.Comment ?? "");
        Add("结果", "result", ResultText(instance.Status));
        Add("当前审批人", "assignees", values?.Assignees ?? []);
        Add("转交给", "transferTo", values?.TransferTo ?? "");
        Add("截止时间", "dueAt", values?.DueAt);
        Add("退回到", "returnTo", values?.ReturnTo ?? "");
        Add("发起时间", "createdAt", instance.CreatedAt);
        Add("流程", "flow", instance.DefinitionKey);
        Add("业务类型", "businessType", instance.BusinessType);
        Add("业务单号", "businessKey", instance.BusinessKey);
        Add("实例ID", "instanceId", instance.Id.ToString());
        Add("链接", "link", options.InstanceUrlFormat is null ? "" : string.Format(options.InstanceUrlFormat, instance.Id));
        Add("表单", "form", form);
        return result;
    }

    public static string ResultText(InstanceStatus status) => status switch
    {
        InstanceStatus.Approved => "已通过",
        InstanceStatus.Rejected => "已驳回",
        InstanceStatus.Withdrawn => "已撤回",
        InstanceStatus.Terminated => "已终止",
        _ => "审批中"
    };
}
