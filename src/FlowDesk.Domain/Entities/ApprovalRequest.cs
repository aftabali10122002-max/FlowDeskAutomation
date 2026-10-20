using FlowDesk.Domain.Enums;

namespace FlowDesk.Domain.Entities;

public class ApprovalRequest 
{
    public int Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }

    public RequestPriority? Priority { get; private set; }

    public RequestStatus Status { get; private set; } = RequestStatus.Pending;

    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public int? CreatedByUserId { get; private set; }

    public User? CreatedByUser { get; private set; }

    public int? DepartmentId { get; private set; }

    public Department? Department { get; private set; }

    public ICollection<ApprovalAssignment> Assignments { get; private set; }
        = new List<ApprovalAssignment>();

    public ApprovalWorkflowInstance? WorkflowInstance { get; private set; }

    private ApprovalRequest() { }

    public ApprovalRequest(string title, decimal amount)
    {
        Validate(title, amount);
        Title = title.Trim();
        Amount = amount;
    }

    public ApprovalRequest(
        string title,
        decimal amount,
        int createdByUserId,
        int? departmentId)
        : this(title, amount)
    {
        if (createdByUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(createdByUserId));
        }

        if (departmentId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(departmentId));
        }

        CreatedByUserId = createdByUserId;
        DepartmentId = departmentId;
    }

    public void Approve()
    {
        EnsurePending();
        Status = RequestStatus.Approved;
    }

    public void Reject()
    {
        EnsurePending();
        Status = RequestStatus.Rejected;
    }

    public void Cancel()
    {
        EnsurePending();
        Status = RequestStatus.Cancelled;
    }

    public void SetPriority(RequestPriority priority)
    {
        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        Priority = priority;
    }

    private static void Validate(string title, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title cannot be empty.", nameof(title));
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Amount must be greater than zero.");
        }
    }

    private void EnsurePending()
    {
        if (Status != RequestStatus.Pending)
        {
            throw new InvalidOperationException(
                "Only pending requests can be changed.");
        }
    }
}