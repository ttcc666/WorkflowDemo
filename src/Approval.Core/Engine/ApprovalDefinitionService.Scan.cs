using Approval.Core.Abstractions;
using Approval.Core.Definitions;
using Approval.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine;

public sealed record SynchronousActionFinding(Guid DefinitionId, string Key, int Version, string? NodeId,
    string? StepName, string? ActionType, string Reason, int RunningInstances);
public sealed record SynchronousActionScan(int DefinitionsScanned, int TotalDefinitions, int Page, int PageSize,
    IReadOnlyList<SynchronousActionFinding> Findings);

public sealed partial class ApprovalDefinitionService
{
    /// <summary>只读扫描全部历史发布版本，返回动作位置及在途数量；不输出动作参数或改变执行方式。</summary>
    public async Task<SynchronousActionScan> ScanSynchronousActionsAsync(PageRequest? page = null, CancellationToken ct = default)
    {
        await _authorization.RequireAsync(ApprovalPermissions.DiagnosticsRead, ct: ct);
        await _authorization.RequireAsync(ApprovalPermissions.DefinitionReadSensitive, ct: ct);
        var batch = await _db.Set<ApprovalDefinition>().AsNoTracking().Where(d => d.IsPublished)
            .OrderBy(d => d.Key).ThenBy(d => d.Version).ToPagedAsync(page ?? new(), ct);
        var ids = batch.Items.Select(d => d.Id).ToArray();
        var counts = await _db.Set<ApprovalInstance>().Where(i => i.Status == InstanceStatus.Running && ids.Contains(i.DefinitionId))
            .GroupBy(i => i.DefinitionId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Id, g => g.Count, ct);
        var findings = new List<SynchronousActionFinding>();
        foreach (var definition in batch.Items)
        {
            await _authorization.RequireAsync(ApprovalPermissions.DefinitionReadSensitive, new() { DefinitionId = definition.Id }, ct: ct);
            FlowDefinition flow;
            try { flow = FlowDefinition.Parse(definition.DefinitionJson); }
            catch (ApprovalException) { findings.Add(new(definition.Id, definition.Key, definition.Version, null, null, null, "InvalidDefinition", counts.GetValueOrDefault(definition.Id))); continue; }
            foreach (var node in flow.Nodes.Values)
            foreach (var step in node.Actions)
            {
                _actions.TryGetValue(step.Type, out var handler);
                var reason = handler?.CommitsWorkUnit == true ? "SelfCommit" : step.Mode != ActionMode.Sync ? null
                    : handler is null ? "UnregisteredSyncAction" : handler.HasExternalSideEffects ? "ExternalSideEffect" : null;
                if (reason is not null) findings.Add(new(definition.Id, definition.Key, definition.Version, node.Id, step.Name,
                    step.Type, reason, counts.GetValueOrDefault(definition.Id)));
            }
        }
        return new(batch.Items.Count, batch.Total, batch.Page, batch.PageSize, findings);
    }
}
