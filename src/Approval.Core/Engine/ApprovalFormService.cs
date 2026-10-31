using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Conditions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Forms;
using Approval.Core.Persistence;
using Approval.Core.Templates;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine;

/// <summary>发起中心列表项（不含节点与表单）。</summary>
public sealed record StartableFlow(string Key, string Name, int Version, string? Category, string? Description);

/// <summary>发起页：最新版本的表单与设置。</summary>
public sealed record StartForm(Guid DefinitionId, string Key, string Name, int Version, FlowSettings Settings, FormSchema Form);

public sealed class ApprovalFormOptions
{
    /// <summary>表单审批实例的业务类型；表单数据即单据，存放在 <see cref="ApprovalInstance.FormDataJson"/>。</summary>
    public string BusinessType { get; set; } = "Form";
}

/// <summary>
/// 表单审批：用户在发起中心选择 <see cref="FlowSettings.Startable"/> 的流程、填写表单直接发起，无需业务代码。
/// 表单数据按流程定义中的 <see cref="FormSchema"/> 在服务端校验与规范化。
/// </summary>
public sealed partial class ApprovalFormService(
    ApprovalDbContextAccessor accessor,
    ApprovalEngine engine,
    IApprovalIdentityProvider identity,
    ApprovalFormOptions formOptions,
    ApprovalOptions options,
    TimeProvider clock,
    IApprovalUserDirectory? directory = null)
{
    private const int MaxTitleLength = 500;
    private readonly DbContext _db = accessor.Context;

    public string BusinessType => formOptions.BusinessType;

    public async Task<List<StartableFlow>> ListStartableAsync(string userId, CancellationToken ct = default)
    {
        var result = new List<StartableFlow>();
        foreach (var definition in await LatestDefinitions().ToListAsync(ct))
        {
            if (TryParse(definition) is not { Settings.Startable: true } flow) continue;
            if (!await CanStartAsync(flow.Settings!, userId, ct)) continue;
            result.Add(new StartableFlow(definition.Key, definition.Name, definition.Version, flow.Settings!.Category, flow.Settings.Description));
        }
        return result.OrderBy(f => f.Category ?? "").ThenBy(f => f.Name).ToList();
    }

    public async Task<StartForm> GetStartFormAsync(string key, string userId, CancellationToken ct = default)
    {
        var (definition, flow) = await LoadStartableAsync(key, userId, ct);
        return new StartForm(definition.Id, definition.Key, definition.Name, definition.Version, flow.Settings!, flow.Form ?? new FormSchema());
    }

    /// <summary>发起表单审批：校验表单 → 生成标题 → 发起实例（业务主键为新 ID）。从草稿提交时同一事务删除草稿。</summary>
    public async Task<ApprovalInstance> StartAsync(string key, string userId, JsonElement formData, string? comment = null,
        Guid? draftId = null, CancellationToken ct = default)
    {
        var (definition, flow) = await LoadStartableAsync(key, userId, ct);
        var json = await NormalizeAsync(flow.Form ?? new FormSchema(), formData, ct);

        // 草稿的删除随发起一起提交（引擎在同一 DbContext 中 SaveChanges）
        if (draftId is { } id) _db.Set<ApprovalDraft>().Remove(await FindDraftAsync(userId, id, ct));

        return await engine.StartAsync(new StartApprovalRequest(definition.Key, BusinessType, Guid.NewGuid().ToString("N"), userId)
        {
            Title = RenderTitle(flow.Settings!, definition.Name, userId, clock.GetUtcNow().UtcDateTime, json),
            FormData = json,
            Comment = comment
        }, ct);
    }

    /// <summary>
    /// 退回发起人后重新提交。表单审批可传入新的表单数据（按实例绑定版本的表单校验，标题随之更新）；
    /// 其他业务的数据由业务单据决定，传入表单数据会被拒绝。
    /// </summary>
    public async Task ResubmitAsync(Guid taskId, string userId, JsonElement? formData, string? comment = null, CancellationToken ct = default)
    {
        if (formData is not { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } data)
        {
            await engine.ResubmitAsync(taskId, userId, comment, ct: ct);
            return;
        }

        var task = await _db.Set<ApprovalTask>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct)
                   ?? throw new ApprovalException("任务不存在") { Code = ApprovalErrorCode.NotFound };
        // 跟踪加载：引擎随后在同一 DbContext 中取到同一实体，标题修改随重新提交一起保存
        var instance = await _db.Set<ApprovalInstance>().FirstAsync(i => i.Id == task.InstanceId, ct);
        if (!string.Equals(instance.BusinessType, BusinessType, StringComparison.OrdinalIgnoreCase))
            throw new ApprovalException("该申请的数据由业务单据决定，不能在审批中修改表单");
        if (task.Kind != ApprovalTaskKind.Resubmit || instance.InitiatorId != userId)
            throw new ApprovalException("只有被退回的发起人可以修改表单后重新提交") { Code = ApprovalErrorCode.Forbidden };

        var definition = await _db.Set<ApprovalDefinition>().AsNoTracking().FirstAsync(d => d.Id == instance.DefinitionId, ct);
        var flow = FlowDefinition.Parse(definition.DefinitionJson);
        var json = await NormalizeAsync(flow.Form ?? new FormSchema(), data, ct);
        instance.Title = RenderTitle(flow.Settings ?? new FlowSettings(), definition.Name, userId, instance.CreatedAt, json);

        await engine.ResubmitAsync(taskId, userId, comment, json, ct);
    }

    private async Task<string> NormalizeAsync(FormSchema schema, JsonElement data, CancellationToken ct)
    {
        var result = FormDataValidator.Validate(schema, data);
        var errors = result.Errors.ToDictionary(e => e.Key, e => e.Value);

        // 人员字段可能被用作审批人：注册了用户目录时校验用户存在
        if (directory is not null && result.IsValid)
        {
            var values = FormData.Parse(result.Json);
            foreach (var field in schema.Fields.Where(f => f.Type is FormFieldType.User or FormFieldType.Users))
            {
                var ids = values.GetValueOrDefault(field.Key) switch
                {
                    string id => [id],
                    object?[] list => list.OfType<string>().ToArray(),
                    _ => Array.Empty<string>()
                };
                if (ids.Length == 0) continue;
                var found = (await directory.GetUsersAsync(ids, ct)).Select(u => u.Id).ToHashSet();
                var missing = ids.Where(id => !found.Contains(id)).ToList();
                if (missing.Count > 0) errors[field.Key] = [$"{field.Label}中的用户不存在：{string.Join("、", missing)}"];
            }
        }

        return errors.Count == 0 ? result.Json : throw new ApprovalValidationException(errors, "表单填写有误，请检查标红的字段");
    }

    private string RenderTitle(FlowSettings settings, string flowName, string userId, DateTime createdAt, string formJson)
    {
        var template = string.IsNullOrWhiteSpace(settings.TitleTemplate) ? "{流程名} - {申请人}" : settings.TitleTemplate;
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["流程名"] = flowName, ["flowName"] = flowName,
            ["申请人"] = userId, ["initiator"] = userId,
            ["发起时间"] = createdAt, ["createdAt"] = createdAt
        };
        var form = FormData.Parse(formJson);
        variables["form"] = form;
        variables["表单"] = form;

        var tz = options.DisplayTimeZone;
        var title = TemplateRenderer.Render(template, variables, timeZone: tz).Trim();
        if (title.Length == 0) title = flowName;
        return title.Length > MaxTitleLength ? title[..MaxTitleLength] : title;
    }

    private async Task<(ApprovalDefinition Definition, FlowDefinition Flow)> LoadStartableAsync(string key, string userId, CancellationToken ct)
    {
        var definition = await LatestDefinitions().FirstOrDefaultAsync(d => d.Key == key, ct);
        if (definition is null || TryParse(definition) is not { Settings.Startable: true } flow)
            throw new ApprovalException($"流程 '{key}' 不存在或不支持在发起中心发起") { Code = ApprovalErrorCode.NotFound };
        if (!await CanStartAsync(flow.Settings!, userId, ct))
            throw new ApprovalException("你没有发起该流程的权限") { Code = ApprovalErrorCode.Forbidden };
        return (definition, flow);
    }

    private async Task<bool> CanStartAsync(FlowSettings settings, string userId, CancellationToken ct)
    {
        if (settings.InitiatorRoles.Count == 0) return true;
        foreach (var role in settings.InitiatorRoles)
        {
            if (await identity.IsInRoleAsync(userId, role, ct)) return true;
        }
        return false;
    }

    private IQueryable<ApprovalDefinition> LatestDefinitions() =>
        _db.Set<ApprovalDefinition>().AsNoTracking()
            .Where(d => d.IsPublished && d.Version == _db.Set<ApprovalDefinition>()
                .Where(x => x.Key == d.Key && x.IsPublished).Max(x => x.Version));

    private static FlowDefinition? TryParse(ApprovalDefinition definition)
    {
        try
        {
            return FlowDefinition.Parse(definition.DefinitionJson);
        }
        catch (ApprovalException)
        {
            return null;
        }
    }
}

/// <summary>表单审批的业务回调：表单数据即单据，审批结果只体现在实例状态上，无需额外处理。</summary>
public sealed class FormApprovalHandler(ApprovalFormOptions options) : IApprovalBusinessHandler
{
    public string BusinessType => options.BusinessType;

    public Task OnApprovedAsync(ApprovalInstance instance, CancellationToken ct) => Task.CompletedTask;

    public Task OnRejectedAsync(ApprovalInstance instance, CancellationToken ct) => Task.CompletedTask;
}
