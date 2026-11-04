using Approval.Core.Domain;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine;

/// <summary>统计范围：发起时间 [From, To)（UTC，最长 366 天），可按流程筛选。</summary>
public sealed record StatisticsQuery(DateTime From, DateTime To, string? DefinitionKey = null);

/// <summary>流程状态分布与耗时（分钟，只统计已结束的流程）。</summary>
public sealed record FlowStatistics(
    string DefinitionKey,
    string DefinitionName,
    int Total,
    int Running,
    int Approved,
    int Rejected,
    int Withdrawn,
    int Terminated,
    double? AverageMinutes,
    double? P90Minutes)
{
    public int Returned { get; init; }
}

/// <summary>节点处理时长：已同意 / 驳回的人工处理任务（不含系统自动处理），Overdue 为超过时限才处理的任务数。</summary>
public sealed record NodeStatistics(string DefinitionKey, string DefinitionName, string NodeId, string NodeName, int Handled,
    double AverageMinutes, int Overdue);

/// <summary>待办积压：当前待处理任务按处理人（用户或角色）汇总。</summary>
public sealed record BacklogItem(AssigneeKind AssigneeKind, string AssigneeValue, int Count, int Overdue, DateTime OldestCreatedAt);

/// <param name="ApprovalRate">通过率 = 通过 /（通过 + 驳回 + 终止），撤回与进行中不计入；没有已结束流程时为 null。</param>
/// <param name="OverdueTasks">超时任务数：超过时限才处理的任务 + 当前已超时的待办。</param>
/// <param name="Truncated">流程数超过上限（<see cref="ApprovalStatisticsService.MaxInstances"/>）时只统计最早的部分。</param>
/// <param name="Backlog">当前待办积压前 10 的处理人（不受时间范围限制，受流程筛选限制）。</param>
public sealed record ApprovalStatistics(
    DateTime From,
    DateTime To,
    FlowStatistics Summary,
    double? ApprovalRate,
    int OverdueTasks,
    bool Truncated,
    IReadOnlyList<FlowStatistics> Flows,
    IReadOnlyList<NodeStatistics> Nodes,
    IReadOnlyList<BacklogItem> Backlog);

/// <summary>
/// 审批统计：读取必要列并在预算内流式聚合；超限拒绝整次查询，不返回部分任务指标。
/// 数据量很大的部署建议替换为宿主自己的报表。
/// </summary>
public sealed class ApprovalStatisticsService
{
    public const int MaxDays = 366;
    public const int MaxInstances = 100_000;
    private const int BacklogSize = 10;

    private readonly Approval.Core.Authorization.IApprovalAuthorization _authorization;
    private readonly DbContext _db;
    private readonly TimeProvider _clock;
    private readonly ApprovalStatisticsOptions _options;

    public ApprovalStatisticsService(ApprovalDbContextAccessor accessor, TimeProvider clock, ApprovalOptions options)
    {
        _authorization = accessor.Authorization;
        _db = accessor.Context;
        _clock = clock;
        _options = options.Statistics;
        _options.Validate();
    }

    private sealed record InstanceRow(Guid Id, string DefinitionKey, InstanceStatus Status, DateTime CreatedAt, DateTime? CompletedAt);

    private sealed record TaskRow(string DefinitionKey, string NodeId, string NodeName, ApprovalTaskStatus Status, ApprovalTaskKind Kind,
        string? HandledBy, DateTime CreatedAt, DateTime? CompletedAt, DateTime? DueAt);

    private sealed class NodeTotals
    {
        public string Name = "";
        public int Count, Overdue;
        public double Minutes;
    }

    public async Task<ApprovalStatistics> GetStatisticsAsync(StatisticsQuery query, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(Approval.Core.Abstractions.ApprovalPermissions.StatisticsRead, new(), null, ct);
        if (query.To <= query.From) throw new ApprovalException("统计结束时间必须晚于开始时间");
        if (query.To - query.From > TimeSpan.FromDays(MaxDays)) throw new ApprovalException($"统计时间范围不能超过 {MaxDays} 天");

        _options.Validate();
        var now = _clock.GetUtcNow().UtcDateTime;
        var key = string.IsNullOrWhiteSpace(query.DefinitionKey) ? null : query.DefinitionKey.Trim();
        var names = await LatestNamesAsync(ct);

        var inRange = (await _authorization.ScopeAsync(_db.Set<ApprovalInstance>().AsNoTracking(), ct)).Where(i => i.CreatedAt >= query.From && i.CreatedAt < query.To);
        if (key is not null) inRange = inRange.Where(i => i.DefinitionKey == key);

        var ordered = inRange.OrderBy(i => i.CreatedAt).ThenBy(i => i.Id);
        var instances = await ordered
            .Select(i => new InstanceRow(i.Id, i.DefinitionKey, i.Status, i.CreatedAt, i.CompletedAt))
            .Take(MaxInstances + 1)
            .ToListAsync(ct);
        var truncated = instances.Count > MaxInstances;
        if (truncated)
        {
            instances.RemoveAt(instances.Count - 1);
        }

        // Limit the exact instance set in SQL, including ties on CreatedAt. No large client-side ID list.
        var selected = ordered.Take(MaxInstances).Select(i => new { i.Id, i.DefinitionKey });
        var tasks = (
                from t in _db.Set<ApprovalTask>().AsNoTracking()
                join i in selected on t.InstanceId equals i.Id
                select new TaskRow(i.DefinitionKey, t.NodeId, t.NodeName, t.Status, t.Kind, t.HandledBy, t.CreatedAt, t.CompletedAt, t.DueAt))
            .Take(_options.MaxTaskRows + 1);

        var totals = new Dictionary<(string Flow, string Node), NodeTotals>();
        var taskCount = 0;
        var overdue = 0;
        await foreach (var task in tasks.AsAsyncEnumerable().WithCancellation(ct))
        {
            if (++taskCount > _options.MaxTaskRows)
                throw LimitExceeded("关联任务", _options.MaxTaskRows, "请缩短时间范围、按流程筛选或使用宿主报表");
            var handled = task.Status is ApprovalTaskStatus.Approved or ApprovalTaskStatus.Rejected;
            if (task.DueAt is { } due && ((handled && task.CompletedAt > due)
                || (task.Status == ApprovalTaskStatus.Pending && now > due))) overdue++;
            if (!handled || task.CompletedAt is not { } completed || task.Kind == ApprovalTaskKind.Resubmit
                || task.HandledBy == ApprovalEngine.SystemOperator) continue;
            var group = (task.DefinitionKey, task.NodeId);
            if (!totals.TryGetValue(group, out var total)) totals[group] = total = new();
            total.Name = task.NodeName;
            total.Count++;
            total.Minutes += Minutes(task.CreatedAt, completed);
            if (task.DueAt is { } deadline && completed > deadline) total.Overdue++;
        }
        string NameOf(string definitionKey) => names.GetValueOrDefault(definitionKey, definitionKey);

        var flows = instances.GroupBy(i => i.DefinitionKey)
            .Select(g => Summarize(g.Key, NameOf(g.Key), g.ToList()))
            .OrderByDescending(f => f.Total).ThenBy(f => f.DefinitionKey)
            .ToList();
        var summary = Summarize(key ?? "", key is null ? "全部流程" : NameOf(key), instances);
        var finished = summary.Approved + summary.Rejected + summary.Terminated;

        var nodes = totals.Select(g => new NodeStatistics(g.Key.Flow, NameOf(g.Key.Flow), g.Key.Node, g.Value.Name,
                g.Value.Count, Round(g.Value.Minutes / g.Value.Count), g.Value.Overdue))
            .OrderByDescending(n => n.AverageMinutes)
            .ToList();

        return new ApprovalStatistics(query.From, query.To, summary, finished == 0 ? null : Round(100.0 * summary.Approved / finished),
            overdue, truncated, flows, nodes, await BacklogAsync(key, now, ct));
    }

    private static FlowStatistics Summarize(string key, string name, IReadOnlyCollection<InstanceRow> rows)
    {
        var durations = rows.Where(r => r.CompletedAt is not null)
            .Select(r => Minutes(r.CreatedAt, r.CompletedAt!.Value))
            .Order()
            .ToList();
        int Count(InstanceStatus status) => rows.Count(r => r.Status == status);
        return new FlowStatistics(key, name, rows.Count, Count(InstanceStatus.Running), Count(InstanceStatus.Approved),
            Count(InstanceStatus.Rejected), Count(InstanceStatus.Withdrawn), Count(InstanceStatus.Terminated),
            durations.Count == 0 ? null : Round(durations.Average()),
            durations.Count == 0 ? null : Round(durations[(int)Math.Ceiling(durations.Count * 0.9) - 1]))
        { Returned = Count(InstanceStatus.Returned) };
    }

    /// <summary>当前待处理任务按处理人汇总，取数量最多的前 10 个。</summary>
    private async Task<List<BacklogItem>> BacklogAsync(string? key, DateTime now, CancellationToken ct)
    {
        var scoped = await _authorization.ScopeAsync(_db.Set<ApprovalInstance>().AsNoTracking(), ct);
        var pending =
                from t in _db.Set<ApprovalTask>().AsNoTracking()
                join i in scoped on t.InstanceId equals i.Id
                where t.Status == ApprovalTaskStatus.Pending && i.Status == InstanceStatus.Running && (key == null || i.DefinitionKey == key)
                select new { t.AssigneeKind, t.AssigneeValue, t.CreatedAt, t.DueAt };
        // Keep ordinal assignee grouping independent of the host database collation, without retaining every task row.
        var groups = new Dictionary<(AssigneeKind Kind, string Value), (int Count, int Overdue, DateTime Oldest)>();
        var count = 0;
        await foreach (var task in pending.Take(_options.MaxBacklogRows + 1).AsAsyncEnumerable().WithCancellation(ct))
        {
            if (++count > _options.MaxBacklogRows)
                throw LimitExceeded("当前待办积压", _options.MaxBacklogRows, "积压不受日期范围限制，请按流程筛选或使用宿主报表");
            var group = (task.AssigneeKind, task.AssigneeValue);
            if (groups.TryGetValue(group, out var value))
                groups[group] = (value.Count + 1, value.Overdue + (task.DueAt < now ? 1 : 0), task.CreatedAt < value.Oldest ? task.CreatedAt : value.Oldest);
            else groups[group] = (1, task.DueAt < now ? 1 : 0, task.CreatedAt);
        }

        return groups.Select(g => new BacklogItem(g.Key.Kind, g.Key.Value, g.Value.Count, g.Value.Overdue, g.Value.Oldest))
            .OrderByDescending(b => b.Count).ThenBy(b => b.OldestCreatedAt)
            .Take(BacklogSize)
            .ToList();
    }

    /// <summary>各流程最新发布版本的名称。</summary>
    private async Task<Dictionary<string, string>> LatestNamesAsync(CancellationToken ct)
    {
        var definitions = await _db.Set<ApprovalDefinition>().AsNoTracking()
            .Select(d => new { d.Key, d.Name, d.Version })
            .ToListAsync(ct);
        return definitions.GroupBy(d => d.Key).ToDictionary(g => g.Key, g => g.MaxBy(d => d.Version)!.Name);
    }

    private static double Minutes(DateTime from, DateTime to) => (to - from).TotalMinutes;

    private static ApprovalException LimitExceeded(string kind, int limit, string advice) =>
        new($"{kind}超过统计读取预算 {limit}；{advice}") { Code = ApprovalErrorCode.StatisticsLimitExceeded };

    private static double Round(double value) => Math.Round(value, 1);
}
