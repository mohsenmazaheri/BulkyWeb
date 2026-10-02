using Bulky.DataAccess.Data;
using Bulky.DataAccess.Repository;
using Bulky.Models;
using Bulky.Tests.Helpers;
using Bulky.Utility;
using BulkyWeb.Areas.Customer.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bulky.Tests.Controllers
{
    /// <summary>
    /// Product details > "Add to cart" (HomeController.Details POST) binds a ShoppingCart from the form.
    /// </summary>
    public class AddToCartTests
    {
        private const string Customer = "customer";
        private readonly ApplicationDbContext _db = TestDb.Create();

        public AddToCartTests()
        {
            _db.ApplicationUsers.AddRange(TestDb.User(Customer), TestDb.User("someone-else"));
            _db.Products.Add(TestDb.Product(1));
            _db.ShoppingCarts.Add(new ShoppingCart { Id = 50, ProductId = 1, ApplicationUserId = "someone-else", Count = 1 });
            _db.SaveAndDetach();
        }

        private IActionResult AddToCart(ShoppingCart posted)
        {
            var controller = new HomeController(NullLogger<HomeController>.Instance, new UnitOfWork(_db)).WithUser(Customer, SD.Role_Customer);
            var result = controller.Details(posted);
            _db.ChangeTracker.Clear();
            return result;
        }

        private IEnumerable<ShoppingCart> MyCart => _db.ShoppingCarts.Where(c => c.ApplicationUserId == Customer).ToList();

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]       // would have put "minus five books" in the cart, and a negative total in the order
        [InlineData(1001)]
        public void QuantityOutsideOneToThousand_IsRejected(int count)
        {
            var result = AddToCart(new ShoppingCart { ProductId = 1, Count = count });

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Empty(MyCart);
        }

        [Fact]
        public void AddingToAnExistingLine_CannotGoAboveOneThousand()
        {
            AddToCart(new ShoppingCart { ProductId = 1, Count = 900 });

            AddToCart(new ShoppingCart { ProductId = 1, Count = 200 });

            Assert.Equal(900, Assert.Single(MyCart).Count);
        }

        [Fact]
        public void PostedIdAndOwner_AreIgnored()
        {
            // Tries to reuse another user's cart row id and to add to someone else's cart
            AddToCart(new ShoppingCart { Id = 50, ProductId = 1, Count = 2, ApplicationUserId = "someone-else" });

            var line = Assert.Single(MyCart);
            Assert.NotEqual(50, line.Id);
            Assert.Equal(2, line.Count);
            Assert.Equal(1, _db.ShoppingCarts.Single(c => c.Id == 50).Count);   // the other user's line is untouched
        }

        [Fact]
        public void ValidQuantity_IsAdded()
        {
            AddToCart(new ShoppingCart { ProductId = 1, Count = 3 });

            Assert.Equal(3, Assert.Single(MyCart).Count);
        }
    }
}
