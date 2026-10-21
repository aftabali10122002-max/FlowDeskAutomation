using System.Net;
using System.Net.Mail;
using FlowDesk.Application.Interfaces;

namespace FlowDisk.Api.Notifications;

public sealed class SmtpWorkflowEmailSender(IConfiguration configuration)
    : IWorkflowEmailSender
{
    public async Task SendAsync(
        string recipient,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        var host = configuration["Email:Smtp:Host"];
        var from = configuration["Email:Smtp:From"];
        if (string.IsNullOrWhiteSpace(host) ||
            string.IsNullOrWhiteSpace(from))
        {
            throw new InvalidOperationException(
                "Email is enabled but SMTP host or sender address is not configured.");
        }

        var port = int.TryParse(configuration["Email:Smtp:Port"], out var parsedPort)
            ? parsedPort
            : 587;
        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                "Configured SMTP port is invalid.");
        }

        using var message = new MailMessage(from, recipient, subject, body)
        {
            IsBodyHtml = false
        };
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = !bool.TryParse(
                configuration["Email:Smtp:EnableSsl"],
                out var enableSsl) || enableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        var username = configuration["Email:Smtp:Username"];
        var password = configuration["Email:Smtp:Password"];
        if (!string.IsNullOrWhiteSpace(username))
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    "SMTP password must be configured when a username is supplied.");
            }

            client.Credentials = new NetworkCredential(username, password);
        }
        else if (!string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "SMTP username must be configured when a password is supplied.");
        }

        await client.SendMailAsync(message, cancellationToken);
    }
}
