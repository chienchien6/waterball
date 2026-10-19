using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WebFramework.Http;
using WebFramework.Middleware;
using WebFramework.Routing;
using WebFramework.Server;

namespace WebFramework.Core
{
    /// <summary>
    /// Web 應用程式核心門面類別，負責管線組裝、路由註冊與伺服器生命週期。
    /// </summary>
    public class WebApplication
    {
        private IHttpServer _server;
        private readonly IRouter _router;
        private readonly List<Func<RequestDelegate, RequestDelegate>> _pipeline = new List<Func<RequestDelegate, RequestDelegate>>();

        public WebApplication(IRouter router = null, IHttpServer server = null)
        {
            _router = router ?? new Router();
            _server = server;
        }

        /// <summary>
        /// 註冊洋蔥模型中介軟體。
        /// </summary>
        public WebApplication Use(Func<HttpContext, Func<Task>, Task> middleware)
        {
            if (middleware == null)
            {
                throw new ArgumentNullException(nameof(middleware));
            }

            _pipeline.Add(next =>
            {
                return context => middleware(context, () => next(context));
            });

            return this;
        }

        /// <summary>
        /// 註冊實作 IMiddleware 的強型別中介軟體。
        /// </summary>
        public WebApplication UseMiddleware<TMiddleware>() where TMiddleware : IMiddleware, new()
        {
            _pipeline.Add(next =>
            {
                var instance = new TMiddleware();
                return context => instance.InvokeAsync(context, next);
            });

            return this;
        }

        /// <summary>
        /// 註冊通用 HTTP 端點路由。
        /// </summary>
        public WebApplication Map(string method, string pattern, RequestDelegate handler)
        {
            _router.AddRoute(method, pattern, handler);
            return this;
        }

        /// <summary>
        /// 註冊 HTTP GET 端點。
        /// </summary>
        public WebApplication MapGet(string pattern, RequestDelegate handler)
        {
            return Map("GET", pattern, handler);
        }

        /// <summary>
        /// 註冊 HTTP POST 端點。
        /// </summary>
        public WebApplication MapPost(string pattern, RequestDelegate handler)
        {
            return Map("POST", pattern, handler);
        }

        /// <summary>
        /// 啟動 Web 伺服器並執行請求處理管線。
        /// </summary>
        public async Task RunAsync(string url = "http://localhost:5000/", CancellationToken cancellationToken = default)
        {
            _server ??= new HttpListenerServer(url);

            // 建立終端處理器（路由分派）
            RequestDelegate app = async (context) =>
            {
                var handled = await _router.RouteAsync(context);
                if (!handled)
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("404 Not Found");
                }
            };

            // 反向組裝洋蔥管線
            for (int i = _pipeline.Count - 1; i >= 0; i--)
            {
                app = _pipeline[i](app);
            }

            await _server.StartAsync(app, cancellationToken);
        }
    }
}

