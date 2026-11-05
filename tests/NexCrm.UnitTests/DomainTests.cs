using NexCrm.Domain.Common;
using NexCrm.Domain.Entities;

namespace NexCrm.UnitTests;

public sealed class DealTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 10, 0, 0);

    [Theory]
    [InlineData(DealStage.Prospecting, 10)]
    [InlineData(DealStage.Qualification, 25)]
    [InlineData(DealStage.Proposal, 50)]
    [InlineData(DealStage.Negotiation, 75)]
    [InlineData(DealStage.Won, 100)]
    [InlineData(DealStage.Lost, 0)]
    public void Moving_sets_the_stage_default_probability(DealStage stage, int expected)
    {
        var deal = new Deal();
        deal.MoveTo(stage, Now);
        Assert.Equal(expected, deal.Probability);
    }

    [Fact]
    public void Closing_stamps_the_close_date_and_reopening_clears_it()
    {
        var deal = new Deal();

        deal.MoveTo(DealStage.Won, Now);
        Assert.Equal(Now, deal.ClosedAt);

        deal.MoveTo(DealStage.Negotiation, Now.AddDays(1));
        Assert.Null(deal.ClosedAt);
    }

    [Fact]
    public void A_closed_deal_ignores_a_forecast_override()
    {
        var deal = new Deal();
        deal.MoveTo(DealStage.Lost, Now);

        deal.Forecast(60);

        Assert.Equal(0, deal.Probability);
    }

    [Fact]
    public void Forecast_is_clamped_to_a_percentage()
    {
        var deal = new Deal();
        deal.Forecast(140);
        Assert.Equal(100, deal.Probability);
    }

    [Fact]
    public void Close_only_accepts_won_or_lost()
    {
        Assert.Throws<DomainException>(() => new Deal().Close(DealStage.Proposal, Now));
    }
}

public sealed class LeadTests
{
    [Fact]
    public void Converting_twice_is_refused()
    {
        var lead = new Lead { Status = LeadStatus.Qualified };
        lead.MarkConverted(dealId: 7, DateTime.Now);

        Assert.True(lead.IsConverted);
        Assert.Equal(7, lead.ConvertedDealId);
        Assert.Throws<DomainException>(() => lead.MarkConverted(8, DateTime.Now));
    }

    [Theory]
    [InlineData(LeadStatus.New)]
    [InlineData(LeadStatus.Contacted)]
    [InlineData(LeadStatus.Unqualified)]
    public void Only_a_qualified_lead_can_be_converted(LeadStatus status)
    {
        var lead = new Lead { Status = status };
        Assert.Throws<DomainException>(lead.EnsureCanConvert);
    }

    [Theory]
    [InlineData("Sara Abbasi", "Sara", "Abbasi")]
    [InlineData("Mary Ann van der Berg", "Mary", "Ann van der Berg")]
    [InlineData("Cher", "Cher", "-")]
    public void Splits_a_full_name_for_the_new_contact(string name, string first, string last)
    {
        Assert.Equal((first, last), new Lead { Name = name }.SplitName());
    }
}

public sealed class TaskTests
{
    [Fact]
    public void Completing_a_task_records_the_touch_on_its_contact()
    {
        var at = new DateTime(2026, 11, 5, 15, 30, 0);
        var contact = new Contact();
        var task = new CrmTask { Contact = contact };

        task.Complete(at);

        Assert.True(task.IsDone);
        Assert.Equal(at, task.CompletedAt);
        Assert.Equal(at, contact.LastContactedAt);
    }

    [Fact]
    public void Reopening_clears_the_completion()
    {
        var task = new CrmTask();
        task.Complete(DateTime.Now);

        task.Reopen();

        Assert.False(task.IsDone);
        Assert.Null(task.CompletedAt);
    }

    [Fact]
    public void Archive_marks_the_record_and_the_time()
    {
        var at = DateTime.Now;
        var company = new Company();

        company.Archive(at);

        Assert.True(company.IsDeleted);
        Assert.Equal(at, company.DeletedAt);
    }
}
