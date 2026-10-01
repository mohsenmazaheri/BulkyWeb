using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using Bulky.Tests.Helpers;
using Bulky.Utility;
using BulkyWeb.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Bulky.Tests.Controllers
{
    /// <summary>
    /// Uses Moq instead of a database: the tests only check what the controller asks
    /// the Unit of Work to do, not how the data is stored.
    /// </summary>
    public class CategoryControllerTests
    {
        private readonly Mock<ICategoryRepository> _categoryRepo = new();
        private readonly Mock<IUnitOfWork> _unitOfWork = new();
        private readonly CategoryController _controller;

        public CategoryControllerTests()
        {
            _unitOfWork.Setup(u => u.Category).Returns(_categoryRepo.Object);
            _controller = new CategoryController(_unitOfWork.Object).WithUser("admin", SD.Role_Admin);
        }

        [Fact]
        public void Create_ValidCategory_AddsSavesAndRedirects()
        {
            var category = new Category { Name = "Poetry", DisplayOrder = 5 };

            var result = _controller.Create(category);

            _categoryRepo.Verify(r => r.Add(category), Times.Once);
            _unitOfWork.Verify(u => u.Save(), Times.Once);
            Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
            Assert.Equal("Category created successfully", _controller.TempData["success"]);
        }

        [Fact]
        public void Create_NameEqualsDisplayOrder_ReturnsViewWithoutSaving()
        {
            var category = new Category { Name = "5", DisplayOrder = 5 };

            var result = _controller.Create(category);

            Assert.IsType<ViewResult>(result);
            Assert.True(_controller.ModelState.ContainsKey("Name"));
            _unitOfWork.Verify(u => u.Save(), Times.Never);
        }

        [Theory]
        [InlineData("test")]
        [InlineData("TEST")]
        public void Create_ReservedNameTest_ReturnsViewWithoutSaving(string name)
        {
            var result = _controller.Create(new Category { Name = name, DisplayOrder = 1 });

            Assert.IsType<ViewResult>(result);
            _categoryRepo.Verify(r => r.Add(It.IsAny<Category>()), Times.Never);
            _unitOfWork.Verify(u => u.Save(), Times.Never);
        }
    }
}
