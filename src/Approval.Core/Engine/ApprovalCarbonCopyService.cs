using Approval.Core.Abstractions;
using Approval.Core.Domain;
using Approval.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine;

public sealed record CarbonCopyItem
{
    public Guid Id { get; init; }
    public Guid InstanceId { get; init; }
    public required string NodeId { get; init; }
    public int Round { get; init; }
    public AssigneeKind AssigneeKind { get; init; }
    public required string AssigneeValue { get; init; }
    public DateTime CreatedAt { get; init; }
    public required string DefinitionKey { get; init; }
    public required string DefinitionName { get; init; }
    public int DefinitionVersion { get; init; }
    public required string BusinessType { get; init; }
    public required string BusinessKey { get; init; }
    public string? Title { get; init; }
    public required string InitiatorId { get; init; }
    public InstanceStatus Status { get; init; }
    public DateTime? ReadAt { get; init; }
}

public sealed record CarbonCopyRecipient(string UserId, string? Name, bool IsKnown, bool? IsActive);
public sealed record CarbonCopyRecipients(Guid CarbonCopyId, AssigneeKind AssigneeKind, string AssigneeValue,
    bool IsCurrentRoleMembership, DateTime QueriedAt, IReadOnlyList<CarbonCopyRecipient> Items, int Total, int Page, int PageSize);

public sealed class ApprovalCarbonCopyService(ApprovalDbContextAccessor accessor, IApprovalIdentityProvider identity,
    ApprovalQueryService queries, TimeProvider clock, IApprovalUserDirectory? directory = null)
{
    private readonly DbContext _db = accessor.Context;

    private async Task<IQueryable<CarbonCopyItem>> MineAsync(string userId, CancellationToken ct, bool ownOnly = true, InstanceQuery? query = null)
    {
        await accessor.Authorization.RequireAsync(ApprovalPermissions.InstanceRead, userId: userId, ct: ct);
        var roles = (await identity.GetRolesAsync(userId, ct)).ToList();
        var visible = await accessor.Authorization.ScopeAsync(_db.Set<ApprovalInstance>().AsNoTracking(), ct);
        if (query is not null) visible = await queries.ApplyFiltersAsync(visible, query with { From = null, To = null }, ct);
        return from copy in _db.Set<ApprovalCarbonCopy>().AsNoTracking()
               join instance in visible on copy.InstanceId equals instance.Id
               join definition in _db.Set<ApprovalDefinition>() on instance.DefinitionId equals definition.Id
               where !ownOnly || copy.AssigneeKind == AssigneeKind.User && copy.AssigneeValue == userId
                   || copy.AssigneeKind == AssigneeKind.Role && roles.Contains(copy.AssigneeValue)
               select new CarbonCopyItem
               {
                   Id = copy.Id, InstanceId = instance.Id, NodeId = copy.NodeId, Round = copy.Round,
                   AssigneeKind = copy.AssigneeKind, AssigneeValue = copy.AssigneeValue, CreatedAt = copy.CreatedAt,
                   DefinitionKey = instance.DefinitionKey, DefinitionName = definition.Name, DefinitionVersion = instance.DefinitionVersion,
                   BusinessType = instance.BusinessType, BusinessKey = instance.BusinessKey, Title = instance.Title,
                   InitiatorId = instance.InitiatorId, Status = instance.Status,
                   ReadAt = _db.Set<ApprovalCarbonCopyRead>().Where(r => r.CarbonCopyId == copy.Id && r.UserId == userId)
                       .Select(r => (DateTime?)r.ReadAt).FirstOrDefault()
               };
    }

    public async Task<PagedResult<CarbonCopyItem>> ListAsync(string userId, bool unreadOnly = false,
        InstanceQuery? query = null, PageRequest? page = null, CancellationToken ct = default)
    {
        var items = await MineAsync(userId, ct, query: query);
        if (unreadOnly) items = items.Where(c => c.ReadAt == null);
        if (query?.Status is { } status) items = items.Where(c => c.Status == status);
        if (!string.IsNullOrWhiteSpace(query?.DefinitionKey)) items = items.Where(c => c.DefinitionKey == query.DefinitionKey);
        if (query?.From is { } from) items = items.Where(c => c.CreatedAt >= from);
        if (query?.To is { } to) items = items.Where(c => c.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(query?.InitiatorId)) items = items.Where(c => c.InitiatorId == query.InitiatorId);
        if (!string.IsNullOrWhiteSpace(query?.BusinessType)) items = items.Where(c => c.BusinessType == query.BusinessType);
        if (!string.IsNullOrWhiteSpace(query?.BusinessKey)) items = items.Where(c => c.BusinessKey.Contains(query.BusinessKey));
        if (query?.From is { } start && query.To is { } end && start >= end) throw new ApprovalException("时间范围无效");
        var ordered = query?.Sort switch
        {
            ApprovalSort.Oldest => items.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id),
            ApprovalSort.Title => items.OrderBy(c => c.Title).ThenBy(c => c.Id),
            _ => items.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
        };
        return await ordered.ToPagedAsync(page ?? new(), ct);
    }

    public async Task<CarbonCopyItem> GetAsync(string userId, Guid id, CancellationToken ct = default) =>
        await (await MineAsync(userId, ct)).SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new ApprovalException("抄送记录不存在或无权查看") { Code = ApprovalErrorCode.NotFound };

    public Task MarkReadAsync(string userId, Guid id, CancellationToken ct = default) =>
        accessor.UnitOfWork.ExecuteCommandAsync(async token =>
        {
            var item = await GetAsync(userId, id, token);
            if (item.ReadAt is not null) return;
            _db.Add(new ApprovalCarbonCopyRead
            {
                Id = Guid.ParseExact(ApprovalCommandHash.Of(id.ToString(), userId)[..32], "N"),
                CarbonCopyId = id, UserId = userId, ReadAt = clock.GetUtcNow().UtcDateTime
            });
            try { await _db.SaveChangesAsync(token); }
            catch (DbUpdateException error) when (ApprovalDatabaseErrors.Unique(error))
            { throw new ApprovalException("阅读状态已被其他请求更新，请重试", error) { Code = ApprovalErrorCode.Conflict }; }
        }, ct);

    public async Task<IReadOnlyList<CarbonCopyItem>> HistoryAsync(string userId, Guid instanceId, CancellationToken ct = default)
    {
        if (await queries.GetInstanceForViewerAsync(instanceId, new(userId, false), ct) is null)
            throw new ApprovalException("流程不存在或无权查看") { Code = ApprovalErrorCode.NotFound };
        return await (await MineAsync(userId, ct, ownOnly: false)).Where(c => c.InstanceId == instanceId)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToListAsync(ct);
    }

    /// <summary>受权查询接收人员。角色来自宿主当前成员；旧抄送没有人员快照，不推断历史名单。</summary>
    public async Task<CarbonCopyRecipients> GetRecipientsAsync(string userId, Guid id, PageRequest? page = null,
        CancellationToken ct = default, IApprovalUserDirectory? publicDirectory = null)
    {
        await accessor.Authorization.RequireAsync(ApprovalPermissions.DirectoryRead, userId: userId, ct: ct);
        var copy = await (await MineAsync(userId, ct, ownOnly: false)).SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new ApprovalException("抄送记录不存在或无权查看") { Code = ApprovalErrorCode.NotFound };
        if (await queries.GetInstanceForViewerAsync(copy.InstanceId, new(userId, false), ct) is null)
            throw new ApprovalException("抄送记录不存在或无权查看") { Code = ApprovalErrorCode.NotFound };
        var users = publicDirectory ?? directory
            ?? throw new ApprovalException("查询抄送人员需要宿主接入用户目录") { Code = ApprovalErrorCode.InvalidDefinition };
        var ids = (copy.AssigneeKind == AssigneeKind.Role
                ? await identity.GetUserIdsInRoleAsync(copy.AssigneeValue, ct) : [copy.AssigneeValue])
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        page ??= new PageRequest();
        var selected = ids.Skip(page.Skip).Take(page.PageSize).ToArray();
        var known = selected.Length == 0 ? [] : await users.GetUsersAsync(selected, ct);
        var byId = known.Where(u => selected.Contains(u.Id, StringComparer.Ordinal)).GroupBy(u => u.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var items = selected.Select(id => byId.TryGetValue(id, out var user)
            ? new CarbonCopyRecipient(id, user.Name, true, user.IsActive) : new CarbonCopyRecipient(id, null, false, null)).ToArray();
        return new(copy.Id, copy.AssigneeKind, copy.AssigneeValue, copy.AssigneeKind == AssigneeKind.Role,
            clock.GetUtcNow().UtcDateTime, items, ids.Length, page.Page, page.PageSize);
    }
}
