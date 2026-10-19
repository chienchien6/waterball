using System.Threading.Tasks;
using WebFramework.Http;

namespace WebFramework.Routing
{
    /// <summary>
    /// 定義路由比對與分派介面。
    /// </summary>
    public interface IRouter
    {
        /// <summary>
        /// 註冊路由。
        /// </summary>
        void AddRoute(string method, string pattern, RequestDelegate handler);

        /// <summary>
        /// 針對傳入的上下文進行路由匹配與執行。
        /// </summary>
        /// <returns>若有匹配並執行則回傳 true，否則回傳 false。</returns>
        Task<bool> RouteAsync(HttpContext context);
    }
}

