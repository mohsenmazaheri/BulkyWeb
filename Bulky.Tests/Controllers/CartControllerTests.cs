using Bulky.DataAccess.Data;
using Bulky.DataAccess.Repository;
using Bulky.Models;
using Bulky.Models.ViewModels;
using Bulky.Tests.Helpers;
using Bulky.Utility;
using BulkyWeb.Areas.Customer.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bulky.Tests.Controllers
{
    /// <summary>
    /// Runs the real CartController with the real UnitOfWork on an in-memory database.
    /// </summary>
    public class CartControllerTests
    {
        private const string UserA = "user-a";
        private const string UserB = "user-b";

        private readonly ApplicationDbContext _db = TestDb.Create();

        public CartControllerTests()
        {
            _db.ApplicationUsers.AddRange(TestDb.User(UserA), TestDb.User(UserB));
            _db.Products.AddRange(TestDb.Product(1), TestDb.Product(2));
        }

        private CartController CreateController(string userId) =>
            new CartController(new UnitOfWork(_db)).WithUser(userId, SD.Role_Customer);

        private ShoppingCart ReloadCart(int id)
        {
            _db.ChangeTracker.Clear();
            return _db.ShoppingCarts.Single(c => c.Id == id);
        }

        // ---------- Lesson 01: IDOR protection ----------

        [Fact]
        public void Plus_OwnCart_IncreasesCount()
        {
            _db.ShoppingCarts.Add(new ShoppingCart { Id = 10, ProductId = 1, ApplicationUserId = UserA, Count = 2 });
            _db.SaveAndDetach();

            var result = CreateController(UserA).Plus(10);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Equal(3, ReloadCart(10).Count);
        }

        [Fact]
        public void Plus_OtherUsersCart_ReturnsNotFound_AndDoesNotChangeIt()
        {
            _db.ShoppingCarts.Add(new ShoppingCart { Id = 10, ProductId = 1, ApplicationUserId = UserB, Count = 2 });
            _db.SaveAndDetach();

            var result = CreateController(UserA).Plus(10);

            Assert.IsType<NotFoundResult>(result);
            Assert.Equal(2, ReloadCart(10).Count);
        }

        [Fact]
        public void Minus_OtherUsersCart_ReturnsNotFound()
        {
            _db.ShoppingCarts.Add(new ShoppingCart { Id = 10, ProductId = 1, ApplicationUserId = UserB, Count = 2 });
            _db.SaveAndDetach();

            var result = CreateController(UserA).Minus(10);

            Assert.IsType<NotFoundResult>(result);
            Assert.Equal(2, ReloadCart(10).Count);
        }

        [Fact]
        public void Remove_OtherUsersCart_ReturnsNotFound_AndKeepsIt()
        {
            _db.ShoppingCarts.Add(new ShoppingCart { Id = 10, ProductId = 1, ApplicationUserId = UserB, Count = 2 });
            _db.SaveAndDetach();

            var result = CreateController(UserA).Remove(10);

            Assert.IsType<NotFoundResult>(result);
            Assert.True(_db.ShoppingCarts.Any(c => c.Id == 10));
        }

        [Fact]
        public void Minus_LastItem_RemovesCartRow_AndUpdatesSessionCount()
        {
            _db.ShoppingCarts.AddRange(
                new ShoppingCart { Id = 10, ProductId = 1, ApplicationUserId = UserA, Count = 1 },
                new ShoppingCart { Id = 11, ProductId = 2, ApplicationUserId = UserA, Count = 5 });
            _db.SaveAndDetach();
            var controller = CreateController(UserA);

            controller.Minus(10);

            Assert.False(_db.ShoppingCarts.Any(c => c.Id == 10));
            Assert.Equal(1, controller.HttpContext.Session.GetInt32(SD.SessionCart));
        }

        [Fact]
        public void Remove_OwnCart_DeletesRow_AndUpdatesSessionCount()
        {
            _db.ShoppingCarts.AddRange(
                new ShoppingCart { Id = 10, ProductId = 1, ApplicationUserId = UserA, Count = 3 },
                new ShoppingCart { Id = 11, ProductId = 2, ApplicationUserId = UserA, Count = 5 });
            _db.SaveAndDetach();
            var controller = CreateController(UserA);

            controller.Remove(10);

            Assert.False(_db.ShoppingCarts.Any(c => c.Id == 10));
            Assert.Equal(1, controller.HttpContext.Session.GetInt32(SD.SessionCart));
        }

        [Fact]
        public void Index_ShowsOnlyTheCurrentUsersCart()
        {
            _db.ShoppingCarts.AddRange(
                new ShoppingCart { Id = 10, ProductId = 1, ApplicationUserId = UserA, Count = 1 },
                new ShoppingCart { Id = 11, ProductId = 2, ApplicationUserId = UserB, Count = 1 });
            _db.SaveAndDetach();

            var view = Assert.IsType<ViewResult>(CreateController(UserA).Index());

            var model = Assert.IsType<ShoppingCartVM>(view.Model);
            var item = Assert.Single(model.ShoppingCartList);
            Assert.Equal(10, item.Id);
        }

        // ---------- Business rule: price depends on quantity ----------

        [Theory]
        [InlineData(1, 40)]
        [InlineData(50, 40)]   // up to 50 books: normal price
        [InlineData(51, 30)]   // 51-100 books: Price50
        [InlineData(100, 30)]
        [InlineData(101, 20)]  // more than 100 books: Price100
        public void Index_UsesPriceTierForQuantity(int count, double expectedPrice)
        {
            _db.ShoppingCarts.Add(new ShoppingCart { Id = 10, ProductId = 1, ApplicationUserId = UserA, Count = count });
            _db.SaveAndDetach();

            var view = Assert.IsType<ViewResult>(CreateController(UserA).Index());

            var model = Assert.IsType<ShoppingCartVM>(view.Model);
            Assert.Equal(expectedPrice, model.ShoppingCartList.Single().Price);
            Assert.Equal(expectedPrice * count, model.OrderHeader.OrderTotal);
        }

        // ---------- Lesson 01 Part D: order confirmation ----------

        [Fact]
        public void OrderConfirmation_OtherUsersOrder_ReturnsNotFound_AndKeepsTheirCart()
        {
            _db.OrderHeaders.Add(TestDb.Order(100, UserB, SD.PaymentStatusDelayedPayment));
            _db.ShoppingCarts.Add(new ShoppingCart { Id = 10, ProductId = 1, ApplicationUserId = UserB, Count = 1 });
            _db.SaveAndDetach();

            var result = CreateController(UserA).OrderConfirmation(100);

            Assert.IsType<NotFoundResult>(result);
            Assert.True(_db.ShoppingCarts.Any(c => c.ApplicationUserId == UserB));
        }

        [Fact]
        public void OrderConfirmation_OwnDelayedPaymentOrder_ClearsOnlyOwnCart()
        {
            // Company orders use delayed payment, so no Stripe call is made
            _db.OrderHeaders.Add(TestDb.Order(100, UserA, SD.PaymentStatusDelayedPayment));
            _db.ShoppingCarts.AddRange(
                new ShoppingCart { Id = 10, ProductId = 1, ApplicationUserId = UserA, Count = 1 },
                new ShoppingCart { Id = 11, ProductId = 1, ApplicationUserId = UserB, Count = 1 });
            _db.SaveAndDetach();

            var result = CreateController(UserA).OrderConfirmation(100);

            Assert.IsType<ViewResult>(result);
            Assert.False(_db.ShoppingCarts.Any(c => c.ApplicationUserId == UserA));
            Assert.True(_db.ShoppingCarts.Any(c => c.ApplicationUserId == UserB));
        }
    }
}
