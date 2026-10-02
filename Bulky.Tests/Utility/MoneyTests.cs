using Bulky.Utility;

namespace Bulky.Tests.Utility
{
    public class MoneyTests
    {
        // With double, (long)(19.99 * 100) is 1998: the customer was charged one cent less than the order total
        [Theory]
        [InlineData("20.50", 2050)]
        [InlineData("19.99", 1999)]
        [InlineData("4.35", 435)]
        [InlineData("0.29", 29)]
        [InlineData("9.95", 995)]
        [InlineData("1.15", 115)]
        [InlineData("1000", 100000)]
        public void ToStripeAmount_ConvertsDollarsToExactCents(string dollars, long expectedCents)
        {
            // InlineData cannot hold decimal constants, so the amount is parsed from a string
            var amount = decimal.Parse(dollars, System.Globalization.CultureInfo.InvariantCulture);

            Assert.Equal(expectedCents, Money.ToStripeAmount(amount));
        }

        [Theory]
        [InlineData("10.005", 1001)]   // half a cent rounds up, as people expect for money
        [InlineData("10.004", 1000)]
        public void ToStripeAmount_RoundsFractionsOfACent(string dollars, long expectedCents)
        {
            var amount = decimal.Parse(dollars, System.Globalization.CultureInfo.InvariantCulture);

            Assert.Equal(expectedCents, Money.ToStripeAmount(amount));
        }
    }
}
