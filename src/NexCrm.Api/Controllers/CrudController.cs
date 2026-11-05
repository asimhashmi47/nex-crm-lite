using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NexCrm.Application.Common;
using NexCrm.Application.Common.Querying;

namespace NexCrm.Api.Controllers;

[ApiController]
[Authorize]
[Produces("application/json")]
public abstract class ApiController : ControllerBase
{
    protected CancellationToken Aborted => HttpContext.RequestAborted;
}

/// <summary>
/// Every grid in the app speaks the same six endpoints. A feature controller only names its service,
/// its default sort and its export file, then adds whatever extra actions it needs.
/// </summary>
public abstract class CrudController<TService, TDto, TRequest>(TService service) : ApiController
    where TService : ICrudService<TDto, TRequest>
    where TDto : class
{
    protected TService Service { get; } = service;

    protected abstract string DefaultSort { get; }
    protected virtual bool DefaultSortDesc => true;
    protected abstract string ExportName { get; }

    [HttpPost("query")]
    public Task<PagedResult<TDto>> Query(TableQuery query) =>
        Service.Query().ToPagedAsync(query, DefaultSort, DefaultSortDesc, Aborted);

    [HttpPost("export"), EnableRateLimiting(Program.ExportRateLimit)]
    public async Task<IActionResult> Export(ExportRequest request, [FromServices] IExportService exporter, [FromServices] AuditTrail audit)
    {
        var rows = await Service.Query().ToExportListAsync(request.Query, DefaultSort, DefaultSortDesc, Aborted);
        var file = exporter.Export(rows, request, ExportName);
        var entity = char.ToUpperInvariant(ExportName[0]) + ExportName[1..];
        await audit.WriteAsync($"{entity}.Exported", entity, null, $"{request.Format} · {rows.Count} rows · {request.Columns.Count} columns", ct: Aborted);
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpGet("{id:int}")]
    public Task<TDto> Get(int id) => Service.GetAsync(id, Aborted);

    [HttpPost]
    public async Task<ActionResult<TDto>> Create(TRequest request)
    {
        var created = await Service.CreateAsync(request, Aborted);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut("{id:int}")]
    public Task<TDto> Update(int id, TRequest request) => Service.UpdateAsync(id, request, Aborted);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await Service.ArchiveAsync(id, Aborted);
        return NoContent();
    }
}
