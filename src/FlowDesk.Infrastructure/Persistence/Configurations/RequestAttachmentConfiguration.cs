using FlowDesk.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowDesk.Infrastructure.Persistence.Configurations;

public sealed class RequestAttachmentConfiguration
    : IEntityTypeConfiguration<RequestAttachment>
{
    public void Configure(EntityTypeBuilder<RequestAttachment> entity)
    {
        entity.ToTable("RequestAttachments", table =>
        {
            table.HasCheckConstraint(
                "CK_RequestAttachments_ScanStatus",
                "[ScanStatus] IN (0, 1, 2, 3)");
            table.HasCheckConstraint(
                "CK_RequestAttachments_DeletionAudit",
                "([DeletedAtUtc] IS NULL AND [DeletedByUserId] IS NULL AND " +
                "[StorageCleanupPending] = 0) OR " +
                "([DeletedAtUtc] IS NOT NULL AND [DeletedByUserId] IS NOT NULL)");
            table.HasCheckConstraint(
                "CK_RequestAttachments_ScanAttempts",
                "[ScanAttempts] >= 0");
        });

        entity.HasKey(x => x.Id);
        entity.Property(x => x.OriginalFileName)
            .HasMaxLength(255)
            .IsRequired();
        entity.Property(x => x.Extension)
            .HasMaxLength(10)
            .IsRequired();
        entity.Property(x => x.MediaType)
            .HasMaxLength(100)
            .IsRequired();
        entity.Property(x => x.SizeBytes).IsRequired();
        entity.Property(x => x.Sha256)
            .HasMaxLength(64)
            .IsUnicode(false)
            .IsFixedLength()
            .IsRequired();
        entity.Property(x => x.UploadedAtUtc)
            .HasColumnType("datetime2")
            .IsRequired();
        entity.Property(x => x.ScanStatus)
            .HasConversion<int>()
            .IsRequired();
        entity.Property(x => x.ScannedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.ScannerName).HasMaxLength(100);
        entity.Property(x => x.ScanFailureCode).HasMaxLength(40);
        entity.Property(x => x.ScanAttempts).IsRequired();
        entity.Property(x => x.DeletedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.LastScanAttemptAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.NextScanAttemptAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.ScanLeaseExpiresAtUtc).HasColumnType("datetime2");

        entity.HasIndex(x => x.StorageKey).IsUnique();
        entity.HasIndex(x => x.ClientUploadId).IsUnique();
        entity.HasIndex(x => new
        {
            x.RequestId,
            x.DeletedAtUtc,
            x.UploadedAtUtc
        });
        entity.HasIndex(x => x.UploadedByUserId);
        entity.HasIndex(x => new { x.StorageCleanupPending, x.DeletedAtUtc });
        entity.HasIndex(x => new
        {
            x.ScanStatus,
            x.NextScanAttemptAtUtc,
            x.ScanLeaseExpiresAtUtc,
            x.Id
        });

        entity.HasOne(x => x.Request)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.RequestId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne(x => x.UploadedByUser)
            .WithMany()
            .HasForeignKey(x => x.UploadedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
        entity.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.DeletedByUserId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
