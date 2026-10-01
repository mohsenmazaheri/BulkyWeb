using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;

namespace Bulky.Tests.Helpers
{
    public static class ControllerTestExtensions
    {
        /// <summary>
        /// Makes the controller behave as if the given user is logged in:
        /// sets the claims that Identity would put in the auth cookie, plus a session and TempData.
        /// </summary>
        public static T WithUser<T>(this T controller, string userId, params string[] roles) where T : Controller
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

            var httpContext = new DefaultHttpContext
            {
                // Passing an authentication type makes IsAuthenticated true
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
                Session = new TestSession()
            };

            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
            return controller;
        }
    }

    /// <summary>
    /// A simple in-memory ISession, so tests can read what the controller stored in the session.
    /// </summary>
    public class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public bool IsAvailable => true;
        public string Id => "test-session";
        public IEnumerable<string> Keys => _store.Keys;

        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
    }
}
