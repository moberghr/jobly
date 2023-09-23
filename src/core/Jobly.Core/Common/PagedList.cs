using Microsoft.EntityFrameworkCore;

namespace Jobly.Core;

public static class PagedListExtensions
{
    public static async Task<PagedList<T>> ToPagedListAsync<T>(this IQueryable<T> query, BaseListRequest request)
        where T : class
    {
        var totalCount = query.Count();

        var pageCount = (int)Math.Ceiling(totalCount / (double)request.PageSize);

        var data = await query
            .Skip(request.Page * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        return new PagedList<T>(totalCount, data, pageCount);
    }
}

public class PagedList<T>
{
    public PagedList(int totalCount, List<T> items, int pageCount)
    {
        TotalCount = totalCount;
        Items = items;
        PageCount = pageCount;
    }

    public int TotalCount { get; set; }

    public int PageCount { get; set; }

    public List<T> Items { get; set; }
}

public class BaseListRequest
{
    public int Page { get; set; } = 0;

    public int PageSize { get; set; } = 20;
}
