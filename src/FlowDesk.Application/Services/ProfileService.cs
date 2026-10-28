using FlowDesk.Application.DTOs;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Services;

public sealed class ProfileService(
    IUserRepository userRepository,
    IProfileImageStorage imageStorage)
{
    public async Task<UserProfileResponseDto?> GetMyProfileAsync(int userId)
    {
        var user = await userRepository.GetProfileByIdAsync(userId);
        return user is { IsActive: true } ? MapProfile(user) : null;
    }

    public async Task<UserProfileResponseDto?> UpdateMyProfileAsync(
        int userId,
        string fullName,
        string? phoneNumber)
    {
        var user = await userRepository.GetTrackedByIdAsync(userId);
        if (user is not { IsActive: true })
        {
            return null;
        }

        user.UpdateProfile(fullName, phoneNumber);
        await userRepository.SaveChangesAsync();
        return MapProfile(await userRepository.GetProfileByIdAsync(userId) ?? user);
    }

    public async Task UpdateMyProfileImageAsync(
        int userId,
        Stream image,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetTrackedByIdAsync(userId);
        if (user is not { IsActive: true })
        {
            throw new UnauthorizedAccessException(
                "An active account is required to update a profile image.");
        }

        var previousKey = user.ProfileImageStorageKey;
        var newKey = await imageStorage.SaveAsync(image, cancellationToken);
        try
        {
            user.SetProfileImageStorageKey(newKey);
            await userRepository.SaveChangesAsync();
        }
        catch (Exception saveException)
        {
            try
            {
                await imageStorage.DeleteAsync(newKey, cancellationToken);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "The profile image could not be saved and its temporary file could not be removed.",
                    saveException,
                    cleanupException);
            }

            throw;
        }

        if (previousKey is not null)
        {
            await imageStorage.DeleteAsync(previousKey, cancellationToken);
        }
    }

    public async Task RemoveMyProfileImageAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetTrackedByIdAsync(userId);
        if (user is not { IsActive: true })
        {
            throw new UnauthorizedAccessException(
                "An active account is required to update a profile image.");
        }

        var previousKey = user.ProfileImageStorageKey;
        if (previousKey is null)
        {
            return;
        }

        user.SetProfileImageStorageKey(null);
        await userRepository.SaveChangesAsync();
        await imageStorage.DeleteAsync(previousKey, cancellationToken);
    }

    public async Task<Stream?> GetProfileImageAsync(
        int requesterUserId,
        int targetUserId,
        CancellationToken cancellationToken)
    {
        var requester = await userRepository.GetByIdAsync(requesterUserId);
        if (requester is not { IsActive: true } ||
            (requester.Id != targetUserId &&
             requester.Role != UserRoles.Admin))
        {
            throw new UnauthorizedAccessException(
                "You are not authorized to view this profile image.");
        }

        var target = await userRepository.GetByIdAsync(targetUserId);
        if (target?.ProfileImageStorageKey is not string storageKey)
        {
            return null;
        }

        return await imageStorage.OpenReadAsync(
            storageKey,
            cancellationToken);
    }

    private static UserProfileResponseDto MapProfile(User user)
    {
        return new UserProfileResponseDto
        {
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role,
            DepartmentId = user.DepartmentId,
            DepartmentName = user.Department?.Name,
            PhoneNumber = user.PhoneNumber,
            HasProfileImage = user.ProfileImageStorageKey is not null,
            ProfileImageUrl = $"/api/profile/users/{user.Id}/photo"
        };
    }
}
