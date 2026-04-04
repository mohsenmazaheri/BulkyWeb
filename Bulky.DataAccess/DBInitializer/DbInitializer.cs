using Bulky.DataAccess.Data;
using Bulky.Models;
using Bulky.Utility;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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

        public DbInitializer(UserManager<ApplicationUser> userManager, 
            RoleManager<IdentityRole> roleManager, 
            ApplicationDbContext db)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _db = db;
        }

        public async Task InitializeAsync()
        {
            // Perform Migrations if they are not applied
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


            // Create roles if they are not created
            if (!await _roleManager.RoleExistsAsync(SD.Role_Customer))
            {
                await _roleManager.CreateAsync(new IdentityRole(SD.Role_Customer));
                await _roleManager.CreateAsync(new IdentityRole(SD.Role_Employee));
                await _roleManager.CreateAsync(new IdentityRole(SD.Role_Admin));
                await _roleManager.CreateAsync(new IdentityRole(SD.Role_Company));

                string email = "admin@bulky.com";
                // If roles are not created, then we will create admin user as well
                await _userManager.CreateAsync(new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    Name = "Admin",
                    PhoneNumber = "1234567890",
                    StreetAddress = "Test address",
                    State = "ESF",
                    City = "ESF",
                    PostalCode = "1234567890"
                }, "Qwerty123*");

                ApplicationUser user = await _db.ApplicationUsers.FirstOrDefaultAsync(a => a.Email == email);
                await _userManager.AddToRoleAsync(user, SD.Role_Admin);
            }

            return;
        }
    }
}
