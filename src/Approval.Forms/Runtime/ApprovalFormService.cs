using Approval.Core;
using Approval.Core.Abstractions;
using Approval.Core.Conditions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine;
using Approval.Core.Persistence;
using Approval.Core.Templates;
using Approval.Forms.Attachments;
using Approval.Forms.Schema;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Approval.Forms.Runtime;

/// <summary>发起中心列表项（不含节点与表单）。</summary>
public sealed record StartableFlow(string Key, string Name, int Version, string? Category, string? Description);

/// <summary>发起页：最新版本的表单与设置。</summary>
public sealed record StartForm(Guid DefinitionId, string Key, string Name, int Version, FlowSettings Settings, FormSchema Form);

public sealed class ApprovalFormOptions
{
    public ApprovalFileOptions Files { get; } = new();

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
    ApprovalFileService files,
    IApprovalIdentityProvider identity,
    ApprovalFormOptions formOptions,
    ApprovalOptions options,
    TimeProvider clock,
    IApprovalUserDirectory? directory = null)
{
    private const int MaxTitleLength = 500;
    private readonly Approval.Core.Authorization.ApprovalAuthorizationService _authorization = ApprovalFormsBridge.Authorization(accessor);
    private readonly DbContext _db = accessor.Context;
    private readonly ApprovalFormsBridge _bridge = new(accessor);
    private readonly IApprovalIdentityProvider _hostIdentity = ApprovalFormsBridge.Identity(accessor, identity);
    private readonly IApprovalUserDirectory? _hostDirectory = ApprovalFormsBridge.Directory(accessor, directory);
    private readonly TimeProvider _clock = ApprovalFormsBridge.Clock(accessor, clock);

    public string BusinessType => formOptions.BusinessType;

    public async Task<List<StartableFlow>> ListStartableAsync(string userId, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceStart, new(), userId, ct);
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
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceStart, new() { DefinitionKey = key }, userId, ct);
        var (definition, flow) = await LoadStartableAsync(key, userId, ct);
        return new StartForm(definition.Id, definition.Key, definition.Name, definition.Version, flow.Settings!, flow.GetForm() ?? new FormSchema());
    }

    /// <summary>发起表单审批：校验表单 → 生成标题 → 发起实例（业务主键为新 ID）。从草稿提交时同一事务删除草稿。</summary>
    public Task<ApprovalInstance> StartAsync(string key, string userId, JsonElement formData, string? comment = null,
        Guid? draftId = null, CancellationToken ct = default) =>
        _bridge.ExecuteAsync(token => StartAsyncCore(key, userId, formData, comment, draftId, token), ct);

    private async Task<ApprovalInstance> StartAsyncCore(string key, string userId, JsonElement formData, string? comment = null,
        Guid? draftId = null, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceStart, new() { DefinitionKey = key }, userId, ct);
        var (definition, flow) = await LoadStartableAsync(key, userId, ct);
        var schema = flow.GetForm() ?? new FormSchema();
        var draft = draftId is { } id ? await FindDraftAsync(userId, id, ct) : null;
        var (json, attachments) = await files.ResolveAsync(schema, await NormalizeAsync(schema, formData, ct), userId, null, draft?.Id, false, ct);

        // 草稿删除、附件绑定随发起一起提交（引擎在同一 DbContext 中 SaveChanges）
        var instanceId = _bridge.NewId("ApprovalFormService");
        await files.BindToInstanceAsync(instanceId, attachments, draft?.Id, ct);
        if (draft is not null) _db.Set<ApprovalDraft>().Remove(draft);

        return await engine.StartAsync(new StartApprovalRequest(definition.Key, BusinessType, _bridge.NewId("ApprovalFormService").ToString("N"), userId)
        {
            InstanceId = instanceId,
            DefinitionId = definition.Id,
            Title = RenderTitle(flow.Settings!, definition.Name, userId, _clock.GetUtcNow().UtcDateTime, json),
            FormData = json,
            Comment = comment
        }, ct);
    }

    /// <summary>
    /// 退回发起人后重新提交。表单审批可传入新的表单数据（按实例绑定版本的表单校验，标题随之更新）；
    /// 其他业务的数据由业务单据决定，传入表单数据会被拒绝。
    /// </summary>
    public Task ResubmitAsync(Guid taskId, string userId, JsonElement? formData, string? comment = null, CancellationToken ct = default) =>
        _bridge.ExecuteAsync(token => ResubmitAsyncCore(taskId, userId, formData, comment, token), ct);

    private async Task ResubmitAsyncCore(Guid taskId, string userId, JsonElement? formData, string? comment = null, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskResubmit, new() { TaskId = taskId }, userId, ct);
        if (formData is not { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } data)
        {
            await engine.ResubmitAsync(taskId, userId, comment, ct: ct);
            return;
        }

        var task = await _db.Set<ApprovalTask>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct)
                   ?? throw new ApprovalException("任务不存在") { Code = ApprovalErrorCode.NotFound };
        // 跟踪加载：引擎随后在同一 DbContext 中取到同一实体，标题修改随重新提交一起保存
        var instance = await _db.Set<ApprovalInstance>().FirstAsync(i => i.Id == task.InstanceId, ct);
        if (!IsFormInstance(instance.BusinessType))
            throw new ApprovalException("该申请的数据由业务单据决定，不能在审批中修改表单");
        if (task.Kind != ApprovalTaskKind.Resubmit || instance.InitiatorId != userId)
            throw new ApprovalException("只有被退回的发起人可以修改表单后重新提交") { Code = ApprovalErrorCode.Forbidden };

        var definition = await _db.Set<ApprovalDefinition>().AsNoTracking().FirstAsync(d => d.Id == instance.DefinitionId, ct);
        var flow = FlowDefinition.Parse(definition.DefinitionJson);
        var schema = flow.GetForm() ?? new FormSchema();
        var (json, attachments) = await files.ResolveAsync(schema, await NormalizeAsync(schema, data, ct), userId, instance.Id, null, false, ct);
        await files.BindToInstanceAsync(instance.Id, attachments, null, ct);
        using (_bridge.EditTitle(instance))
            instance.Title = RenderTitle(flow.Settings ?? new FlowSettings(), definition.Name, userId, instance.CreatedAt, json);

        await engine.ResubmitAsync(taskId, userId, comment, json, ct);
    }

    /// <summary>
    /// 同意；表单审批可同时修改任务所在节点的可编辑字段（<see cref="FieldAccess.Editable"/>）：formData 只能包含这些字段，
    /// 与当前数据合并后整表重新校验与计算（条件分支按新值流转、标题随之更新），轨迹记录修改了哪些字段（修改前后的值存入 Details）。
    /// 其他业务的数据由业务单据决定，传入表单数据会被拒绝。
    /// </summary>
    public Task ApproveAsync(Guid taskId, string userId, JsonElement? formData, string? comment = null, CancellationToken ct = default) =>
        formData is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } data
            ? engine.ApproveWithDataAsync(taskId, userId, comment, (instance, task, flow) => EditAsync(instance, task, flow, data, userId, ct), ct)
            : engine.ApproveAsync(taskId, userId, comment, ct);

    /// <summary>任务所在节点可在同意时修改的字段（仅表单审批的审批任务）；审批详情页据此显示编辑框。</summary>
    public async Task<IReadOnlyList<string>> GetEditableFieldsAsync(ApprovalTaskContext task, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskRead, new() { TaskId = task.Id }, null, ct);
        var actualTask = await _db.Set<ApprovalTask>().AsNoTracking().SingleAsync(t => t.Id == task.Id, ct);
        if (actualTask.Kind == ApprovalTaskKind.Resubmit) return [];
        var instance = await _db.Set<ApprovalInstance>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == actualTask.InstanceId, ct);
        if (instance is null || !IsFormInstance(instance.BusinessType)) return [];

        var definition = await _db.Set<ApprovalDefinition>().AsNoTracking().FirstAsync(d => d.Id == instance.DefinitionId, ct);
        return FieldVisibility.EditableFields(FlowDefinition.Parse(definition.DefinitionJson), actualTask.NodeId).Select(f => f.Key).ToList();
    }

    /// <summary>审批人修改可编辑字段：合并到当前数据 → 整表校验与计算 → 字段差异；没有实际修改时返回 null。</summary>
    private async Task<ApprovalDataEdit?> EditAsync(ApprovalInstanceContext instance, ApprovalTaskContext task, FlowDefinition flow, JsonElement data,
        string userId, CancellationToken ct)
    {
        if (!IsFormInstance(instance.BusinessType)) throw new ApprovalException("该申请的数据由业务单据决定，不能在审批中修改表单");
        if (data.ValueKind != JsonValueKind.Object) throw new ApprovalException("表单数据必须是 JSON 对象");

        var schema = flow.GetForm() ?? new FormSchema();
        var editable = FieldVisibility.EditableFields(flow, task.NodeId).ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);
        var merged = JsonNode.Parse(instance.FormDataJson) as JsonObject ?? new JsonObject();
        var denied = new List<string>();
        foreach (var property in data.EnumerateObject())
        {
            if (editable.TryGetValue(property.Name, out var field)) merged[field.Key] = JsonNode.Parse(property.Value.GetRawText());
            else denied.Add(schema.Fields.FirstOrDefault(f => string.Equals(f.Key, property.Name, StringComparison.OrdinalIgnoreCase))?.Label
                            ?? property.Name);
        }
        if (denied.Count > 0) throw new ApprovalException($"当前节点不能修改：{string.Join("、", denied)}");

        // 只报告可编辑字段的错误：其他字段是发起时已校验的数据（如人员字段中的用户后来离职），不应阻止审批
        var normalized = await NormalizeAsync(schema, JsonSerializer.SerializeToElement(merged), ct,
            editable.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
        // 附件：保留本实例已有的，新上传的须为审批人本人上传
        var (resolved, attachments) = await files.ResolveAsync(schema, normalized, userId, instance.Id, null, false, ct);
        var json = KeepNonFormVariables(instance.FormDataJson, resolved);
        var changes = FieldVisibility.Diff(schema, instance.FormDataJson, json);
        if (changes.Count == 0) return null;
        await files.BindToInstanceAsync(instance.Id, attachments, null, ct);

        // 标题模板可能引用了被修改的字段
        var definition = await _db.Set<ApprovalDefinition>().AsNoTracking().FirstAsync(d => d.Id == instance.DefinitionId, ct);
        return new ApprovalDataEdit(json, FieldVisibility.Summary(changes.Select(c => c.Label)), JsonSerializer.Serialize(changes, FormData.JsonOptions))
        {
            ReplaceTitle = true,
            Title = RenderTitle(flow.Settings ?? new FlowSettings(), definition.Name, instance.InitiatorId, instance.CreatedAt, json)
        };
    }

    /// <summary>表单之外的宿主准备变量和历史快照值原样保留：流程继续流转，后续条件分支可能用到。</summary>
    private static string KeepNonFormVariables(string original, string normalized)
    {
        if (JsonNode.Parse(original) is not JsonObject before) return normalized;
        var after = JsonNode.Parse(normalized, new JsonNodeOptions { PropertyNameCaseInsensitive = true })!.AsObject();
        var extra = before.Where(p => !after.ContainsKey(p.Key)).ToList();
        if (extra.Count == 0) return normalized;

        foreach (var (key, value) in extra) after[key] = value?.DeepClone();
        return after.ToJsonString(FormData.JsonOptions);
    }

    private bool IsFormInstance(string businessType) =>
        string.Equals(businessType, BusinessType, StringComparison.OrdinalIgnoreCase);

    /// <param name="reportedFields">只报告这些字段（含其明细单元格）的错误；为空时报告全部。</param>
    private async Task<string> NormalizeAsync(FormSchema schema, JsonElement data, CancellationToken ct,
        IReadOnlySet<string>? reportedFields = null)
    {
        var result = FormDataValidator.Validate(schema, data);
        bool Reported(string path) => reportedFields is null || reportedFields.Contains(path.Split('.')[0]);
        var errors = result.Errors.Where(e => Reported(e.Key)).ToDictionary(e => e.Key, e => e.Value);

        // 人员字段可能被用作审批人：注册了用户目录时校验用户存在
        if (directory is not null && errors.Count == 0)
        {
            var values = FormData.Parse(result.Json);
            var userFields = schema.Fields.Where(f => f.Type is FormFieldType.User or FormFieldType.Users && Reported(f.Key))
                .Select(field => new
                {
                    Field = field,
                    Ids = values.GetValueOrDefault(field.Key) switch
                    {
                        string id => new[] { id },
                        object?[] list => list.OfType<string>().ToArray(),
                        _ => Array.Empty<string>()
                    }
                }).ToList();
            var ids = userFields.SelectMany(f => f.Ids).Distinct().ToArray();
            var found = ids.Length == 0 ? new HashSet<string>() : (await _hostDirectory!.GetUsersAsync(ids, ct)).Select(u => u.Id).ToHashSet();
            foreach (var item in userFields)
            {
                var missing = item.Ids.Where(id => !found.Contains(id)).ToList();
                if (missing.Count > 0) errors[item.Field.Key] = [$"{item.Field.Label}中的用户不存在：{string.Join("、", missing)}"];
            }
        }

        return errors.Count == 0 ? result.Json : throw new ApprovalValidationException(errors, "表单填写有误，请检查标红的字段");
    }

    private string RenderTitle(FlowSettings settings, string flowName, string userId, DateTime createdAt, string formJson)
    {
        var template = string.IsNullOrWhiteSpace(settings.TitleTemplate) ? "{流程名} - {申请人}" : settings.TitleTemplate;
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["流程名"] = flowName,
            ["flowName"] = flowName,
            ["申请人"] = userId,
            ["initiator"] = userId,
            ["发起时间"] = createdAt,
            ["createdAt"] = createdAt
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
        var pinned = _bridge.PinnedDefinition(key);
        var definition = pinned is { } id
            ? await _db.Set<ApprovalDefinition>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == id && d.Key == key && d.IsPublished, ct)
            : await LatestDefinitions().FirstOrDefaultAsync(d => d.Key == key, ct);
        if (definition is null || TryParse(definition) is not { Settings.Startable: true } flow)
            throw new ApprovalException($"流程 '{key}' 不存在或不支持在发起中心发起") { Code = ApprovalErrorCode.NotFound };
        if (!await CanStartAsync(flow.Settings!, userId, ct))
            throw new ApprovalException("你没有发起该流程的权限") { Code = ApprovalErrorCode.Forbidden };
        _bridge.PinDefinition(key, definition.Id);
        return (definition, flow);
    }

    private async Task<bool> CanStartAsync(FlowSettings settings, string userId, CancellationToken ct)
    {
        if (settings.InitiatorRoles.Count == 0) return true;
        foreach (var role in settings.InitiatorRoles)
        {
            if (await _hostIdentity.IsInRoleAsync(userId, role, ct)) return true;
        }
        return false;
    }

    private IQueryable<ApprovalDefinition> LatestDefinitions() =>
        _bridge.StartDefinitions().AsNoTracking();

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

    public Task OnApprovedAsync(ApprovalInstanceContext instance, CancellationToken ct) => Task.CompletedTask;

    public Task OnRejectedAsync(ApprovalInstanceContext instance, CancellationToken ct) => Task.CompletedTask;
}
