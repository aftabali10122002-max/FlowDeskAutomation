using FlowDesk.Application.DTOs;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Services;

public sealed class RequestTypeService(
    IRequestTypeRepository requestTypeRepository,
    IUserRepository userRepository)
{
    public async Task<List<RequestTypeResponseDto>> GetEnabledForUserAsync(
        int userId)
    {
        await RequireActiveUserAsync(userId);
        return (await requestTypeRepository.GetEnabledAsync())
            .Select(Map)
            .ToList();
    }

    public async Task<List<RequestTypeResponseDto>> GetAllForAdminAsync(
        int adminUserId)
    {
        await RequireRoleAsync(adminUserId, UserRoles.Admin);
        return (await requestTypeRepository.GetAllAsync())
            .Select(Map)
            .ToList();
    }

    public async Task<RequestTypeResponseDto?> SetEnabledAsync(
        int adminUserId,
        int requestTypeId,
        bool isEnabled)
    {
        await RequireRoleAsync(adminUserId, UserRoles.Admin);
        var requestType = await requestTypeRepository
            .GetTrackedByIdAsync(requestTypeId);
        if (requestType is null)
        {
            return null;
        }

        requestType.SetEnabled(isEnabled);
        await requestTypeRepository.SaveChangesAsync();
        return Map(requestType);
    }

    private async Task RequireRoleAsync(int userId, string role)
    {
        var user = await RequireActiveUserAsync(userId);
        if (!string.Equals(user.Role, role, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException(
                "The authenticated user is not authorized for this operation.");
        }
    }

    private async Task<User> RequireActiveUserAsync(int userId)
    {
        var user = userId > 0
            ? await userRepository.GetByIdAsync(userId)
            : null;
        if (user is not { IsActive: true })
        {
            throw new UnauthorizedAccessException(
                "An active account is required for this operation.");
        }

        return user;
    }

    private static RequestTypeResponseDto Map(RequestType requestType)
    {
        return new RequestTypeResponseDto
        {
            Id = requestType.Id,
            Code = requestType.Code,
            DisplayName = requestType.DisplayName,
            RequiresAmount = requestType.RequiresAmount,
            IsEnabled = requestType.IsEnabled
        };
    }
}
