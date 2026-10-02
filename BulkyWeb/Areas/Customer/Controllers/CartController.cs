using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using Bulky.Models.ViewModels;
using Bulky.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe.Checkout;
using System.Security.Claims;

namespace BulkyWeb.Areas.Customer.Controllers
{
    [Area("Customer")]
    [Authorize]
    public class CartController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        [BindProperty]
        public ShoppingCartVM ShoppingCartVM { get; set; }

        public CartController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }
        
        public IActionResult Index()
        {
            var userId = User.GetUserId();

            ShoppingCartVM = new()
            {
                ShoppingCartList = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == userId,
                includeProperties: "Product"),
                OrderHeader = new()
            };

            foreach (var item in ShoppingCartVM.ShoppingCartList)
            {
                item.Price = GetPriceBasedOnQuantity(item);
                ShoppingCartVM.OrderHeader.OrderTotal += item.Price * item.Count;
            }

            return View(ShoppingCartVM);
        }

        public IActionResult Summary()
        {
            var userId = User.GetUserId();

            ShoppingCartVM = new()
            {
                ShoppingCartList = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == userId,
                includeProperties: "Product"),
                OrderHeader = new()
            };

            ShoppingCartVM.OrderHeader.ApplicationUser = _unitOfWork.ApplicationUser.Get(a => a.Id == userId);

            ShoppingCartVM.OrderHeader.Name = ShoppingCartVM.OrderHeader.ApplicationUser.Name;
            ShoppingCartVM.OrderHeader.PhoneNumber = ShoppingCartVM.OrderHeader.ApplicationUser.PhoneNumber;
            ShoppingCartVM.OrderHeader.StreetAddress = ShoppingCartVM.OrderHeader.ApplicationUser.StreetAddress;
            ShoppingCartVM.OrderHeader.City = ShoppingCartVM.OrderHeader.ApplicationUser.City;
            ShoppingCartVM.OrderHeader.State = ShoppingCartVM.OrderHeader.ApplicationUser.State;
            ShoppingCartVM.OrderHeader.PostalCode = ShoppingCartVM.OrderHeader.ApplicationUser.PostalCode;

            foreach (var item in ShoppingCartVM.ShoppingCartList)
            {
                item.Price = GetPriceBasedOnQuantity(item);
                ShoppingCartVM.OrderHeader.OrderTotal += item.Price * item.Count;
            }
            return View(ShoppingCartVM);
        }

        [HttpPost]
        [ActionName("Summary")]
        public IActionResult SummaryPOST()
        {
            var userId = User.GetUserId();

            ShoppingCartVM.ShoppingCartList = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == userId,
                includeProperties: "Product");

            ShoppingCartVM.OrderHeader.OrderDate = DateTime.Now;
            ShoppingCartVM.OrderHeader.ApplicationUserId =userId;

            ApplicationUser applicationUser = _unitOfWork.ApplicationUser.Get(a => a.Id == userId);

            foreach (var item in ShoppingCartVM.ShoppingCartList)
            {
                item.Price = GetPriceBasedOnQuantity(item);
                ShoppingCartVM.OrderHeader.OrderTotal += item.Price * item.Count;
            }

            // User is a regular customer account
            if(applicationUser.CompanyId.GetValueOrDefault() == 0)
            {
                ShoppingCartVM.OrderHeader.PaymentStatus = SD.PaymentStatusPending;
                ShoppingCartVM.OrderHeader.OrderStatus = SD.StatusPending;
            }
            else // User is a company with delayed payment
            {
                ShoppingCartVM.OrderHeader.PaymentStatus = SD.PaymentStatusDelayedPayment;
                ShoppingCartVM.OrderHeader.OrderStatus = SD.StatusApproved;
            }

            _unitOfWork.OrderHeader.Add(ShoppingCartVM.OrderHeader);
            _unitOfWork.Save();

            foreach (var item in ShoppingCartVM.ShoppingCartList)
            {
                OrderDetail orderDetail = new()
                {
                    ProductId = item.ProductId,
                    OrderHeaderId = ShoppingCartVM.OrderHeader.Id,
                    Price = item.Price,
                    Count = item.Count
                };
                _unitOfWork.OrderDetail.Add(orderDetail);
                _unitOfWork.Save();
            }

            // User is a regular customer account and we need to capture payment
            if (applicationUser.CompanyId.GetValueOrDefault() == 0)
            {
                // Stripe Logic
                var options = new Stripe.Checkout.SessionCreateOptions
                {
                    SuccessUrl = GetOrderConfirmationUrl(ShoppingCartVM.OrderHeader.Id),
                    CancelUrl = GetCartUrl(),
                    LineItems = new List<Stripe.Checkout.SessionLineItemOptions>(),
                    Mode = "payment",
                };

                foreach (var item in ShoppingCartVM.ShoppingCartList)
                {
                    var sessionLineItem = new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            UnitAmount = Money.ToStripeAmount(item.Price), // $20.50 ==> 2050 (exact: decimal, not double)
                            Currency = "usd",
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = item.Product.Title
                            }
                        },
                        Quantity = item.Count
                    };
                    options.LineItems.Add(sessionLineItem);
                }

                var service = new Stripe.Checkout.SessionService();
                Stripe.Checkout.Session session = service.Create(options);

                _unitOfWork.OrderHeader.UpdateStripePaymentId(ShoppingCartVM.OrderHeader.Id, session.Id, session.PaymentIntentId);
                _unitOfWork.Save();

                Response.Headers.Add("Location", session.Url);
                return new StatusCodeResult(303);
            }

            return RedirectToAction(nameof(OrderConfirmation), new {id = ShoppingCartVM.OrderHeader.Id});
        }

        public IActionResult OrderConfirmation(int id)
        {
            var userId = User.GetUserId();
            // Only the customer who placed the order lands here after checkout
            OrderHeader orderHeader = _unitOfWork.OrderHeader.Get(o => o.Id == id && o.ApplicationUserId == userId, includeProperties: "ApplicationUser");
            if (orderHeader == null)
                return NotFound();

            if(orderHeader.PaymentStatus != SD.PaymentStatusDelayedPayment)
            {
                // This is an order by customer
                var service = new SessionService();
                Session session = service.Get(orderHeader.SessionId);

                // session.PaymentStatus is "paid" or "unpaid" or "no_payment_required"
                if (session.PaymentStatus.ToLower() == "paid")
                {
                    // The Stripe webhook may already have recorded this payment; MarkPaid then does nothing
                    if (_unitOfWork.OrderHeader.MarkPaid(id, session.Id, session.PaymentIntentId))
                        _unitOfWork.Save();

                    HttpContext.Session.Clear();
                }
            }

            List<ShoppingCart> shoppingCarts = _unitOfWork.ShoppingCart
                .GetAll(a => a.ApplicationUserId == userId).ToList();
            _unitOfWork.ShoppingCart.RemoveRange(shoppingCarts);
            _unitOfWork.Save();

            return View(id);
        }

        [HttpPost]
        public IActionResult Plus(int cartId)
        {
            var userId = User.GetUserId();
            // The owner check is part of the query: another user's cartId returns null
            var cartFromDB = _unitOfWork.ShoppingCart.Get(a => a.Id == cartId && a.ApplicationUserId == userId);
            if (cartFromDB == null)
                return NotFound();

            cartFromDB.Count += 1;
            _unitOfWork.ShoppingCart.Update(cartFromDB);
            _unitOfWork.Save();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public IActionResult Minus(int cartId)
        {
            var userId = User.GetUserId();
            var cartFromDB = _unitOfWork.ShoppingCart.Get(a => a.Id == cartId && a.ApplicationUserId == userId, tracked: true);
            if (cartFromDB == null)
                return NotFound();

            if (cartFromDB.Count <= 1)
            {
                // Remove it from the cart and the session
                HttpContext.Session.SetInt32(SD.SessionCart, _unitOfWork.ShoppingCart.GetAll(a => a.ApplicationUserId == userId).Count() - 1);
                _unitOfWork.ShoppingCart.Remove(cartFromDB);
            }
            else
            {
                cartFromDB.Count -= 1;
                _unitOfWork.ShoppingCart.Update(cartFromDB);
            }
            _unitOfWork.Save();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public IActionResult Remove(int cartId)
        {
            var userId = User.GetUserId();
            var cartFromDB = _unitOfWork.ShoppingCart.Get(a => a.Id == cartId && a.ApplicationUserId == userId, tracked: true);
            if (cartFromDB == null)
                return NotFound();

            HttpContext.Session.SetInt32(SD.SessionCart, _unitOfWork.ShoppingCart.GetAll(a => a.ApplicationUserId == userId).Count() - 1);
            _unitOfWork.ShoppingCart.Remove(cartFromDB);
            _unitOfWork.Save();
            return RedirectToAction(nameof(Index));
        }

        // Stripe sends the customer back to these pages after checkout. They are built from the current request
        // (scheme + host) and the app's routes, so they work on any domain or port, not only https://localhost:7197.
        internal string GetOrderConfirmationUrl(int orderId) =>
            Url.Action(nameof(OrderConfirmation), "Cart", new { area = "Customer", id = orderId }, Request.Scheme)!;

        internal string GetCartUrl() =>
            Url.Action(nameof(Index), "Cart", new { area = "Customer" }, Request.Scheme)!;

        private decimal GetPriceBasedOnQuantity(ShoppingCart shoppingCart)
        {
            if(shoppingCart.Count <= 50)
                return shoppingCart.Product.Price;
            else if (shoppingCart.Count <= 100)
                return shoppingCart.Product.Price50;
            else 
                return shoppingCart.Product.Price100;
        }
    }
}
