using Microsoft.EntityFrameworkCore;

namespace Approval.Core.Engine;

/// <summary>分页参数：页码从 1 开始；每页条数默认 20，最大 100（超出范围自动修正）。</summary>
public sealed record PageRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public PageRequest(int? page = null, int? pageSize = null)
    {
        PageSize = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        Page = Math.Clamp(page ?? 1, 1, int.MaxValue / PageSize);
    }

    public int Page { get; }

    public int PageSize { get; }

    public int Skip => (Page - 1) * PageSize;
}

/// <summary>分页结果：Total 为满足条件的总条数；页码超出范围时 Items 为空。</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public static class PagingExtensions
{
    /// <summary>先取总数再取当前页。调用方需先排序且排序唯一（如 CreatedAt + Id），保证翻页时不重复、不遗漏。</summary>
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, PageRequest page, CancellationToken ct = default)
    {
        var total = await query.CountAsync(ct);
        List<T> items = total <= page.Skip ? [] : await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<T>(items, total, page.Page, page.PageSize);
    }
}
