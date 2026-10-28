using System.Data;
using FlowDesk.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace FlowDesk.Infrastructure.Persistence;

public sealed class DevelopmentPasswordResetService(
    AppDbContext context,
    IPasswordHasher passwordHasher)
{
    public async Task<int> ResetAsync(
        IReadOnlyCollection<string> accountEmails,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountEmails);
        if (accountEmails.Count == 0)
        {
            throw new ArgumentException(
                "Select at least one account email.",
                nameof(accountEmails));
        }

        if (string.IsNullOrWhiteSpace(newPassword) ||
            newPassword.Length < 12)
        {
            throw new ArgumentException(
                "The new development password must be at least 12 characters.",
                nameof(newPassword));
        }

        var emails = accountEmails
            .Select(email =>
            {
                if (string.IsNullOrWhiteSpace(email))
                {
                    throw new ArgumentException(
                        "Account emails cannot be empty.",
                        nameof(accountEmails));
                }

                return email.Trim().ToLowerInvariant();
            })
            .ToArray();
        if (emails.Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            emails.Length)
        {
            throw new ArgumentException(
                "Account emails must be unique.",
                nameof(accountEmails));
        }

        await using var transaction = await context.Database
            .BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var users = await context.Users
            .Where(user => emails.Contains(user.Email))
            .ToListAsync(cancellationToken);
        var foundEmails = users
            .Select(user => user.Email)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingEmails = emails
            .Where(email => !foundEmails.Contains(email))
            .ToArray();
        if (missingEmails.Length > 0)
        {
            throw new InvalidOperationException(
                $"No account exists for selected email(s): {string.Join(", ", missingEmails)}. No password hashes were changed.");
        }

        foreach (var user in users)
        {
            user.ChangePasswordHash(passwordHasher.Hash(newPassword));
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return users.Count;
    }
}
