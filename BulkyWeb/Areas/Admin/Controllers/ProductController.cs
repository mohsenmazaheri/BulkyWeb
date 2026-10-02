using Bulky.DataAccess.Data;
using Bulky.DataAccess.Repository;
using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using Bulky.Models.ViewModels;
using Bulky.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BulkyWeb.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SD.Role_Admin)]

    public class ProductController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        // Using IWebHostEnvironment injection for accessing wwwroot folder
        private readonly IWebHostEnvironment _webHostEnvironment;
        public ProductController(IUnitOfWork unitOfWork, IWebHostEnvironment webHostEnvironment)
        {
            _unitOfWork = unitOfWork;
            _webHostEnvironment = webHostEnvironment;
        }
        public IActionResult Index()
        {
            var productList = _unitOfWork.Product.GetAll(null, includeProperties:"Category").ToList();
            
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
                }),
                Product = new Bulky.Models.Product()
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
                string wwwRootPath = _webHostEnvironment.WebRootPath;
                if(file!= null)
                {
                    string fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
                    // Same casing as the folder in the repo: Linux file systems are case-sensitive
                    string productPath = Path.Combine(wwwRootPath, "Images", "Product");
                    // The folder is git-ignored, so it does not exist after a fresh clone
                    Directory.CreateDirectory(productPath);

                    if (!string.IsNullOrEmpty(productVM.Product.ImageURL))
                    {
                        // First delete the old
                        var oldImagePath = GetImageFilePath(productVM.Product.ImageURL);
                        if (oldImagePath != null && System.IO.File.Exists(oldImagePath))
                            System.IO.File.Delete(oldImagePath);
                    }
                    using(var fileStream = new FileStream(Path.Combine(productPath, fileName), FileMode.Create))
                    {
                        file.CopyTo(fileStream);
                    }

                    // URLs use forward slashes; "\" only worked because Windows browsers convert it
                    productVM.Product.ImageURL = "/Images/Product/" + fileName;
                }

                if (productVM.Product.Id == 0) //ADD
                {
                    _unitOfWork.Product.Add(productVM.Product);
                    TempData["success"] = "Product created successfully";
                }
                else // Update
                {
                    _unitOfWork.Product.Update(productVM.Product);
                    TempData["success"] = "Product updated successfully";
                }

                _unitOfWork.Save();
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

        //public IActionResult Delete(int? id)
        //{
        //    if (id == null || id == 0)
        //    {
        //        return NotFound();
        //    }

        //    var product = _unitOfWork.Product.Get(a => a.Id == id);
        //    if (product == null)
        //    {
        //        return NotFound();
        //    }
        //    return View(product);
        //}

        //[HttpPost, ActionName("Delete")]
        //public IActionResult DeletePost(int? id)
        //{
        //    var product = _unitOfWork.Product.Get(a => a.Id == id);
        //    if (product == null)
        //    {
        //        return NotFound();
        //    }

        //    _unitOfWork.Product.Remove(product);
        //    _unitOfWork.Save();
        //    TempData["success"] = "Product deleted successfully";
        //    return RedirectToAction("Index");
        //}

        #region API CALLS
        [HttpGet]
        public IActionResult GetAll()
        {
            var productList = _unitOfWork.Product.GetAll(null, includeProperties: "Category").ToList();
            return Json(new {data =  productList});
        }

        [HttpDelete]
        public IActionResult Delete(int? id)
        {
            var productToBeDeleted = _unitOfWork.Product.Get(a => a.Id == id);
            if (productToBeDeleted == null)
            {
                return Json(new { success = false, message = "Error while deleting"}); 
            }

            if (!string.IsNullOrEmpty(productToBeDeleted.ImageURL))
            {
                var oldImagePath = GetImageFilePath(productToBeDeleted.ImageURL);
                if (oldImagePath != null && System.IO.File.Exists(oldImagePath))
                    System.IO.File.Delete(oldImagePath);
            }

            _unitOfWork.Product.Remove(productToBeDeleted);
            _unitOfWork.Save();
            return Json(new { success = true, message = "Deleted Successfully" });
        }
        #endregion

        /// <summary>
        /// Converts a stored image URL ("/Images/Product/x.jpg", or the older "\images\product\x.jpg")
        /// into a file path, or returns null when it does not point inside wwwroot/Images/Product.
        /// On edit the URL comes from a hidden form field, so a tampered value such as
        /// "../../appsettings.json" or "C:/..." must never reach File.Delete.
        /// </summary>
        private string? GetImageFilePath(string imageUrl)
        {
            var relativePath = imageUrl.TrimStart('/', '\\').Replace('\\', '/');
            var fullPath = Path.GetFullPath(Path.Combine(_webHostEnvironment.WebRootPath, relativePath));

            var imagesFolder = Path.GetFullPath(Path.Combine(_webHostEnvironment.WebRootPath, "Images", "Product"))
                + Path.DirectorySeparatorChar;
            // Ignore case: older rows were stored as "\images\product\..." and Windows paths are case-insensitive
            return fullPath.StartsWith(imagesFolder, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
        }
    }
}
