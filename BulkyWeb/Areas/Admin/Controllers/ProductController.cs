using Bulky.DataAccess.Data;
using Bulky.DataAccess.Repository;
using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using Bulky.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BulkyWeb.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ProductController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        public ProductController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }
        public IActionResult Index()
        {
            var productList = _unitOfWork.Product.GetAll().ToList();
            
            return View(productList);
        }

        /// <summary>
        /// Update or Insert
        /// </summary>
        /// <returns></returns>
        public IActionResult Upsert(int? id) 
        {

            ProductVM productVM = new()
            {
                CategoryList = _unitOfWork.Category.GetAll()
                .Select(a => new SelectListItem
                {
                    Text = a.Name,
                    Value = a.Id.ToString()
                })
                ,
                Product = new Product()
            };
            //---------------------------------
            // Create
            if (id == null || id == 0)
            {
                return View(productVM);
            }
            else // Update
            {
                productVM.Product = _unitOfWork.Product.Get(a => a.Id == id);
                return View(productVM);
            }
        }

        [HttpPost]
        public IActionResult Upsert(ProductVM productVM, IFormFile? file)
        {
            if (ModelState.IsValid)
            {
                _unitOfWork.Product.Add(productVM.Product);
                _unitOfWork.Save();
                TempData["success"] = "Product created successfully";
                return RedirectToAction("Index");
            }
            else
            {
                productVM.CategoryList = _unitOfWork.Category.GetAll()
                .Select(a => new SelectListItem
                {
                    Text = a.Name,
                    Value = a.Id.ToString()
                });
                return View(productVM);
            }
        }

        public IActionResult Delete(int? id)
        {
            if (id == null || id == 0)
            {
                return NotFound();
            }

            var product = _unitOfWork.Product.Get(a => a.Id == id);
            if (product == null)
            {
                return NotFound();
            }
            return View(product);
        }

        [HttpPost, ActionName("Delete")]
        public IActionResult DeletePost(int? id)
        {
            var product = _unitOfWork.Product.Get(a => a.Id == id);
            if (product == null)
            {
                return NotFound();
            }

            _unitOfWork.Product.Remove(product);
            _unitOfWork.Save();
            TempData["success"] = "Product deleted successfully";
            return RedirectToAction("Index");
        }
    }
}
