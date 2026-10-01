using BulkyWeb.Extensions;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Bulky.Tests.Web
{
    public class UrlHelperExtensionsTests
    {
        private readonly IUrlHelper _url;

        public UrlHelperExtensionsTests()
        {
            // Url.Content turns "~/x" into "/x"; that is all ProductImage relies on
            var url = new Mock<IUrlHelper>();
            url.Setup(u => u.Content(It.IsAny<string>()))
               .Returns<string>(path => path.StartsWith("~/") ? path[1..] : path);
            _url = url.Object;
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ProductImage_WithoutImage_ReturnsPlaceholder(string? imageUrl)
        {
            Assert.Equal("/Images/book.png", _url.ProductImage(imageUrl));
        }

        [Fact]
        public void ProductImage_WithImage_ReturnsIt()
        {
            Assert.Equal("/Images/Product/cover.jpg", _url.ProductImage("/Images/Product/cover.jpg"));
        }
    }
}
