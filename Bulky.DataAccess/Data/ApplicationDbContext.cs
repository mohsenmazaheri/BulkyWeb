using Bulky.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Bulky.DataAccess.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
            
        }

        public DbSet<Category> Categories { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Company> Companies { get; set; }
        public DbSet<ShoppingCart> ShoppingCarts { get; set; }
        public DbSet<ApplicationUser> ApplicationUsers { get; set; }
        public DbSet<OrderHeader> OrderHeaders { get; set; }
        public DbSet<OrderDetail> OrderDetails { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Category>().HasData(
                new Category { Id = 1, Name = "Action", DisplayOrder = 1 },
                new Category { Id = 2, Name = "SciFi", DisplayOrder = 2 },
                new Category { Id = 3, Name = "History", DisplayOrder = 3 }
                );

            modelBuilder.Entity<Company>().HasData(
                new Company
                {
                    Id = 1,
                    Name = "Tech Solution",
                    StreetAddress = "123 Tech St",
                    City = "Tech City",
                    PostalCode = "123123",
                    PhoneNumber = "669990000",
                    State = "IL"
                },
                new Company
                {
                    Id = 2,
                    Name = "Vivid Books",
                    StreetAddress = "999 Vid St",
                    City = "Vid City",
                    PostalCode = "9876446",
                    PhoneNumber = "7879871333",
                    State = "IL"
                },
                new Company
                {
                    Id = 3,
                    Name = "Readers Club",
                    StreetAddress = "456 Main St",
                    City = "Lala land",
                    PostalCode = "9999999",
                    PhoneNumber = "11112222",
                    State = "NY"
                }
                );

            modelBuilder.Entity<Product>().HasData(
                 new Product
                 {
                     Id = 1,
                     Title = "Fortune of Time",
                     Author = "Billy Spark",
                     Description = "Presents vitae sodales libro.",
                     ISBN = "SWD99999001",
                     ListPrice = 99,
                     Price = 90,
                     Price50 = 85,
                     Price100 = 80,
                     CategoryId = 1,
                     ImageURL = ""
                 },
                 new Product
                 {
                     Id = 2,
                     Title = "Dark Skies",
                     Author = "Nancy Hoover",
                     Description = "Presents dark skies.",
                     ISBN = "SWD777777001",
                     ListPrice = 40,
                     Price = 30,
                     Price50 = 25,
                     Price100 = 20,
                     CategoryId = 1,
                     ImageURL = ""
                 },
                 new Product
                 {
                     Id = 3,
                     Title = "Vanish in the Sunset",
                     Author = "Julian Button",
                     Description = "Presents a romantic story.",
                     ISBN = "SWD555555501",
                     ListPrice = 55,
                     Price = 50,
                     Price50 = 45,
                     Price100 = 40,
                     CategoryId = 2,
                     ImageURL = ""
                 },
                 new Product
                 {
                     Id = 4,
                     Title = "Blind Owl",
                     Author = "Sadegh Hedayat",
                     Description = "A dark story.",
                     ISBN = "SWD12323423401",
                     ListPrice = 5.5,
                     Price = 5,
                     Price50 = 4.5,
                     Price100 = 4,
                     CategoryId = 3,
                     ImageURL = ""
                 }
            );
        }
    }
}
