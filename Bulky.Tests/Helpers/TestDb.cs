using Bulky.DataAccess.Data;
using Bulky.Models;
using Microsoft.EntityFrameworkCore;

namespace Bulky.Tests.Helpers
{
    /// <summary>
    /// Creates an EF Core in-memory database for tests. Every call gets its own
    /// database name, so tests never see each other's data.
    /// </summary>
    public static class TestDb
    {
        public static ApplicationDbContext Create()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new ApplicationDbContext(options);
        }

        /// <summary>
        /// Saves the seed data and then forgets all tracked entities. In the real app every
        /// request gets a new DbContext, so the code under test must not find the seed
        /// objects already tracked.
        /// </summary>
        public static void SaveAndDetach(this ApplicationDbContext db)
        {
            db.SaveChanges();
            db.ChangeTracker.Clear();
        }

        public static ApplicationUser User(string id) => new()
        {
            Id = id,
            UserName = $"{id}@test.com",
            Email = $"{id}@test.com",
            Name = id
        };

        public static Product Product(int id, decimal price = 40, decimal price50 = 30, decimal price100 = 20) => new()
        {
            Id = id,
            Title = $"Book {id}",
            Author = "Author",
            ISBN = $"ISBN{id}",
            Description = "Description",
            ListPrice = 50,
            Price = price,
            Price50 = price50,
            Price100 = price100,
            CategoryId = 1,
            ImageURL = ""
        };

        public static OrderHeader Order(int id, string userId, string paymentStatus = "Pending") => new()
        {
            Id = id,
            ApplicationUserId = userId,
            OrderDate = DateTime.UtcNow,
            OrderStatus = "Pending",
            PaymentStatus = paymentStatus,
            Name = "Customer",
            PhoneNumber = "123",
            StreetAddress = "Street",
            City = "City",
            State = "State",
            PostalCode = "12345"
        };
    }
}
