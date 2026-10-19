using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WebFramework.Http;

namespace WebFramework.Routing
{
    /// <summary>
    /// 預設路由比對器實作。
    /// </summary>
    public class Router : IRouter
    {
        private readonly List<Route> _routes = new List<Route>();

        public void AddRoute(string method, string pattern, RequestDelegate handler)
        {
            if (string.IsNullOrWhiteSpace(method))
            {
                throw new ArgumentException("HTTP method cannot be null or empty.", nameof(method));
            }

            if (string.IsNullOrWhiteSpace(pattern))
            {
                throw new ArgumentException("Route pattern cannot be null or empty.", nameof(pattern));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _routes.Add(new Route(method, pattern, handler));
        }

        public async Task<bool> RouteAsync(HttpContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var path = context.Request.Path;
            var method = context.Request.Method;

            foreach (var route in _routes)
            {
                var routeValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (route.Match(method, path, routeValues))
                {
                    context.Items["RouteValues"] = routeValues;
                    await route.Handler(context);
                    return true;
                }
            }

            return false;
        }
    }
}

