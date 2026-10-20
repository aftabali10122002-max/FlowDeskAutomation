namespace FlowDesk.Application.Interfaces;

public interface IWorkflowEmailSender
{
    Task SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken);
}
