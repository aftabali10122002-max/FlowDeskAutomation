using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace FlowDesk.Infrastructure.Security;

public class PasswordHasherService : IPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password)
    {
        return _hasher.HashPassword(null!, password);
    }

    public bool Verify(string password, string passwordHash)
    {
        var result = _hasher.VerifyHashedPassword(
            null!,
            passwordHash,
            password
        );

        return result != PasswordVerificationResult.Failed;
    }
}