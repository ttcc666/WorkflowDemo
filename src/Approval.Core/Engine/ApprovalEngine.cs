using System.Text.Json;
using Approval.Core.Abstractions;
using Approval.Core.Conditions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Approval.Core.Forms;
using Approval.Core.Engine.Extensions;
using Approval.Core.Engine.Runtime;
using Approval.Core.Notifications;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Approval.Core.Engine;

/// <summary>审批中修改表单：修改后的完整表单数据与字段差异（见 <see cref="ApprovalEngine.ApproveWithFormAsync"/>）。</summary>
internal sealed record FormEdit(string FormDataJson, IReadOnlyList<FormFieldChange> Changes);

/// <summary>
/// 审批引擎：发起、同意、驳回 / 退回、撤回、转交、加签、超时处理。
/// 每个公开方法是一次完整操作：校验 → 修改状态 → 宿主生命周期 / 业务回调 → 写入站内信与 Outbox → SaveChanges（同一事务）。
/// 外部通知与可靠事件由后台任务在提交后投递（见 <see cref="Background.ApprovalOutboxDispatcher"/>）。
/// </summary>
public sealed partial class ApprovalEngine
{
    public const string SystemOperator = "system";
    private const int MaxAutoSteps = 200;

    private readonly Approval.Core.Authorization.IApprovalAuthorization _authorization;
    private readonly DbContext _db;
    private readonly ApprovalUnitOfWork _unit;
    private readonly IApprovalCommandExecutor? _commands;
    private readonly IApprovalIdentityProvider _identity;
    private readonly ApprovalAssigneeService _assignees;
    private readonly IReadOnlyDictionary<string, IApprovalBusinessHandler> _handlers;
    private readonly IReadOnlySet<string> _channels;
    private readonly IReadOnlyList<IApprovalNotifier> _notifiers;
    private readonly IConditionEvaluator _evaluator;
    private readonly ApprovalOptions _options;
    private readonly ApprovalDisplaySettings _displaySettings;
    private readonly ApprovalOutboxSignal _outboxSignal;
    private readonly TimeProvider _clock;
    private readonly ILogger<ApprovalEngine> _logger;
    private readonly IApprovalUserDirectory? _directory;
    private readonly Dictionary<Guid, FlowDefinition> _loadedFlows = [];
    private readonly List<ApprovalNotification> _pendingNotifications = [];
    private readonly Dictionary<FlowDefinition, FlowStructure> _structures = [];
    private readonly Dictionary<Guid, ExecutionState> _states = [];
    private int _steps;
    private Guid? _operationTaskId;
    internal bool EventsEnabled { get; set; }
    internal IApprovalLifecycleHandler? Lifecycle { get; set; }
    internal IApprovalUserStatusProvider? UserStatus { get => _assignees.UserStatus; set => _assignees.UserStatus = value; }

    public ApprovalEngine(
        ApprovalDbContextAccessor accessor,
        IApprovalIdentityProvider identity,
        IEnumerable<IApproverResolver> resolvers,
        IEnumerable<IApprovalBusinessHandler> handlers,
        IEnumerable<IApprovalChannel> channels,
        IEnumerable<IApprovalNotifier> notifiers,
        IConditionEvaluator evaluator,
        ApprovalOptions options,
        ApprovalOutboxSignal outboxSignal,
        TimeProvider clock,
        ILogger<ApprovalEngine> logger,
        IApprovalUserDirectory? directory = null)
        : this(accessor, identity,
            new ApprovalExtensionCatalog(() => resolvers, () => handlers, () => channels),
            notifiers, evaluator, options, outboxSignal, clock, logger, directory)
    {
    }

    internal ApprovalEngine(
        ApprovalDbContextAccessor accessor,
        IApprovalIdentityProvider identity,
        ApprovalExtensionCatalog extensions,
        IEnumerable<IApprovalNotifier> notifiers,
        IConditionEvaluator evaluator,
        ApprovalOptions options,
        ApprovalOutboxSignal outboxSignal,
        TimeProvider clock,
        ILogger<ApprovalEngine> logger,
        IApprovalUserDirectory? directory)
    {
        _db = accessor.Context;
        _unit = accessor.UnitOfWork;
        _commands = accessor.Commands;
        _authorization = accessor.Authorization;
        _identity = identity;
        _assignees = new(identity, extensions.Resolvers, options, directory);
        _handlers = extensions.BusinessHandlers;
        _channels = extensions.ChannelNames;
        _notifiers = notifiers.ToList();
        _evaluator = evaluator;
        _options = options;
        _displaySettings = new ApprovalDisplaySettings(options.DisplayTimeZone, options.InstanceUrlFormat, options.GlobalVariables);
        _outboxSignal = outboxSignal;
        _clock = clock;
        _logger = logger;
        _directory = directory;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public Task<ApprovalInstance> StartAsync(StartApprovalRequest request, CancellationToken ct = default) => StartCommandAsync(request, ct);

    private async Task<ApprovalInstance> StartAsyncCore(StartApprovalRequest request, CancellationToken ct = default)
    {
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
            FormDataJson = FormData.Serialize(request.FormData),
            CreatedAt = Now
        };
        _db.Set<ApprovalInstance>().Add(instance);
        _unit.Reference(instance.Id, definition.Id);
        if (_options.SingleRunningInstancePerBusiness)
            _db.Set<ApprovalBusinessSlot>().Add(new() { Id = _unit.NewId("ApprovalEngine"), InstanceId = instance.Id,
                LookupHash = ApprovalCommandHash.Of(instance.BusinessType, instance.BusinessKey), BusinessType = instance.BusinessType, BusinessKey = instance.BusinessKey });

        if (Lifecycle is not null)
        {
            using var callback = _unit.Extension();
            await Lifecycle.OnStartingAsync(instance, ct);
        }

        var flow = selectedFlow;
        var start = flow.GetNode(flow.StartNodeId);
        AddLog(instance, start, ApprovalAction.Submit, request.InitiatorId, request.Comment);

        await MoveToAsync(instance, flow, start.Next!, ct);
        await CommitAsync(instance, ct);
        return instance;
    }

    /// <summary>同意。对“重新提交”任务（退回发起人后）等同于不修改表单直接重新提交。</summary>
    public Task ApproveAsync(Guid taskId, string userId, string? comment = null, CancellationToken ct = default) =>
        _unit.ExecuteCommandAsync(token => ApproveAsyncCore(taskId, userId, comment, token), ct);

    private async Task ApproveAsyncCore(Guid taskId, string userId, string? comment = null, CancellationToken ct = default)
    {
        var (instance, task, flow) = await LoadTaskAsync(taskId, ct);
        await _authorization.RequireAsync(task.Kind == ApprovalTaskKind.Resubmit ? ApprovalPermissions.TaskResubmit : ApprovalPermissions.TaskApprove, new() { TaskId = taskId }, userId, ct);
        await EnsureCanHandleAsync(instance, task, userId, ct);
        if (task.Kind == ApprovalTaskKind.Resubmit) await ResubmitCoreAsync(instance, flow, task, userId, comment, null, ct);
        else await ApproveCoreAsync(instance, flow, task, userId, comment, ApprovalAction.Approve, ct);
        await CommitAsync(instance, ct);
    }

    /// <summary>
    /// 同意并修改表单（表单审批的节点可编辑字段，由 <see cref="ApprovalFormService"/> 调用）：校验处理权限后由 <paramref name="edit"/>
    /// 计算修改（返回 null 表示没有修改）；表单数据替换为修改后的数据，轨迹先记录修改了哪些字段（差异存入 Details），再记录同意并按新数据流转。
    /// </summary>
    internal Task ApproveWithFormAsync(Guid taskId, string userId, string? comment,
        Func<ApprovalInstance, ApprovalTask, FlowDefinition, Task<FormEdit?>> edit, CancellationToken ct) =>
        _unit.ExecuteCommandAsync(token => ApproveWithFormCoreAsync(taskId, userId, comment, edit, token), ct);

    private async Task ApproveWithFormCoreAsync(Guid taskId, string userId, string? comment,
        Func<ApprovalInstance, ApprovalTask, FlowDefinition, Task<FormEdit?>> edit, CancellationToken ct)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskApprove, new() { TaskId = taskId }, userId, ct);
        var (instance, task, flow) = await LoadTaskAsync(taskId, ct);
        await EnsureCanHandleAsync(instance, task, userId, ct);
        if (task.Kind == ApprovalTaskKind.Resubmit) throw new ApprovalException("退回到发起人的申请请修改后重新提交");

        if (await edit(instance, task, flow) is { } change)
        {
            instance.FormDataJson = change.FormDataJson;
            AddLog(instance, ApprovalAction.FormEdit, userId, FieldVisibility.Summary(change.Changes.Select(c => c.Label)),
                task.NodeId, task.NodeName, JsonSerializer.Serialize(change.Changes, FormData.JsonOptions));
        }
        await ApproveCoreAsync(instance, flow, task, userId, comment, ApprovalAction.Approve, ct);
        await CommitAsync(instance, ct);
    }

    /// <summary>驳回：流程结束为 Rejected，业务方可修改后重新发起新流程。退回到指定节点见 <see cref="ReturnAsync"/>。</summary>
    public Task RejectAsync(Guid taskId, string userId, string comment, CancellationToken ct = default) =>
        _unit.ExecuteCommandAsync(token => RejectAsyncCore(taskId, userId, comment, token), ct);

    private async Task RejectAsyncCore(Guid taskId, string userId, string comment, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskReject, new() { TaskId = taskId }, userId, ct);
        if (string.IsNullOrWhiteSpace(comment)) throw new ApprovalException("驳回必须填写意见");

        var (instance, task, flow) = await LoadTaskAsync(taskId, ct);
        await EnsureCanHandleAsync(instance, task, userId, ct);
        if (task.Kind == ApprovalTaskKind.Resubmit) throw new ApprovalException("退回到发起人的申请请重新提交或撤回");
        await RejectCoreAsync(instance, flow, task, userId, comment, ApprovalAction.Reject, ct);
        await CommitAsync(instance, ct);
    }

    /// <summary>发起人撤回进行中的申请。</summary>
    public Task WithdrawAsync(Guid instanceId, string userId, string? comment = null, CancellationToken ct = default) =>
        _unit.ExecuteCommandAsync(token => WithdrawAsyncCore(instanceId, userId, comment, token), ct);

    private async Task WithdrawAsyncCore(Guid instanceId, string userId, string? comment = null, CancellationToken ct = default)
    {
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

        var flow = await LoadFlowAsync(instance, ct);
        var node = OpenTasks(instance).Select(t => flow.Nodes.GetValueOrDefault(t.NodeId)).FirstOrDefault(n => n is not null);
        Touch(instance);
        AddLog(instance, null, ApprovalAction.Withdraw, userId, comment);
        CancelOpenTasks(instance.Tasks);
        await FinishAsync(instance, flow, node, InstanceStatus.Withdrawn, new EventInfo([]) { OperatorId = userId, Comment = comment }, ct);

        await CommitAsync(instance, ct);
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
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskTransfer, new() { TaskId = taskId }, userId, ct);
        if (string.IsNullOrWhiteSpace(toUserId) || toUserId == userId) throw new ApprovalException("转交对象无效");
        await EnsureActiveUserAsync(toUserId, ct);

        var (instance, task, flow) = await LoadTaskAsync(taskId, ct);
        await EnsureCanHandleAsync(instance, task, userId, ct);
        if (task.Kind == ApprovalTaskKind.Resubmit) throw new ApprovalException("重新提交的任务不能转交");
        var node = flow.GetNode(task.NodeId);
        if (!node.AllowTransfer) throw new ApprovalException("该节点不允许转交") { Code = ApprovalErrorCode.Forbidden };
        if (node.RequireQualifiedAssignee && !(await _assignees.GetEligibleApproversAsync(instance, node, ct)).Contains(toUserId))
            throw new ApprovalException("转交对象不具备本节点审批资格") { Code = ApprovalErrorCode.Forbidden };
        if (!_options.AllowInitiatorApprove && toUserId == instance.InitiatorId)
            throw new ApprovalException("不能转交给发起人");
        if (OpenTasks(CurrentTasksOf(instance, task.NodeId)).Any(t => t.AssigneeKind == AssigneeKind.User && t.AssigneeValue == toUserId))
            throw new ApprovalException("对方已是该节点的审批人");

        // 转交不重置时限（含提醒进度），加签关系跟随新任务
        HandOver(instance, task, toUserId, userId, comment);
        AddLog(instance, ApprovalAction.Transfer, userId, $"转交给 {toUserId}" + (comment is null ? "" : $"：{comment}"),
            task.NodeId, task.NodeName);

        var target = new Assignee(AssigneeKind.User, toUserId);
        Notify(NotificationKind.TaskCreated, instance, [target]);
        await RaiseAsync(NotifyEvent.Transferred, instance, flow, flow.GetNode(task.NodeId),
            new EventInfo([target]) { OperatorId = userId, Comment = comment, TransferTo = toUserId, DueAt = task.DueAt }, ct);

        await CommitAsync(instance, ct);
    }

    /// <summary>当前用户能否处理该任务（供前端控制按钮显示，最终仍以操作时的校验为准）。</summary>
    public async Task<bool> CanHandleAsync(Guid taskId, string userId, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.SessionRead, new(), userId, ct);
        try
        {
            await _authorization.RequireAsync(ApprovalPermissions.TaskRead, new() { TaskId = taskId }, userId, ct);
            var (instance, task, _) = await LoadTaskAsync(taskId, ct);
            await EnsureCanHandleAsync(instance, task, userId, ct);
            return true;
        }
        catch (ApprovalException ex) when (ex.Code is ApprovalErrorCode.Forbidden or ApprovalErrorCode.NotFound or ApprovalErrorCode.InvalidOperation)
        {
            return false;
        }
    }

    /// <summary>查找当前用户在该流程中可处理的待办任务（审批详情页据此显示同意/驳回按钮），没有则返回 null。</summary>
    public async Task<ApprovalTask?> FindHandleableTaskAsync(Guid instanceId, string userId, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.TaskRead, new() { InstanceId = instanceId }, userId, ct);
        var instance = await _db.Set<ApprovalInstance>().AsNoTracking()
            .Include(i => i.Tasks)
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct);
        if (instance is null) return null;

        foreach (var task in instance.Tasks.Where(t => t.Status == ApprovalTaskStatus.Pending).OrderBy(t => t.CreatedAt).ThenBy(t => t.Order))
        {
            try
            {
                await EnsureCanHandleAsync(instance, task, userId, ct);
                return task;
            }
            catch (ApprovalException ex) when (ex.Code is ApprovalErrorCode.Forbidden or ApprovalErrorCode.NotFound or ApprovalErrorCode.InvalidOperation)
            {
                // 该任务当前用户无权处理，继续检查下一个
            }
        }
        return null;
    }

    // ───────────────────────── 流程推进 ─────────────────────────

    private async Task ApproveCoreAsync(ApprovalInstance instance, FlowDefinition flow, ApprovalTask task, string userId,
        string? comment, ApprovalAction action, CancellationToken ct)
    {
        var node = flow.GetNode(task.NodeId);
        if (node.RequireComment && userId != SystemOperator && string.IsNullOrWhiteSpace(comment))
            throw new ApprovalException("该节点必须填写审批意见");
        CompleteTask(task, ApprovalTaskStatus.Approved, userId, comment);
        AddLog(instance, node, action, userId, comment);

        var nodeTasks = CurrentTasksOf(instance, node.Id);
        await RaiseAsync(NotifyEvent.Approved, instance, flow, node,
            new EventInfo(PendingAssignees(nodeTasks)) { OperatorId = userId, Comment = comment }, ct);

        switch (task.Kind)
        {
            case ApprovalTaskKind.AddSignBefore:
                await ResumeAfterBeforeSignAsync(instance, flow, node, nodeTasks, task.ParentTaskId, ct);
                break;

            // 依次审批中的后加签：加签人都同意后才轮到下一位
            case ApprovalTaskKind.AddSignAfter when node.Mode == ApproveMode.Sequential
                                                    && AddSignChildrenDone(nodeTasks, task.ParentTaskId, ApprovalTaskKind.AddSignAfter):
                await ActivateNextSequentialAsync(instance, flow, node, nodeTasks, ct);
                break;

            case ApprovalTaskKind.Approval when node.Mode == ApproveMode.Sequential:
                await ActivateNextSequentialAsync(instance, flow, node, nodeTasks, ct);
                break;
        }

        if (IsNodeFinished(node, nodeTasks))
        {
            CancelOpenTasks(nodeTasks);
            await MoveToAsync(instance, flow, node.Next!, ct, node.Id);
        }
    }

    /// <summary>
    /// 节点是否通过：主任务满足审批方式（或签任一同意 / 会签与依次全部同意），且没有未完成的后加签、并加签任务。
    /// 前加签任务不阻塞：或签时其他人已同意，被挂起的任务及其加签任务一并取消。
    /// </summary>
    private static bool IsNodeFinished(FlowNode node, List<ApprovalTask> nodeTasks)
    {
        var primary = nodeTasks.Where(t => t.Kind == ApprovalTaskKind.Approval).ToList();
        var modeSatisfied = node.Mode == ApproveMode.Any
            ? primary.Any(t => t.Status == ApprovalTaskStatus.Approved)
            : primary.Count > 0 && primary.All(t => t.Status == ApprovalTaskStatus.Approved);
        var blocked = nodeTasks.Any(t => t.Kind is ApprovalTaskKind.AddSignAfter or ApprovalTaskKind.AddSignParallel && IsOpen(t));
        return modeSatisfied && !blocked;
    }

    private async Task RejectCoreAsync(ApprovalInstance instance, FlowDefinition flow, ApprovalTask task, string userId,
        string comment, ApprovalAction action, CancellationToken ct)
    {
        CompleteTask(task, ApprovalTaskStatus.Rejected, userId, comment);
        var node = flow.GetNode(task.NodeId);
        AddLog(instance, node, action, userId, comment);
        CancelOpenTasks(instance.Tasks);

        var info = new EventInfo([]) { OperatorId = userId, Comment = comment };
        await RaiseAsync(NotifyEvent.Rejected, instance, flow, node, info, ct);
        await FinishAsync(instance, flow, node, InstanceStatus.Rejected, info, ct);
    }

    /// <summary>
    /// 从 nodeId 开始自动推进，直到停在需要人工处理的节点、到达结束节点，或在合并节点等待其他分支。
    /// 并行节点对每个分支递归推进。
    /// </summary>
    private async Task MoveToAsync(ApprovalInstance instance, FlowDefinition flow, string nodeId, CancellationToken ct,
        string? sourceNodeId = null, ParallelScope? directBranch = null)
    {
        IReadOnlyList<ParallelScope> branchScope = StructureOf(flow).ScopeOf(sourceNodeId ?? nodeId);
        if (directBranch is { } branch) branchScope = [.. branchScope, branch];
        while (instance.Status == InstanceStatus.Running)
        {
            if (++_steps > MaxAutoSteps)
                throw new ApprovalException("流程自动推进步数超限，请检查流程定义") { Code = ApprovalErrorCode.InvalidDefinition };

            var node = flow.GetNode(nodeId);
            AddLog(instance, node, ApprovalAction.NodeEntered, SystemOperator, "进入节点");
            // 每次推进从实例快照读取最新表单值
            var form = FormData.Parse(instance.FormDataJson);

            switch (node.Type)
            {
                case NodeType.End:
                    await FinishAsync(instance, flow, node, InstanceStatus.Approved, new EventInfo([]), ct);
                    return;

                case NodeType.Start:
                    nodeId = node.Next!;
                    continue;

                case NodeType.Condition:
                    nodeId = SelectBranch(node, form);
                    continue;

                case NodeType.Parallel:
                    StateOf(instance).Joins[StructureOf(flow).JoinOf(node.Id)] = new JoinState { Expected = node.Branches.Count };
                    for (var branchIndex = 0; branchIndex < node.Branches.Count; branchIndex++)
                        await MoveToAsync(instance, flow, node.Branches[branchIndex].Next, ct, node.Id, new(node.Id, branchIndex));
                    return;

                case NodeType.Join:
                    {
                        var joins = StateOf(instance).Joins;
                        if (!joins.TryGetValue(node.Id, out var join))
                            throw new ApprovalException($"合并节点 '{node.Name}' 没有进行中的并行分支") { Code = ApprovalErrorCode.InvalidDefinition };
                        if (branchScope.Count == 0 || StructureOf(flow).JoinOf(branchScope[^1].ForkId) != node.Id)
                            throw new ApprovalException("并行汇聚来源无效") { Code = ApprovalErrorCode.InvalidDefinition };
                        if (!join.ArrivedBranches.Add(branchScope[^1].Branch)) return;
                        if (++join.Arrived < join.Expected) return;
                        if (join.Arrived > join.Expected) throw new ApprovalException("并行分支到达数无效") { Code = ApprovalErrorCode.InvalidDefinition };
                        branchScope = branchScope.Take(branchScope.Count - 1).ToArray();
                        joins.Remove(node.Id);
                        nodeId = node.Next!;
                        continue;
                    }


                case NodeType.Cc:
                    {
                        var recipients = await _assignees.ResolveAssigneesAsync(instance, node, form, expandRoles: false, ct);
                        if (recipients.Count > 0)
                        {
                            foreach (var recipient in recipients) AddCarbonCopy(instance, node, recipient);
                            AddLog(instance, node, ApprovalAction.Cc, SystemOperator,
                                "抄送：" + string.Join("、", recipients.Select(Describe)));
                            Notify(NotificationKind.Cc, instance, recipients);
                            await RaiseAsync(NotifyEvent.Enter, instance, flow, node, new EventInfo(recipients), ct,
                                fallback: NotificationDefaults.CcRule);
                        }
                        nodeId = node.Next!;
                        continue;
                    }

                case NodeType.Approval:
                    {
                        // 会签、依次审批要求每个人都处理，因此角色需要展开为具体用户
                        var resolved = await _assignees.ResolveAssigneesAsync(instance, node, form, expandRoles: node.Mode != ApproveMode.Any, ct);
                        var assignees = await _assignees.FilterEligibleAsync(instance, resolved, ct);

                        if (assignees.Count == 0)
                        {
                            if (node.EmptyApproverPolicy != EmptyApproverPolicy.AutoApprove)
                                throw new ApprovalException($"节点 '{node.Name}' 未找到审批人") { Code = ApprovalErrorCode.InvalidDefinition };

                            AddLog(instance, node, ApprovalAction.AutoApprove, SystemOperator, "未找到审批人，自动通过");
                            nodeId = node.Next!;
                            continue;
                        }

                        await CreateTasksAsync(instance, flow, node, assignees, ct);
                        return;
                    }

                default:
                    throw new ApprovalException($"不支持的节点类型 {node.Type}") { Code = ApprovalErrorCode.InvalidDefinition };
            }
        }
    }

    private string SelectBranch(FlowNode node, IReadOnlyDictionary<string, object?> form)
    {
        // 有条件的分支按优先级依次匹配，默认分支始终最后
        foreach (var branch in node.Branches
                     .OrderBy(b => string.IsNullOrWhiteSpace(b.Expression))
                     .ThenBy(b => b.Priority))
        {
            if (string.IsNullOrWhiteSpace(branch.Expression) || _evaluator.Evaluate(branch.Expression, form))
                return branch.Next;
        }
        throw new ApprovalException($"条件节点 '{node.Name}' 没有匹配的分支") { Code = ApprovalErrorCode.InvalidDefinition };
    }

    private async Task CreateTasksAsync(ApprovalInstance instance, FlowDefinition flow, FlowNode node, IReadOnlyList<Assignee> assignees,
        CancellationToken ct)
    {
        var dueAt = DueAtFor(node);
        for (var i = 0; i < assignees.Count; i++)
        {
            // 依次审批只激活第一个人，后续的人轮到时才开始计时
            var pending = node.Mode != ApproveMode.Sequential || i == 0;
            AddTask(instance, new ApprovalTask
            {
                Id = _unit.NewId("ApprovalEngine"),
                InstanceId = instance.Id,
                NodeId = node.Id,
                NodeName = node.Name,
                AssigneeKind = assignees[i].Kind,
                AssigneeValue = assignees[i].Value,
                Kind = ApprovalTaskKind.Approval,
                Round = instance.Round,
                Order = i,
                Status = pending ? ApprovalTaskStatus.Pending : ApprovalTaskStatus.Waiting,
                CreatedAt = Now,
                DueAt = pending ? dueAt : null,
                NextRemindAt = pending ? dueAt : null
            });
        }

        var notified = node.Mode == ApproveMode.Sequential ? assignees.Take(1).ToList() : assignees.ToList();
        Notify(NotificationKind.TaskCreated, instance, notified);
        await RaiseAsync(NotifyEvent.Enter, instance, flow, node, new EventInfo(notified) { DueAt = dueAt }, ct);
    }

    private async Task<bool> ActivateNextSequentialAsync(ApprovalInstance instance, FlowDefinition flow, FlowNode node,
        List<ApprovalTask> nodeTasks, CancellationToken ct)
    {
        var next = nodeTasks.Where(t => t.Kind == ApprovalTaskKind.Approval && t.Status == ApprovalTaskStatus.Waiting).MinBy(t => t.Order);
        if (next is null) return false;

        next.Status = ApprovalTaskStatus.Pending;
        next.DueAt = next.NextRemindAt = DueAtFor(node);
        WriteEvent(instance, "TaskCreated", next.NodeId, next.NodeName, next.Id, SystemOperator, null);
        var assignee = new Assignee(next.AssigneeKind, next.AssigneeValue);
        Notify(NotificationKind.TaskCreated, instance, [assignee]);
        await RaiseAsync(NotifyEvent.Enter, instance, flow, node, new EventInfo([assignee]) { DueAt = next.DueAt }, ct);
        return true;
    }

    private DateTime? DueAtFor(FlowNode node) => node.Timeout is { Minutes: > 0 } timeout ? Now.AddMinutes(timeout.Minutes) : null;

    /// <param name="node">结束时所在的节点：通过时为结束节点，驳回时为驳回所在节点，撤回 / 终止时为当前审批节点。</param>
    private async Task FinishAsync(ApprovalInstance instance, FlowDefinition flow, FlowNode? node, InstanceStatus status, EventInfo info,
        CancellationToken ct)
    {
        instance.Status = status;
        foreach (var slot in await _db.Set<ApprovalBusinessSlot>().Where(s => s.InstanceId == instance.Id).ToListAsync(ct)) _db.Remove(slot);
        foreach (var slot in _db.ChangeTracker.Entries<ApprovalBusinessSlot>().Where(e => e.State == EntityState.Added && e.Entity.InstanceId == instance.Id).ToArray())
            slot.State = EntityState.Detached;
        instance.CompletedAt = Now;
        instance.CurrentNodeId = status == InstanceStatus.Approved ? null : node?.Id;
        StateOf(instance).Joins.Clear();
        CancelOpenTasks(instance.Tasks);
        if (status == InstanceStatus.Approved) AddLog(instance, null, ApprovalAction.Complete, SystemOperator, "审批通过");

        if (_handlers.TryGetValue(instance.BusinessType, out var handler))
        {
            using var extension = _unit.Extension();
            var callback = status switch
            {
                InstanceStatus.Approved => handler.OnApprovedAsync(instance, ct),
                InstanceStatus.Rejected => handler.OnRejectedAsync(instance, ct),
                InstanceStatus.Withdrawn => handler.OnWithdrawnAsync(instance, ct),
                InstanceStatus.Terminated => handler.OnTerminatedAsync(instance, ct),
                _ => Task.CompletedTask
            };
            await callback;
        }
        else if (_options.RequireBusinessHandler)
        {
            throw new ApprovalException($"业务类型 '{instance.BusinessType}' 未注册回调处理器") { Code = ApprovalErrorCode.InvalidDefinition };
        }

        Notify(NotificationKind.Completed, instance, [new Assignee(AssigneeKind.User, instance.InitiatorId)]);
        WriteEvent(instance, "Completed", node?.Id, node?.Name, null, info.OperatorId ?? SystemOperator, info.Comment);
        await RaiseAsync(NotifyEvent.Completed, instance, flow, node, info, ct);
    }

    // ───────────────────────── 校验与加载 ─────────────────────────

    private async Task<(ApprovalInstance Instance, ApprovalTask Task, FlowDefinition Flow)> LoadTaskAsync(Guid taskId, CancellationToken ct)
    {
        var instance = await _db.Set<ApprovalInstance>()
            .Include(i => i.Tasks)
            .FirstOrDefaultAsync(i => i.Tasks.Any(t => t.Id == taskId), ct)
            ?? throw new ApprovalException("审批任务不存在") { Code = ApprovalErrorCode.NotFound };

        var flow = await LoadFlowAsync(instance, ct);
        Touch(instance);
        _operationTaskId = taskId;
        return (instance, instance.Tasks.Single(t => t.Id == taskId), flow);
    }

    private async Task<FlowDefinition> LoadFlowAsync(ApprovalInstance instance, CancellationToken ct)
    {
        if (_loadedFlows.TryGetValue(instance.DefinitionId, out var cached)) return cached;
        var definition = await _db.Set<ApprovalDefinition>().AsNoTracking().FirstAsync(d => d.Id == instance.DefinitionId, ct);
        return _loadedFlows[instance.DefinitionId] = FlowDefinition.Parse(definition.DefinitionJson);
    }

    private async Task EnsureCanHandleAsync(ApprovalInstance instance, ApprovalTask task, string userId, CancellationToken ct)
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

        var node = (await LoadFlowAsync(instance, ct)).GetNode(task.NodeId);
        if (node.RequireQualifiedAssignee && !(await _assignees.GetEligibleApproversAsync(instance, node, ct)).Contains(userId))
            throw new ApprovalException("您已不具备本节点审批资格，请联系管理员改派") { Code = ApprovalErrorCode.Forbidden };
    }

    /// <summary>按当前组织/用户目录计算合格人员；供岗位约束、宿主预检和无人处理告警共用。实例应包含任务历史。</summary>
    public async Task<IReadOnlyList<string>> GetEligibleApproversAsync(ApprovalInstance instance, FlowNode node, CancellationToken ct = default)
    {
        if (!await _authorization.CanAsync(ApprovalPermissions.TaskRead, new() { InstanceId = instance.Id }, ct: ct))
            await _authorization.RequireAsync(ApprovalPermissions.DiagnosticsRead, new() { InstanceId = instance.Id }, ct: ct);
        var actual = await _db.Set<ApprovalInstance>().AsNoTracking().Include(i => i.Tasks).SingleAsync(i => i.Id == instance.Id, ct);
        var flow = await LoadFlowAsync(actual, ct);
        if (!flow.Nodes.TryGetValue(node.Id, out var actualNode)) throw new ApprovalException("节点不存在") { Code = ApprovalErrorCode.NotFound };
        return await _assignees.GetEligibleApproversAsync(actual, actualNode, ct);
    }

    /// <summary>发起前人员预检：校验可信发起人和发起权限，使用当前租户的已发布定义，不创建实例。</summary>
    public async Task<IReadOnlyList<string>> GetStartEligibleApproversAsync(StartApprovalRequest request, string nodeId, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(ApprovalPermissions.InstanceStart, new() { DefinitionKey = request.DefinitionKey,
            DefinitionId = request.DefinitionId, BusinessType = request.BusinessType, BusinessKey = request.BusinessKey }, request.InitiatorId, ct);
        var definition = await _db.Set<ApprovalDefinition>().AsNoTracking()
            .Where(d => d.Key == request.DefinitionKey && d.IsPublished && (request.DefinitionId == null || d.Id == request.DefinitionId))
            .OrderByDescending(d => d.Version).FirstOrDefaultAsync(ct)
            ?? throw new ApprovalException("流程未发布") { Code = ApprovalErrorCode.NotFound };
        var flow = FlowDefinition.Parse(definition.DefinitionJson);
        if (!flow.Nodes.TryGetValue(nodeId, out var node)) throw new ApprovalException("节点不存在") { Code = ApprovalErrorCode.NotFound };
        return await _assignees.GetEligibleApproversAsync(new ApprovalInstance { DefinitionId = definition.Id, DefinitionKey = definition.Key,
            DefinitionVersion = definition.Version, BusinessType = request.BusinessType, BusinessKey = request.BusinessKey,
            InitiatorId = request.InitiatorId, FormDataJson = FormData.Serialize(request.FormData), Status = InstanceStatus.Running }, node, ct);
    }

    // ───────────────────────── 辅助方法 ─────────────────────────

    private FlowStructure StructureOf(FlowDefinition flow)
    {
        if (!_structures.TryGetValue(flow, out var structure)) _structures[flow] = structure = FlowStructure.Analyze(flow);
        return structure;
    }

    private ExecutionState StateOf(ApprovalInstance instance)
    {
        if (!_states.TryGetValue(instance.Id, out var state)) _states[instance.Id] = state = ExecutionState.Load(instance);
        return state;
    }

    private static bool IsOpen(ApprovalTask t) =>
        t.Status is ApprovalTaskStatus.Pending or ApprovalTaskStatus.Waiting or ApprovalTaskStatus.Suspended;

    private static IEnumerable<ApprovalTask> OpenTasks(IEnumerable<ApprovalTask> tasks) => tasks.Where(IsOpen);

    private static IEnumerable<ApprovalTask> OpenTasks(ApprovalInstance instance) => OpenTasks(instance.Tasks);

    /// <summary>节点最近一轮（退回后重新激活的那一轮）的有效任务，不含已取消、已转交的。</summary>
    private static List<ApprovalTask> CurrentTasksOf(ApprovalInstance instance, string nodeId)
    {
        var tasks = instance.Tasks.Where(t => t.NodeId == nodeId).ToList();
        if (tasks.Count == 0) return tasks;
        var round = tasks.Max(t => t.Round);
        return tasks
            .Where(t => t.Round == round && t.Status is not (ApprovalTaskStatus.Canceled or ApprovalTaskStatus.Transferred))
            .ToList();
    }

    private static List<Assignee> PendingAssignees(IEnumerable<ApprovalTask> tasks) =>
        tasks.Where(t => t.Status == ApprovalTaskStatus.Pending).Select(t => new Assignee(t.AssigneeKind, t.AssigneeValue)).ToList();

    private void CompleteTask(ApprovalTask task, ApprovalTaskStatus status, string userId, string? comment)
    {
        task.Status = status;
        task.HandledBy = userId;
        task.Comment = comment;
        task.CompletedAt = Now;
        task.NextRemindAt = null;
    }

    private void CancelOpenTasks(IEnumerable<ApprovalTask> tasks)
    {
        foreach (var t in OpenTasks(tasks).ToList())
        {
            t.Status = ApprovalTaskStatus.Canceled;
            t.CompletedAt = Now;
            t.NextRemindAt = null;
        }
    }

    // 显式 Add：已赋值 Guid 主键的新实体若仅通过导航集合被发现，EF 会误判为已存在而生成 UPDATE
    private void AddTask(ApprovalInstance instance, ApprovalTask task)
    {
        _db.Set<ApprovalTask>().Add(task);
        // EF relationship fixup 可能已经加入导航集合，避免同一任务在内存中重复出现。
        if (!instance.Tasks.Contains(task)) instance.Tasks.Add(task);
        if (task.Status == ApprovalTaskStatus.Pending)
            WriteEvent(instance, "TaskCreated", task.NodeId, task.NodeName, task.Id, SystemOperator, null);
    }

    private void AddCarbonCopy(ApprovalInstance instance, FlowNode node, Assignee recipient) =>
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

    private void AddLog(ApprovalInstance instance, FlowNode? node, ApprovalAction action, string operatorId, string? comment) =>
        AddLog(instance, action, operatorId, comment, node?.Id, node?.Name);

    private void AddLog(ApprovalInstance instance, ApprovalAction action, string operatorId, string? comment,
        string? nodeId, string? nodeName, string? details = null)
    {
        var taskId = action is ApprovalAction.Submit or ApprovalAction.Complete or ApprovalAction.NodeEntered or ApprovalAction.Cc
            or ApprovalAction.Withdraw or ApprovalAction.Terminate ? null : _operationTaskId;
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
        WriteEvent(instance, action.ToString(), nodeId, nodeName, taskId, operatorId, comment);
    }

    private void WriteEvent(ApprovalInstance instance, string type, string? nodeId, string? nodeName, Guid? taskId,
        string operatorId, string? comment)
    {
        if (!EventsEnabled) return;
        AddOutbox(OutboxKind.Event, "Host", instance, null, type,
            id => new ApprovalEvent(id, type, instance.Id, instance.DefinitionId, instance.DefinitionVersion,
                instance.BusinessType, instance.BusinessKey, type switch
                {
                    "Reject" or "TimeoutReject" => InstanceStatus.Rejected,
                    "Withdraw" => InstanceStatus.Withdrawn,
                    "Terminate" => InstanceStatus.Terminated,
                    _ => instance.Status
                }, nodeId, nodeName, taskId, operatorId, comment, Now));
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

    private static string Describe(Assignee a) => a.Kind == AssigneeKind.Role ? $"角色[{a.Value}]" : a.Value;

    /// <summary>进程内提交后通知接口 <see cref="IApprovalNotifier"/>：提交后同步调用，失败只记录日志。</summary>
    private void Notify(NotificationKind kind, ApprovalInstance instance, IReadOnlyList<Assignee> recipients)
    {
        if (_notifiers.Count == 0 || recipients.Count == 0) return;
        _pendingNotifications.Add(new ApprovalNotification(
            kind, instance.Id, instance.BusinessType, instance.BusinessKey, instance.Title, instance.Status, recipients));
    }

    private async Task CommitAsync(ApprovalInstance instance, CancellationToken ct)
    {
        _unit.Reference(instance.Id, instance.DefinitionId);
        // 保存运行时状态与当前活动节点（并行时为多个）
        StateOf(instance).SaveTo(instance);
        if (instance.Status is InstanceStatus.Running or InstanceStatus.Returned)
        {
            var current = string.Join(",", OpenTasks(instance).OrderBy(t => t.CreatedAt).Select(t => t.NodeId).Distinct());
            instance.CurrentNodeId = current.Length == 0 ? null : current.Length <= 500 ? current : current[..500];
        }
        _steps = 0;
        _operationTaskId = null;

        var notifications = _pendingNotifications.ToArray();
        _pendingNotifications.Clear();

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ApprovalException("流程已被他人更新，请刷新后重试", ex) { Code = ApprovalErrorCode.Conflict };
        }
        finally
        {
            _states.Remove(instance.Id);
        }

        // 新写入了待投递消息：立即唤醒后台投递，而不必等到下一次轮询
        if (_outboxWritten)
        {
            _outboxWritten = false;
            _unit.AfterCommit(() => { _outboxSignal.Notify(); return Task.CompletedTask; });
        }

        // 通知在事务提交后发送，失败不影响审批结果
        _unit.AfterCommit(async () =>
        {
          foreach (var notification in notifications)
          {
            foreach (var notifier in _notifiers)
            {
                try
                {
                    await notifier.NotifyAsync(notification, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "审批通知发送失败：{Kind} {InstanceId}", notification.Kind, notification.InstanceId);
                }
            }
          }
        });
    }
}
