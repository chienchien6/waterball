using System.Threading;
using System.Threading.Tasks;
using WebFramework.Http;

namespace WebFramework.Server
{
    /// <summary>
    /// 定義底層 HTTP 伺服器傳輸契約。
    /// </summary>
    public interface IHttpServer
    {
        /// <summary>
        /// 啟動伺服器並持續監聽連線。
        /// </summary>
        /// <param name="handler">處理請求的管線終端或根委派。</param>
        /// <param name="cancellationToken">取消權杖。</param>
        Task StartAsync(RequestDelegate handler, CancellationToken cancellationToken = default);

        /// <summary>
        /// 停止伺服器監聽。
        /// </summary>
        Task StopAsync();
    }
}

