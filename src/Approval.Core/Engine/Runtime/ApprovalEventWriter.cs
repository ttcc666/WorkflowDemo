using Approval.Core.Abstractions;
using Approval.Core.Conditions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Notifications;
using Approval.Core.Persistence;
using Approval.Core.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Approval.Core.Engine.Runtime;

/// <summary>一次事件的上下文：事件对应的“当前审批人”及模板变量。</summary>
internal sealed record EventInfo(IReadOnlyList<Assignee> Assignees)
{
    public string? OperatorId { get; init; }
    public string? Comment { get; init; }
    public string? TransferTo { get; init; }
    public DateTime? DueAt { get; init; }
    public string? ReturnTo { get; init; }
}

/// <summary>追加审批审计、可靠事件、站内消息及通知 Outbox；持久化由调用方的工作单元拥有。</summary>
internal sealed class ApprovalEventWriter(
    DbContext db, ApprovalUnitOfWork unit, IApprovalIdentityProvider identity, ApprovalAssigneeService assignees,
    IReadOnlySet<string> channels, ApprovalOptions options, ApprovalDisplaySettings displaySettings,
    TimeProvider clock, ILogger<ApprovalEngine> logger)
{
    private const string SystemOperator = ApprovalEngine.SystemOperator;
    private readonly DbContext _db = db;
    private readonly ApprovalUnitOfWork _unit = unit;
    private readonly IApprovalIdentityProvider _identity = identity;
    private readonly ApprovalAssigneeService _assignees = assignees;
    private readonly IReadOnlySet<string> _channels = channels;
    private readonly ApprovalOptions _options = options;
    private readonly ApprovalDisplaySettings _displaySettings = displaySettings;
    private readonly TimeProvider _clock = clock;
    private readonly ILogger<ApprovalEngine> _logger = logger;
    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    internal void AddCarbonCopy(ApprovalInstance instance, FlowNode node, Assignee recipient) =>
        _db.Set<ApprovalCarbonCopy>().Add(new ApprovalCarbonCopy
        {
            Id = _unit.NewId("ApprovalEngine"),
            InstanceId = instance.Id,
            NodeId = node.Id,
            AssigneeKind = recipient.Kind,
            AssigneeValue = recipient.Value,
            Round = instance.Round,
            CreatedAt = Now
        });

    internal void AddLog(ApprovalOperationContext operation, ApprovalInstance instance, FlowNode? node, ApprovalAction action, string operatorId, string? comment) =>
        AddLog(operation, instance, action, operatorId, comment, node?.Id, node?.Name);

    internal void AddLog(ApprovalOperationContext operation, ApprovalInstance instance, ApprovalAction action, string operatorId, string? comment,
        string? nodeId, string? nodeName, string? details = null)
    {
        var taskId = action is ApprovalAction.Submit or ApprovalAction.Complete or ApprovalAction.NodeEntered or ApprovalAction.Cc
            or ApprovalAction.Withdraw or ApprovalAction.Terminate ? null : operation.TaskId;
        _db.Set<ApprovalLog>().Add(new ApprovalLog
        {
            Id = _unit.NewId("ApprovalEngine"),
            InstanceId = instance.Id,
            TaskId = taskId,
            Sequence = ++instance.LogSequence,
            NodeId = nodeId,
            NodeName = nodeName,
            Action = action,
            OperatorId = operatorId,
            Comment = comment,
            Details = details,
            CreatedAt = Now
        });
        WriteEvent(operation, instance, action.ToString(), nodeId, nodeName, taskId, operatorId, comment);
    }

    internal void WriteEvent(ApprovalOperationContext operation, ApprovalInstance instance, string type, string? nodeId, string? nodeName, Guid? taskId,
        string operatorId, string? comment)
    {
        AddOutbox(operation, "Host", instance, null, type,
            id => new ApprovalEvent(id, type, instance.Id, instance.DefinitionId, instance.DefinitionVersion,
                instance.BusinessType, instance.BusinessKey, type switch
                {
                    "Reject" or "TimeoutReject" => InstanceStatus.Rejected,
                    "Withdraw" => InstanceStatus.Withdrawn,
                    "Terminate" => InstanceStatus.Terminated,
                    _ => instance.Status
                }, nodeId, nodeName, taskId, operatorId, comment, Now)
            {
                Recipients = taskId is { } tid && instance.Tasks.FirstOrDefault(t => t.Id == tid) is { } task
                ? [new Assignee(task.AssigneeKind, task.AssigneeValue)] : []
            });
    }

    internal static string Describe(Assignee a) => a.Kind == AssigneeKind.Role ? $"角色[{a.Value}]" : a.Value;

    /// <summary>
    /// 触发事件：收集节点规则与流程级规则（流程级规则对审批节点和流程结束生效），
    /// 解析接收人、渲染模板，站内信直接写表，其余渠道写入 Outbox。没有匹配的规则时使用 fallback。
    /// </summary>
    internal async Task RaiseAsync(ApprovalOperationContext operation, NotifyEvent evt, ApprovalInstance instance, FlowDefinition flow, FlowNode? node,
        EventInfo info, CancellationToken ct, NotificationRule? fallback = null)
    {
        var rules = (node?.Notifications ?? []).Where(r => r.Events.Contains(evt)).ToList();
        if (evt == NotifyEvent.Completed || node?.Type == NodeType.Approval)
            rules.AddRange(flow.Notifications.Where(r => r.Events.Contains(evt)));
        if (rules.Count == 0 && fallback is not null) rules.Add(fallback);
        if (rules.Count == 0) return;

        var form = FormData.Parse(instance.FormDataJson);
        var variables = TemplateVariables.Build(instance, form, node, _options, new TemplateVariables.EventValues
        {
            OperatorId = info.OperatorId,
            Comment = info.Comment,
            TransferTo = info.TransferTo,
            DueAt = info.DueAt,
            ReturnTo = info.ReturnTo,
            Assignees = info.Assignees.Select(Describe).ToList()
        }, _displaySettings);

        // 多条规则命中同一人同一渠道同一内容时只发一次
        var sent = new HashSet<(string Channel, string User, string Content)>();
        foreach (var rule in rules)
        {
            var recipients = await ResolveRecipientsAsync(rule, instance, node, form, info.Assignees, ct);
            if (recipients.Count == 0) continue;

            var title = Truncate(Render(string.IsNullOrWhiteSpace(rule.Title) ? NotificationDefaults.Titles[evt] : rule.Title, variables), 200);
            var content = Truncate(Render(string.IsNullOrWhiteSpace(rule.Content) ? NotificationDefaults.Contents[evt] : rule.Content, variables), 4000);
            SendNotifications(operation, evt, instance, node, info, rule.Channels, recipients, title, content,
                variables.GetValueOrDefault("link") as string, sent);
        }
    }

    internal async Task<bool> RaiseActionNotificationAsync(ApprovalOperationContext operation, ApprovalInstance instance, FlowNode node,
        NotificationRule rule, string title, string content, string operatorId, CancellationToken ct)
    {
        if (!_options.Outbox.EnableDispatcher && rule.Channels.Any(channel =>
                !string.Equals(channel, InAppChannel.Name, StringComparison.OrdinalIgnoreCase)))
            throw ApprovalAutomaticTraversal.Invalid("外部通知动作需要启用 Outbox Dispatcher");
        var current = instance.Tasks.Where(t => t.Id == operation.TaskId)
            .Select(t => new Assignee(t.AssigneeKind, t.AssigneeValue)).ToArray();
        var info = new EventInfo(current) { OperatorId = operatorId };
        var recipients = await ResolveRecipientsAsync(rule, instance, node, FormData.Parse(instance.FormDataJson), current, ct);
        return SendNotifications(operation, NotifyEvent.Action, instance, node, info, rule.Channels, recipients,
            Truncate(title, 200), Truncate(content, 4000), null, []);
    }

    private bool SendNotifications(ApprovalOperationContext operation, NotifyEvent evt, ApprovalInstance instance, FlowNode? node,
        EventInfo info, List<string> channels, List<string> recipients, string title, string content, string? link,
        HashSet<(string Channel, string User, string Content)> sent)
    {
        var queued = false;
        foreach (var channel in channels.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var users = recipients.Where(u => sent.Add((channel.ToLowerInvariant(), u, content))).ToList();
            if (users.Count == 0) continue;
            if (string.Equals(channel, InAppChannel.Name, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var user in users) AddMessage(instance, node, evt, user, title, content);
            }
            else if (_channels.Contains(channel))
            {
                queued = true;
                AddOutbox(operation, "Host", instance, node, "NotificationRequested",
                    id => new ApprovalEvent(id, "NotificationRequested", instance.Id, instance.DefinitionId, instance.DefinitionVersion,
                        instance.BusinessType, instance.BusinessKey, instance.Status, node?.Id, node?.Name, null,
                        info.OperatorId ?? SystemOperator, info.Comment, Now)
                    {
                        DeliveryTarget = channel,
                        Notification = new ApprovalNotificationRequest(id, evt, instance.Id, node?.Id,
                        title, content, users, string.IsNullOrEmpty(link) ? null : link)
                    });
            }
            else
            {
                throw new ApprovalException($"通知适配器 '{channel}' 未注册，请先配置宿主适配器后重试")
                    { Code = ApprovalErrorCode.InvalidDefinition };
            }
        }
        return queued;
    }

    /// <summary>解析通知接收人并把角色展开为用户。</summary>
    private async Task<List<string>> ResolveRecipientsAsync(NotificationRule rule, ApprovalInstance instance, FlowNode? node,
        IReadOnlyDictionary<string, object?> form, IReadOnlyList<Assignee> eventAssignees, CancellationToken ct)
    {
        var assignees = new List<Assignee>();
        foreach (var recipient in rule.Recipients)
        {
            if (string.Equals(recipient.Type, NotificationDefaults.AssigneesRecipient, StringComparison.OrdinalIgnoreCase))
                assignees.AddRange(eventAssignees);
            else if (string.Equals(recipient.Type, NotificationDefaults.ApproversRecipient, StringComparison.OrdinalIgnoreCase))
                assignees.AddRange(instance.Tasks
                    .Where(t => t.Status == ApprovalTaskStatus.Approved && t.HandledBy is not null && t.HandledBy != SystemOperator)
                    .Select(t => new Assignee(AssigneeKind.User, t.HandledBy!)));
            else if (_assignees.CanResolve(recipient.Type))
                assignees.AddRange(await _assignees.ResolveRulesAsync([recipient], instance, node ?? new FlowNode(), form, ct));
            else
                _logger.LogWarning("通知接收人类型 '{Type}' 未注册，已跳过", recipient.Type);
        }

        var users = new List<string>();
        foreach (var assignee in assignees.Distinct())
        {
            if (assignee.Kind == AssigneeKind.Role) users.AddRange(await _identity.GetUserIdsInRoleAsync(assignee.Value, ct));
            else users.Add(assignee.Value);
        }
        return users.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct().ToList();
    }

    private string Render(string template, IReadOnlyDictionary<string, object?> variables) =>
        TemplateRenderer.Render(template, variables, TemplateEncoding.Raw, _displaySettings.DisplayTimeZone);

    private void AddMessage(ApprovalInstance instance, FlowNode? node, NotifyEvent evt, string userId, string title, string content) =>
        _db.Set<ApprovalMessage>().Add(new ApprovalMessage
        {
            Id = _unit.NewId("ApprovalEngine.Notifications"),
            UserId = userId,
            Title = title,
            Content = content,
            Event = evt,
            InstanceId = instance.Id,
            NodeId = node?.Id,
            CreatedAt = Now
        });

    /// <summary>写入 Outbox：随本次操作一起提交，提交后由后台任务投递。payload 工厂接收消息 ID（幂等键）。</summary>
    private void AddOutbox(ApprovalOperationContext operation, string target, ApprovalInstance instance, FlowNode? node, string? description,
        Func<Guid, object> payload, Guid? idOverride = null)
    {
        var id = idOverride ?? _unit.NewId("ApprovalEngine.Notifications");
        var json = JsonSerializer.Serialize(payload(id), OutboxJson.Options);
        if (target == WorkflowActionDelivery.Target && System.Text.Encoding.UTF8.GetByteCount(json) > WorkflowActionDelivery.MaxPayloadBytes)
            throw ApprovalAutomaticTraversal.Invalid("异步动作快照超过 512 KiB");
        _db.Set<ApprovalOutboxMessage>().Add(new ApprovalOutboxMessage
        {
            Id = id,
            Target = target,
            InstanceId = instance.Id,
            NodeId = node?.Id,
            Description = description is null ? null : Truncate(description, 500),
            PayloadJson = json,
            Status = OutboxStatus.Pending,
            MaxAttempts = Math.Max(1, _options.Outbox.MaxAttempts),
            NextAttemptAt = Now,
            CreatedAt = Now
        });
        operation.OutboxWritten = true;
    }

    internal void QueueAction(ApprovalOperationContext operation, ApprovalInstance instance, FlowNode node, WorkflowActionDelivery delivery)
    {
        if (!_options.Outbox.EnableDispatcher) throw ApprovalAutomaticTraversal.Invalid("异步动作需要启用 Outbox Dispatcher");
        AddOutbox(operation, WorkflowActionDelivery.Target, instance, node, $"{delivery.Type}/{delivery.Context.ActionId}",
            _ => delivery, delivery.Context.ExecutionId);
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
}
