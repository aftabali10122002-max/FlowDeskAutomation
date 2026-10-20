using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Interfaces;

public interface IJwtTokenService
{
    (string AccessToken, DateTime ExpiresAtUtc) GenerateToken(User user);
}