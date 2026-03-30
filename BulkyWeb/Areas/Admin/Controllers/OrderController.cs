using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using Bulky.Models.ViewModels;
using Bulky.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Build.Tasks;
using Microsoft.IdentityModel.Tokens;
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
            OrderVM = new()
            {
                OrderHeader = _unitOfWork.OrderHeader.Get(u => u.Id == orderId, includeProperties: "ApplicationUser"),
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

        #region API CALLS
        [HttpGet]
        public IActionResult GetAll(string status)
        {
            IEnumerable<OrderHeader> orderHeaders = null;

            if (User.IsInRole(SD.Role_Admin) || User.IsInRole(SD.Role_Employee))
                orderHeaders = _unitOfWork.OrderHeader.GetAll(null, includeProperties: "ApplicationUser").ToList();
            else
            {
                ClaimsIdentity? claimsIdentity = (ClaimsIdentity)User.Identity;
                if (claimsIdentity != null)
                {
                    string userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;
                    orderHeaders = _unitOfWork.OrderHeader.GetAll(a => a.ApplicationUserId == userId, includeProperties: "ApplicationUser");
                }
            }

            if (orderHeaders != null)
            {
                switch (status)
                {
                    case "pending":
                        orderHeaders = orderHeaders.Where(o => o.PaymentStatus == SD.PaymentStatusDelayedPayment);
                        //orderHeaders = orderHeaders.Where(o => o.PaymentStatus == SD.StatusPending);
                        break;
                    case "inprocess":
                        orderHeaders = orderHeaders.Where(o => o.PaymentStatus == SD.StatusInProcess);
                        break;
                    case "completed":
                        orderHeaders = orderHeaders.Where(o => o.PaymentStatus == SD.StatusShipped);
                        break;
                    case "approved":
                        orderHeaders = orderHeaders.Where(o => o.PaymentStatus == SD.StatusApproved);
                        break;
                    default:
                        break;
                }
            }

            return Json(new { data = orderHeaders });
        }

        #endregion
    }
}
