using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;

namespace NexCrm.Api.Infrastructure;

/// <summary>
/// Makes a POST safe to retry: the first response for a user's <c>Idempotency-Key</c> is kept for a day and replayed
/// for the same key. Reusing a key with a different body is refused with 422, as the IETF draft suggests.
/// </summary>
public sealed class IdempotencyFilter(IMemoryCache cache) : IAsyncActionFilter
{
    public const string Header = "Idempotency-Key";
    private const int MaxKeyLength = 100;
    private static readonly TimeSpan Keep = TimeSpan.FromHours(24);

    // A fixed set of locks: same-key requests queue behind each other, unrelated keys rarely share one.
    private static readonly SemaphoreSlim[] Stripes = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private sealed record Stored(string BodyHash, int StatusCode, object? Value);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (!HttpMethods.IsPost(http.Request.Method) || !http.Request.Headers.TryGetValue(Header, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            await next();
            return;
        }

        var key = raw.ToString();
        if (key.Length > MaxKeyLength)
        {
            context.Result = Problem(StatusCodes.Status400BadRequest, $"{Header} can be at most {MaxKeyLength} characters.");
            return;
        }

        var user = http.User.FindFirst("sub")?.Value ?? "anonymous";
        var cacheKey = $"idem:{user}:{http.Request.Path}:{key}";
        var bodyHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(context.ActionArguments)));
        var stripe = Stripes[(uint)cacheKey.GetHashCode() % Stripes.Length];

        await stripe.WaitAsync(http.RequestAborted);
        try
        {
            if (cache.TryGetValue(cacheKey, out Stored? stored) && stored is not null)
            {
                context.Result = stored.BodyHash == bodyHash
                    ? new ObjectResult(stored.Value) { StatusCode = stored.StatusCode }
                    : Problem(StatusCodes.Status422UnprocessableEntity, $"This {Header} was already used for a different request.");
                if (stored.BodyHash == bodyHash) http.Response.Headers["Idempotent-Replayed"] = "true";
                return;
            }

            var executed = await next();
            if (executed.Exception is null && executed.Result is ObjectResult { StatusCode: >= 200 and < 300 } ok)
                cache.Set(cacheKey, new Stored(bodyHash, ok.StatusCode ?? 200, ok.Value), Keep);
        }
        finally
        {
            stripe.Release();
        }
    }

    private static ObjectResult Problem(int status, string title) =>
        new(new ProblemDetails { Status = status, Title = title }) { StatusCode = status, ContentTypes = { "application/problem+json" } };
}

/// <summary>
/// Refuses bodies over the limit from their Content-Length, before anything reads them. Kestrel's own
/// <c>MaxRequestBodySize</c> covers chunked uploads; this also works where Kestrel is not the server (tests, IIS).
/// </summary>
public sealed class RequestSizeGuard(RequestDelegate next, long limit)
{
    public async Task InvokeAsync(HttpContext http)
    {
        if (http.Request.ContentLength > limit)
        {
            http.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            // The body is left unread, so this connection can't be reused; tell the client instead of resetting it.
            http.Response.Headers.Connection = "close";
            await http.Response.WriteAsJsonAsync(new ProblemDetails { Status = 413, Title = $"Requests can be at most {limit / 1024} KB." }, options: null, contentType: "application/problem+json");
            return;
        }
        await next(http);
    }
}

/// <summary>
/// Development needs a signing key but none should ever be committed. The first run creates a random one in
/// App_Data (git-ignored) and reuses it, so local tokens survive restarts and nobody else knows the key.
/// </summary>
public static class DevSigningKey
{
    public static string LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (Encoding.UTF8.GetByteCount(existing) >= 32) return existing;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        File.WriteAllText(path, key);
        return key;
    }
}

public static class LogFilters
{
    /// <summary>SQLITE_CONSTRAINT_UNIQUE anywhere in the exception chain.</summary>
    public static bool IsUniqueViolation(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is Microsoft.Data.Sqlite.SqliteException { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 }) return true;
        return false;
    }
}
