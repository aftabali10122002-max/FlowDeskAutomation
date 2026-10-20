using FlowDesk.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<ApprovalRequest> ApprovalRequests =>
        Set<ApprovalRequest>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<ApprovalAssignment> ApprovalAssignments =>
        Set<ApprovalAssignment>();

    public DbSet<ApprovalWorkflowRule> ApprovalWorkflowRules =>
        Set<ApprovalWorkflowRule>();

    public DbSet<ApprovalWorkflowInstance> ApprovalWorkflowInstances =>
        Set<ApprovalWorkflowInstance>();

    public DbSet<ApprovalWorkflowStage> ApprovalWorkflowStages =>
        Set<ApprovalWorkflowStage>();

    public DbSet<ApprovalDecisionHistory> ApprovalDecisionHistory =>
        Set<ApprovalDecisionHistory>();

    public DbSet<ApprovalWorkflowEscalation> ApprovalWorkflowEscalations =>
        Set<ApprovalWorkflowEscalation>();

    public DbSet<WorkflowNotification> WorkflowNotifications =>
        Set<WorkflowNotification>();

    public DbSet<WorkflowEmailOutboxMessage> WorkflowEmailOutbox =>
        Set<WorkflowEmailOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApprovalRequest>(entity =>
        {
            entity.ToTable("ApprovalRequests", table =>
            {
                table.HasCheckConstraint(
                    "CK_ApprovalRequests_Status",
                    "[Status] IN (1, 2, 3, 4)");
                table.HasCheckConstraint(
                    "CK_ApprovalRequests_Priority",
                    "[Priority] IS NULL OR [Priority] IN (1, 2, 3, 4)");
            });
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Title)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Amount)
                .HasColumnType("decimal(18,2)")
                .IsRequired();

            entity.Property(x => x.Priority)
                .HasConversion<int?>();

            entity.Property(x => x.Status)
                .HasConversion<int>()
                .IsRequired();

            entity.Property(x => x.CreatedAtUtc)
                .HasColumnType("datetime2")
                .IsRequired();

            entity.HasIndex(x => new { x.CreatedByUserId, x.CreatedAtUtc });
            entity.HasIndex(x => new
            {
                x.DepartmentId,
                x.Status,
                x.CreatedAtUtc
            });
            entity.HasIndex(x => new { x.Status, x.CreatedAtUtc });

            entity.HasOne(x => x.CreatedByUser)
                .WithMany(x => x.CreatedRequests)
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(x => x.Department)
                .WithMany(x => x.ApprovalRequests)
                .HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(x => x.WorkflowInstance)
                .WithOne(x => x.Request)
                .HasForeignKey<ApprovalWorkflowInstance>(x => x.RequestId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users", table =>
                table.HasCheckConstraint(
                    "CK_Users_Role",
                    "[Role] IN ('Employee', 'Manager', 'Admin')"));
            entity.HasKey(x => x.Id);

            entity.Property(x => x.FullName)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.Email)
                .HasMaxLength(150)
                .IsRequired();

            entity.HasIndex(x => x.Email)
                .IsUnique();

            entity.Property(x => x.PasswordHash)
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(x => x.Role)
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(x => x.IsActive)
                .HasDefaultValue(true)
                .IsRequired();

            entity.Property(x => x.CreatedAtUtc)
                .HasColumnType("datetime2")
                .IsRequired();

            entity.HasIndex(x => x.DepartmentId);

            entity.HasOne(x => x.Department)
                .WithMany(x => x.Users)
                .HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.ToTable("Departments");
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.Code)
                .HasMaxLength(20);

            entity.HasIndex(x => x.Name)
                .IsUnique();

            entity.HasIndex(x => x.Code)
                .IsUnique()
                .HasFilter("[Code] IS NOT NULL");

            entity.Property(x => x.IsActive)
                .IsRequired();

            entity.Property(x => x.CreatedAtUtc)
                .HasColumnType("datetime2")
                .IsRequired();
        });

        modelBuilder.Entity<ApprovalAssignment>(entity =>
        {
            entity.ToTable("ApprovalAssignments", table =>
            {
                table.HasCheckConstraint(
                    "CK_ApprovalAssignments_Status",
                    "[Status] IN (1, 2, 3, 4)");

                table.HasCheckConstraint(
                    "CK_ApprovalAssignments_DecisionTime",
                    "([Status] = 1 AND [DecidedAtUtc] IS NULL) OR " +
                    "([Status] IN (2, 3, 4) AND [DecidedAtUtc] IS NOT NULL)");
            });

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Status)
                .HasConversion<int>()
                .IsRequired();

            entity.Property(x => x.AssignedAtUtc)
                .HasColumnType("datetime2")
                .IsRequired();

            entity.Property(x => x.DecidedAtUtc)
                .HasColumnType("datetime2");

            entity.HasIndex(x => x.RequestId)
                .IsUnique()
                .HasFilter("[Status] = 1");

            entity.HasIndex(x => new
            {
                x.ApproverUserId,
                x.Status,
                x.AssignedAtUtc
            });

            entity.HasIndex(x => new { x.RequestId, x.AssignedAtUtc });

            entity.HasOne(x => x.Request)
                .WithMany(x => x.Assignments)
                .HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(x => x.ApproverUser)
                .WithMany(x => x.ApprovalAssignments)
                .HasForeignKey(x => x.ApproverUserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(x => x.WorkflowStage)
                .WithMany()
                .HasForeignKey(x => x.WorkflowStageId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasIndex(x => x.WorkflowStageId)
                .IsUnique()
                .HasFilter("[WorkflowStageId] IS NOT NULL");
        });

        modelBuilder.Entity<ApprovalWorkflowRule>(entity =>
        {
            entity.ToTable("ApprovalWorkflowRules", table =>
            {
                table.HasCheckConstraint(
                    "CK_ApprovalWorkflowRules_AmountRange",
                    "[MinimumAmount] >= 0 AND ([MaximumAmountExclusive] IS NULL OR [MaximumAmountExclusive] > [MinimumAmount])");
                table.HasCheckConstraint(
                    "CK_ApprovalWorkflowRules_Status",
                    "[Status] IN (1, 2, 3)");
                table.HasCheckConstraint(
                    "CK_ApprovalWorkflowRules_Priority",
                    "[Priority] IS NULL OR [Priority] IN (1, 2, 3, 4)");
            });

            entity.HasKey(x => x.Id);
            entity.Property(x => x.WorkflowKey).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.MinimumAmount).HasColumnType("decimal(18,2)");
            entity.Property(x => x.MaximumAmountExclusive)
                .HasColumnType("decimal(18,2)");
            entity.Property(x => x.Priority).HasConversion<int?>();
            entity.Property(x => x.Status).HasConversion<int>().IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.PublishedAtUtc).HasColumnType("datetime2");
            entity.HasIndex(x => new { x.WorkflowKey, x.Version }).IsUnique();
            entity.HasIndex(x => new
            {
                x.Status,
                x.DepartmentId,
                x.MinimumAmount,
                x.MaximumAmountExclusive,
                x.Priority
            });
            entity.HasOne(x => x.Department)
                .WithMany()
                .HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasMany(x => x.Stages)
                .WithOne(x => x.WorkflowRule)
                .HasForeignKey(x => x.WorkflowRuleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApprovalWorkflowRuleStage>(entity =>
        {
            entity.ToTable("ApprovalWorkflowRuleStages", table =>
            {
                table.HasCheckConstraint(
                    "CK_ApprovalWorkflowRuleStages_Sequence",
                    "[Sequence] > 0");
                table.HasCheckConstraint(
                    "CK_ApprovalWorkflowRuleStages_ApproverType",
                    "[ApproverType] IN (1, 2, 3)");
                table.HasCheckConstraint(
                    "CK_ApprovalWorkflowRuleStages_Deadline",
                    "[DeadlineMinutes] IS NULL OR [DeadlineMinutes] > 0");
                table.HasCheckConstraint(
                    "CK_ApprovalWorkflowRuleStages_Escalation",
                    "[EscalationAfterMinutes] IS NULL OR ([EscalationAfterMinutes] >= [DeadlineMinutes] AND [DeadlineMinutes] IS NOT NULL AND [EscalationApproverUserId] IS NOT NULL)");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ApproverType).HasConversion<int>().IsRequired();
            entity.HasIndex(x => new { x.WorkflowRuleId, x.Sequence }).IsUnique();
            entity.HasOne(x => x.ApproverDepartment)
                .WithMany()
                .HasForeignKey(x => x.ApproverDepartmentId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.SpecificApproverUser)
                .WithMany()
                .HasForeignKey(x => x.SpecificApproverUserId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.EscalationApproverUser)
                .WithMany()
                .HasForeignKey(x => x.EscalationApproverUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ApprovalWorkflowInstance>(entity =>
        {
            entity.ToTable("ApprovalWorkflowInstances", table =>
                table.HasCheckConstraint(
                    "CK_ApprovalWorkflowInstances_Status",
                    "[Status] IN (1, 2, 3, 4)"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RuleVersion).IsRequired();
            entity.Property(x => x.Status).HasConversion<int>().IsRequired();
            entity.Property(x => x.StartedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.CompletedAtUtc).HasColumnType("datetime2");
            entity.HasIndex(x => x.RequestId).IsUnique();
            entity.HasIndex(x => new { x.WorkflowRuleId, x.RuleVersion });
            entity.HasOne(x => x.WorkflowRule)
                .WithMany()
                .HasForeignKey(x => x.WorkflowRuleId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasMany(x => x.Stages)
                .WithOne(x => x.WorkflowInstance)
                .HasForeignKey(x => x.WorkflowInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApprovalWorkflowStage>(entity =>
        {
            entity.ToTable("ApprovalWorkflowStages", table =>
            {
                table.HasCheckConstraint(
                    "CK_ApprovalWorkflowStages_Status",
                    "[Status] IN (1, 2, 3, 4, 5)");
                table.HasCheckConstraint(
                    "CK_ApprovalWorkflowStages_Sequence",
                    "[Sequence] > 0");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<int>().IsRequired();
            entity.Property(x => x.ActivatedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.DueAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.DecidedAtUtc).HasColumnType("datetime2");
            entity.HasIndex(x => new { x.WorkflowInstanceId, x.Sequence })
                .IsUnique();
            entity.HasIndex(x => x.WorkflowInstanceId)
                .IsUnique()
                .HasFilter("[Status] = 2");
            entity.HasIndex(x => new { x.Status, x.DueAtUtc });
            entity.HasIndex(x => x.ApproverUserId);
            entity.HasOne(x => x.ApproverUser)
                .WithMany()
                .HasForeignKey(x => x.ApproverUserId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.EscalationApproverUser)
                .WithMany()
                .HasForeignKey(x => x.EscalationApproverUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ApprovalDecisionHistory>(entity =>
        {
            entity.ToTable("ApprovalDecisionHistory", table =>
                table.HasCheckConstraint(
                    "CK_ApprovalDecisionHistory_Decision",
                    "[Decision] IN (3, 4)"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Decision).HasConversion<int>().IsRequired();
            entity.Property(x => x.Comment).HasMaxLength(2000);
            entity.Property(x => x.DecidedAtUtc).HasColumnType("datetime2");
            entity.HasIndex(x => x.WorkflowStageId).IsUnique();
            entity.HasOne(x => x.WorkflowStage)
                .WithOne(x => x.DecisionHistory)
                .HasForeignKey<ApprovalDecisionHistory>(
                    x => x.WorkflowStageId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.ManagerUser)
                .WithMany()
                .HasForeignKey(x => x.ManagerUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ApprovalWorkflowEscalation>(entity =>
        {
            entity.ToTable("ApprovalWorkflowEscalations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EscalatedAtUtc).HasColumnType("datetime2");
            entity.HasIndex(x => new { x.WorkflowStageId, x.EscalationNumber })
                .IsUnique();
            entity.HasOne(x => x.WorkflowStage)
                .WithMany(x => x.Escalations)
                .HasForeignKey(x => x.WorkflowStageId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.RecipientUser)
                .WithMany()
                .HasForeignKey(x => x.RecipientUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<WorkflowNotification>(entity =>
        {
            entity.ToTable("WorkflowNotifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type)
                .HasConversion<string>()
                .HasMaxLength(40)
                .IsRequired();
            entity.Property(x => x.DeduplicationKey).HasMaxLength(160);
            entity.Property(x => x.Message).HasMaxLength(500).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.ReadAtUtc).HasColumnType("datetime2");
            entity.HasIndex(x => x.RecipientUserId);
            entity.HasIndex(x => x.RequestId);
            entity.HasIndex(x => x.DeduplicationKey)
                .IsUnique()
                .HasFilter("[DeduplicationKey] IS NOT NULL");
            entity.HasIndex(x => x.EscalationId)
                .IsUnique()
                .HasFilter("[EscalationId] IS NOT NULL");
            entity.HasOne(x => x.Request)
                .WithMany()
                .HasForeignKey(x => x.RequestId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.RecipientUser)
                .WithMany()
                .HasForeignKey(x => x.RecipientUserId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Escalation)
                .WithMany()
                .HasForeignKey(x => x.EscalationId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<WorkflowEmailOutboxMessage>(entity =>
        {
            entity.ToTable("WorkflowEmailOutbox");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.RecipientEmail)
                .HasMaxLength(150)
                .IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            entity.Property(x => x.DeduplicationKey)
                .HasMaxLength(160)
                .IsRequired();
            entity.Property(x => x.CreatedAtUtc)
                .HasColumnType("datetime2")
                .IsRequired();
            entity.Property(x => x.SentAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.LastAttemptAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.LeaseUntilUtc).HasColumnType("datetime2");
            entity.HasIndex(x => x.DeduplicationKey).IsUnique();
            entity.HasIndex(x => new { x.SentAtUtc, x.LeaseUntilUtc, x.CreatedAtUtc });
            entity.HasOne(x => x.RecipientUser)
                .WithMany()
                .HasForeignKey(x => x.RecipientUserId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Notification)
                .WithMany()
                .HasForeignKey(x => x.NotificationId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
