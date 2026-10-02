namespace Bulky.Utility
{
    public static class Money
    {
        /// <summary>
        /// Converts a dollar amount to the whole cents Stripe expects ($19.99 => 1999).
        /// Money is decimal everywhere: with double, (long)(19.99 * 100) was 1998, one cent short.
        /// </summary>
        public static long ToStripeAmount(decimal amount)
        {
            return (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
        }
    }
}
