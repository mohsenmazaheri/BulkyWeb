using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using Bulky.Models.ViewModels;
using Bulky.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;
using Stripe.Climate;
using System.Diagnostics;
using System.Security.Claims;

namespace BulkyWeb.Areas.Admin.Controllers
{
    [Area("admin")]
    [Authorize]
    public class OrderController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;

        [BindProperty]
        public OrderVM OrderVM { get; set; }

        public OrderController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Details(int orderId)
        {
            var orderHeader = GetOrderForCurrentUser(orderId, includeProperties: "ApplicationUser");
            if (orderHeader == null)
                return NotFound();

            OrderVM = new()
            {
                OrderHeader = orderHeader,
                OrderDetails = _unitOfWork.OrderDetail.GetAll(u => u.OrderHeaderId == orderId, includeProperties: "Product")
            };
            return View(OrderVM);
        }

        [HttpPost]
        [Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
        public IActionResult UpdateOrderDetail()
        {
            var orderHeaderFromDb = _unitOfWork.OrderHeader.Get(a => a.Id == OrderVM.OrderHeader.Id);
            orderHeaderFromDb.Name = OrderVM.OrderHeader.Name;
            orderHeaderFromDb.PhoneNumber = OrderVM.OrderHeader.PhoneNumber;
            orderHeaderFromDb.StreetAddress = OrderVM.OrderHeader.StreetAddress;
            orderHeaderFromDb.City = OrderVM.OrderHeader.City;
            orderHeaderFromDb.State = OrderVM.OrderHeader.State;
            orderHeaderFromDb.PostalCode = OrderVM.OrderHeader.PostalCode;
            if (!string.IsNullOrEmpty(OrderVM.OrderHeader.Carrier))
                orderHeaderFromDb.Carrier = OrderVM.OrderHeader.Carrier;
            if (!string.IsNullOrEmpty(OrderVM.OrderHeader.TrackingNumber))
                orderHeaderFromDb.TrackingNumber = OrderVM.OrderHeader.TrackingNumber;

            _unitOfWork.OrderHeader.Update(orderHeaderFromDb);
            _unitOfWork.Save();

            TempData["Success"] = "Order Details Updated Successfully.";
            return RedirectToAction(nameof(Details), new { orderId = orderHeaderFromDb.Id });
        }

        [HttpPost]
        [Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
        public IActionResult StartProcessing()
        {
            _unitOfWork.OrderHeader.UpdateStatus(OrderVM.OrderHeader.Id, SD.StatusInProcess);
            _unitOfWork.Save();
            TempData["Success"] = "Order Details Updated Successfully.";
            return RedirectToAction(nameof(Details), new { orderId = OrderVM.OrderHeader.Id });
        }

        [HttpPost]
        [Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
        public IActionResult ShipOrder()
        {
            var orderHeader = _unitOfWork.OrderHeader.Get(a => a.Id == OrderVM.OrderHeader.Id);
            orderHeader.TrackingNumber = OrderVM.OrderHeader.TrackingNumber;
            orderHeader.Carrier = OrderVM.OrderHeader.Carrier;
            orderHeader.OrderStatus = SD.StatusShipped;
            orderHeader.ShippingDate = DateTime.Now;
            if(orderHeader.PaymentStatus == SD.PaymentStatusDelayedPayment)
            {
                orderHeader.PaymentDueDate = DateOnly.FromDateTime(DateTime.Now.AddDays(30));
            }
            
            _unitOfWork.OrderHeader.Update(orderHeader);
            _unitOfWork.Save();
            TempData["Success"] = "Order Shipped Successfully.";
            return RedirectToAction(nameof(Details), new { orderId = OrderVM.OrderHeader.Id });
        }
        [HttpPost]
        [Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
        public IActionResult CancelOrder()
        {
            var orderHeader = _unitOfWork.OrderHeader.Get(a => a.Id == OrderVM.OrderHeader.Id);

            // Have to make a refund in the stripe
            if(orderHeader.PaymentStatus == SD.PaymentStatusApproved)
            {
                var options = new RefundCreateOptions
                {
                    Reason = RefundReasons.RequestedByCustomer,
                    PaymentIntent = orderHeader.PaymentIntentId
                };

                var service = new RefundService();
                Refund refund = service.Create(options);

                _unitOfWork.OrderHeader.UpdateStatus(orderHeader.Id, SD.StatusCancelled, SD.StatusRefunded);
            }
            else // Cancelling without refund
                _unitOfWork.OrderHeader.UpdateStatus(orderHeader.Id, SD.StatusCancelled, SD.StatusCancelled);

            _unitOfWork.Save();
            TempData["Success"] = "Order Cancelled Successfully.";
            return RedirectToAction(nameof(Details), new { orderId = OrderVM.OrderHeader.Id });

        }

        [ActionName("Details")]
        [HttpPost]
        public IActionResult DetailsPayNow()
        {
            // The order id comes from a hidden form field, so it must be checked like any URL id
            var orderHeader = GetOrderForCurrentUser(OrderVM.OrderHeader.Id, includeProperties: "ApplicationUser");
            if (orderHeader == null)
                return NotFound();

            OrderVM.OrderHeader = orderHeader;
            OrderVM.OrderDetails = _unitOfWork.OrderDetail.GetAll(u => u.OrderHeaderId == orderHeader.Id, includeProperties: "Product");

            // Stripe Logic
            var options = new Stripe.Checkout.SessionCreateOptions
            {
                SuccessUrl = GetPaymentConfirmationUrl(OrderVM.OrderHeader.Id),
                CancelUrl = GetOrderDetailsUrl(OrderVM.OrderHeader.Id),
                LineItems = new List<Stripe.Checkout.SessionLineItemOptions>(),
                Mode = "payment",
            };

            foreach (var item in OrderVM.OrderDetails)
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

            _unitOfWork.OrderHeader.UpdateStripePaymentId(OrderVM.OrderHeader.Id, session.Id, session.PaymentIntentId);
            _unitOfWork.Save();

            Response.Headers.Add("Location", session.Url);
            return new StatusCodeResult(303);
        }

        public IActionResult PaymentConfirmation(int orderHeaderId)
        {
            var orderHeader = GetOrderForCurrentUser(orderHeaderId);
            if (orderHeader == null)
                return NotFound();

            if (orderHeader.PaymentStatus == SD.PaymentStatusDelayedPayment)
            {
                // This is an order by company
                var service = new SessionService();
                Session session = service.Get(orderHeader.SessionId);

                // session.PaymentStatus is "paid" or "unpaid" or "no_payment_required"
                if (session.PaymentStatus.ToLower() == "paid")
                {
                    _unitOfWork.OrderHeader.UpdateStripePaymentId(orderHeaderId, session.Id, session.PaymentIntentId);
                    _unitOfWork.OrderHeader.UpdateStatus(orderHeaderId, orderHeader.OrderStatus, SD.PaymentStatusApproved);
                    _unitOfWork.Save();
                }
            }

            return View(orderHeaderId);
        }
        #region API CALLS

        [HttpGet]
        public IActionResult GetAll(string? status)
        {
            // Staff see every order, everyone else only their own
            var userId = User.IsStaff() ? null : User.GetUserId();
            IEnumerable<OrderHeader> orderHeaders = _unitOfWork.OrderHeader.GetAll(
                userId == null ? null : o => o.ApplicationUserId == userId,
                includeProperties: "ApplicationUser");

            // The tabs on the Manage Order page. "pending" is about payment: company orders that are paid later.
            // The others are about the order itself, so they compare OrderStatus.
            orderHeaders = status switch
            {
                "pending" => orderHeaders.Where(o => o.PaymentStatus == SD.PaymentStatusDelayedPayment),
                "inprocess" => orderHeaders.Where(o => o.OrderStatus == SD.StatusInProcess),
                "completed" => orderHeaders.Where(o => o.OrderStatus == SD.StatusShipped),
                "approved" => orderHeaders.Where(o => o.OrderStatus == SD.StatusApproved),
                _ => orderHeaders
            };

            // Only the columns the DataTable shows (order.js). Serializing the entities would also send
            // the whole ApplicationUser, including its PasswordHash and SecurityStamp, to the browser.
            var rows = orderHeaders.Select(o => new
            {
                o.Id,
                o.Name,
                o.PhoneNumber,
                ApplicationUser = new { o.ApplicationUser.Email },
                o.OrderStatus,
                o.OrderTotal
            });

            return Json(new { data = rows });
        }

        #endregion

        // Stripe sends the company user back to these pages after "Pay now". They are built from the current request
        // (scheme + host) and the app's routes, so they work on any domain or port, not only https://localhost:7197.
        internal string GetPaymentConfirmationUrl(int orderHeaderId) =>
            Url.Action(nameof(PaymentConfirmation), "Order", new { area = "Admin", orderHeaderId }, Request.Scheme)!;

        internal string GetOrderDetailsUrl(int orderId) =>
            Url.Action(nameof(Details), "Order", new { area = "Admin", orderId }, Request.Scheme)!;

        /// <summary>
        /// Loads an order only if the current user may access it:
        /// staff (Admin/Employee) can open any order, everyone else only their own.
        /// Returns null otherwise, so callers can answer with NotFound().
        /// </summary>
        private OrderHeader? GetOrderForCurrentUser(int orderId, string? includeProperties = null)
        {
            if (User.IsStaff())
                return _unitOfWork.OrderHeader.Get(o => o.Id == orderId, includeProperties);

            var userId = User.GetUserId();
            return _unitOfWork.OrderHeader.Get(o => o.Id == orderId && o.ApplicationUserId == userId, includeProperties);
        }
    }
}
