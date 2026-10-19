using System.Threading.Tasks;
using WebFramework.Http;

namespace WebFramework.Middleware
{
    /// <summary>
    /// 定義可插入請求管線的中介軟體契約。
    /// </summary>
    public interface IMiddleware
    {
        /// <summary>
        /// 執行中介軟體邏輯。
        /// </summary>
        /// <param name="context">當前 HTTP 請求上下文。</param>
        /// <param name="next">呼叫管線中的下一個處理委派。</param>
        Task InvokeAsync(HttpContext context, RequestDelegate next);
    }
}

