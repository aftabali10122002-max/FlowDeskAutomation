using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowDisk.Api.Tests;

public class AuthRateLimitingTests
{
    [Fact]
    public async Task LoginAndRegistration_Return429AfterTheirConfiguredLimits()
    {
        using var factory = new ApiFactory();
        await factory.InitializeDatabaseAsync();
        int loginPermitLimit;
        int registrationPermitLimit;
        using (var scope = factory.Services.CreateScope())
        {
            var configuration = scope.ServiceProvider
                .GetRequiredService<IConfiguration>();
            loginPermitLimit = configuration.GetValue<int>(
                "RateLimiting:Login:PermitLimit");
            registrationPermitLimit = configuration.GetValue<int>(
                "RateLimiting:Registration:PermitLimit");
            Assert.Equal(10, loginPermitLimit);
            Assert.Equal(5, registrationPermitLimit);
        }

        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < loginPermitLimit; attempt++)
        {
            using var login = await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    email = "missing@example.test",
                    password = "invalid-password"
                });
            Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        }

        using var rejectedLogin = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                email = "missing@example.test",
                password = "invalid-password"
            });
        await AssertRateLimitedAsync(rejectedLogin);

        for (var attempt = 0; attempt < registrationPermitLimit; attempt++)
        {
            using var registration = await client.PostAsJsonAsync(
                "/api/auth/register",
                new
                {
                    fullName = "Rate Limit Test",
                    email = $"{Guid.NewGuid():N}@example.test",
                    password = "LongEnoughTestPassword123!"
                });
            Assert.Equal(HttpStatusCode.Created, registration.StatusCode);
        }

        using var rejectedRegistration = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                fullName = "Rate Limit Test",
                email = $"{Guid.NewGuid():N}@example.test",
                password = "LongEnoughTestPassword123!"
            });
        await AssertRateLimitedAsync(rejectedRegistration);
    }

    private static async Task AssertRateLimitedAsync(
        HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.TooManyRequests,
            $"Expected HTTP 429, got {(int)response.StatusCode}: {body}");
        Assert.True(response.Headers.Contains("Retry-After"));
    }
}
