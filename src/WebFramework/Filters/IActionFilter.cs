using System.Threading.Tasks;
using WebFramework.Http;

namespace WebFramework.Filters
{
    /// <summary>
    /// 定義 Controller Action 執行前（請求進入）與執行後（回應輸出）的攔截器契約。
    /// </summary>
    public interface IActionFilter
    {
        /// <summary>
        /// 於 Controller Action 執行前觸發。
        /// </summary>
        Task OnActionExecutingAsync(HttpContext context, string controllerName, string actionName);

        /// <summary>
        /// 於 Controller Action 執行後、回應準備輸出時觸發。
        /// </summary>
        Task OnActionExecutedAsync(HttpContext context, string controllerName, string actionName, long elapsedMs);
    }
}

