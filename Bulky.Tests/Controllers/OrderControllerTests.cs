using Bulky.DataAccess.Data;
using Bulky.DataAccess.Repository;
using Bulky.Models.ViewModels;
using Bulky.Tests.Helpers;
using Bulky.Utility;
using BulkyWeb.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Bulky.Tests.Controllers
{
    /// <summary>
    /// Order Details follows the "owner or staff" rule from Lesson 01 Part D.
    /// </summary>
    public class OrderControllerTests
    {
        private const string Owner = "owner";
        private const string OtherUser = "other";
        private const int OrderId = 100;

        private readonly ApplicationDbContext _db = TestDb.Create();

        public OrderControllerTests()
        {
            _db.ApplicationUsers.AddRange(TestDb.User(Owner), TestDb.User(OtherUser));
            _db.OrderHeaders.Add(TestDb.Order(OrderId, Owner));
            _db.SaveAndDetach();
        }

        private OrderController CreateController(string userId, string role) =>
            new OrderController(new UnitOfWork(_db)).WithUser(userId, role);

        [Fact]
        public void Details_Owner_SeesOrder()
        {
            var result = CreateController(Owner, SD.Role_Customer).Details(OrderId);

            var view = Assert.IsType<ViewResult>(result);
            Assert.Equal(OrderId, Assert.IsType<OrderVM>(view.Model).OrderHeader.Id);
        }

        [Theory]
        [InlineData(SD.Role_Customer)]
        [InlineData(SD.Role_Company)]
        public void Details_OtherNonStaffUser_ReturnsNotFound(string role)
        {
            var result = CreateController(OtherUser, role).Details(OrderId);

            Assert.IsType<NotFoundResult>(result);
        }

        [Theory]
        [InlineData(SD.Role_Admin)]
        [InlineData(SD.Role_Employee)]
        public void Details_Staff_SeesAnyOrder(string role)
        {
            var result = CreateController(OtherUser, role).Details(OrderId);

            Assert.IsType<ViewResult>(result);
        }

        [Fact]
        public void Details_UnknownOrder_ReturnsNotFound_EvenForAdmin()
        {
            var result = CreateController(OtherUser, SD.Role_Admin).Details(999);

            Assert.IsType<NotFoundResult>(result);
        }
    }
}
