using System.Net;
using Xunit;

namespace FlowDisk.Api.Tests;

public sealed class HealthCheckTests
{
    [Fact]
    public async Task LivenessEndpoint_ReturnsHealthyWithoutDatabaseInitialization()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
