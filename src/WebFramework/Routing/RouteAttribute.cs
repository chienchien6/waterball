using System;

namespace WebFramework.Routing
{
    /// <summary>
    /// 標註於 Controller 類別或 Action 方法上的路由與 HTTP 方法屬性。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class RouteAttribute : Attribute
    {
        public string Method { get; }
        public string Template { get; }

        /// <summary>
        /// 用於標註類別路由前綴（例如 [Route("/orders")]），或預設為 GET 的方法路由。
        /// </summary>
        public RouteAttribute(string template)
            : this("GET", template)
        {
        }

        /// <summary>
        /// 用於標註 Action 方法的 HTTP 方法與路由模板（例如 [Route("POST", "/orders")]）。
        /// </summary>
        public RouteAttribute(string method, string template)
        {
            Method = string.IsNullOrWhiteSpace(method) ? "GET" : method.ToUpperInvariant();
            Template = template ?? "/";
        }
    }
}

