using System.Text.Json;
using System.Text.Json.Serialization;

namespace Approval.Core.Definitions;

public enum NodeType
{
    Start,
    Approval,
    Cc,
    Condition,

    /// <summary>并行分叉：<see cref="FlowNode.Branches"/> 中的所有分支同时进行，必须由一个 <see cref="Join"/> 合并。</summary>
    Parallel,

    /// <summary>并行合并：等待对应并行节点的所有分支到达后进入下一节点。</summary>
    Join,

    End,

    /// <summary>执行有序业务动作后自动推进。</summary>
    Action
}

/// <summary>节点审批方式。</summary>
public enum ApproveMode
{
    /// <summary>或签：任一处理人同意即通过；角色任务由角色内任意一人处理。</summary>
    Any,

    /// <summary>会签：所有处理人同意才通过；角色会展开为具体用户。</summary>
    All,

    /// <summary>依次审批：按顺序逐个审批；角色会展开为具体用户。</summary>
    Sequential
}

/// <summary>节点解析不到审批人时的处理策略。</summary>
public enum EmptyApproverPolicy
{ Error, AutoApprove }

/// <summary>流程定义（节点图），以 JSON 存储在 <see cref="Domain.ApprovalDefinition.DefinitionJson"/> 中。</summary>
public sealed class FlowDefinition
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>JSON 协议版本，与发布版本独立。外部 JSON 必须显式声明当前版本。</summary>
    [JsonRequired]
    [JsonConverter(typeof(FlowSchemaVersionConverter))]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ImportJsonOptions = new(JsonOptions)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false
    };

    public string StartNodeId { get; set; } = "";

    /// <summary>发起设置（发起中心、发起角色、标题模板），可选。</summary>
    public FlowSettings? Settings { get; set; }

    /// <summary>可选模块的 JSON 快照；Core 不解释其字段结构，随流程版本固化。</summary>
    public object? Form { get; set; }

    /// <summary>流程级通知规则：对所有审批节点生效，以及流程结束（<see cref="NotifyEvent.Completed"/>）。</summary>
    public List<NotificationRule> Notifications { get; set; } = [];

    public Dictionary<string, FlowNode> Nodes { get; set; } = [];

    public FlowNode GetNode(string id) =>
        Nodes.TryGetValue(id, out var node)
            ? node
            : throw new ApprovalException($"流程定义中不存在节点 '{id}'");

    public static FlowDefinition Parse(string json) => Parse(json, JsonOptions);

    internal static FlowDefinition ParseImported(string json) => Parse(json, ImportJsonOptions);

    private static FlowDefinition Parse(string json, JsonSerializerOptions options)
    {
        FlowDefinition definition;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("流程定义根节点必须是 JSON 对象");
            if (document.RootElement.TryGetProperty("nodes", out var nodeMap) && nodeMap.ValueKind == JsonValueKind.Object
                && nodeMap.EnumerateObject().GroupBy(p => p.Name, StringComparer.Ordinal).Any(g => g.Count() > 1))
                throw new ApprovalException("流程包含重复节点 ID") { Code = ApprovalErrorCode.InvalidDefinition };
            definition = JsonSerializer.Deserialize<FlowDefinition>(json, options)
                         ?? throw new ApprovalException("流程定义为空");
        }
        catch (JsonException ex)
        {
            throw new ApprovalException($"流程定义 JSON 格式错误：{ex.Message}", ex) { Code = ApprovalErrorCode.InvalidDefinition };
        }

        definition.EnsureSupportedSchema();
        if (definition.Nodes is null || definition.Nodes.Values.Any(n => n is null))
            throw new ApprovalException("节点集合不能为空或包含空节点") { Code = ApprovalErrorCode.InvalidDefinition };
        // 字典 key 即节点 ID，未显式填写 Id 时以 key 为准
        foreach (var (key, node) in definition.Nodes)
        {
            node.Name ??= "";
            node.Branches ??= []; node.Approvers ??= []; node.Notifications ??= []; node.FieldPermissions ??= [];
            if (node.Branches.Any(b => b is null || string.IsNullOrWhiteSpace(b.Next))
                || node.Approvers.Any(r => r is null || string.IsNullOrWhiteSpace(r.Type)))
                throw new ApprovalException("节点包含空的分支、审批人或扩展配置") { Code = ApprovalErrorCode.InvalidDefinition };
            if (string.IsNullOrWhiteSpace(node.Id)) node.Id = key;
        }
        return definition;
    }

    public string ToJson()
    {
        EnsureSupportedSchema();
        return JsonSerializer.Serialize(this, JsonOptions);
    }

    private void EnsureSupportedSchema()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            throw new ApprovalException($"不支持流程协议版本 {SchemaVersion}，当前支持 v{CurrentSchemaVersion}") { Code = ApprovalErrorCode.InvalidDefinition };
    }

    /// <summary>
    /// 公开视图（给实例的查看者画流程图、展示表单）：去掉通知规则、时限策略和发起角色，
    /// 保留节点、连线、名称、类型、坐标与表单结构。完整定义仅管理员可读。
    /// </summary>
    public FlowDefinition ToPublicView()
    {
        var view = Parse(ToJson());
        view.Notifications = [];
        if (view.Settings is { } settings) settings.InitiatorRoles = [];
        foreach (var node in view.Nodes.Values)
        {
            node.Notifications = [];
            node.Timeout = null;
            node.Actions = null;
        }
        return view;
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class FlowNode
{
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public NodeType Type { get; set; }

    public ApproveMode Mode { get; set; } = ApproveMode.Any;

    /// <summary>审批人（Approval 节点）或抄送人（Cc 节点）规则。</summary>
    public List<ApproverRule> Approvers { get; set; } = [];

    /// <summary>条件分支（Condition 节点，按 Priority 升序匹配，Expression 为空表示默认分支）；并行分支（Parallel 节点，全部激活）。</summary>
    public List<ConditionBranch> Branches { get; set; } = [];

    public string? Next { get; set; }

    /// <summary>仅 Action 节点使用。数组顺序即执行顺序。</summary>
    public List<ActionStep>? Actions { get; set; }

    public EmptyApproverPolicy EmptyApproverPolicy { get; set; } = EmptyApproverPolicy.Error;

    /// <summary>审批节点：驳回时是否允许退回到之前的节点或发起人（否则只能驳回并结束）。</summary>
    public bool AllowReturn { get; set; } = true;

    /// <summary>审批节点：审批人是否可以加签。</summary>
    public bool AllowAddSign { get; set; } = true;

    /// <summary>是否允许处理人转交；管理员改派仍由独立授权和资格规则控制。</summary>
    public bool AllowTransfer { get; set; } = true;

    /// <summary>处理、改派和加签时重新核对原审批人规则与用户目录，避免岗位任务变为任意用户任务。</summary>
    public bool RequireQualifiedAssignee { get; set; }

    public bool RequireComment { get; set; }

    /// <summary>节点通知规则。</summary>
    public List<NotificationRule> Notifications { get; set; } = [];

    /// <summary>处理时限（仅 Approval 节点），为空表示不限时。</summary>
    public TimeoutPolicy? Timeout { get; set; }

    /// <summary>
    /// 表单字段在本节点的权限：字段标识 → 可编辑 / 隐藏，未列出的字段只读。审批节点可设可编辑与隐藏，抄送节点只能设隐藏。
    /// 可编辑：表单审批的审批人同意时可修改（合并后整表重新校验与计算）；
    /// 隐藏：只以设置了隐藏的节点的处理人 / 抄送人身份查看的人看不到该字段（服务端过滤表单数据与修改轨迹）。
    /// </summary>
    public Dictionary<string, FieldAccess> FieldPermissions { get; set; } = [];

    /// <summary>设计器中的画布坐标，仅用于界面展示，引擎不使用。</summary>
    public NodePosition? Position { get; set; }

    /// <summary>指定权限的字段（字段标识不区分大小写）。</summary>
    public IReadOnlySet<string> FieldsWith(FieldAccess access) =>
        FieldPermissions.Where(p => p.Value == access).Select(p => p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
}

/// <summary>表单字段在节点的权限；未设置为只读。</summary>
public enum FieldAccess
{
    Editable,
    Hidden
}

public sealed record NodePosition(double X, double Y);

/// <param name="Type">审批人解析器类型，对应 <see cref="Abstractions.IApproverResolver.Type"/>，如 User / Role / FormField。</param>
/// <param name="Value">解析参数，如用户 ID、角色名、表单字段名。</param>
public sealed record ApproverRule(string Type, string Value);

public sealed record ConditionBranch(int Priority, string? Expression, string Next);

/// <summary>触发通知的事件。</summary>
public enum NotifyEvent
{
    /// <summary>到达节点（审批节点生成待办或抄送）。</summary>
    Enter,

    Approved,
    Rejected,
    Transferred,

    /// <summary>待办超过处理时限。</summary>
    Timeout,

    /// <summary>流程结束（通过、驳回、撤回）。</summary>
    Completed,

    Returned,
    Resubmitted,

    /// <summary>由显式 Notification 动作触发。</summary>
    Action
}

/// <summary>
/// 通知规则：<see cref="Events"/> 中任一事件发生时，按模板给 <see cref="Recipients"/> 通过 <see cref="Channels"/> 发消息。
/// 接收人类型除审批人解析器（User / Role / FormField / Initiator / 自定义）外，
/// 还支持 <c>Assignees</c>（当前审批人）和 <c>Approvers</c>（已审批过的人）。
/// </summary>
public sealed class NotificationRule
{
    public List<NotifyEvent> Events { get; set; } = [];

    public List<ApproverRule> Recipients { get; set; } = [];

    /// <summary>渠道名：InApp（站内信，内置）或宿主声明的通知路由提示。</summary>
    public List<string> Channels { get; set; } = [];

    /// <summary>标题模板，为空时按事件使用默认标题。</summary>
    public string? Title { get; set; }

    /// <summary>内容模板，如 <c>【待审批】{标题}，申请人 {申请人}</c>；为空时按事件使用默认模板。</summary>
    public string? Content { get; set; }
}

public enum TimeoutAction
{
    /// <summary>通知（按节点的 Timeout 通知规则），可按间隔重复催办。</summary>
    Remind,

    AutoApprove,
    AutoReject
}

/// <summary>审批节点的处理时限，按自然时间计算。</summary>
public sealed class TimeoutPolicy
{
    /// <summary>待办生成后多少分钟超时。</summary>
    public int Minutes { get; set; }

    public TimeoutAction Action { get; set; } = TimeoutAction.Remind;

    /// <summary>提醒后仍未处理时，每隔多少分钟再催办一次；为空只提醒一次。</summary>
    public int? RepeatMinutes { get; set; }

    /// <summary>最多提醒次数（含第一次）。</summary>
    public int MaxReminders { get; set; } = 3;
}
