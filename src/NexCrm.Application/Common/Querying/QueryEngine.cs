using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace NexCrm.Application.Common.Querying;

/// <summary>
/// Turns a <see cref="TableQuery"/> into LINQ over a projected DTO. Field names are resolved
/// against the DTO's public properties (an allow-list by construction) and every value becomes
/// a captured variable, so EF Core emits parameterized SQL. The same engine runs over
/// in-memory sequences for report rows.
///
/// Column filter syntax:
///   text    - substring match
///   number  - "250", "&gt;100", "&gt;=100", "&lt;50", "&lt;=50", "10..20"
///   date    - "2026-10-01" (whole day) or "2026-10-01..2026-10-31"
///   enum    - "Completed" or "Completed,Refunded"
///   bool    - "true" / "false"
/// </summary>
public static class QueryEngine
{
    public const int MaxPageSize = 500;
    public const int MaxExportRows = 100_000;

    private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> PropertyCache = new();

    public static IReadOnlyDictionary<string, PropertyInfo> PropertiesOf(Type type) =>
        PropertyCache.GetOrAdd(type, t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && IsSupported(p.PropertyType))
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase));

    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> source, TableQuery query,
        string defaultSort, bool defaultDesc = true, CancellationToken ct = default)
    {
        var filtered = ApplyFilters(source, query);
        var isAsync = source.Provider is IAsyncQueryProvider;

        var total = isAsync ? await filtered.CountAsync(ct) : filtered.Count();
        var sums = await SumAsync(filtered, query.SumFields, isAsync, ct);

        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var page = Math.Max(1, query.Page);
        var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        if (page > lastPage) page = lastPage;

        var paged = ApplySort(filtered, query, defaultSort, defaultDesc).Skip((page - 1) * pageSize).Take(pageSize);
        var items = isAsync ? await paged.ToListAsync(ct) : paged.ToList();

        return new PagedResult<T> { Items = items, Total = total, Page = page, PageSize = pageSize, Sums = sums };
    }

    /// <summary>Filtered and sorted rows without paging, capped for export.</summary>
    public static async Task<List<T>> ToExportListAsync<T>(this IQueryable<T> source, TableQuery query,
        string defaultSort, bool defaultDesc = true, CancellationToken ct = default)
    {
        var rows = ApplySort(ApplyFilters(source, query), query, defaultSort, defaultDesc).Take(MaxExportRows);
        return source.Provider is IAsyncQueryProvider ? await rows.ToListAsync(ct) : rows.ToList();
    }

    public static IQueryable<T> ApplyFilters<T>(IQueryable<T> source, TableQuery query)
    {
        var props = PropertiesOf(typeof(T));
        var x = Expression.Parameter(typeof(T), "x");

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            Expression? any = null;
            foreach (var p in props.Values.Where(p => p.PropertyType == typeof(string)))
            {
                var match = StringOp(Expression.Property(x, p), "contains", term);
                any = any is null ? match : Expression.OrElse(any, match);
            }
            if (any is not null) source = source.Where(Expression.Lambda<Func<T, bool>>(any, x));
        }

        if (query.ColumnFilters is { Count: > 0 })
        {
            foreach (var (field, raw) in query.ColumnFilters)
            {
                if (string.IsNullOrWhiteSpace(raw) || !props.TryGetValue(field, out var p)) continue;
                var body = ColumnFilter(Expression.Property(x, p), raw.Trim());
                if (body is not null) source = source.Where(Expression.Lambda<Func<T, bool>>(body, x));
            }
        }

        if (query.Filters is { Count: > 0 })
        {
            var useOr = string.Equals(query.Logic, "or", StringComparison.OrdinalIgnoreCase);
            Expression? combined = null;
            foreach (var rule in query.Filters.Take(25))
            {
                if (!props.TryGetValue(rule.Field, out var p)) continue;
                var body = Rule(Expression.Property(x, p), rule);
                if (body is null) continue;
                combined = combined is null ? body : useOr ? Expression.OrElse(combined, body) : Expression.AndAlso(combined, body);
            }
            if (combined is not null) source = source.Where(Expression.Lambda<Func<T, bool>>(combined, x));
        }

        return source;
    }

    public static IQueryable<T> ApplySort<T>(IQueryable<T> source, TableQuery query, string defaultSort, bool defaultDesc)
    {
        var props = PropertiesOf(typeof(T));
        var (field, desc) = !string.IsNullOrWhiteSpace(query.SortBy) && props.ContainsKey(query.SortBy)
            ? (query.SortBy, query.SortDesc)
            : (defaultSort, defaultDesc);
        if (!props.TryGetValue(field, out var prop)) return source;

        var x = Expression.Parameter(typeof(T), "x");
        var key = Expression.Lambda(Expression.Property(x, prop), x);
        var ordered = Expression.Call(typeof(Queryable), desc ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy),
            [typeof(T), prop.PropertyType], source.Expression, Expression.Quote(key));
        var result = (IOrderedQueryable<T>)source.Provider.CreateQuery<T>(ordered);

        // A stable tiebreaker keeps rows from jumping between pages when the sort key repeats.
        if (props.TryGetValue("Id", out var id) && !string.Equals(prop.Name, "Id", StringComparison.Ordinal))
        {
            var idKey = Expression.Lambda(Expression.Property(x, id), x);
            var then = Expression.Call(typeof(Queryable), desc ? nameof(Queryable.ThenByDescending) : nameof(Queryable.ThenBy),
                [typeof(T), id.PropertyType], result.Expression, Expression.Quote(idKey));
            result = (IOrderedQueryable<T>)source.Provider.CreateQuery<T>(then);
        }
        return result;
    }

    private static async Task<Dictionary<string, decimal>?> SumAsync<T>(IQueryable<T> source, List<string>? fields, bool isAsync, CancellationToken ct)
    {
        if (fields is not { Count: > 0 }) return null;
        var props = PropertiesOf(typeof(T));
        var sums = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var x = Expression.Parameter(typeof(T), "x");
        foreach (var f in fields.Distinct(StringComparer.OrdinalIgnoreCase).Take(10))
        {
            if (!props.TryGetValue(f, out var p) || !IsNumeric(Underlying(p.PropertyType))) continue;
            Expression member = Expression.Property(x, p);
            var asDecimal = Expression.Convert(member, typeof(decimal?));
            var lambda = Expression.Lambda<Func<T, decimal?>>(asDecimal, x);
            var value = isAsync ? await source.SumAsync(lambda, ct) : source.Sum(lambda);
            sums[ToCamel(p.Name)] = value ?? 0m;
        }
        return sums;
    }

    private static Expression? ColumnFilter(MemberExpression member, string raw)
    {
        var type = Underlying(member.Type);
        if (type == typeof(string))
        {
            // "Low,Out" means any of those values (used by drill-down links on computed status columns).
            var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length <= 1) return StringOp(member, "contains", raw);
            return parts.Select(p => StringOp(member, "contains", p)).Aggregate(Expression.OrElse);
        }
        if (type == typeof(bool))
            return bool.TryParse(raw, out var b) ? Compare(member, b, ExpressionType.Equal) : null;
        if (type.IsEnum)
        {
            var values = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => Enum.TryParse(type, v, true, out var e) ? e : null).Where(e => e is not null).ToList();
            return values.Count == 0 ? null : AnyEqual(member, values!);
        }
        if (type == typeof(DateTime))
        {
            var parts = raw.Split("..", 2);
            var from = ParseDate(parts[0]);
            var to = parts.Length > 1 ? ParseDate(parts[1]) : from;
            return DateRange(member, from, to);
        }
        if (IsNumeric(type))
        {
            if (raw.Contains(".."))
            {
                var parts = raw.Split("..", 2);
                return Range(member, ParseNumber(parts[0], type), ParseNumber(parts[1], type));
            }
            foreach (var (prefix, op) in new[] { (">=", ExpressionType.GreaterThanOrEqual), ("<=", ExpressionType.LessThanOrEqual),
                         (">", ExpressionType.GreaterThan), ("<", ExpressionType.LessThan), ("=", ExpressionType.Equal) })
            {
                if (!raw.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var n = ParseNumber(raw[prefix.Length..], type);
                return n is null ? null : Compare(member, n, op);
            }
            var eq = ParseNumber(raw, type);
            return eq is null ? null : Compare(member, eq, ExpressionType.Equal);
        }
        return null;
    }

    private static Expression? Rule(MemberExpression member, FilterRule rule)
    {
        var type = Underlying(member.Type);
        var op = rule.Operator.Trim().ToLowerInvariant();
        var v = rule.Value?.Trim();

        switch (op)
        {
            case "isempty":
                return type == typeof(string)
                    ? Expression.OrElse(Expression.Equal(member, Expression.Constant(null, member.Type)), Expression.Equal(member, Expression.Constant("")))
                    : IsNullable(member.Type) ? Expression.Equal(member, Expression.Constant(null, member.Type)) : Expression.Constant(false);
            case "isnotempty":
                return type == typeof(string)
                    ? Expression.AndAlso(Expression.NotEqual(member, Expression.Constant(null, member.Type)), Expression.NotEqual(member, Expression.Constant("")))
                    : IsNullable(member.Type) ? Expression.NotEqual(member, Expression.Constant(null, member.Type)) : Expression.Constant(true);
            case "istrue": return type == typeof(bool) ? Compare(member, true, ExpressionType.Equal) : null;
            case "isfalse": return type == typeof(bool) ? Compare(member, false, ExpressionType.Equal) : null;
        }
        if (string.IsNullOrEmpty(v)) return null;

        if (type == typeof(string)) return StringOp(member, op, v);

        if (type.IsEnum)
        {
            var values = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => Enum.TryParse(type, s, true, out var e) ? e : null).Where(e => e is not null).ToList();
            if (values.Count == 0) return null;
            var any = AnyEqual(member, values!);
            return op is "notequals" or "notin" ? Expression.Not(any) : any;
        }

        if (type == typeof(bool))
            return bool.TryParse(v, out var b) ? Compare(member, b, op == "notequals" ? ExpressionType.NotEqual : ExpressionType.Equal) : null;

        if (type == typeof(DateTime))
        {
            var d1 = ParseDate(v);
            if (d1 is null) return null;
            return op switch
            {
                "equals" or "on" => DateRange(member, d1, d1),
                "notequals" => Expression.Not(DateRange(member, d1, d1)!),
                "before" or "lt" => Compare(member, d1.Value.Date, ExpressionType.LessThan),
                "after" or "gt" => Compare(member, d1.Value.Date.AddDays(1), ExpressionType.GreaterThanOrEqual),
                "gte" => Compare(member, d1.Value.Date, ExpressionType.GreaterThanOrEqual),
                "lte" => Compare(member, d1.Value.Date.AddDays(1), ExpressionType.LessThan),
                "between" => DateRange(member, d1, ParseDate(rule.Value2) ?? d1),
                _ => null
            };
        }

        if (IsNumeric(type))
        {
            var n = ParseNumber(v, type);
            if (n is null) return null;
            return op switch
            {
                "equals" => Compare(member, n, ExpressionType.Equal),
                "notequals" => Compare(member, n, ExpressionType.NotEqual),
                "gt" => Compare(member, n, ExpressionType.GreaterThan),
                "gte" => Compare(member, n, ExpressionType.GreaterThanOrEqual),
                "lt" => Compare(member, n, ExpressionType.LessThan),
                "lte" => Compare(member, n, ExpressionType.LessThanOrEqual),
                "between" => Range(member, n, ParseNumber(rule.Value2, type)),
                _ => null
            };
        }
        return null;
    }

    private static Expression StringOp(MemberExpression member, string op, string value)
    {
        var notNull = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
        // SQLite's instr() and LINQ-to-objects are both case-sensitive, so lower both sides. instr() also
        // treats % and _ literally, which is what a search box should do. SQLite's lower() covers ASCII only.
        Expression target = Expression.Call(member, typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!);
        value = value.ToLowerInvariant();
        var arg = Captured(value, typeof(string));
        Expression Call(string method) => Expression.Call(target, typeof(string).GetMethod(method, [typeof(string)])!, arg);

        Expression body = op switch
        {
            "equals" => Expression.Equal(target, arg),
            "notequals" => Expression.NotEqual(target, arg),
            "startswith" => Call(nameof(string.StartsWith)),
            "endswith" => Call(nameof(string.EndsWith)),
            "notcontains" => Expression.Not(Call(nameof(string.Contains))),
            _ => Call(nameof(string.Contains)),
        };
        return op is "notcontains" or "notequals"
            ? Expression.OrElse(Expression.Equal(member, Expression.Constant(null, typeof(string))), body)
            : Expression.AndAlso(notNull, body);
    }

    private static Expression? DateRange(MemberExpression member, DateTime? from, DateTime? to)
    {
        if (from is null && to is null) return null;
        Expression? body = null;
        if (from is not null) body = Compare(member, from.Value.Date, ExpressionType.GreaterThanOrEqual);
        if (to is not null)
        {
            var upper = Compare(member, to.Value.Date.AddDays(1), ExpressionType.LessThan);
            body = body is null ? upper : Expression.AndAlso(body, upper);
        }
        return body;
    }

    private static Expression? Range(MemberExpression member, object? from, object? to)
    {
        Expression? body = null;
        if (from is not null) body = Compare(member, from, ExpressionType.GreaterThanOrEqual);
        if (to is not null)
        {
            var upper = Compare(member, to, ExpressionType.LessThanOrEqual);
            body = body is null ? upper : Expression.AndAlso(body, upper);
        }
        return body;
    }

    private static Expression AnyEqual(MemberExpression member, List<object> values)
    {
        Expression? any = null;
        foreach (var value in values)
        {
            var eq = Compare(member, value, ExpressionType.Equal);
            any = any is null ? eq : Expression.OrElse(any, eq);
        }
        return any!;
    }

    private static Expression Compare(MemberExpression member, object value, ExpressionType op)
    {
        var right = Captured(value, member.Type);
        return Expression.MakeBinary(op, member, right);
    }

    /// <summary>Wraps a value in a closure field so EF Core parameterizes it instead of inlining a literal.</summary>
    private static Expression Captured(object value, Type targetType)
    {
        var holderType = typeof(ValueHolder<>).MakeGenericType(targetType);
        var holder = Activator.CreateInstance(holderType)!;
        var underlying = Underlying(targetType);
        var converted = underlying.IsEnum ? Enum.ToObject(underlying, value) : Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
        holderType.GetField(nameof(ValueHolder<int>.Value))!.SetValue(holder, converted);
        return Expression.Field(Expression.Constant(holder), nameof(ValueHolder<int>.Value));
    }

    private sealed class ValueHolder<TValue>
    {
#pragma warning disable CS0649 // assigned through reflection
        public TValue? Value;
#pragma warning restore CS0649
    }

    private static DateTime? ParseDate(string? s) =>
        DateTime.TryParse(s?.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static object? ParseNumber(string? s, Type type)
    {
        if (string.IsNullOrWhiteSpace(s) || !decimal.TryParse(s.Trim().Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return null;
        try { return Convert.ChangeType(d, type, CultureInfo.InvariantCulture); }
        catch (OverflowException) { return null; }
    }

    private static Type Underlying(Type t) => Nullable.GetUnderlyingType(t) ?? t;
    private static bool IsNullable(Type t) => !t.IsValueType || Nullable.GetUnderlyingType(t) is not null;

    private static bool IsNumeric(Type t) =>
        t == typeof(int) || t == typeof(long) || t == typeof(decimal) || t == typeof(double) || t == typeof(short) || t == typeof(float);

    private static bool IsSupported(Type t)
    {
        var u = Underlying(t);
        return u == typeof(string) || u == typeof(bool) || u == typeof(DateTime) || u.IsEnum || IsNumeric(u);
    }

    private static string ToCamel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
