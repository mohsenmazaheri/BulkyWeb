using Bulky.Tests.Helpers;
using BulkyWeb.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bulky.Tests.Web
{
    public class DatabaseHealthCheckTests
    {
        [Fact]
        public async Task ReachableDatabase_IsHealthy()
        {
            var check = new DatabaseHealthCheck(TestDb.Create());

            var result = await check.CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Healthy, result.Status);
        }
    }
}
