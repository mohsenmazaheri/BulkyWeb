using Bulky.DataAccess.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BulkyWeb.HealthChecks
{
    /// <summary>
    /// Reports whether the database can be reached. Used by GET /health, which Docker, CI and hosting
    /// platforms call to decide whether the app is ready.
    /// </summary>
    public class DatabaseHealthCheck : IHealthCheck
    {
        private readonly ApplicationDbContext _db;

        public DatabaseHealthCheck(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            return await _db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("The database is reachable.")
                : HealthCheckResult.Unhealthy("Cannot connect to the database.");
        }
    }
}
