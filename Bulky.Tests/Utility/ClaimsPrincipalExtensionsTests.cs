using System.Security.Claims;
using Bulky.Utility;

namespace Bulky.Tests.Utility
{
    public class ClaimsPrincipalExtensionsTests
    {
        private static ClaimsPrincipal CreateUser(params Claim[] claims) =>
            new(new ClaimsIdentity(claims, "TestAuth"));

        [Fact]
        public void GetUserId_ReturnsNameIdentifierClaim()
        {
            var user = CreateUser(new Claim(ClaimTypes.NameIdentifier, "user-42"));

            Assert.Equal("user-42", user.GetUserId());
        }

        [Fact]
        public void GetUserId_WithoutNameIdentifierClaim_Throws()
        {
            var user = CreateUser(new Claim(ClaimTypes.Name, "no-id"));

            Assert.Throws<InvalidOperationException>(() => user.GetUserId());
        }

        [Theory]
        [InlineData(SD.Role_Admin, true)]
        [InlineData(SD.Role_Employee, true)]
        [InlineData(SD.Role_Customer, false)]
        [InlineData(SD.Role_Company, false)]
        public void IsStaff_DependsOnRole(string role, bool expected)
        {
            var user = CreateUser(new Claim(ClaimTypes.Role, role));

            Assert.Equal(expected, user.IsStaff());
        }
    }
}
