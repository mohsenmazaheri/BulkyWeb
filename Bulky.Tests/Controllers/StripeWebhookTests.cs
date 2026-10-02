using System.Text;
using Bulky.DataAccess.Data;
using Bulky.DataAccess.Repository;
using Bulky.Models;
using Bulky.Tests.Helpers;
using Bulky.Utility;
using BulkyWeb.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stripe;

namespace Bulky.Tests.Controllers
{
    /// <summary>
    /// The webhook marks orders as paid even when the customer never returns to the confirmation page.
    /// Events are signed with StripeWebhookSigner, so the real signature check runs.
    /// </summary>
    public class StripeWebhookTests
    {
        private const string Customer = "customer";
        private const string Company = "company";
        private const string CheckoutSessionId = "cs_test_customer";
        private const string CompanySessionId = "cs_test_company";

        private readonly ApplicationDbContext _db = TestDb.Create();

        public StripeWebhookTests()
        {
            _db.ApplicationUsers.AddRange(TestDb.User(Customer), TestDb.User(Company));
            _db.Products.Add(TestDb.Product(1));

            // A normal checkout that was started but never confirmed by the browser
            var customerOrder = TestDb.Order(1, Customer, SD.PaymentStatusPending);
            customerOrder.SessionId = CheckoutSessionId;
            // A company order: approved and already shipped, paid later with "Pay now"
            var companyOrder = TestDb.Order(2, Company, SD.PaymentStatusDelayedPayment);
            companyOrder.OrderStatus = SD.StatusShipped;
            companyOrder.SessionId = CompanySessionId;
            _db.OrderHeaders.AddRange(customerOrder, companyOrder);

            _db.ShoppingCarts.Add(new ShoppingCart { Id = 10, ProductId = 1, ApplicationUserId = Customer, Count = 2 });
            _db.SaveAndDetach();
        }

        private async Task<IActionResult> SendAsync(string payload, string? signature = null, string? configuredSecret = StripeWebhookSigner.Secret)
        {
            var controller = new StripeWebhookController(new UnitOfWork(_db),
                Options.Create(new StripeSettings { WebhookSecret = configuredSecret }),
                NullLogger<StripeWebhookController>.Instance);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
            httpContext.Request.Headers["Stripe-Signature"] = signature ?? StripeWebhookSigner.Sign(payload);
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var result = await controller.Receive();
            _db.ChangeTracker.Clear();
            return result;
        }

        private OrderHeader Order(int id) => _db.OrderHeaders.Single(o => o.Id == id);

        private static string Completed(string sessionId, string paymentStatus = "paid") =>
            StripeWebhookSigner.CheckoutSessionEvent(EventTypes.CheckoutSessionCompleted, sessionId, paymentStatus);

        [Fact]
        public async Task PaidCheckout_ApprovesTheOrder_StoresThePayment_AndEmptiesTheCart()
        {
            var result = await SendAsync(Completed(CheckoutSessionId));

            Assert.IsType<OkResult>(result);
            var order = Order(1);
            Assert.Equal(SD.StatusApproved, order.OrderStatus);
            Assert.Equal(SD.PaymentStatusApproved, order.PaymentStatus);
            Assert.Equal("pi_test_123", order.PaymentIntentId);
            Assert.False(_db.ShoppingCarts.Any(c => c.ApplicationUserId == Customer));
        }

        [Fact]
        public async Task CompanyPayNow_MarksPaymentApproved_ButKeepsTheShippedStatus()
        {
            await SendAsync(Completed(CompanySessionId));

            var order = Order(2);
            Assert.Equal(SD.PaymentStatusApproved, order.PaymentStatus);
            Assert.Equal(SD.StatusShipped, order.OrderStatus);
        }

        [Fact]
        public async Task SameEventTwice_IsHarmless()
        {
            // Stripe retries deliveries, and the confirmation page may record the payment too
            var payload = Completed(CheckoutSessionId);

            Assert.IsType<OkResult>(await SendAsync(payload));
            var firstPaymentDate = Order(1).PaymentDate;
            Assert.IsType<OkResult>(await SendAsync(payload));

            Assert.Equal(firstPaymentDate, Order(1).PaymentDate);
            Assert.Equal(SD.PaymentStatusApproved, Order(1).PaymentStatus);
        }

        [Fact]
        public async Task UnpaidSession_LeavesTheOrderPending()
        {
            // e.g. a bank transfer that has not arrived yet: Stripe sends async_payment_succeeded later
            await SendAsync(Completed(CheckoutSessionId, paymentStatus: "unpaid"));

            Assert.Equal(SD.PaymentStatusPending, Order(1).PaymentStatus);
        }

        [Fact]
        public async Task AsyncPaymentSucceeded_ApprovesTheOrder()
        {
            var payload = StripeWebhookSigner.CheckoutSessionEvent(EventTypes.CheckoutSessionAsyncPaymentSucceeded, CheckoutSessionId, "paid");

            await SendAsync(payload);

            Assert.Equal(SD.PaymentStatusApproved, Order(1).PaymentStatus);
        }

        // ---------- Security: only genuine Stripe events are accepted ----------

        [Fact]
        public async Task WrongSignature_IsRejected_AndNothingChanges()
        {
            var payload = Completed(CheckoutSessionId);

            var result = await SendAsync(payload, signature: StripeWebhookSigner.Sign(payload, secret: "whsec_an_attackers_guess"));

            Assert.IsType<BadRequestResult>(result);
            Assert.Equal(SD.PaymentStatusPending, Order(1).PaymentStatus);
        }

        [Fact]
        public async Task TamperedPayload_IsRejected()
        {
            // Signed for one session, then changed to approve the company's order
            var signedPayload = Completed(CheckoutSessionId);
            var tampered = signedPayload.Replace(CheckoutSessionId, CompanySessionId);

            var result = await SendAsync(tampered, signature: StripeWebhookSigner.Sign(signedPayload));

            Assert.IsType<BadRequestResult>(result);
            Assert.Equal(SD.PaymentStatusDelayedPayment, Order(2).PaymentStatus);
        }

        [Fact]
        public async Task OldEvent_IsRejected_AsAReplay()
        {
            var payload = Completed(CheckoutSessionId);

            var result = await SendAsync(payload, signature: StripeWebhookSigner.Sign(payload, signedAt: DateTimeOffset.UtcNow.AddMinutes(-10)));

            Assert.IsType<BadRequestResult>(result);
        }

        [Fact]
        public async Task UnknownSessionOrOtherEventType_IsAcknowledged()
        {
            // 200 tells Stripe "received"; anything else makes Stripe retry for days
            Assert.IsType<OkResult>(await SendAsync(Completed("cs_test_unknown")));
            Assert.IsType<OkResult>(await SendAsync(StripeWebhookSigner.CheckoutSessionEvent("checkout.session.expired", CheckoutSessionId, "unpaid")));
            Assert.Equal(SD.PaymentStatusPending, Order(1).PaymentStatus);
        }

        [Fact]
        public async Task MissingWebhookSecret_Returns503()
        {
            var result = await SendAsync(Completed(CheckoutSessionId), configuredSecret: null);

            Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<StatusCodeResult>(result).StatusCode);
        }
    }
}
