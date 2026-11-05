using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexCrm.Domain.Common;
using NexCrm.Domain.Entities;

namespace NexCrm.Infrastructure.Persistence;

/// <summary>Shared mapping for every owned record: owner, archive flag and the index the filter needs.</summary>
internal abstract class OwnedEntityConfiguration<T> : IEntityTypeConfiguration<T> where T : OwnedEntity
{
    public void Configure(EntityTypeBuilder<T> b)
    {
        b.HasIndex(x => x.OwnerId);
        b.HasIndex(x => x.IsDeleted);
        b.HasOne(typeof(User), "Owner").WithMany().HasForeignKey(nameof(OwnedEntity.OwnerId)).OnDelete(DeleteBehavior.Restrict);
        ConfigureEntity(b);
    }

    protected abstract void ConfigureEntity(EntityTypeBuilder<T> b);
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.Property(x => x.Username).HasMaxLength(50);
        b.Property(x => x.FullName).HasMaxLength(100);
        b.Property(x => x.Email).HasMaxLength(160);
        b.HasIndex(x => x.Username).IsUnique();
        b.Ignore(x => x.IsManager);
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> b)
    {
        b.ToTable("AuditLog");
        b.Property(x => x.Username).HasMaxLength(50);
        b.Property(x => x.Action).HasMaxLength(60);
        b.Property(x => x.EntityType).HasMaxLength(40);
        b.Property(x => x.EntityId).HasMaxLength(40);
        b.Property(x => x.Summary).HasMaxLength(500);
        b.Property(x => x.IpAddress).HasMaxLength(45);
        b.HasIndex(x => x.At);
        b.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}

internal sealed class CompanyConfiguration : OwnedEntityConfiguration<Company>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Company> b)
    {
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Industry).HasMaxLength(60);
        b.Property(x => x.Website).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.City).HasMaxLength(80);
        b.Property(x => x.Country).HasMaxLength(80);
        b.Property(x => x.Name).HasField("_name");
        b.Property(x => x.NameKey).HasMaxLength(120);
        b.HasIndex(x => x.Name);
        // Archived companies free their name for reuse.
        b.HasIndex(x => x.NameKey).IsUnique().HasFilter("\"IsDeleted\" = 0");
    }
}

internal sealed class ContactConfiguration : OwnedEntityConfiguration<Contact>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Contact> b)
    {
        b.Property(x => x.FirstName).HasMaxLength(60);
        b.Property(x => x.LastName).HasMaxLength(60);
        b.Property(x => x.Email).HasMaxLength(160);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.JobTitle).HasMaxLength(100);
        b.HasIndex(x => x.Email).IsUnique().HasFilter("\"IsDeleted\" = 0");
        b.HasOne(x => x.Company).WithMany(c => c.Contacts).HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class LeadConfiguration : OwnedEntityConfiguration<Lead>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Lead> b)
    {
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.CompanyName).HasMaxLength(120);
        b.Property(x => x.Email).HasMaxLength(160);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Ignore(x => x.IsConverted);
        // Two people converting the same lead at once: the second save finds the status already changed.
        b.Property(x => x.Status).IsConcurrencyToken();
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.CreatedAt);
    }
}

internal sealed class DealConfiguration : OwnedEntityConfiguration<Deal>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Deal> b)
    {
        b.Property(x => x.Title).HasMaxLength(150);
        b.Ignore(x => x.IsClosed);
        b.HasIndex(x => x.Stage);
        b.HasIndex(x => x.ClosedAt);
        // Leaderboard and board: deals per owner and stage, won ones by close date.
        b.HasIndex(x => new { x.OwnerId, x.Stage, x.ClosedAt });
        b.HasOne(x => x.Company).WithMany(c => c.Deals).HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Contact).WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class TaskConfiguration : OwnedEntityConfiguration<CrmTask>
{
    protected override void ConfigureEntity(EntityTypeBuilder<CrmTask> b)
    {
        b.ToTable("Tasks");
        b.Property(x => x.Subject).HasMaxLength(150);
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasIndex(x => new { x.IsDone, x.DueAt });
        b.HasOne(x => x.Contact).WithMany().HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.Deal).WithMany().HasForeignKey(x => x.DealId).OnDelete(DeleteBehavior.SetNull);
    }
}
