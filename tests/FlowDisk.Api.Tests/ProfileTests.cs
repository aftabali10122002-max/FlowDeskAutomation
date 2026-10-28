using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FlowDesk.Domain.Entities;
using FlowDesk.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class ProfileTests
{
    [Fact]
    public async Task ProfileEndpoints_ReturnAndUpdateOnlyPermittedFields()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var employee = await factory.AddUserAsync();
        int departmentId;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var department = new Department("Profile department");
            context.Departments.Add(department);
            await context.SaveChangesAsync();
            departmentId = department.Id;
            var user = await context.Users.SingleAsync(x => x.Id == employee.Id);
            user.ChangeDepartment(departmentId);
            await context.SaveChangesAsync();
        }

        using var client = factory.CreateAuthorizedClient(
            employee.Id,
            "Employee");
        var initialProfile = await client.GetFromJsonAsync<JsonElement>(
            "/api/profile/me");
        Assert.Equal(employee.Email, initialProfile.GetProperty("email").GetString());
        Assert.Equal(
            departmentId,
            initialProfile.GetProperty("departmentId").GetInt32());
        Assert.Equal(
            "Profile department",
            initialProfile.GetProperty("departmentName").GetString());

        using var update = await client.PatchAsJsonAsync(
            "/api/profile/me",
            new
            {
                fullName = "Updated Employee",
                phoneNumber = "+1 (555) 010-1200",
                email = "attacker@example.test",
                role = "Admin",
                departmentId = (int?)null,
                isActive = false
            });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updatedProfile = await update.Content
            .ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "Updated Employee",
            updatedProfile.GetProperty("fullName").GetString());
        Assert.Equal(
            "+1 (555) 010-1200",
            updatedProfile.GetProperty("phoneNumber").GetString());

        using var scopeAfter = factory.Services.CreateScope();
        var persistedUser = await scopeAfter.ServiceProvider
            .GetRequiredService<AppDbContext>()
            .Users
            .AsNoTracking()
            .SingleAsync(x => x.Id == employee.Id);
        Assert.Equal(employee.Email, persistedUser.Email);
        Assert.Equal("Employee", persistedUser.Role);
        Assert.Equal(departmentId, persistedUser.DepartmentId);
        Assert.True(persistedUser.IsActive);
    }

    [Fact]
    public async Task ProfileImage_IsDecodedStoredPrivatelyAndOwnerScoped()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var employee = await factory.AddUserAsync();
        var otherEmployee = await factory.AddUserAsync();
        var admin = await factory.AddUserAsync("Admin");
        using var employeeClient = factory.CreateAuthorizedClient(
            employee.Id,
            "Employee");
        using var otherClient = factory.CreateAuthorizedClient(
            otherEmployee.Id,
            "Employee");
        using var adminClient = factory.CreateAuthorizedClient(
            admin.Id,
            "Admin");

        using var imageContent = new MultipartFormDataContent();
        using var pngContent = new ByteArrayContent(CreatePng());
        pngContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        imageContent.Add(pngContent, "image", "not-an-image.txt");
        using var upload = await employeeClient.PutAsync(
            "/api/profile/me/photo",
            imageContent);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        using var response = await employeeClient.GetAsync(
            $"/api/profile/users/{employee.Id}/photo");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync());

        using var forbidden = await otherClient.GetAsync(
            $"/api/profile/users/{employee.Id}/photo");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var adminPhoto = await adminClient.GetAsync(
            $"/api/profile/users/{employee.Id}/photo");
        Assert.Equal(HttpStatusCode.OK, adminPhoto.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storageKey = await context.Users
            .Where(x => x.Id == employee.Id)
            .Select(x => x.ProfileImageStorageKey)
            .SingleAsync();
        Assert.NotNull(storageKey);
        Assert.True(Guid.TryParseExact(storageKey, "N", out _));
        Assert.True(File.Exists(Path.Combine(
            factory.ProfileImageStoragePath,
            $"{storageKey}.jpg")));
        var webRoot = factory.Services
            .GetRequiredService<IWebHostEnvironment>()
            .WebRootPath;
        if (!string.IsNullOrWhiteSpace(webRoot))
        {
            var relativePath = Path.GetRelativePath(
                Path.GetFullPath(webRoot),
                Path.GetFullPath(factory.ProfileImageStoragePath));
            Assert.True(
                relativePath == ".." ||
                relativePath.StartsWith(
                    $"..{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal) ||
                Path.IsPathRooted(relativePath));
        }

        using var remove = await employeeClient.DeleteAsync(
            "/api/profile/me/photo");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        using var removedPhoto = await employeeClient.GetAsync(
            $"/api/profile/users/{employee.Id}/photo");
        Assert.Equal(HttpStatusCode.NotFound, removedPhoto.StatusCode);
    }

    [Fact]
    public async Task ProfileImage_RejectsInvalidAndOversizedContent()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        var employee = await factory.AddUserAsync();
        using var client = factory.CreateAuthorizedClient(
            employee.Id,
            "Employee");

        using var invalidContent = new MultipartFormDataContent();
        using var invalidBytes = new ByteArrayContent([1, 2, 3, 4]);
        invalidContent.Add(invalidBytes, "image", "fake.png");
        using var invalidResponse = await client.PutAsync(
            "/api/profile/me/photo",
            invalidContent);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        using var oversizedContent = new MultipartFormDataContent();
        using var oversizedBytes = new ByteArrayContent(
            new byte[2 * 1024 * 1024 + 1]);
        oversizedContent.Add(oversizedBytes, "image", "large.png");
        using var oversizedResponse = await client.PutAsync(
            "/api/profile/me/photo",
            oversizedContent);
        Assert.Equal(
            HttpStatusCode.RequestEntityTooLarge,
            oversizedResponse.StatusCode);
    }

    private static byte[] CreatePng()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(2, 2));
        bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
