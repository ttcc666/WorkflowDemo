using Approval.Core.Abstractions;
using Approval.Core.Conditions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Engine.Extensions;
using Approval.Core.Engine.Runtime;
using Approval.Core.Notifications;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Approval.Core.Engine;

/// <summary>审批中修改表单：修改后的完整表单数据与字段差异（见 <see cref="ApprovalEngine.ApproveWithDataAsync"/>）。</summary>
public sealed record ApprovalDataEdit(string DataJson, string Summary, string ChangesJson)
{
    public bool ReplaceTitle { get; init; }
    public string? Title { get; init; }
}

/// <summary>
/// 审批引擎：发起、同意、驳回 / 退回、撤回、转交、加签、超时处理。
/// 每个公开方法是一次完整操作：校验 → 修改状态 → 宿主生命周期 / 业务回调 → 写入站内信与 Outbox → SaveChanges（同一事务）。
/// 外部通知与可靠事件由后台任务在提交后投递（见 <see cref="Background.ApprovalOutboxDispatcher"/>）。
/// </summary>
public sealed partial class ApprovalEngine
{
    public const string SystemOperator = "system";

    private readonly Approval.Core.Authorization.IApprovalAuthorization _authorization;
    private readonly DbContext _db;
    private readonly ApprovalUnitOfWork _unit;
    private readonly IApprovalCommandExecutor _commands;
    private readonly Func<IApprovalAccessPolicy> _accessPolicy;
    private readonly ApprovalDbContextAccessor _contextAccessor;
    private readonly IApprovalIdentityProvider _identity;
    private readonly ApprovalAssigneeService _assignees;
    private readonly ApprovalEventWriter _events;
    private readonly ApprovalTaskCoordinator _tasks;
    private readonly ApprovalRuntimeNavigator _navigator;
    private readonly IReadOnlyDictionary<string, IApprovalBusinessHandler> _handlers;
    private readonly ApprovalOptions _options;
    private readonly ApprovalOutboxSignal _outboxSignal;
    private readonly TimeProvider _clock;
    private readonly IApprovalUserDirectory? _directory;
    internal IApprovalDefinitionExtension? DefinitionExtension { get; }
    internal IApprovalLifecycleHandler? Lifecycle { get; }
    internal IApprovalUserStatusProvider? UserStatus => _assignees.UserStatus;

    internal ApprovalEngine(
        ApprovalDbContextAccessor accessor,
        IApprovalIdentityProvider identity,
        ApprovalExtensionCatalog extensions,
        IConditionEvaluator evaluator,
        ApprovalOptions options,
        ApprovalOutboxSignal outboxSignal,
        TimeProvider clock,
        ILogger<ApprovalEngine> logger,
        IApprovalUserDirectory? directory,
        Func<IApprovalAccessPolicy> accessPolicy,
        IApprovalDefinitionExtension? definitionExtension = null,
        IApprovalLifecycleHandler? lifecycle = null,
        IApprovalUserStatusProvider? userStatus = null)
    {
        _db = accessor.Context;
        _unit = accessor.UnitOfWork;
        _commands = accessor.Commands;
        _accessPolicy = accessPolicy;
        _contextAccessor = accessor;
        _authorization = accessor.Authorization;
        _identity = ApprovalHostServices.Identity(_unit, identity);
        _assignees = new(_identity, extensions.Resolvers, options, ApprovalHostServices.Directory(_unit, directory), _unit);
        _handlers = extensions.BusinessHandlers;
        _options = options;
        _outboxSignal = outboxSignal;
        _clock = ApprovalHostServices.Clock(_unit, clock);
        _directory = ApprovalHostServices.Directory(_unit, directory);
        _events = new(_db, _unit, _identity, _assignees, extensions.ChannelNames, options,
            new ApprovalDisplaySettings(options.DisplayTimeZone, options.InstanceUrlFormat, options.GlobalVariables), _clock, logger);
        _tasks = new(_db, _unit, _events, _clock);
        _navigator = new(_assignees, ApprovalHostServices.Conditions(_unit, evaluator), _tasks, _events);
        DefinitionExtension = definitionExtension;
        Lifecycle = lifecycle;
        _assignees.UserStatus = ApprovalHostServices.Status(_unit, userStatus);
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private string SerializeData(object? data)
    { using var callback = _unit.Extension(); return FormData.Serialize(data); }

    public Task<ApprovalInstance> StartAsync(StartApprovalRequest request, CancellationToken ct = default) => StartCommandAsync(request, ct);

    private async Task<ApprovalInstance> StartAsyncCore(StartApprovalRequest request, CancellationToken ct = default)
    {
        var operation = new ApprovalOperationContext();
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceStart, new() { DefinitionKey = request.DefinitionKey, DefinitionId = request.DefinitionId, BusinessType = request.BusinessType, BusinessKey = request.BusinessKey }, request.InitiatorId, ct);
        await EnsureActiveUserAsync(request.InitiatorId, ct);
        if (await _db.Set<ApprovalDefinitionManagement>().AnyAsync(m => m.Key == request.DefinitionKey && !m.IsEnabled, ct))
            throw new ApprovalException($"流程 '{request.DefinitionKey}' 已停用，不能发起新审批");
        var pinned = _unit.PinnedDefinition(request.DefinitionKey);
        var definitionId = request.DefinitionId ?? pinned;
        var available = definitionId is not null ? _db.Set<ApprovalDefinition>().AsQueryable() : ApprovalDefinitionAvailability.ForNewStarts(_db);
        var definition = await available
            .Where(d => d.Key == request.DefinitionKey && d.IsPublished && (definitionId == null || d.Id == definitionId))
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync(ct)
            ?? throw new ApprovalException($"流程 '{request.DefinitionKey}' 未发布") { Code = ApprovalErrorCode.NotFound };
        _unit.PinDefinition(request.DefinitionKey, definition.Id);

        if (_options.SingleRunningInstancePerBusiness &&
            (await _db.Set<ApprovalInstance>().AsNoTracking().Where(i =>
                i.BusinessType == request.BusinessType &&
                i.BusinessKey == request.BusinessKey &&
                (i.Status == InstanceStatus.Running || i.Status == InstanceStatus.Returned)).Select(i => new { i.BusinessType, i.BusinessKey }).ToListAsync(ct))
                .Any(i => string.Equals(i.BusinessType, request.BusinessType, StringComparison.Ordinal) && string.Equals(i.BusinessKey, request.BusinessKey, StringComparison.Ordinal)))
        {
            throw new ApprovalException("该业务单据已有进行中的审批") { Code = ApprovalErrorCode.Conflict };
        }

        var selectedFlow = FlowDefinition.Parse(definition.DefinitionJson);

        var instance = new ApprovalInstance
        {
            Id = request.InstanceId ?? _unit.NewId("ApprovalEngine"),
            DefinitionId = definition.Id,
            DefinitionKey = definition.Key,
            DefinitionVersion = definition.Version,
            BusinessType = request.BusinessType,
            BusinessKey = request.BusinessKey,
            Title = request.Title,
            InitiatorId = request.InitiatorId,
            Status = InstanceStatus.Running,
            FormDataJson = SerializeData(request.FormData),
            CreatedAt = Now
        };
        _db.Set<ApprovalInstance>().Add(instance);
        _unit.Reference(instance.Id, definition.Id);
        if (_options.SingleRunningInstancePerBusiness)
            _db.Set<ApprovalBusinessSlot>().Add(new()
            {
                Id = _unit.NewId("ApprovalEngine"),
                InstanceId = instance.Id,
                LookupHash = ApprovalCommandHash.Of(instance.BusinessType, instance.BusinessKey),
                BusinessType = instance.BusinessType,
                BusinessKey = instance.BusinessKey
            });

        if (Lifecycle is not null)
        {
            ApprovalStartPreparation preparation;
            using (var callback = _unit.Extension())
                preparation = await Lifecycle.OnStartingAsync(ApprovalContextSnapshots.Instance(instance), ct);
            instance.Title = preparation.Title;
            instance.FormDataJson = preparation.FormDataJson;
        }

        var flow = selectedFlow;
        var start = flow.GetNode(flow.StartNodeId);
        _events.AddLog(operation, instance, start, ApprovalAction.Submit, request.InitiatorId, request.Comment);

        await MoveToAsync(operation, instance, flow, start.Next!, ct);
        await FlushOperationAsync(operation, instance, ct);
        return instance;
    }

    /// <summary>同意。对“重新提交”任务（退回发起人后）等同于不修改表单直接重新提交。</summary>
    public Task ApproveAsync(Guid taskId, string userId, string? comment = null, CancellationToken ct = default) =>
        _unit.ExecuteCommandAsync(token => ApproveAsyncCore(taskId, userId, comment, token), ct);

    private async Task ApproveAsyncCore(Guid taskId, string userId, string? comment = null, CancellationToken ct = default)
    {
        var operation = new ApprovalOperationContext();
        var (instance, task, flow) = await LoadTaskAsync(operation, taskId, ct);
        await _authorization.RequireAsync(task.Kind == ApprovalTaskKind.Resubmit ? ApprovalPermissions.TaskResubmit : ApprovalPermissions.TaskApprove, new() { TaskId = taskId }, userId, ct);
        await EnsureCanHandleAsync(operation, instance, task, userId, ct);
        if (task.Kind == ApprovalTaskKind.Resubmit) await ResubmitCoreAsync(operation, instance, flow, task, userId, comment, null, ct);
        else await ApproveCoreAsync(operation, instance, flow, task, userId, comment, ApprovalAction.Approve, ct);
        await FlushOperationAsync(operation, instance, ct);
    }

    /// <summary>
    /// 同意并修改表单（表单审批的节点可编辑字段，由 <see cref="ApprovalFormService"/> 调用）：校验处理权限后由 <paramref name="edit"/>
    /// 计算修改（返回 null 表示没有修改）；表单数据替换为修改后的数据，轨迹先记录修改了哪些字段（差异存入 Details），再记录同意并按新数据流转。
    /// </summary>
    public Task ApproveWithDataAsync(Guid taskId, string userId, string? comment,
        Func<ApprovalInstanceContext, ApprovalTaskContext, FlowDefinition, Task<ApprovalDataEdit?>> edit, CancellationToken ct) =>
        _unit.ExecuteCommandAsync(token => ApproveWithDataCoreAsync(taskId, userId, comment, edit, token), ct);

    private async Task ApproveWithDataCoreAsync(Guid taskId, string userId, string? comment,
        Func<ApprovalInstanceContext, ApprovalTaskContext, FlowDefinition, Task<ApprovalDataEdit?>> edit, CancellationToken ct)
    {
        var operation = new ApprovalOperationContext();
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskApprove, new() { TaskId = taskId }, userId, ct);
        var (instance, task, flow) = await LoadTaskAsync(operation, taskId, ct);
        await EnsureCanHandleAsync(operation, instance, task, userId, ct);
        if (task.Kind == ApprovalTaskKind.Resubmit) throw new ApprovalException("退回到发起人的申请请修改后重新提交");

        ApprovalDataEdit? change;
        using (var callback = _unit.Extension())
            change = await edit(ApprovalContextSnapshots.Instance(instance), ApprovalContextSnapshots.Task(task), FlowDefinition.Parse(flow.ToJson()));
        if (change is not null)
        {
            instance.FormDataJson = change.DataJson;
            if (change.ReplaceTitle) instance.Title = change.Title;
            _events.AddLog(operation, instance, ApprovalAction.FormEdit, userId, change.Summary,
                task.NodeId, task.NodeName, change.ChangesJson);
        }
        await ApproveCoreAsync(operation, instance, flow, task, userId, comment, ApprovalAction.Approve, ct);
        await FlushOperationAsync(operation, instance, ct);
    }

    /// <summary>驳回：流程结束为 Rejected，业务方可修改后重新发起新流程。退回到指定节点见 <see cref="ReturnAsync"/>。</summary>
    public Task RejectAsync(Guid taskId, string userId, string comment, CancellationToken ct = default) =>
        _unit.ExecuteCommandAsync(token => RejectAsyncCore(taskId, userId, comment, token), ct);

    private async Task RejectAsyncCore(Guid taskId, string userId, string comment, CancellationToken ct = default)
    {
        var operation = new ApprovalOperationContext();
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskReject, new() { TaskId = taskId }, userId, ct);
        if (string.IsNullOrWhiteSpace(comment)) throw new ApprovalException("驳回必须填写意见");

        var (instance, task, flow) = await LoadTaskAsync(operation, taskId, ct);
        await EnsureCanHandleAsync(operation, instance, task, userId, ct);
        if (task.Kind == ApprovalTaskKind.Resubmit) throw new ApprovalException("退回到发起人的申请请重新提交或撤回");
        await RejectCoreAsync(operation, instance, flow, task, userId, comment, ApprovalAction.Reject, ct);
        await FlushOperationAsync(operation, instance, ct);
    }

    /// <summary>发起人撤回进行中的申请。</summary>
    public Task WithdrawAsync(Guid instanceId, string userId, string? comment = null, CancellationToken ct = default) =>
        _unit.ExecuteCommandAsync(token => WithdrawAsyncCore(instanceId, userId, comment, token), ct);

    private async Task WithdrawAsyncCore(Guid instanceId, string userId, string? comment = null, CancellationToken ct = default)
    {
        var operation = new ApprovalOperationContext();
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.InstanceWithdraw, new() { InstanceId = instanceId }, userId, ct);
        var instance = await _db.Set<ApprovalInstance>()
            .Include(i => i.Tasks)
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new ApprovalException("流程不存在") { Code = ApprovalErrorCode.NotFound };

        if (instance.Status is not (InstanceStatus.Running or InstanceStatus.Returned)) throw new ApprovalException("流程已结束，无法撤回");
        if (instance.InitiatorId != userId)
            throw new ApprovalException("只有发起人可以撤回") { Code = ApprovalErrorCode.Forbidden };
        if (!CanWithdraw(instance, userId, _options))
            throw new ApprovalException("已有人审批通过，无法撤回");

        var flow = await LoadFlowAsync(operation, instance, ct);
        var node = ApprovalTaskCoordinator.OpenTasks(instance).Select(t => flow.Nodes.GetValueOrDefault(t.NodeId)).FirstOrDefault(n => n is not null);
        Touch(instance);
        _events.AddLog(operation, instance, null, ApprovalAction.Withdraw, userId, comment);
        _tasks.CancelOpenTasks(instance.Tasks);
        await FinishAsync(operation, instance, flow, node, InstanceStatus.Withdrawn, new EventInfo([]) { OperatorId = userId, Comment = comment }, ct);

        await FlushOperationAsync(operation, instance, ct);
    }

    /// <summary>详情中的撤回能力与实际操作共用的规则；instance 必须已加载任务。退回发起人后始终可撤回。</summary>
    internal static bool CanWithdraw(ApprovalInstance instance, string userId, ApprovalOptions options) =>
        (instance.Status is InstanceStatus.Running or InstanceStatus.Returned) && instance.InitiatorId == userId
        && (options.AllowWithdrawAfterApproval
            || instance.Tasks.Any(t => t.Kind == ApprovalTaskKind.Resubmit && t.Status == ApprovalTaskStatus.Pending)
            || !instance.Tasks.Any(t => t.Status == ApprovalTaskStatus.Approved));

    /// <summary>把自己的待办转交给指定用户。</summary>
    public Task TransferAsync(Guid taskId, string userId, string toUserId, string? comment = null, CancellationToken ct = default) =>
        _unit.ExecuteCommandAsync(token => TransferAsyncCore(taskId, userId, toUserId, comment, token), ct);

    private async Task TransferAsyncCore(Guid taskId, string userId, string toUserId, string? comment = null, CancellationToken ct = default)
    {
        var operation = new ApprovalOperationContext();
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskTransfer, new() { TaskId = taskId }, userId, ct);
        if (string.IsNullOrWhiteSpace(toUserId) || toUserId == userId) throw new ApprovalException("转交对象无效");
        await EnsureActiveUserAsync(toUserId, ct);

        var (instance, task, flow) = await LoadTaskAsync(operation, taskId, ct);
        await EnsureCanHandleAsync(operation, instance, task, userId, ct);
        if (task.Kind == ApprovalTaskKind.Resubmit) throw new ApprovalException("重新提交的任务不能转交");
        var node = flow.GetNode(task.NodeId);
        if (!node.AllowTransfer) throw new ApprovalException("该节点不允许转交") { Code = ApprovalErrorCode.Forbidden };
        if (node.RequireQualifiedAssignee && !(await _assignees.GetEligibleApproversAsync(instance, node, ct)).Contains(toUserId))
            throw new ApprovalException("转交对象不具备本节点审批资格") { Code = ApprovalErrorCode.Forbidden };
        if (!_options.AllowInitiatorApprove && toUserId == instance.InitiatorId)
            throw new ApprovalException("不能转交给发起人");
        if (ApprovalTaskCoordinator.OpenTasks(ApprovalTaskCoordinator.CurrentTasksOf(instance, task.NodeId)).Any(t => t.AssigneeKind == AssigneeKind.User && t.AssigneeValue == toUserId))
            throw new ApprovalException("对方已是该节点的审批人");

        // 转交不重置时限（含提醒进度），加签关系跟随新任务
        _tasks.HandOver(operation, instance, task, toUserId, userId, comment);
        _events.AddLog(operation, instance, ApprovalAction.Transfer, userId, $"转交给 {toUserId}" + (comment is null ? "" : $"：{comment}"),
            task.NodeId, task.NodeName);

        var target = new Assignee(AssigneeKind.User, toUserId);
        await _events.RaiseAsync(operation, NotifyEvent.Transferred, instance, flow, flow.GetNode(task.NodeId),
            new EventInfo([target]) { OperatorId = userId, Comment = comment, TransferTo = toUserId, DueAt = task.DueAt }, ct);

        await FlushOperationAsync(operation, instance, ct);
    }

    /// <summary>当前用户能否处理该任务（供前端控制按钮显示，最终仍以操作时的校验为准）。</summary>
    public async Task<bool> CanHandleAsync(Guid taskId, string userId, CancellationToken ct = default)
    {
        var operation = new ApprovalOperationContext();
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.SessionRead, new(), userId, ct);
        try
        {
            await _authorization.RequireAsync(ApprovalPermissions.TaskRead, new() { TaskId = taskId }, userId, ct);
            var (instance, task, _) = await LoadTaskAsync(operation, taskId, ct, write: false);
            await EnsureCanHandleAsync(operation, instance, task, userId, ct);
            return true;
        }
        catch (ApprovalException ex) when (ex.Code is ApprovalErrorCode.Forbidden or ApprovalErrorCode.NotFound or ApprovalErrorCode.InvalidOperation)
        {
            return false;
        }
    }

    /// <summary>查找当前用户在该流程中可处理的待办任务（审批详情页据此显示同意/驳回按钮），没有则返回 null。</summary>
    public async Task<ApprovalTaskContext?> FindHandleableTaskAsync(Guid instanceId, string userId, CancellationToken ct = default)
    {
        var operation = new ApprovalOperationContext();
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskRead, new() { InstanceId = instanceId }, userId, ct);
        var instance = await _db.Set<ApprovalInstance>().AsNoTracking()
            .Include(i => i.Tasks)
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct);
        if (instance is null) return null;

        foreach (var task in instance.Tasks.Where(t => t.Status == ApprovalTaskStatus.Pending).OrderBy(t => t.CreatedAt).ThenBy(t => t.Order))
        {
            try
            {
                await EnsureCanHandleAsync(operation, instance, task, userId, ct);
                return ApprovalContextSnapshots.Task(task);
            }
            catch (ApprovalException ex) when (ex.Code is ApprovalErrorCode.Forbidden or ApprovalErrorCode.NotFound or ApprovalErrorCode.InvalidOperation)
            {
                // 该任务当前用户无权处理，继续检查下一个
            }
        }
        return null;
    }

    // ───────────────────────── 流程推进 ─────────────────────────

    private async Task ApproveCoreAsync(ApprovalOperationContext operation, ApprovalInstance instance, FlowDefinition flow, ApprovalTask task, string userId,
        string? comment, ApprovalAction action, CancellationToken ct)
    {
        var node = flow.GetNode(task.NodeId);
        if (node.RequireComment && userId != SystemOperator && string.IsNullOrWhiteSpace(comment))
            throw new ApprovalException("该节点必须填写审批意见");
        _tasks.CompleteTask(task, ApprovalTaskStatus.Approved, userId, comment);
        _events.AddLog(operation, instance, node, action, userId, comment);

        var nodeTasks = ApprovalTaskCoordinator.CurrentTasksOf(instance, node.Id);
        await _events.RaiseAsync(operation, NotifyEvent.Approved, instance, flow, node,
            new EventInfo(ApprovalTaskCoordinator.PendingAssignees(nodeTasks)) { OperatorId = userId, Comment = comment }, ct);

        switch (task.Kind)
        {
            case ApprovalTaskKind.AddSignBefore:
                await _tasks.ResumeAfterBeforeSignAsync(operation, instance, flow, node, nodeTasks, task.ParentTaskId, ct);
                break;

            // 依次审批中的后加签：加签人都同意后才轮到下一位
            case ApprovalTaskKind.AddSignAfter when node.Mode == ApproveMode.Sequential
                                                    && ApprovalTaskCoordinator.AddSignChildrenDone(nodeTasks, task.ParentTaskId, ApprovalTaskKind.AddSignAfter):
                await _tasks.ActivateNextSequentialAsync(operation, instance, flow, node, nodeTasks, ct);
                break;

            case ApprovalTaskKind.Approval when node.Mode == ApproveMode.Sequential:
                await _tasks.ActivateNextSequentialAsync(operation, instance, flow, node, nodeTasks, ct);
                break;
        }

        if (ApprovalTaskCoordinator.IsNodeFinished(node, nodeTasks))
        {
            _tasks.CancelOpenTasks(nodeTasks);
            await MoveToAsync(operation, instance, flow, node.Next!, ct, node.Id);
        }
    }

    private async Task RejectCoreAsync(ApprovalOperationContext operation, ApprovalInstance instance, FlowDefinition flow, ApprovalTask task, string userId,
        string comment, ApprovalAction action, CancellationToken ct)
    {
        _tasks.CompleteTask(task, ApprovalTaskStatus.Rejected, userId, comment);
        var node = flow.GetNode(task.NodeId);
        _events.AddLog(operation, instance, node, action, userId, comment);
        _tasks.CancelOpenTasks(instance.Tasks);

        var info = new EventInfo([]) { OperatorId = userId, Comment = comment };
        await _events.RaiseAsync(operation, NotifyEvent.Rejected, instance, flow, node, info, ct);
        await FinishAsync(operation, instance, flow, node, InstanceStatus.Rejected, info, ct);
    }

    /// <summary>
    /// 从 nodeId 开始自动推进，直到停在需要人工处理的节点、到达结束节点，或在合并节点等待其他分支。
    /// 并行节点对每个分支递归推进。
    /// </summary>
    private async Task MoveToAsync(ApprovalOperationContext operation, ApprovalInstance instance, FlowDefinition flow, string nodeId, CancellationToken ct,
        string? sourceNodeId = null, ParallelScope? directBranch = null)
    {
        if (await _navigator.AdvanceAsync(operation, instance, flow, nodeId, ct, sourceNodeId, directBranch) is { } end)
            await FinishAsync(operation, instance, flow, end, InstanceStatus.Approved, new EventInfo([]), ct);
    }

    /// <param name="node">结束时所在的节点：通过时为结束节点，驳回时为驳回所在节点，撤回 / 终止时为当前审批节点。</param>
    private async Task FinishAsync(ApprovalOperationContext operation, ApprovalInstance instance, FlowDefinition flow, FlowNode? node, InstanceStatus status, EventInfo info,
        CancellationToken ct)
    {
        instance.Status = status;
        foreach (var slot in await _db.Set<ApprovalBusinessSlot>().Where(s => s.InstanceId == instance.Id).ToListAsync(ct)) _db.Remove(slot);
        foreach (var slot in _db.ChangeTracker.Entries<ApprovalBusinessSlot>().Where(e => e.State == EntityState.Added && e.Entity.InstanceId == instance.Id).ToArray())
            slot.State = EntityState.Detached;
        instance.CompletedAt = Now;
        instance.CurrentNodeId = status == InstanceStatus.Approved ? null : node?.Id;
        operation.StateOf(instance).Joins.Clear();
        _tasks.CancelOpenTasks(instance.Tasks);
        if (status == InstanceStatus.Approved) _events.AddLog(operation, instance, null, ApprovalAction.Complete, SystemOperator, "审批通过");

        if (_handlers.TryGetValue(instance.BusinessType, out var handler))
        {
            using var extension = _unit.Extension();
            var context = ApprovalContextSnapshots.Instance(instance);
            var callback = status switch
            {
                InstanceStatus.Approved => handler.OnApprovedAsync(context, ct),
                InstanceStatus.Rejected => handler.OnRejectedAsync(context, ct),
                InstanceStatus.Withdrawn => handler.OnWithdrawnAsync(context, ct),
                InstanceStatus.Terminated => handler.OnTerminatedAsync(context, ct),
                _ => Task.CompletedTask
            };
            await callback;
        }
        else if (_options.RequireBusinessHandler)
        {
            throw new ApprovalException($"业务类型 '{instance.BusinessType}' 未注册回调处理器") { Code = ApprovalErrorCode.InvalidDefinition };
        }

        _events.WriteEvent(operation, instance, "Completed", node?.Id, node?.Name, null, info.OperatorId ?? SystemOperator, info.Comment);
        await _events.RaiseAsync(operation, NotifyEvent.Completed, instance, flow, node, info, ct);
    }

    // ───────────────────────── 校验与加载 ─────────────────────────

    private async Task<(ApprovalInstance Instance, ApprovalTask Task, FlowDefinition Flow)> LoadTaskAsync(ApprovalOperationContext operation, Guid taskId, CancellationToken ct, bool write = true)
    {
        var instance = await _db.Set<ApprovalInstance>()
            .Include(i => i.Tasks)
            .FirstOrDefaultAsync(i => i.Tasks.Any(t => t.Id == taskId), ct)
            ?? throw new ApprovalException("审批任务不存在") { Code = ApprovalErrorCode.NotFound };

        var flow = await LoadFlowAsync(operation, instance, ct);
        if (write) Touch(instance);
        operation.TaskId = taskId;
        return (instance, instance.Tasks.Single(t => t.Id == taskId), flow);
    }

    private async Task<FlowDefinition> LoadFlowAsync(ApprovalOperationContext operation, ApprovalInstance instance, CancellationToken ct)
    {
        if (operation.LoadedFlows.TryGetValue(instance.DefinitionId, out var cached)) return cached;
        var definition = await _db.Set<ApprovalDefinition>().AsNoTracking().FirstAsync(d => d.Id == instance.DefinitionId, ct);
        return operation.LoadedFlows[instance.DefinitionId] = FlowDefinition.Parse(definition.DefinitionJson);
    }

    private async Task EnsureCanHandleAsync(ApprovalOperationContext operation, ApprovalInstance instance, ApprovalTask task, string userId, CancellationToken ct)
    {
        await EnsureActiveUserAsync(userId, ct);
        if (instance.Status is not (InstanceStatus.Running or InstanceStatus.Returned)) throw new ApprovalException("流程已结束");
        if (instance.Status == InstanceStatus.Returned && task.Kind != ApprovalTaskKind.Resubmit)
            throw new ApprovalException("申请正在等待发起人修改");
        // 只有 Pending 才能处理：保证重复提交时幂等，也挡住依次审批中尚未轮到的人、等待加签的人
        if (task.Status != ApprovalTaskStatus.Pending) throw new ApprovalException("任务已处理或尚未轮到");

        var isAssignee = task.AssigneeKind == AssigneeKind.User
            ? task.AssigneeValue == userId
            : await _identity.IsInRoleAsync(userId, task.AssigneeValue, ct);
        if (!isAssignee) throw new ApprovalException("无权处理该任务") { Code = ApprovalErrorCode.Forbidden };

        // 重新提交任务本来就属于发起人
        if (task.Kind == ApprovalTaskKind.Resubmit)
        {
            if (userId != instance.InitiatorId) throw new ApprovalException("只有发起人可以重新提交") { Code = ApprovalErrorCode.Forbidden };
            return;
        }

        if (!_options.AllowInitiatorApprove && instance.InitiatorId == userId)
            throw new ApprovalException("不能审批自己发起的申请") { Code = ApprovalErrorCode.Forbidden };

        if (!_options.AllowSameUserMultipleNodes && instance.Tasks.Any(t =>
                t.NodeId != task.NodeId && t.HandledBy == userId && t.Status == ApprovalTaskStatus.Approved))
            throw new ApprovalException("您已审批过该申请的其他环节") { Code = ApprovalErrorCode.Forbidden };

        var node = (await LoadFlowAsync(operation, instance, ct)).GetNode(task.NodeId);
        if (node.RequireQualifiedAssignee && !(await _assignees.GetEligibleApproversAsync(instance, node, ct)).Contains(userId))
            throw new ApprovalException("您已不具备本节点审批资格，请联系管理员改派") { Code = ApprovalErrorCode.Forbidden };
    }

    /// <summary>按当前组织/用户目录计算合格人员；供岗位约束、宿主预检和无人处理告警共用。实例应包含任务历史。</summary>
    public async Task<IReadOnlyList<string>> GetEligibleApproversAsync(ApprovalInstance instance, FlowNode node, CancellationToken ct = default)
        => await GetEligibleApproversAsync(instance, node, null, ct);

    internal async Task<bool> HasEligibleTaskAssigneeAsync(ApprovalInstance instance, FlowNode node, Assignee assignee, CancellationToken ct)
        => (await GetEligibleApproversAsync(instance, node, assignee, ct)).Count > 0;

    private async Task<IReadOnlyList<string>> GetEligibleApproversAsync(ApprovalInstance instance, FlowNode node, Assignee? taskAssignee, CancellationToken ct)
    {
        var operation = new ApprovalOperationContext();
        if (!await _authorization.CanAsync(ApprovalPermissions.TaskRead, new() { InstanceId = instance.Id }, ct: ct))
            await _authorization.RequireAsync(ApprovalPermissions.DiagnosticsRead, new() { InstanceId = instance.Id }, ct: ct);
        var actual = await _db.Set<ApprovalInstance>().AsNoTracking().Include(i => i.Tasks).SingleAsync(i => i.Id == instance.Id, ct);
        var flow = await LoadFlowAsync(operation, actual, ct);
        if (!flow.Nodes.TryGetValue(node.Id, out var actualNode)) throw new ApprovalException("节点不存在") { Code = ApprovalErrorCode.NotFound };
        return taskAssignee is null ? await _assignees.GetEligibleApproversAsync(actual, actualNode, ct)
            : await _assignees.GetEligibleTaskApproversAsync(actual, actualNode, taskAssignee, ct);
    }

    /// <summary>发起前人员预检：校验可信发起人和发起权限，使用当前宿主的已发布定义，不创建实例。</summary>
    public async Task<IReadOnlyList<string>> GetStartEligibleApproversAsync(StartApprovalRequest request, string nodeId, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(ApprovalPermissions.InstanceStart, new()
        {
            DefinitionKey = request.DefinitionKey,
            DefinitionId = request.DefinitionId,
            BusinessType = request.BusinessType,
            BusinessKey = request.BusinessKey
        }, request.InitiatorId, ct);
        var definition = await _db.Set<ApprovalDefinition>().AsNoTracking()
            .Where(d => d.Key == request.DefinitionKey && d.IsPublished && (request.DefinitionId == null || d.Id == request.DefinitionId))
            .OrderByDescending(d => d.Version).FirstOrDefaultAsync(ct)
            ?? throw new ApprovalException("流程未发布") { Code = ApprovalErrorCode.NotFound };
        var flow = FlowDefinition.Parse(definition.DefinitionJson);
        if (flow.Form is not null && DefinitionExtension is null)
            throw new ApprovalException("此定义包含可选模块快照，请注册拥有它的模块") { Code = ApprovalErrorCode.InvalidDefinition };
        if (!flow.Nodes.TryGetValue(nodeId, out var node)) throw new ApprovalException("节点不存在") { Code = ApprovalErrorCode.NotFound };
        return await _assignees.GetEligibleApproversAsync(new ApprovalInstance
        {
            DefinitionId = definition.Id,
            DefinitionKey = definition.Key,
            DefinitionVersion = definition.Version,
            BusinessType = request.BusinessType,
            BusinessKey = request.BusinessKey,
            InitiatorId = request.InitiatorId,
            FormDataJson = SerializeData(request.FormData),
            Status = InstanceStatus.Running
        }, node, ct);
    }

    private async Task EnsureActiveUserAsync(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ApprovalException("用户不能为空");
        if (UserStatus is null && _directory is null)
            throw new ApprovalException("宿主必须提供用户目录或用户状态适配器") { Code = ApprovalErrorCode.InvalidDefinition };
        var active = UserStatus is not null ? await UserStatus.IsActiveAsync(userId, ct)
            : (await _directory!.GetUsersAsync([userId], ct)).Any(u => u.Id == userId && u.IsActive);
        if (!active) throw new ApprovalException("宿主用户不存在或已停用") { Code = ApprovalErrorCode.Forbidden };
    }

    /// <summary>递增并发令牌：并发操作同一实例时，后提交者 SaveChanges 失败而不是静默覆盖。</summary>
    private static void Touch(ApprovalInstance instance) => instance.ConcurrencyStamp++;

    private async Task FlushOperationAsync(ApprovalOperationContext operation, ApprovalInstance instance, CancellationToken ct)
    {
        _unit.Reference(instance.Id, instance.DefinitionId);
        // 保存运行时状态与当前活动节点（并行时为多个）
        operation.StateOf(instance).SaveTo(instance);
        if (instance.Status is InstanceStatus.Running or InstanceStatus.Returned)
        {
            var current = string.Join(",", ApprovalTaskCoordinator.OpenTasks(instance).OrderBy(t => t.CreatedAt).Select(t => t.NodeId).Distinct());
            instance.CurrentNodeId = current.Length == 0 ? null : current.Length <= 500 ? current : current[..500];
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ApprovalException("流程已被他人更新，请刷新后重试", ex) { Code = ApprovalErrorCode.Conflict };
        }

        // 新写入了待投递消息：立即唤醒后台投递，而不必等到下一次轮询
        if (operation.OutboxWritten)
        {
            _unit.AfterCommit(() => { _outboxSignal.Notify(); return Task.CompletedTask; });
        }
    }
}
