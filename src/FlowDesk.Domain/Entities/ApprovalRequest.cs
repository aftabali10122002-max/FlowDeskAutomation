using FlowDesk.Domain.Enums;

namespace FlowDesk.Domain.Entities;

public class ApprovalRequest 
{
    public int Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public decimal? Amount { get; private set; }

    public int? RequestTypeId { get; private set; }
    public RequestType? RequestType { get; private set; }

    public MaterialPurchaseRequestDetails? MaterialPurchaseDetails
        { get; private set; }
    public LeaveRequestDetails? LeaveDetails { get; private set; }
    public ExpenseReimbursementRequestDetails? ExpenseReimbursementDetails
        { get; private set; }
    public ItAccessRequestDetails? ItAccessDetails { get; private set; }
    public TravelRequestDetails? TravelDetails { get; private set; }
    public WorkFromHomeRequestDetails? WorkFromHomeDetails { get; private set; }

    public RequestPriority? Priority { get; private set; }

    public RequestStatus Status { get; private set; } = RequestStatus.Pending;

    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public int? CreatedByUserId { get; private set; }

    public User? CreatedByUser { get; private set; }

    public int? DepartmentId { get; private set; }

    public Department? Department { get; private set; }

    public ICollection<ApprovalAssignment> Assignments { get; private set; }
        = new List<ApprovalAssignment>();

    public ICollection<RequestAttachment> Attachments { get; private set; }
        = new List<RequestAttachment>();

    public void AddAttachment(RequestAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (Id != 0 && attachment.RequestId != Id)
        {
            throw new ArgumentException(
                "The attachment belongs to a different request.",
                nameof(attachment));
        }

        if (Id == 0 && !ReferenceEquals(attachment.Request, this))
        {
            throw new ArgumentException(
                "A new request can only add attachments created for itself.",
                nameof(attachment));
        }

        if (Attachments.Any(x => x.ClientUploadId == attachment.ClientUploadId))
        {
            throw new InvalidOperationException(
                "An attachment upload ID has already been used for this request.");
        }

        Attachments.Add(attachment);
    }

    public ICollection<LeaveRequestPolicyDay> LeavePolicyDays
        { get; private set; } = new List<LeaveRequestPolicyDay>();

    public ApprovalWorkflowInstance? WorkflowInstance { get; private set; }

    private ApprovalRequest() { }

    public ApprovalRequest(
        string title,
        decimal amount,
        string? description = null)
        : this(title, amount, description, requireAmount: true)
    {
    }

    private ApprovalRequest(
        string title,
        decimal? amount,
        string? description,
        bool requireAmount)
    {
        Validate(title, amount, description, requireAmount);
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description)
            ? null
            : description.Trim();
        Amount = amount;
    }

    public ApprovalRequest(
        string title,
        decimal? amount,
        int createdByUserId,
        int? departmentId,
        string? description = null,
        bool requireAmount = true)
        : this(title, amount, description, requireAmount)
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

    public void SetMaterialPurchaseDetails(
        RequestType requestType,
        MaterialPurchaseRequestDetails details)
    {
        ConfigureType(
            requestType,
            RequestTypeCodes.MaterialPurchase,
            requiresAmount: true);
        details.AttachTo(this);
        MaterialPurchaseDetails = details;
    }

    public void SetLeaveDetails(
        RequestType requestType,
        LeaveRequestDetails details)
    {
        ConfigureType(
            requestType,
            RequestTypeCodes.Leave,
            requiresAmount: false);
        details.AttachTo(this);
        LeaveDetails = details;
    }

    public void SetExpenseReimbursementDetails(
        RequestType requestType,
        ExpenseReimbursementRequestDetails details)
    {
        ConfigureType(
            requestType,
            RequestTypeCodes.ExpenseReimbursement,
            requiresAmount: true);
        details.AttachTo(this);
        ExpenseReimbursementDetails = details;
    }

    public void SetItAccessDetails(
        RequestType requestType,
        ItAccessRequestDetails details)
    {
        ConfigureType(
            requestType,
            RequestTypeCodes.ItAccess,
            requiresAmount: false);
        details.AttachTo(this);
        ItAccessDetails = details;
    }

    public void SetTravelDetails(
        RequestType requestType,
        TravelRequestDetails details)
    {
        ConfigureType(
            requestType,
            RequestTypeCodes.Travel,
            requiresAmount: true);
        details.AttachTo(this);
        TravelDetails = details;
    }

    public void SetWorkFromHomeDetails(
        RequestType requestType,
        WorkFromHomeRequestDetails details)
    {
        ConfigureType(
            requestType,
            RequestTypeCodes.WorkFromHome,
            requiresAmount: false);
        details.AttachTo(this);
        WorkFromHomeDetails = details;
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

    private static void Validate(
        string title,
        decimal? amount,
        string? description,
        bool requireAmount)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200)
        {
            throw new ArgumentException(
                "Title is required and cannot exceed 200 characters.",
                nameof(title));
        }

        if (amount is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Amount must be greater than zero.");
        }

        if (requireAmount && !amount.HasValue)
        {
            throw new ArgumentException(
                "Amount is required for this request.",
                nameof(amount));
        }

        if (!requireAmount && amount.HasValue)
        {
            throw new ArgumentException(
                "Amount must be omitted for this request.",
                nameof(amount));
        }

        if (description?.Length > 2000)
        {
            throw new ArgumentException(
                "Description cannot exceed 2000 characters.",
                nameof(description));
        }
    }

    private void ConfigureType(
        RequestType requestType,
        string expectedCode,
        bool requiresAmount)
    {
        ArgumentNullException.ThrowIfNull(requestType);
        if (RequestTypeId.HasValue)
        {
            throw new InvalidOperationException(
                "A request type has already been assigned.");
        }

        if (!requestType.IsEnabled ||
            !string.Equals(
                requestType.Code,
                expectedCode,
                StringComparison.Ordinal) ||
            requestType.RequiresAmount != requiresAmount ||
            Amount.HasValue != requiresAmount)
        {
            throw new ArgumentException(
                "The request type is disabled, invalid, or incompatible with its amount.",
                nameof(requestType));
        }

        RequestType = requestType;
        RequestTypeId = requestType.Id;
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