using Microsoft.AspNetCore.Mvc;
using NexCrm.Application.Features.Companies;
using NexCrm.Application.Features.Contacts;
using NexCrm.Application.Features.Deals;
using NexCrm.Application.Features.Leads;
using NexCrm.Application.Features.Tasks;

namespace NexCrm.Api.Controllers;

[Route("api/companies")]
public sealed class CompaniesController(CompanyService service)
    : CrudController<CompanyService, CompanyDto, SaveCompanyRequest>(service)
{
    protected override string DefaultSort => nameof(CompanyDto.WonRevenue);
    protected override string ExportName => "companies";
}

[Route("api/contacts")]
public sealed class ContactsController(ContactService service)
    : CrudController<ContactService, ContactDto, SaveContactRequest>(service)
{
    protected override string DefaultSort => nameof(ContactDto.CreatedAt);
    protected override string ExportName => "contacts";
}

[Route("api/leads")]
public sealed class LeadsController(LeadService service)
    : CrudController<LeadService, LeadDto, SaveLeadRequest>(service)
{
    protected override string DefaultSort => nameof(LeadDto.CreatedAt);
    protected override string ExportName => "leads";

    [HttpPost("{id:int}/convert")]
    public Task<ConvertLeadResult> Convert(int id) => Service.ConvertAsync(id, Aborted);
}

[Route("api/deals")]
public sealed class DealsController(DealService service)
    : CrudController<DealService, DealDto, SaveDealRequest>(service)
{
    protected override string DefaultSort => nameof(DealDto.ExpectedCloseDate);
    protected override string ExportName => "deals";

    [HttpGet("board")]
    public Task<IReadOnlyList<BoardColumnDto>> Board([FromQuery] int? ownerId) => Service.BoardAsync(ownerId, Aborted);

    [HttpPatch("{id:int}/stage")]
    public Task<DealDto> Move(int id, MoveDealRequest request) => Service.MoveAsync(id, request.Stage!.Value, Aborted);
}

[Route("api/tasks")]
public sealed class TasksController(TaskService service)
    : CrudController<TaskService, TaskDto, SaveTaskRequest>(service)
{
    protected override string DefaultSort => nameof(TaskDto.DueAt);
    protected override bool DefaultSortDesc => false;
    protected override string ExportName => "tasks";

    [HttpPatch("{id:int}/done")]
    public Task<TaskDto> SetDone(int id, CompleteTaskRequest request) => Service.SetDoneAsync(id, request.Done, Aborted);
}
