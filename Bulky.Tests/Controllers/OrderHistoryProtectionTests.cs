using System.Text.Json;
using Bulky.DataAccess.Data;
using Bulky.DataAccess.Repository;
using Bulky.Models;
using Bulky.Tests.Helpers;
using Bulky.Utility;
using BulkyWeb.Areas.Admin.Controllers;
using BulkyWeb.Areas.Customer.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Bulky.Tests.Controllers
{
    /// <summary>
    /// Lesson 12: deleting products, categories or companies must never remove order history.
    /// </summary>
    public class OrderHistoryProtectionTests
    {
        private const string Customer = "customer";

        private readonly ApplicationDbContext _db = TestDb.Create();

        public OrderHistoryProtectionTests()
        {
            _db.Categories.AddRange(
                new Category { Id = 1, Name = "Action", DisplayOrder = 1 },
                new Category { Id = 2, Name = "Empty", DisplayOrder = 2 });
            _db.Companies.Add(new Company { Id = 1, Name = "Acme" });
            _db.Companies.Add(new Company { Id = 2, Name = "No Users" });
            var companyUser = TestDb.User("company-user");
            companyUser.CompanyId = 1;
            _db.ApplicationUsers.AddRange(TestDb.User(Customer), companyUser);
            _db.Products.AddRange(TestDb.Product(1), TestDb.Product(2));
            _db.ShoppingCarts.Add(new ShoppingCart { Id = 10, ProductId = 1, ApplicationUserId = Customer, Count = 2 });
            _db.SaveAndDetach();
        }

        private static JsonElement JsonOf(IActionResult result) =>
            JsonDocument.Parse(JsonSerializer.Serialize(Assert.IsType<JsonResult>(result).Value,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))).RootElement;

        private ProductController ProductAdmin() =>
            new ProductController(new UnitOfWork(_db), Mock.Of<IWebHostEnvironment>()).WithUser("admin", SD.Role_Admin);

        private HomeController Store() =>
            new HomeController(NullLogger<HomeController>.Instance, new UnitOfWork(_db)).WithUser(Customer, SD.Role_Customer);

        private void SoftDeleteProduct1()
        {
            ProductAdmin().Delete(1);
            _db.ChangeTracker.Clear();
        }

        // ---------- The database rules ----------

        [Theory]
        [InlineData(typeof(OrderDetail), typeof(Product))]
        [InlineData(typeof(Product), typeof(Category))]
        public void Model_DeletingThePrincipal_IsRestricted(Type dependent, Type principal)
        {
            var foreignKey = _db.Model.FindEntityType(dependent)!.GetForeignKeys()
                .Single(fk => fk.PrincipalEntityType.ClrType == principal);

            Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        }

        // ---------- Products: soft delete ----------

        [Fact]
        public void DeleteProduct_KeepsTheRow_MarksItDeleted_AndEmptiesCarts()
        {
            var json = JsonOf(ProductAdmin().Delete(1));
            _db.ChangeTracker.Clear();

            Assert.True(json.GetProperty("success").GetBoolean());
            var product = _db.Products.Single(p => p.Id == 1);
            Assert.True(product.IsDeleted);
            Assert.False(_db.ShoppingCarts.Any(c => c.ProductId == 1));
        }

        [Fact]
        public void DeletedProduct_IsHiddenFromStoreAndAdminList()
        {
            SoftDeleteProduct1();

            var storeModel = Assert.IsAssignableFrom<IEnumerable<Product>>(Assert.IsType<ViewResult>(Store().Index()).Model);
            Assert.Equal(new[] { 2 }, storeModel.Select(p => p.Id));

            var adminIds = JsonOf(ProductAdmin().GetAll()).GetProperty("data").EnumerateArray()
                .Select(p => p.GetProperty("id").GetInt32());
            Assert.Equal(new[] { 2 }, adminIds);
        }

        [Fact]
        public void DeletedProduct_CannotBeOpenedEditedOrAddedToCart()
        {
            SoftDeleteProduct1();

            Assert.IsType<NotFoundResult>(Store().Details(productId: 1));
            Assert.IsType<NotFoundResult>(ProductAdmin().Upsert(id: 1));
            Assert.IsType<NotFoundResult>(Store().Details(new ShoppingCart { ProductId = 1, Count = 1 }));
            Assert.False(_db.ShoppingCarts.Any(c => c.ProductId == 1));
        }

        [Fact]
        public void UnknownProduct_DetailsReturnsNotFound_InsteadOfCrashing()
        {
            Assert.IsType<NotFoundResult>(Store().Details(productId: 999));
        }

        [Fact]
        public void OrderDetails_StillShowTheDeletedProduct()
        {
            _db.OrderHeaders.Add(TestDb.Order(100, Customer));
            _db.OrderDetails.Add(new OrderDetail { Id = 1, OrderHeaderId = 100, ProductId = 1, Count = 1, Price = 40 });
            _db.SaveAndDetach();
            SoftDeleteProduct1();

            var details = new UnitOfWork(_db).OrderDetail.GetAll(d => d.OrderHeaderId == 100, includeProperties: "Product");

            Assert.Equal("Book 1", Assert.Single(details).Product.Title);
        }

        // ---------- Categories and companies: refuse instead of losing data ----------

        [Fact]
        public void DeleteCategory_WithProducts_IsRefusedWithAMessage()
        {
            SoftDeleteProduct1();   // deleted products still count: old orders refer to them
            var controller = new CategoryController(new UnitOfWork(_db)).WithUser("admin", SD.Role_Admin);

            var result = controller.DeletePost(1);

            Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
            Assert.Contains("cannot be deleted", controller.TempData["error"]?.ToString());
            Assert.True(_db.Categories.Any(c => c.Id == 1));
        }

        [Fact]
        public void DeleteCategory_WithoutProducts_IsDeleted()
        {
            var controller = new CategoryController(new UnitOfWork(_db)).WithUser("admin", SD.Role_Admin);

            controller.DeletePost(2);

            Assert.False(_db.Categories.Any(c => c.Id == 2));
        }

        [Fact]
        public void DeleteCompany_WithUsers_IsRefusedWithAMessage()
        {
            var controller = new CompanyController(new UnitOfWork(_db)).WithUser("admin", SD.Role_Admin);

            var json = JsonOf(controller.Delete(1));

            Assert.False(json.GetProperty("success").GetBoolean());
            Assert.Contains("1 user(s)", json.GetProperty("message").GetString());
            Assert.True(_db.Companies.Any(c => c.Id == 1));
        }

        [Fact]
        public void DeleteCompany_WithoutUsers_IsDeleted()
        {
            var controller = new CompanyController(new UnitOfWork(_db)).WithUser("admin", SD.Role_Admin);

            var json = JsonOf(controller.Delete(2));

            Assert.True(json.GetProperty("success").GetBoolean());
            Assert.False(_db.Companies.Any(c => c.Id == 2));
        }
    }
}
