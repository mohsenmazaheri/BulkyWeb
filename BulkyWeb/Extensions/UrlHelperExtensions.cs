using Microsoft.AspNetCore.Mvc;

namespace BulkyWeb.Extensions
{
    public static class UrlHelperExtensions
    {
        public const string PlaceholderProductImage = "~/Images/book.png";

        /// <summary>
        /// URL of a product's cover image, or a placeholder when the product has none
        /// (for example the seeded products, whose ImageURL is empty).
        /// </summary>
        public static string ProductImage(this IUrlHelper url, string? imageUrl)
        {
            return url.Content(string.IsNullOrWhiteSpace(imageUrl) ? PlaceholderProductImage : imageUrl);
        }
    }
}
