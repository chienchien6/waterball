using System;
using System.Threading.Tasks;
using WebFramework.Http;
using WebFramework.Logging;

namespace WebFramework.Filters
{
    /// <summary>
    /// 標註於 Controller 類別或 Action 方法上的裝飾器屬性（[HttpLogging]），
    /// 自動在每次進來的 HTTP Request 與輸出的 HTTP Response 兩個時間點寫入日誌。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class HttpLoggingAttribute : Attribute, IActionFilter
    {
        private static readonly IHttpLogger FallbackLogger = new ConsoleHttpLogger();

        public Task OnActionExecutingAsync(HttpContext context, string controllerName, string actionName)
        {
            var logger = ResolveLogger(context);
            logger.LogRequest(context, controllerName, actionName);
            return Task.CompletedTask;
        }

        public Task OnActionExecutedAsync(HttpContext context, string controllerName, string actionName, long elapsedMs)
        {
            var logger = ResolveLogger(context);
            logger.LogResponse(context, controllerName, actionName, elapsedMs);
            return Task.CompletedTask;
        }

        private static IHttpLogger ResolveLogger(HttpContext context)
        {
            if (context?.RequestServices != null &&
                context.RequestServices.GetService(typeof(IHttpLogger)) is IHttpLogger resolved)
            {
                return resolved;
            }

            return FallbackLogger;
        }
    }
}

