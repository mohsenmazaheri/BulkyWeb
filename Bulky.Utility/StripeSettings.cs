using System;
using System.Collections.Generic;
using System.Text;

namespace Bulky.Utility
{
    public class StripeSettings
    {
        public String SecretKey { get; set; }
        public String PublishableKey { get; set; }

        /// <summary>
        /// Signing secret of the webhook endpoint ("whsec_..."), used to check that a webhook really comes from Stripe.
        /// </summary>
        public String? WebhookSecret { get; set; }
    }
}
