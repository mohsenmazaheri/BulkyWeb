using Bulky.DataAccess.Data;
using Bulky.DataAccess.Repository;
using Bulky.Models;
using Bulky.Models.ViewModels;
using Bulky.Tests.Helpers;
using Bulky.Utility;
using BulkyWeb.Areas.Customer.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Bulky.Tests.Controllers
{
    /// <summary>
    /// Cart > Summary > "Place order" (SummaryPOST). A company user is used because company orders are paid
    /// later: the same order-saving code runs, but no Stripe session is created.
    /// </summary>
    public class CheckoutTests
    {
        private const string CompanyUser = "company-user";

        private static ApplicationDbContext Seed(ApplicationDbContext db, bool withCart = true)
        {
            db.Companies.Add(new Company { Id = 1, Name = "Acme" });
            var user = TestDb.User(CompanyUser);
            user.CompanyId = 1;
            db.ApplicationUsers.Add(user);
            db.Products.AddRange(TestDb.Product(1, price: 40), TestDb.Product(2, price: 10));
            if (withCart)
            {
                db.ShoppingCarts.AddRange(
                    new ShoppingCart { Id = 10, ProductId = 1, ApplicationUserId = CompanyUser, Count = 2 },   // 80
                    new ShoppingCart { Id = 11, ProductId = 2, ApplicationUserId = CompanyUser, Count = 1 });  // 10
            }
            db.SaveAndDetach();
            return db;
        }

        private static OrderHeader ShippingForm() => new()
        {
            Name = "Acme Buyer",
            PhoneNumber = "555-0100",
            StreetAddress = "1 Main St",
            City = "Springfield",
            State = "IL",
            PostalCode = "62701"
        };

        private static CartController Checkout(ApplicationDbContext db, OrderHeader postedForm)
        {
            var controller = new CartController(new UnitOfWork(db)).WithUser(CompanyUser, SD.Role_Company);
            // [BindProperty] ShoppingCartVM: what model binding fills from the posted form
            controller.ShoppingCartVM = new ShoppingCartVM { OrderHeader = postedForm, ShoppingCartList = [] };
            return controller;
        }

        [Fact]
        public void PlaceOrder_SavesTheOrderWithAllLinesAndTheRightTotal()
        {
            var db = Seed(TestDb.Create());

            var result = Checkout(db, ShippingForm()).SummaryPOST();
            db.ChangeTracker.Clear();

            var order = Assert.Single(db.OrderHeaders);
            Assert.Equal(90m, order.OrderTotal);
            Assert.Equal(SD.StatusApproved, order.OrderStatus);
            Assert.Equal(SD.PaymentStatusDelayedPayment, order.PaymentStatus);
            Assert.Equal("Acme Buyer", order.Name);
            Assert.Equal(2, db.OrderDetails.Count(d => d.OrderHeaderId == order.Id));
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("OrderConfirmation", redirect.ActionName);
        }

        // ---------- Atomic: all or nothing ----------

        /// <summary>Simulates a database failure in the moment the order lines are written.</summary>
        private sealed class FailWhenSavingOrderLines : SaveChangesInterceptor
        {
            public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
            {
                if (eventData.Context!.ChangeTracker.Entries<OrderDetail>().Any(e => e.State == EntityState.Added))
                    throw new DbUpdateException("Simulated database failure while saving order lines");
                return result;
            }
        }

        [Fact]
        public void FailureWhileSavingTheLines_LeavesNoHalfSavedOrderBehind()
        {
            var db = Seed(TestDb.Create(new FailWhenSavingOrderLines()));

            Assert.Throws<DbUpdateException>(() => Checkout(db, ShippingForm()).SummaryPOST());
            db.ChangeTracker.Clear();

            Assert.Empty(db.OrderHeaders);
            Assert.Empty(db.OrderDetails);
        }

        // ---------- Over-posting: only the shipping fields come from the form ----------

        [Fact]
        public void ExtraPostedOrderFields_AreIgnored()
        {
            var db = Seed(TestDb.Create());
            var tampered = ShippingForm();
            tampered.Id = 999;
            tampered.OrderTotal = -89.99m;                  // would have made a $90 order cost $0.01
            tampered.PaymentStatus = SD.PaymentStatusApproved;
            tampered.SessionId = "cs_fake";
            tampered.PaymentIntentId = "pi_fake";
            tampered.TrackingNumber = "FAKE-TRACKING";
            tampered.Carrier = "Fake Carrier";

            Checkout(db, tampered).SummaryPOST();
            db.ChangeTracker.Clear();

            var order = Assert.Single(db.OrderHeaders);
            Assert.NotEqual(999, order.Id);
            Assert.Equal(90m, order.OrderTotal);
            Assert.Equal(SD.PaymentStatusDelayedPayment, order.PaymentStatus);
            Assert.Null(order.SessionId);
            Assert.Null(order.PaymentIntentId);
            Assert.Null(order.TrackingNumber);
            Assert.Null(order.Carrier);
        }

        // ---------- Validation ----------

        [Fact]
        public void EmptyCart_CreatesNoOrder()
        {
            var db = Seed(TestDb.Create(), withCart: false);

            var result = Checkout(db, ShippingForm()).SummaryPOST();

            Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
            Assert.Empty(db.OrderHeaders);
        }

        [Fact]
        public void MissingShippingField_CreatesNoOrder_AndAsksAgain()
        {
            var db = Seed(TestDb.Create());
            var form = ShippingForm();
            form.City = "";
            var controller = Checkout(db, form);

            var result = controller.SummaryPOST();

            Assert.Equal("Summary", Assert.IsType<RedirectToActionResult>(result).ActionName);
            Assert.NotNull(controller.TempData["error"]);
            Assert.Empty(db.OrderHeaders);
        }
    }
}
