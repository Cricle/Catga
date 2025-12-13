using Microsoft.AspNetCore.Http;

namespace Catga.AspNetCore.Pagination;

/// <summary>
/// Pagination and sorting support for list endpoints.
/// </summary>
public class PaginationParams
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; }
    public SortOrder SortOrder { get; set; } = SortOrder.Ascending;

    public int Skip => (Page - 1) * PageSize;
    public int Take => PageSize;

    public void Validate()
    {
        if (Page < 1) Page = 1;
        if (PageSize < 1) PageSize = 20;
        if (PageSize > 1000) PageSize = 1000;
    }
}

public enum SortOrder
{
    Ascending,
    Descending
}

/// <summary>
/// Paginated response wrapper.
/// </summary>
public class PaginatedResponse<T>
{
    public required IEnumerable<T> Items { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages => (TotalCount + PageSize - 1) / PageSize;
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}

/// <summary>
/// Extension methods for pagination.
/// </summary>
public static class PaginationExtensions
{
    /// <summary>
    /// Extracts pagination parameters from query string.
    /// </summary>
    public static PaginationParams ExtractPaginationParams(this HttpRequest request)
    {
        var page = int.TryParse(request.Query["page"], out var p) ? p : 1;
        var pageSize = int.TryParse(request.Query["pageSize"], out var ps) ? ps : 20;
        var sortBy = request.Query["sortBy"].ToString();
        var sortOrder = request.Query["sortOrder"].ToString() == "desc" ? SortOrder.Descending : SortOrder.Ascending;

        var @params = new PaginationParams
        {
            Page = page,
            PageSize = pageSize,
            SortBy = sortBy,
            SortOrder = sortOrder
        };

        @params.Validate();
        return @params;
    }

    /// <summary>
    /// Applies pagination to a queryable collection.
    /// </summary>
    public static IEnumerable<T> ApplyPagination<T>(this IEnumerable<T> source, PaginationParams @params)
    {
        return source.Skip(@params.Skip).Take(@params.Take);
    }

    /// <summary>
    /// Applies sorting to a queryable collection.
    /// </summary>
    public static IEnumerable<T> ApplySorting<T>(
        this IEnumerable<T> source,
        string? sortBy,
        SortOrder sortOrder) where T : class
    {
        if (string.IsNullOrEmpty(sortBy))
            return source;

        var property = typeof(T).GetProperty(sortBy, System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public);
        if (property == null)
            return source;

        return sortOrder == SortOrder.Ascending
            ? source.OrderBy(x => property.GetValue(x))
            : source.OrderByDescending(x => property.GetValue(x));
    }

    /// <summary>
    /// Creates a paginated response.
    /// </summary>
    public static PaginatedResponse<T> ToPaginatedResponse<T>(
        this IEnumerable<T> items,
        int totalCount,
        PaginationParams @params)
    {
        return new PaginatedResponse<T>
        {
            Items = items,
            Page = @params.Page,
            PageSize = @params.PageSize,
            TotalCount = totalCount
        };
    }

    /// <summary>
    /// Adds pagination headers to response.
    /// </summary>
    public static void AddPaginationHeaders<T>(
        this HttpResponse response,
        PaginatedResponse<T> paginatedResponse)
    {
        response.Headers.Add("X-Pagination-Page", paginatedResponse.Page.ToString());
        response.Headers.Add("X-Pagination-PageSize", paginatedResponse.PageSize.ToString());
        response.Headers.Add("X-Pagination-TotalCount", paginatedResponse.TotalCount.ToString());
        response.Headers.Add("X-Pagination-TotalPages", paginatedResponse.TotalPages.ToString());
        response.Headers.Add("X-Pagination-HasNextPage", paginatedResponse.HasNextPage.ToString());
        response.Headers.Add("X-Pagination-HasPreviousPage", paginatedResponse.HasPreviousPage.ToString());
    }
}

/// <summary>
/// Filter builder for complex query filtering.
/// </summary>
public class FilterBuilder<T> where T : class
{
    private readonly List<Func<T, bool>> _predicates = new();

    public FilterBuilder<T> Where(Func<T, bool> predicate)
    {
        _predicates.Add(predicate);
        return this;
    }

    public Func<T, bool> Build()
    {
        return item => _predicates.All(p => p(item));
    }

    public IEnumerable<T> Apply(IEnumerable<T> source)
    {
        var predicate = Build();
        return source.Where(predicate);
    }
}
