using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bulky.DataAccess.Data
{
    /// <summary>
    /// Values for the "DatabaseProvider" setting. Each one is also the name of its connection string.
    /// </summary>
    public static class DatabaseProviders
    {
        public const string SqlServer = "SqlServer";
        public const string MariaDb = "MariaDb";
    }

    public static class DatabaseServiceCollectionExtensions
    {
        /// <summary>
        /// Registers ApplicationDbContext for the provider chosen by the "DatabaseProvider" setting
        /// (SqlServer when missing). Each provider has its own migrations project, because
        /// migrations contain provider-specific SQL types.
        /// </summary>
        public static IServiceCollection AddBulkyDatabase(this IServiceCollection services, IConfiguration configuration)
        {
            var provider = configuration["DatabaseProvider"] ?? DatabaseProviders.SqlServer;
            var connectionString = configuration.GetConnectionString(provider)
                ?? throw new InvalidOperationException($"Connection string '{provider}' is missing.");

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                switch (provider)
                {
                    case DatabaseProviders.SqlServer:
                        options.UseSqlServer(connectionString,
                            sql => sql.MigrationsAssembly("Bulky.Migrations.SqlServer"));
                        break;

                    case DatabaseProviders.MariaDb:
                        // The server version is set explicitly instead of ServerVersion.AutoDetect, which would
                        // open a connection just to read it (and fail in "dotnet ef" when the server is not reachable).
                        options.UseMySql(connectionString, new MariaDbServerVersion(new Version(11, 4)),
                            mariaDb => mariaDb.MigrationsAssembly("Bulky.Migrations.MariaDb"));
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Unknown DatabaseProvider '{provider}'. Use '{DatabaseProviders.SqlServer}' or '{DatabaseProviders.MariaDb}'.");
                }
            });

            return services;
        }
    }
}
