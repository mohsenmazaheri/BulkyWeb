using Bulky.DataAccess.Repository.IRepository;
using Bulky.Tests.Helpers;
using Bulky.Utility;
using BulkyWeb.Areas.Admin.Controllers;
using BulkyWeb.Areas.Customer.Controllers;
using Moq;

namespace Bulky.Tests.Controllers
{
    /// <summary>
    /// Stripe sends customers back to these URLs after paying. They were hard-coded to https://localhost:7197,
    /// which breaks on any other port and on every real domain.
    /// </summary>
    public class StripeReturnUrlTests
    {
        private static CartController Cart(string scheme, string host) =>
            new CartController(Mock.Of<IUnitOfWork>()).WithUser("customer", SD.Role_Customer).WithRealUrls(scheme, host);

        private static OrderController Orders(string scheme, string host) =>
            new OrderController(Mock.Of<IUnitOfWork>()).WithUser("company", SD.Role_Company).WithRealUrls(scheme, host);

        [Theory]
        [InlineData("https", "shop.example.com")]
        [InlineData("http", "localhost:5095")]       // the "http" launch profile, which the old code broke
        [InlineData("https", "localhost:7197")]
        public void Checkout_ReturnUrls_UseTheDomainOfTheCurrentRequest(string scheme, string host)
        {
            var cart = Cart(scheme, host);

            Assert.Equal($"{scheme}://{host}/Customer/Cart/OrderConfirmation/42", cart.GetOrderConfirmationUrl(42));
            Assert.Equal($"{scheme}://{host}/Customer/Cart", cart.GetCartUrl());
        }

        [Fact]
        public void CompanyPayNow_ReturnUrls_UseTheDomainOfTheCurrentRequest()
        {
            var orders = Orders("https", "shop.example.com");

            Assert.Equal("https://shop.example.com/Admin/Order/PaymentConfirmation?orderHeaderId=7", orders.GetPaymentConfirmationUrl(7));
            Assert.Equal("https://shop.example.com/Admin/Order/Details?orderId=7", orders.GetOrderDetailsUrl(7));
        }
    }
}
