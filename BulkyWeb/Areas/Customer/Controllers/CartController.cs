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

            var cartItems = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == userId,
                includeProperties: "Product").ToList();
            if (cartItems.Count == 0)
            {
                TempData["error"] = "Your cart is empty.";
                return RedirectToAction(nameof(Index));
            }

            // Only the six shipping fields come from the form. Model binding would fill ANY OrderHeader property
            // that is posted (Id, OrderTotal, PaymentStatus, PaymentIntentId...), so the order is built from
            // scratch instead of saving the bound object (over-posting / mass assignment).
            var shipping = ShoppingCartVM.OrderHeader;
            if (new[] { shipping.Name, shipping.PhoneNumber, shipping.StreetAddress, shipping.City, shipping.State, shipping.PostalCode }
                .Any(string.IsNullOrWhiteSpace))
            {
                TempData["error"] = "Please fill in all shipping details.";
                return RedirectToAction(nameof(Summary));
            }

            var applicationUser = _unitOfWork.ApplicationUser.Get(a => a.Id == userId);
            var isCompany = applicationUser.CompanyId.GetValueOrDefault() != 0;

            var orderHeader = new OrderHeader
            {
                ApplicationUserId = userId,
                OrderDate = DateTime.Now,
                Name = shipping.Name,
                PhoneNumber = shipping.PhoneNumber,
                StreetAddress = shipping.StreetAddress,
                City = shipping.City,
                State = shipping.State,
                PostalCode = shipping.PostalCode,
                // Company users get the order approved now and pay later; everyone else pays now
                OrderStatus = isCompany ? SD.StatusApproved : SD.StatusPending,
                PaymentStatus = isCompany ? SD.PaymentStatusDelayedPayment : SD.PaymentStatusPending
            };
            _unitOfWork.OrderHeader.Add(orderHeader);

            foreach (var item in cartItems)
            {
                item.Price = GetPriceBasedOnQuantity(item);
                orderHeader.OrderTotal += item.Price * item.Count;

                // Linked through the navigation property, so EF fills in OrderHeaderId when it saves
                _unitOfWork.OrderDetail.Add(new OrderDetail
                {
                    OrderHeader = orderHeader,
                    ProductId = item.ProductId,
                    Price = item.Price,
                    Count = item.Count
                });
            }

            // ONE SaveChanges for the header and all lines: EF Core runs it in a single database transaction,
            // so either the whole order is saved or nothing is (no half-saved orders).
            _unitOfWork.Save();
            ShoppingCartVM.OrderHeader = orderHeader;
            ShoppingCartVM.ShoppingCartList = cartItems;

            // User is a regular customer account and we need to capture payment
            if (!isCompany)
            {
                // Stripe Logic. The call to Stripe happens after the order is saved, outside the database
                // transaction: holding a transaction open while waiting for another server would block the database.
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

                Response.Headers.Location = session.Url;
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
