using Bulky.DataAccess.Data;
using Bulky.DataAccess.Repository;
using Bulky.Models;
using Bulky.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Bulky.Tests.Repositories
{
    public class ProductRepositoryTests
    {
        private readonly ApplicationDbContext _db = TestDb.Create();
        private readonly ProductRepository _repository;

        public ProductRepositoryTests()
        {
            _db.Categories.Add(new Category { Id = 1, Name = "Action", DisplayOrder = 1 });
            var product = TestDb.Product(1);
            product.ImageURL = @"\images\product\old.jpg";
            _db.Products.Add(product);
            _db.SaveAndDetach();

            _repository = new ProductRepository(_db);
        }

        [Fact]
        public void Get_WithIncludeProperties_LoadsCategory()
        {
            var product = _repository.Get(p => p.Id == 1, includeProperties: "Category");

            Assert.NotNull(product.Category);
            Assert.Equal("Action", product.Category.Name);
        }

        [Fact]
        public void Get_ByDefault_ReturnsUntrackedEntity()
        {
            var product = _repository.Get(p => p.Id == 1);

            Assert.Equal(EntityState.Detached, _db.Entry(product).State);
        }

        [Fact]
        public void Get_Tracked_ReturnsTrackedEntity()
        {
            var product = _repository.Get(p => p.Id == 1, tracked: true);

            Assert.Equal(EntityState.Unchanged, _db.Entry(product).State);
        }

        [Fact]
        public void Update_WithoutNewImage_KeepsExistingImageUrl()
        {
            var edited = TestDb.Product(1);
            edited.Title = "New title";
            edited.ImageURL = null!; // the form sends no new image

            _repository.Update(edited);
            _db.SaveAndDetach();

            var saved = _db.Products.Single(p => p.Id == 1);
            Assert.Equal("New title", saved.Title);
            Assert.Equal(@"\images\product\old.jpg", saved.ImageURL);
        }

        [Fact]
        public void Update_WithNewImage_ReplacesImageUrl()
        {
            var edited = TestDb.Product(1);
            edited.ImageURL = @"\images\product\new.jpg";

            _repository.Update(edited);
            _db.SaveAndDetach();

            Assert.Equal(@"\images\product\new.jpg", _db.Products.Single(p => p.Id == 1).ImageURL);
        }
    }
}
