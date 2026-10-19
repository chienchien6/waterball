using System.Threading.Tasks;

namespace WebFramework.Http
{
    /// <summary>
    /// 代表處理 HTTP 請求的非同步委派函式。
    /// </summary>
    /// <param name="context">當前請求的 HTTP 上下文。</param>
    public delegate Task RequestDelegate(HttpContext context);
}

