namespace Marketplace.Application.Common.Models;

/// <summary>Standard paginated response envelope. Every list endpoint returns one of these.</summary>
public sealed class PagedResult<T>
{
    public PagedResult()
    {
    }

    public PagedResult(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
    {
        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
    }

    public IReadOnlyList<T> Items { get; init; } = [];

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public int TotalCount { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;

    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);

    public PagedResult<TOut> Map<TOut>(Func<T, TOut> selector) =>
        new(Items.Select(selector).ToList(), Page, PageSize, TotalCount);
}

/// <summary>Normalised paging input with hard caps.</summary>
public sealed class PageRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public PageRequest()
    {
    }

    public PageRequest(int? page, int? pageSize)
    {
        Page = page is > 0 ? page.Value : 1;
        PageSize = pageSize switch
        {
            null or <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize.Value
        };
    }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    public int Skip => (Page - 1) * PageSize;
}

/// <summary>Result of an operation that either succeeds with a value or fails with a reason.</summary>
public class Result
{
    protected Result(bool isSuccess, string? error, IReadOnlyDictionary<string, string[]>? errors)
    {
        IsSuccess = isSuccess;
        Error = error;
        Errors = errors ?? new Dictionary<string, string[]>();
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public string? Error { get; }

    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public static Result Success() => new(true, null, null);

    public static Result Failure(string error) => new(false, error, null);

    public static Result Failure(string error, IReadOnlyDictionary<string, string[]> errors) => new(false, error, errors);
}

/// <summary>Result carrying a value on success.</summary>
public sealed class Result<T> : Result
{
    private Result(bool isSuccess, T? value, string? error, IReadOnlyDictionary<string, string[]>? errors)
        : base(isSuccess, error, errors)
    {
        Value = value;
    }

    public T? Value { get; }

    public static Result<T> Success(T value) => new(true, value, null, null);

    public new static Result<T> Failure(string error) => new(false, default, error, null);

    public static Result<T> Failure(IReadOnlyDictionary<string, string[]> errors) => new(false, default, "One or more validation errors occurred.", errors);
}
