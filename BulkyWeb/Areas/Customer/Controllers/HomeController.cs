using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using Bulky.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Security.Claims;

namespace BulkyWeb.Areas.Customer.Controllers
{
    [Area("Customer")]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IUnitOfWork _unitOfWork;

        public HomeController(ILogger<HomeController> logger, IUnitOfWork unitOfWork)
        {
            _logger = logger;
            _unitOfWork = unitOfWork;
        }

        public IActionResult Index()
        {
            
            IEnumerable<Product> productList = _unitOfWork.Product.GetAll(p => !p.IsDeleted, includeProperties: "Category");
            return View(productList);
        }

        public IActionResult Details(int productId)
        {
            var product = _unitOfWork.Product.Get(a => a.Id == productId && !a.IsDeleted, includeProperties: "Category");
            if (product == null)
                return NotFound();

            ShoppingCart shoppingCart = new()
            {
                Product = product,
                Count = 1,
                ProductId = productId
            };
            return View(shoppingCart);
        }

        [HttpPost]
        [Authorize]
        public IActionResult Details(ShoppingCart shoppingCart)
        {
            // The product id comes from the form: it must be a product that is still for sale
            if (_unitOfWork.Product.Get(p => p.Id == shoppingCart.ProductId && !p.IsDeleted) == null)
                return NotFound();

            var userId = User.GetUserId();

            // [Range(1, 1000)] on ShoppingCart.Count is only enforced in the browser: check it here too,
            // otherwise a posted Count=-5 puts "minus five books" in the cart (and a negative total in the order)
            const int MaxQuantity = 1000;
            ShoppingCart cartFromDB = _unitOfWork.ShoppingCart.Get(a => a.ApplicationUserId == userId && a.ProductId == shoppingCart.ProductId);
            var newCount = (cartFromDB?.Count ?? 0) + shoppingCart.Count;
            if (shoppingCart.Count < 1 || newCount > MaxQuantity)
            {
                TempData["error"] = $"Please choose a quantity between 1 and {MaxQuantity} per book.";
                return RedirectToAction(nameof(Details), new { productId = shoppingCart.ProductId });
            }

            // If the product already exists in the shopping cart,
            // We must Update its count
            if (cartFromDB != null)
            {
                cartFromDB.Count = newCount;
                _unitOfWork.ShoppingCart.Update(cartFromDB);
                _unitOfWork.Save();
            }
            else // It's new prosuct, So, Add it
            {
                // A new row with only the product and quantity from the form: a posted Id or owner is never saved
                _unitOfWork.ShoppingCart.Add(new ShoppingCart
                {
                    ApplicationUserId = userId,
                    ProductId = shoppingCart.ProductId,
                    Count = shoppingCart.Count
                });
                _unitOfWork.Save();
                // Adding the number of the cart items to the session
                HttpContext.Session.SetInt32(SD.SessionCart, _unitOfWork.ShoppingCart.GetAll(a => a.ApplicationUserId == userId).Count());
            }
            TempData["success"] = "Cart updated successfully";

            // Go to Home page
            return RedirectToAction(nameof(Index));
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
