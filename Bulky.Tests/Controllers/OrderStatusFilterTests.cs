using System.Text.Json;
using Bulky.DataAccess.Data;
using Bulky.DataAccess.Repository;
using Bulky.Models;
using Bulky.Tests.Helpers;
using Bulky.Utility;
using BulkyWeb.Areas.Admin.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Bulky.Tests.Controllers
{
    /// <summary>
    /// The status tabs on Admin > Manage Order call GetAll(status), which feeds the DataTable.
    /// </summary>
    public class OrderStatusFilterTests
    {
        private const string Customer = "customer";
        private const string Company = "company";

        private readonly ApplicationDbContext _db = TestDb.Create();

        public OrderStatusFilterTests()
        {
            _db.ApplicationUsers.AddRange(TestDb.User(Customer), TestDb.User(Company));
            _db.OrderHeaders.AddRange(
                Order(1, Customer, SD.StatusApproved,   SD.PaymentStatusApproved),          // paid, waiting
                Order(2, Customer, SD.StatusInProcess,  SD.PaymentStatusApproved),          // being packed
                Order(3, Customer, SD.StatusShipped,    SD.PaymentStatusApproved),          // done
                Order(4, Company,  SD.StatusApproved,   SD.PaymentStatusDelayedPayment),    // company pays later
                Order(5, Customer, SD.StatusPending,    SD.PaymentStatusPending));          // checkout not paid yet
            _db.SaveAndDetach();
        }

        private static OrderHeader Order(int id, string userId, string orderStatus, string paymentStatus)
        {
            var order = TestDb.Order(id, userId, paymentStatus);
            order.OrderStatus = orderStatus;
            return order;
        }

        /// <summary>
        /// Serializes the JSON result the way MVC does (camelCase), so the test sees exactly what the browser gets.
        /// </summary>
        private static JsonElement GetData(IActionResult result)
        {
            var json = JsonSerializer.Serialize(Assert.IsType<JsonResult>(result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return JsonDocument.Parse(json).RootElement.GetProperty("data");
        }

        private static int[] Ids(JsonElement data) =>
            data.EnumerateArray().Select(order => order.GetProperty("id").GetInt32()).Order().ToArray();

        private OrderController CreateController(string userId, string role) =>
            new OrderController(new UnitOfWork(_db)).WithUser(userId, role);

        [Theory]
        [InlineData("inprocess", new[] { 2 })]
        [InlineData("completed", new[] { 3 })]
        [InlineData("approved",  new[] { 1, 4 })]
        [InlineData("pending",   new[] { 4 })]          // "Payment pending" tab: company orders paid later
        [InlineData("all",       new[] { 1, 2, 3, 4, 5 })]
        public void GetAll_AsAdmin_ReturnsOrdersForTheSelectedTab(string status, int[] expectedIds)
        {
            var result = CreateController("admin", SD.Role_Admin).GetAll(status);

            Assert.Equal(expectedIds, Ids(GetData(result)));
        }

        [Fact]
        public void GetAll_SendsOnlyTheColumnsTheTableShows_AndNoIdentityData()
        {
            var order = GetData(CreateController("admin", SD.Role_Admin).GetAll("all")).EnumerateArray().First();

            var fields = order.EnumerateObject().Select(p => p.Name).Order().ToArray();
            Assert.Equal(new[] { "applicationUser", "id", "name", "orderStatus", "orderTotal", "phoneNumber" }, fields);

            // The user object must only carry the email, never PasswordHash, SecurityStamp, etc.
            var userFields = order.GetProperty("applicationUser").EnumerateObject().Select(p => p.Name).ToArray();
            Assert.Equal(new[] { "email" }, userFields);
        }

        [Fact]
        public void GetAll_AsCustomer_FiltersOnlyTheirOwnOrders()
        {
            var result = CreateController(Customer, SD.Role_Customer).GetAll("approved");

            Assert.Equal(new[] { 1 }, Ids(GetData(result)));
        }
    }
}
