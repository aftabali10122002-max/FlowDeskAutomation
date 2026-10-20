using FlowDesk.Domain.Enums;

namespace FlowDesk.Application.DTOs;
public class CreateRequestDto
{
    public string Title { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public RequestPriority? Priority { get; set; }
}