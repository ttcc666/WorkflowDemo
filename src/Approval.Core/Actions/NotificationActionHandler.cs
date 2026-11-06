using System.Text.Json;
using System.Text.Json.Serialization;
using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Notifications;
using Approval.Core.Persistence;

namespace Approval.Core.Actions;

internal sealed class NotificationActionHandler(IEnumerable<IApproverResolver> resolvers, ApprovalOptions options)
    : IApprovalActionHandler, IApprovalActionPreviewHandler
{
    private sealed record Config(List<ApproverRule> Recipients, List<string> Channels, string Title, string Content);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly HashSet<string> _channels = new(
        [InAppChannel.Name, .. options.NotificationTargets.Select(c => c.Name)], StringComparer.OrdinalIgnoreCase);
    private readonly FlowValidationContext _recipientContext = RecipientContext(resolvers);

    public ApprovalActionDescriptor Descriptor => new("Notification", "消息通知", "站内信随事务保存，外部通知提交后投递",
        [ActionExecutionMode.Sync], ConfigSchema: JsonSerializer.SerializeToElement(new
        {
            type = "object", required = new[] { "recipients", "channels", "title", "content" },
            properties = new
            {
                recipients = new { type = "array" }, channels = new { type = "array", items = new { @enum = _channels.Order().ToArray() } },
                title = new { type = "string", maxLength = 200 }, content = new { type = "string", maxLength = 4000 }
            }
        }));

    public bool ProvidesOutput => false;

    public IReadOnlyList<string> ValidateConfig(int version, JsonElement config) => ValidateConfig(version, config, null);

    internal IReadOnlyList<string> ValidateConfig(int version, JsonElement config, IReadOnlySet<string>? userFields)
    {
        try
        {
            var value = Read(config);
            if (version != 1 || value.Recipients is not { Count: > 0 and <= 20 } || value.Channels is not { Count: > 0 and <= 10 } ||
                value.Recipients.Any(r => r is null || !_recipientContext.ApproverTypes.Contains(r.Type) || r.Value is not { Length: <= 256 }) ||
                value.Channels.Any(channel => !_channels.Contains(channel) ||
                    !options.Outbox.EnableDispatcher && !string.Equals(channel, InAppChannel.Name, StringComparison.OrdinalIgnoreCase)) ||
                value.Title is not { Length: > 0 and <= 200 } || value.Content is not { Length: > 0 and <= 4000 } ||
                !ActionVariables.IsTemplate(value.Title) || !ActionVariables.IsTemplate(value.Content))
                return ["Notification 接收人、渠道或模板无效"];
            var errors = new List<string>();
            foreach (var recipient in value.Recipients)
            {
                if ((string.Equals(recipient.Type, "FormField", StringComparison.OrdinalIgnoreCase) ||
                     _recipientContext.ApproverDescriptors?.GetValueOrDefault(recipient.Type)?.ValueKind == ApproverValueKind.FormUserField) &&
                    (string.IsNullOrWhiteSpace(recipient.Value) || userFields?.Contains(recipient.Value) != true))
                    errors.Add($"Notification 接收人引用了无效的人员字段 '{recipient.Value}'，必须选择人员字段（User / Users）");
                else
                    FlowDefinitionValidator.ValidateRuleValue(errors, "Notification 接收人", recipient, _recipientContext, userFields);
            }
            return errors;
        }
        catch (JsonException) { return ["Notification 配置无效"]; }
    }

    public async Task<ApprovalActionResult> ExecuteAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct)
    {
        var runtime = context.Runtime ?? throw new InvalidOperationException("通知动作需要当前审批事务");
        var value = Read(config);
        var root = ActionVariables.Snapshot(context);
        var externalId = Guid.ParseExact(ApprovalCommandHash.Of("Notification", context.InstanceId.ToString("N"), context.NodeId, context.ActionId)[..32], "N");
        var external = value.Channels.Any(channel => !IsInApp(channel));
        var channels = external && runtime.State.QueuedActions?.Contains(externalId) == true
            ? value.Channels.Where(IsInApp).ToList() : value.Channels;
        var queued = await runtime.Events.RaiseActionNotificationAsync(runtime.Operation, runtime.Instance, runtime.Node,
            new NotificationRule { Recipients = value.Recipients, Channels = channels },
            ActionVariables.Render(value.Title, root), ActionVariables.Render(value.Content, root), context.OperatorId, ct);
        if (queued) (runtime.State.QueuedActions ??= []).Add(externalId);
        return new(true);
    }

    public Task<ApprovalActionResult> PreviewAsync(ApprovalActionContext context, JsonElement config, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var value = Read(config);
        var root = ActionVariables.Snapshot(context);
        return Task.FromResult(new ApprovalActionResult(true, JsonSerializer.SerializeToElement(new
        {
            value.Recipients, value.Channels,
            title = ActionVariables.Render(value.Title, root), content = ActionVariables.Render(value.Content, root)
        }, Json)));
    }

    private static Config Read(JsonElement json) => json.Deserialize<Config>(Json) ?? throw new JsonException();
    private static bool IsInApp(string channel) => string.Equals(channel, InAppChannel.Name, StringComparison.OrdinalIgnoreCase);

    private static FlowValidationContext RecipientContext(IEnumerable<IApproverResolver> resolvers)
    {
        var descriptors = new Dictionary<string, ApproverTypeDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var resolver in resolvers) descriptors[resolver.Type] = resolver.Descriptor;
        foreach (var descriptor in NotificationDefaults.NotificationRecipientDescriptors) descriptors[descriptor.Type] = descriptor;
        return new() { ApproverTypes = descriptors.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase), ApproverDescriptors = descriptors };
    }
}
