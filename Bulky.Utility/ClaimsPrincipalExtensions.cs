using System.Security.Claims;

namespace Bulky.Utility
{
    public static class ClaimsPrincipalExtensions
    {
        /// <summary>
        /// Reads the logged-in user's Id from the signed auth cookie claims.
        /// Unlike ids from the URL or form, this value cannot be changed by the user.
        /// Only call it where [Authorize] guarantees a logged-in user.
        /// </summary>
        public static string GetUserId(this ClaimsPrincipal user)
        {
            return user.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? throw new InvalidOperationException("The current user has no NameIdentifier claim.");
        }

        /// <summary>
        /// Admins and Employees manage orders, so they may access every user's orders.
        /// </summary>
        public static bool IsStaff(this ClaimsPrincipal user)
        {
            return user.IsInRole(SD.Role_Admin) || user.IsInRole(SD.Role_Employee);
        }
    }
}
