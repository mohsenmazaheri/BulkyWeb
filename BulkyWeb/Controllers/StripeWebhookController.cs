using Bulky.DataAccess.Repository.IRepository;
using Bulky.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace BulkyWeb.Controllers
{
    /// <summary>
    /// Stripe calls this endpoint directly when a checkout is paid. Without it, an order was only marked as paid
    /// when the customer's browser came back to the confirmation page; closing the tab after paying left the
    /// order "Pending" although the money had been taken.
    /// </summary>
    [Route("stripe/webhook")]
    [AllowAnonymous]
    // Stripe's servers have no anti-forgery token (Lesson 03). The Stripe signature protects this endpoint instead.
    [IgnoreAntiforgeryToken]
    public class StripeWebhookController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly StripeSettings _stripeSettings;
        private readonly ILogger<StripeWebhookController> _logger;

        public StripeWebhookController(IUnitOfWork unitOfWork, IOptions<StripeSettings> stripeSettings,
            ILogger<StripeWebhookController> logger)
        {
            _unitOfWork = unitOfWork;
            _stripeSettings = stripeSettings.Value;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Receive()
        {
            if (string.IsNullOrEmpty(_stripeSettings.WebhookSecret))
            {
                _logger.LogError("Stripe webhook received, but Stripe:WebhookSecret is not configured.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            // The signature is calculated over the exact bytes Stripe sent, so read the raw body (no model binding)
            string json;
            using (var reader = new StreamReader(Request.Body))
                json = await reader.ReadToEndAsync();

            Event stripeEvent;
            try
            {
                // Throws when the Stripe-Signature header does not match: the request did not come from Stripe,
                // was changed on the way, or is older than 5 minutes (replay protection).
                // throwOnApiVersionMismatch: false, because events are sent in the API version of the Stripe account
                // or endpoint, which can differ from the version this Stripe.net release was built for.
                stripeEvent = EventUtility.ConstructEvent(json, Request.Headers["Stripe-Signature"],
                    _stripeSettings.WebhookSecret, throwOnApiVersionMismatch: false);
            }
            catch (StripeException ex)
            {
                _logger.LogWarning("Rejected a Stripe webhook: {Reason}", ex.Message);
                return BadRequest();
            }

            if ((stripeEvent.Type == EventTypes.CheckoutSessionCompleted ||
                 stripeEvent.Type == EventTypes.CheckoutSessionAsyncPaymentSucceeded) &&
                stripeEvent.Data.Object is Session session)
            {
                RecordPayment(session);
            }

            // Answer 200 to every genuine event, also the ones we ignore: otherwise Stripe retries them for days
            return Ok();
        }

        private void RecordPayment(Session session)
        {
            // Some payment methods finish later; Stripe then sends checkout.session.async_payment_succeeded
            if (session.PaymentStatus != "paid")
                return;

            var order = _unitOfWork.OrderHeader.Get(o => o.SessionId == session.Id);
            if (order == null)
            {
                _logger.LogWarning("Stripe webhook for unknown checkout session {SessionId}", session.Id);
                return;
            }

            var isCustomerCheckout = order.PaymentStatus != SD.PaymentStatusDelayedPayment;
            if (!_unitOfWork.OrderHeader.MarkPaid(order.Id, session.Id, session.PaymentIntentId))
                return;   // already recorded by the confirmation page or an earlier delivery of this event

            // The customer paid for their cart, even if they never came back to the confirmation page
            if (isCustomerCheckout)
            {
                var cartItems = _unitOfWork.ShoppingCart.GetAll(c => c.ApplicationUserId == order.ApplicationUserId);
                _unitOfWork.ShoppingCart.RemoveRange(cartItems);
            }

            _unitOfWork.Save();
            _logger.LogInformation("Order {OrderId} marked as paid by Stripe webhook", order.Id);
        }
    }
}
