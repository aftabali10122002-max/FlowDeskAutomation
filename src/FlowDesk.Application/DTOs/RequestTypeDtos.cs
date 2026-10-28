namespace FlowDesk.Application.DTOs;

public sealed class RequestTypeResponseDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool RequiresAmount { get; set; }
    public bool IsEnabled { get; set; }
}

public sealed class SetRequestTypeEnabledDto
{
    public bool IsEnabled { get; set; }
}
