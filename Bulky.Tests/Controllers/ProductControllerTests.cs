using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using Bulky.Models.ViewModels;
using Bulky.Tests.Helpers;
using Bulky.Utility;
using BulkyWeb.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Bulky.Tests.Controllers
{
    /// <summary>
    /// Image upload in Product Upsert, using a temporary folder as wwwroot.
    /// </summary>
    public class ProductControllerTests : IDisposable
    {
        private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "bulky-tests-" + Guid.NewGuid());
        private readonly Mock<IProductRepository> _productRepo = new();
        private readonly ProductController _controller;

        public ProductControllerTests()
        {
            Directory.CreateDirectory(_webRoot);
            var env = new Mock<IWebHostEnvironment>();
            env.Setup(e => e.WebRootPath).Returns(_webRoot);

            var unitOfWork = new Mock<IUnitOfWork>();
            unitOfWork.Setup(u => u.Product).Returns(_productRepo.Object);

            _controller = new ProductController(unitOfWork.Object, env.Object).WithUser("admin", SD.Role_Admin);
        }

        public void Dispose() => Directory.Delete(_webRoot, recursive: true);

        private static IFormFile FakeUpload(string fileName)
        {
            var content = new MemoryStream(new byte[] { 1, 2, 3 });
            return new FormFile(content, 0, content.Length, "file", fileName);
        }

        private static ProductVM Edit(string imageUrl) => new()
        {
            Product = new Product { Id = 1, Title = "Book", ImageURL = imageUrl },
            CategoryList = []
        };

        [Fact]
        public void Upsert_NewImage_CreatesMissingFolder_AndStoresForwardSlashUrl()
        {
            _controller.Upsert(Edit(""), FakeUpload("cover.jpg"));

            var files = Directory.GetFiles(Path.Combine(_webRoot, "Images", "Product"));
            var saved = Assert.Single(files);
            _productRepo.Verify(r => r.Update(It.Is<Product>(p =>
                p.ImageURL == "/Images/Product/" + Path.GetFileName(saved))), Times.Once);
        }

        [Theory]
        [InlineData("/Images/Product/old.jpg")]    // current format
        // Format stored before Lesson 06 (backslashes). Older rows are also lowercase, but those only exist on
        // Windows, where paths ignore case; CI runs on Linux, so this case uses the real folder casing.
        [InlineData(@"\Images\Product\old.jpg")]
        public void Upsert_ReplacingImage_DeletesOldFile(string oldImageUrl)
        {
            var folder = Path.Combine(_webRoot, "Images", "Product");
            Directory.CreateDirectory(folder);
            var oldFile = Path.Combine(folder, "old.jpg");
            File.WriteAllBytes(oldFile, [1]);

            _controller.Upsert(Edit(oldImageUrl), FakeUpload("new.jpg"));

            Assert.False(File.Exists(oldFile));
            Assert.Single(Directory.GetFiles(folder));
        }

        // ---------- Security: the old image URL comes from a hidden form field ----------

        [Fact]
        public void Upsert_TamperedRelativeImageUrl_DoesNotDeleteFilesOutsideImageFolder()
        {
            var secret = Path.Combine(_webRoot, "appsettings.json");
            File.WriteAllText(secret, "{}");

            _controller.Upsert(Edit("/Images/Product/../../appsettings.json"), FakeUpload("new.jpg"));

            Assert.True(File.Exists(secret));
        }

        [Fact]
        public void Upsert_TamperedAbsoluteImageUrl_DoesNotDeleteThatFile()
        {
            var secret = Path.Combine(_webRoot, "secret.txt");
            File.WriteAllText(secret, "keep me");

            _controller.Upsert(Edit(secret), FakeUpload("new.jpg"));

            Assert.True(File.Exists(secret));
        }
    }
}
