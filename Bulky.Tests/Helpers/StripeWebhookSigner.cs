using System.Security.Cryptography;
using System.Text;
using Stripe;

namespace Bulky.Tests.Helpers
{
    /// <summary>
    /// Creates Stripe webhook payloads and signs them the way Stripe does, so tests can run the real
    /// signature check (EventUtility.ConstructEvent) without a Stripe account.
    /// </summary>
    public static class StripeWebhookSigner
    {
        public const string Secret = "whsec_test_secret_for_unit_tests";

        public static string CheckoutSessionEvent(string eventType, string sessionId, string paymentStatus, string paymentIntentId = "pi_test_123") => $$"""
            {
              "id": "evt_test_{{Guid.NewGuid():N}}",
              "object": "event",
              "api_version": "{{StripeConfiguration.ApiVersion}}",
              "created": {{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}},
              "livemode": false,
              "pending_webhooks": 1,
              "request": { "id": null, "idempotency_key": null },
              "type": "{{eventType}}",
              "data": {
                "object": {
                  "id": "{{sessionId}}",
                  "object": "checkout.session",
                  "mode": "payment",
                  "payment_status": "{{paymentStatus}}",
                  "payment_intent": "{{paymentIntentId}}"
                }
              }
            }
            """;

        /// <summary>The Stripe-Signature header: t=timestamp,v1=HMAC-SHA256(secret, "timestamp.payload") as hex.</summary>
        public static string Sign(string payload, string secret = Secret, DateTimeOffset? signedAt = null)
        {
            var timestamp = (signedAt ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
            return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
        }
    }
}
