using Bulky.DataAccess.Data;
using Bulky.DataAccess.DBInitializer;
using Bulky.Models;
using Bulky.Utility;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bulky.Tests.DataAccess
{
    /// <summary>
    /// Runs DbInitializer with the real ASP.NET Core Identity (UserManager, RoleManager, password rules)
    /// on an in-memory database.
    /// </summary>
    public class DbInitializerTests : IDisposable
    {
        private const string AdminEmail = "admin@test.com";
        private const string StrongPassword = "Str0ng!Passw0rd";

        private readonly ServiceProvider _services;

        public DbInitializerTests()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            var databaseName = Guid.NewGuid().ToString();
            services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(databaseName));
            services.AddIdentity<ApplicationUser, IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
            _services = services.BuildServiceProvider();
        }

        public void Dispose() => _services.Dispose();

        /// <summary>Each call uses a new scope, like a separate application start.</summary>
        private async Task InitializeAsync(string? password, string email = AdminEmail)
        {
            using var scope = _services.CreateScope();
            var provider = scope.ServiceProvider;
            var initializer = new DbInitializer(
                provider.GetRequiredService<UserManager<ApplicationUser>>(),
                provider.GetRequiredService<RoleManager<IdentityRole>>(),
                provider.GetRequiredService<ApplicationDbContext>(),
                Options.Create(new AdminUserSettings { Email = email, Password = password }));

            await initializer.InitializeAsync();
        }

        private async Task<T> WithUserManagerAsync<T>(Func<UserManager<ApplicationUser>, Task<T>> action)
        {
            using var scope = _services.CreateScope();
            return await action(scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>());
        }

        [Fact]
        public async Task EmptyDatabase_CreatesAllRolesAndTheConfiguredAdmin()
        {
            await InitializeAsync(StrongPassword);

            using var scope = _services.CreateScope();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in new[] { SD.Role_Customer, SD.Role_Employee, SD.Role_Admin, SD.Role_Company })
                Assert.True(await roles.RoleExistsAsync(role), $"Role {role} is missing");

            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await users.FindByEmailAsync(AdminEmail);
            Assert.NotNull(admin);
            Assert.True(await users.IsInRoleAsync(admin, SD.Role_Admin));
            Assert.True(await users.CheckPasswordAsync(admin, StrongPassword));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task EmptyDatabase_WithoutPassword_FailsWithHowToFixMessage(string? password)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => InitializeAsync(password));

            Assert.Contains("AdminUser:Password", error.Message);
            Assert.Contains("dotnet user-secrets set", error.Message);
        }

        [Fact]
        public async Task WeakPassword_ReportsIdentityError_AndAFixedConfigurationCanBeRetried()
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => InitializeAsync("123"));
            Assert.Contains("Passwords must", error.Message);

            // The roles were already created by the failed start; the retry must still create the admin
            await InitializeAsync(StrongPassword);

            var isAdmin = await WithUserManagerAsync(async users =>
                await users.IsInRoleAsync((await users.FindByEmailAsync(AdminEmail))!, SD.Role_Admin));
            Assert.True(isAdmin);
        }

        [Fact]
        public async Task ExistingAdmin_NeedsNoPassword_AndCreatesNothing()
        {
            await InitializeAsync(StrongPassword);

            // Later starts, e.g. on a machine without the user secret, must work
            await InitializeAsync(password: null);

            var userCount = await WithUserManagerAsync(users => Task.FromResult(users.Users.Count()));
            Assert.Equal(1, userCount);
        }

        [Fact]
        public async Task AdminEmailTakenByNormalUser_FailsAndDoesNotPromoteThatUser()
        {
            // Someone registered the admin address before the first admin was created
            await InitializeAsync(StrongPassword, email: "first-admin@test.com");
            await WithUserManagerAsync(async users =>
            {
                var admin = (await users.FindByEmailAsync("first-admin@test.com"))!;
                await users.RemoveFromRoleAsync(admin, SD.Role_Admin);
                return await users.CreateAsync(new ApplicationUser { UserName = AdminEmail, Email = AdminEmail }, StrongPassword);
            });

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => InitializeAsync(StrongPassword));

            Assert.Contains("already exists but is not an admin", error.Message);
            var promoted = await WithUserManagerAsync(async users =>
                await users.IsInRoleAsync((await users.FindByEmailAsync(AdminEmail))!, SD.Role_Admin));
            Assert.False(promoted);
        }
    }
}
