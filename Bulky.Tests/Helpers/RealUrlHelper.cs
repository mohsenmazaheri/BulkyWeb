using BulkyWeb.Areas.Customer.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Bulky.Tests.Helpers
{
    /// <summary>
    /// Gives a controller a real IUrlHelper, backed by the app's actual controllers and route table,
    /// so tests can check the exact URLs that Url.Action produces.
    /// </summary>
    public static class RealUrlHelper
    {
        private static readonly Lazy<WebApplication> App = new(() =>
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddControllersWithViews().AddApplicationPart(typeof(CartController).Assembly);
            var app = builder.Build();
            // The app is never started, so the routes are registered with UseEndpoints, which hands them to the
            // URL generator right away (MapControllerRoute on its own only does that when the app starts).
            // Same pattern as Program.cs.
            app.UseRouting();
            app.UseEndpoints(endpoints =>
                endpoints.MapControllerRoute(name: "default", pattern: "{area=Customer}/{controller=Home}/{action=Index}/{id?}"));
            return app;
        });

        /// <summary>Simulates a request to scheme://host and attaches a real URL helper to the controller.</summary>
        public static T WithRealUrls<T>(this T controller, string scheme, string host) where T : Controller
        {
            var httpContext = controller.HttpContext;
            httpContext.RequestServices = App.Value.Services;
            httpContext.Request.Scheme = scheme;
            httpContext.Request.Host = new HostString(host);
            // Marks the request as endpoint-routed, so the endpoint-based URL helper is used
            httpContext.SetEndpoint(new Endpoint(null, null, "test"));

            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            controller.Url = App.Value.Services.GetRequiredService<IUrlHelperFactory>().GetUrlHelper(actionContext);
            return controller;
        }
    }
}
