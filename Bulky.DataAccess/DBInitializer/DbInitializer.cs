using Bulky.DataAccess.Data;
using Bulky.Models;
using Bulky.Utility;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Text;

namespace Bulky.DataAccess.DBInitializer
{
    public class DbInitializer : IDbInitializer
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ApplicationDbContext _db;
        private readonly AdminUserSettings _adminUser;

        public DbInitializer(UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ApplicationDbContext db,
            IOptions<AdminUserSettings> adminUser)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _db = db;
            _adminUser = adminUser.Value;
        }

        public async Task InitializeAsync()
        {
            // Perform Migrations if they are not applied.
            // Migrations only exist for relational databases (not for the in-memory database used in tests).
            if (_db.Database.IsRelational())
            {
                try
                {
                    var pending = await _db.Database.GetPendingMigrationsAsync();
                    if (pending != null && pending.Any())
                    {
                        await _db.Database.MigrateAsync();
                    }
                }
                catch (Exception ex)
                {
                    // Surface migration errors during startup so they can be observed and fixed
                    Console.WriteLine($"Database migration failed: {ex}");
                    throw;
                }
            }

            // Every step only does what is still missing, so a failed start can simply be retried
            foreach (var role in new[] { SD.Role_Customer, SD.Role_Employee, SD.Role_Admin, SD.Role_Company })
            {
                if (!await _roleManager.RoleExistsAsync(role))
                    EnsureSucceeded(await _roleManager.CreateAsync(new IdentityRole(role)), $"create role '{role}'");
            }

            // Existing databases already have an admin and need no AdminUser configuration
            if ((await _userManager.GetUsersInRoleAsync(SD.Role_Admin)).Any())
                return;

            if (string.IsNullOrWhiteSpace(_adminUser.Password))
            {
                throw new InvalidOperationException(
                    $"The database has no admin account yet, but '{AdminUserSettings.SectionName}:Password' is not configured. " +
                    $"Set it with: dotnet user-secrets set \"{AdminUserSettings.SectionName}:Password\" \"<password>\" --project BulkyWeb");
            }

            // Never promote an existing account: someone could have registered the admin email address
            if (await _userManager.FindByEmailAsync(_adminUser.Email) != null)
            {
                throw new InvalidOperationException(
                    $"A user with the email '{_adminUser.Email}' already exists but is not an admin. " +
                    $"Configure another '{AdminUserSettings.SectionName}:Email', or promote that user manually.");
            }

            var admin = new ApplicationUser
            {
                UserName = _adminUser.Email,
                Email = _adminUser.Email,
                Name = "Admin",
                PhoneNumber = "1234567890",
                StreetAddress = "Test address",
                State = "ESF",
                City = "ESF",
                PostalCode = "1234567890"
            };
            EnsureSucceeded(await _userManager.CreateAsync(admin, _adminUser.Password), $"create admin user '{_adminUser.Email}'");
            EnsureSucceeded(await _userManager.AddToRoleAsync(admin, SD.Role_Admin), "add the admin user to the Admin role");
        }

        /// <summary>
        /// Identity reports problems (e.g. a password that is too weak) through IdentityResult instead of exceptions,
        /// so ignoring the result hides the real error.
        /// </summary>
        private static void EnsureSucceeded(IdentityResult result, string action)
        {
            if (!result.Succeeded)
            {
                var errors = string.Join(" ", result.Errors.Select(e => e.Description));
                throw new InvalidOperationException($"DbInitializer could not {action}: {errors}");
            }
        }
    }
}
