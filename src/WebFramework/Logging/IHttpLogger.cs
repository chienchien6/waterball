using WebFramework.Http;

namespace WebFramework.Logging
{
    /// <summary>
    /// 定義於 HTTP 請求進入與 HTTP 回應輸出兩個時間點記錄日誌的介面。
    /// </summary>
    public interface IHttpLogger
    {
        /// <summary>
        /// 於收到 HTTP 請求、進入 Controller Action 前記錄請求日誌。
        /// </summary>
        void LogRequest(HttpContext context, string controllerName, string actionName);

        /// <summary>
        /// 於 Controller Action 執行完畢、準備輸出 HTTP 回應時記錄回應日誌。
        /// </summary>
        void LogResponse(HttpContext context, string controllerName, string actionName, long elapsedMs);
    }
}

