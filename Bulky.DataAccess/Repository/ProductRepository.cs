using Bulky.DataAccess.Data;
using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;

namespace Bulky.DataAccess.Repository
{
    public class ProductRepository : Repository<Product>, IProductRepository
    {
        private ApplicationDbContext _db;

        public ProductRepository(ApplicationDbContext db) : base(db)
        {
            _db = db;
        }

        public void Update(Product obj)
        {
            //_db.Products.Update(obj);
            // Changing to explicit updating with a manual mapping
            var objFromDB = _db.Products.FirstOrDefault(a => a.Id == obj.Id);
            if (objFromDB != null)
            {
                objFromDB.Title = obj.Title;
                objFromDB.ISBN = obj.ISBN;
                objFromDB.Price = obj.Price;
                objFromDB.Price50 = obj.Price50;
                objFromDB.Price100 = obj.Price100;
                objFromDB.ListPrice = obj.ListPrice;
                objFromDB.Description = obj.Description;
                objFromDB.CategoryId = obj.CategoryId;
                objFromDB.Author = obj.Author;

                if(obj.ImageURL != null)
                    objFromDB.ImageURL = obj.ImageURL;
            }
        }
    }
}
