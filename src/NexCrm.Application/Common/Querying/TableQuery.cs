namespace NexCrm.Application.Common.Querying;

/// <summary>
/// The one request shape every grid in the app sends. Fields are matched against the
/// projected DTO's public properties, so a client can only filter or sort on what it can see.
/// </summary>
public sealed class TableQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    /// <summary>Free text matched against every text column.</summary>
    public string? Search { get; set; }
    public string? SortBy { get; set; }
    public bool SortDesc { get; set; }
    /// <summary>Per-column quick filters typed in the header row. Syntax depends on column type (see QueryEngine).</summary>
    public Dictionary<string, string>? ColumnFilters { get; set; }
    /// <summary>Advanced filter builder rules.</summary>
    public List<FilterRule>? Filters { get; set; }
    /// <summary>"and" or "or" between advanced rules.</summary>
    public string? Logic { get; set; }
    /// <summary>Numeric columns to total across the whole filtered result (not just the page).</summary>
    public List<string>? SumFields { get; set; }
}

public sealed class FilterRule
{
    public string Field { get; set; } = "";
    public string Operator { get; set; } = "contains";
    public string? Value { get; set; }
    public string? Value2 { get; set; }
}

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Total { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public Dictionary<string, decimal>? Sums { get; init; }
}

public sealed class ExportColumn
{
    public string Field { get; set; } = "";
    public string Header { get; set; } = "";
}

public sealed class ExportRequest
{
    public TableQuery Query { get; set; } = new();
    public List<ExportColumn> Columns { get; set; } = [];
    /// <summary>xlsx or csv.</summary>
    public string Format { get; set; } = "xlsx";
    public string? FileName { get; set; }
    public string? Title { get; set; }
}

public sealed record FileResultDto(byte[] Content, string ContentType, string FileName);
