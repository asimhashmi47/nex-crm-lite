using FluentValidation;
using NexCrm.Application.Features.Auth;
using NexCrm.Application.Features.Companies;
using NexCrm.Application.Features.Contacts;
using NexCrm.Application.Features.Deals;
using NexCrm.Application.Features.Leads;
using NexCrm.Application.Features.Tasks;
using NexCrm.Domain.Entities;

namespace NexCrm.UnitTests;

public sealed class ValidatorTests
{
    private static readonly DateTime Due = new(2026, 12, 1, 10, 0, 0);
    private static readonly FixedClock Clock = new(TestDb.Now);

    private static string[] Errors<T>(AbstractValidator<T> validator, T request) =>
        validator.Validate(request).Errors.Select(e => e.PropertyName).Distinct().ToArray();

    // Auth

    [Theory, Trait("AC", "AUTH-4")]
    [InlineData("", "secret", "Username")]
    [InlineData("admin", "", "Password")]
    public void Login_EmptyField_IsRejected(string username, string password, string field) =>
        Assert.Contains(field, Errors(new LoginValidator(), new LoginRequest(username, password)));

    [Fact, Trait("AC", "AUTH-4")]
    public void Login_OverLongFields_AreRejected()
    {
        var errors = Errors(new LoginValidator(), new LoginRequest(new string('a', 51), new string('p', 101)));
        Assert.Equal(["Username", "Password"], errors);
    }

    [Fact, Trait("AC", "AUTH-4")]
    public void Login_MaxLengthFields_AreAccepted() =>
        Assert.Empty(Errors(new LoginValidator(), new LoginRequest(new string('a', 50), new string('p', 100))));

    // Companies

    private static SaveCompanyRequest Company(string name = "Acme", string? phone = null, int employees = 10, decimal revenue = 0, int owner = 1) =>
        new(name, "Software", null, phone, "Karachi", "Pakistan", employees, revenue, owner);

    [Fact, Trait("AC", "CO-3")]
    public void Company_Valid_HasNoErrors() => Assert.Empty(Errors(new SaveCompanyValidator(), Company()));

    [Theory, Trait("AC", "CO-3")]
    [InlineData("", "Name")]
    [InlineData("   ", "Name")]
    public void Company_BlankName_IsRejected(string name, string field) =>
        Assert.Contains(field, Errors(new SaveCompanyValidator(), Company(name)));

    [Fact, Trait("AC", "CO-3")]
    public void Company_NameAtMaxLength_IsAccepted_AndOneOverIsRejected()
    {
        Assert.Empty(Errors(new SaveCompanyValidator(), Company(new string('x', 120))));
        Assert.Contains("Name", Errors(new SaveCompanyValidator(), Company(new string('x', 121))));
    }

    [Theory, Trait("AC", "CO-3")]
    [InlineData("+92 (21) 555-0101", true)]
    [InlineData("call me", false)]
    [InlineData("<script>", false)]
    public void Company_Phone_AllowsDigitsAndPunctuationOnly(string phone, bool valid) =>
        Assert.Equal(valid, !Errors(new SaveCompanyValidator(), Company(phone: phone)).Contains("Phone"));

    [Theory, Trait("AC", "CO-3")]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1_000_000, true)]
    [InlineData(1_000_001, false)]
    public void Company_EmployeeBoundaries(int employees, bool valid) =>
        Assert.Equal(valid, !Errors(new SaveCompanyValidator(), Company(employees: employees)).Contains("Employees"));

    [Fact, Trait("AC", "CO-3")]
    public void Company_NegativeRevenueOrMissingOwner_IsRejected()
    {
        var errors = Errors(new SaveCompanyValidator(), Company(revenue: -1, owner: 0));
        Assert.Contains("AnnualRevenue", errors);
        Assert.Contains("OwnerId", errors);
    }

    // Contacts

    private static SaveContactRequest Contact(string email = "zoe@acme.test", string first = "Zoë") =>
        new(first, "Ahmed", email, null, null, ContactStage.Lead, null, 1);

    [Theory, Trait("AC", "CT-2")]
    [InlineData("not-an-email")]
    [InlineData("")]
    public void Contact_InvalidEmail_IsRejected(string email) =>
        Assert.Contains("Email", Errors(new SaveContactValidator(), Contact(email)));

    [Theory, Trait("AC", "CT-3")]
    [InlineData("Zoë")]
    [InlineData("محمد")]
    [InlineData("李")]
    public void Contact_UnicodeNames_AreAccepted(string first) =>
        Assert.Empty(Errors(new SaveContactValidator(), Contact(first: first)));

    // Leads

    private static SaveLeadRequest Lead(int score = 50, decimal value = 0, LeadStatus status = LeadStatus.New) =>
        new("Bilal Rana", "Acme", "b@acme.test", null, LeadSource.Website, status, score, value, 1);

    [Theory, Trait("AC", "LD-1")]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Lead_ScoreBoundaries(int score, bool valid) =>
        Assert.Equal(valid, !Errors(new SaveLeadValidator(), Lead(score)).Contains("Score"));

    [Fact, Trait("AC", "LD-1")]
    public void Lead_NegativeValue_IsRejected() =>
        Assert.Contains("EstimatedValue", Errors(new SaveLeadValidator(), Lead(value: -0.01m)));

    [Fact, Trait("AC", "LD-2")]
    public void Lead_StatusConverted_CannotBeSaved() =>
        Assert.Contains("Status", Errors(new SaveLeadValidator(), Lead(status: LeadStatus.Converted)));

    // Deals

    private static SaveDealRequest Deal(decimal amount = 1000, DateTime? close = null, string title = "Renewal") =>
        new(title, amount, DealStage.Prospecting, 10, close ?? Due, 1, null, 1);

    [Theory, Trait("AC", "DL-1")]
    [InlineData(0)]
    [InlineData(-5)]
    public void Deal_NonPositiveAmount_IsRejected(decimal amount) =>
        Assert.Contains("Amount", Errors(new SaveDealValidator(Clock), Deal(amount)));

    [Fact, Trait("AC", "DL-1")]
    public void Deal_MissingCloseDate_IsRejected() =>
        Assert.Contains("ExpectedCloseDate", Errors(new SaveDealValidator(Clock), Deal(close: default(DateTime))));

    [Fact, Trait("AC", "DL-1")]
    public void Deal_TitleOverMaxLength_IsRejected() =>
        Assert.Contains("Title", Errors(new SaveDealValidator(Clock), Deal(title: new string('t', 151))));

    [Fact, Trait("AC", "DL-5")]
    public void MoveDeal_UnknownStage_IsRejected() =>
        Assert.Contains("Stage", Errors(new MoveDealValidator(), new MoveDealRequest((DealStage)99)));

    [Fact, Trait("AC", "DL-5")]
    public void MoveDeal_MissingStage_IsRejected() =>
        Assert.Contains("Stage", Errors(new MoveDealValidator(), new MoveDealRequest(null)));

    // Tasks

    private static SaveTaskRequest Task(string subject = "Call", DateTime? due = null, string? notes = null) =>
        new(subject, TaskType.Call, TaskPriority.Normal, due ?? Due, notes, null, null, 1);

    [Fact, Trait("AC", "TK-1")]
    public void Task_MissingDueDate_IsRejected() =>
        Assert.Contains("DueAt", Errors(new SaveTaskValidator(Clock), Task(due: default(DateTime))));

    [Fact, Trait("AC", "TK-1")]
    public void Task_EmptySubjectOrLongNotes_AreRejected()
    {
        var errors = Errors(new SaveTaskValidator(Clock), Task(subject: "", notes: new string('n', 1001)));
        Assert.Contains("Subject", errors);
        Assert.Contains("Notes", errors);
    }
}
