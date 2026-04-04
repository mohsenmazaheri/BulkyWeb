using System;
using System.Collections.Generic;
using System.Text;

namespace Bulky.DataAccess.DBInitializer
{
    public interface IDbInitializer
    {
        Task InitializeAsync();
    }
}
