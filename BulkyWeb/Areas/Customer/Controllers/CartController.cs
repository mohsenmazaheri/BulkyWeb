using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using Bulky.Models.ViewModels;
using Bulky.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

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
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

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
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

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
            }

            return RedirectToAction(nameof(OrderConfirmation), new {id = ShoppingCartVM.OrderHeader.Id});
        }

        public IActionResult OrderConfirmation(int id)
        {
            return View(id);
        }

        public IActionResult Plus(int cartId)
        {
            var cartFromDB = _unitOfWork.ShoppingCart.Get(a => a.Id == cartId);
            cartFromDB.Count += 1;
            _unitOfWork.ShoppingCart.Update(cartFromDB);
            _unitOfWork.Save();
            return RedirectToAction(nameof(Index));
        }

        public IActionResult Minus(int cartId)
        {
            var cartFromDB = _unitOfWork.ShoppingCart.Get(a => a.Id == cartId);
            if (cartFromDB.Count <= 1)
            {
                // Remove it from the cart
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

        public IActionResult Remove(int cartId)
        {
            var cartFromDB = _unitOfWork.ShoppingCart.Get(a => a.Id == cartId);
            _unitOfWork.ShoppingCart.Remove(cartFromDB);
            _unitOfWork.Save();
            return RedirectToAction(nameof(Index));
        }

        private double GetPriceBasedOnQuantity(ShoppingCart shoppingCart)
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
