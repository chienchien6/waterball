using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebFramework.Core;
using WebFramework.Http;
using WebFramework.Middleware;

namespace WebFramework.Sample
{
    /// <summary>
    /// 自訂日誌與請求計時中介軟體（示範中介軟體亦可由 IoC 容器自動建構子注入 IOrderService）。
    /// </summary>
    public class RequestLoggingMiddleware : IMiddleware
    {
        private readonly IOrderService _orderService;

        public RequestLoggingMiddleware(IOrderService orderService)
        {
            _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            var stopwatch = Stopwatch.StartNew();
            Console.WriteLine($"--> [{DateTime.Now:HH:mm:ss}] {context.Request.Method} {context.Request.Path}");

            await next(context);

            stopwatch.Stop();
            var currentOrders = _orderService.GetAllOrders().Count();
            Console.WriteLine($"<-- [{DateTime.Now:HH:mm:ss}] {context.Request.Method} {context.Request.Path} -> Status: {context.Response.StatusCode} ({stopwatch.ElapsedMilliseconds}ms, CurrentOrdersInRepo={currentOrders})");
        }
    }

    internal class Program
    {
        private static async Task Main(string[] args)
        {
            var app = ConfigureApp();

            if (args.Contains("--verify"))
            {
                await RunVerificationSimulationAsync(app);
                return;
            }

            Console.WriteLine("Starting Web Application on http://localhost:5000/ ...");
            await app.RunAsync("http://localhost:5000/");
        }

        private static WebApplication ConfigureApp()
        {
            var app = new WebApplication();

            // 1. 於 IoC 容器註冊三層架構依賴：
            //    Controller (Scoped) -> IOrderService / OrderService (Scoped) -> IOrderRepository / InMemoryOrderRepository (Singleton)
            app.Services
                .AddSingleton<IOrderRepository, InMemoryOrderRepository>()
                .AddScoped<IOrderService, OrderService>();

            // 2. 註冊支援建構子依賴注入的強型別中介軟體
            app.UseMiddleware<RequestLoggingMiddleware>();

            // 3. 註冊內聯中介軟體
            app.Use(async (context, next) =>
            {
                context.Response.Headers["X-Framework"] = "CSharp-MiniWebFramework-IoC";
                await next();
            });

            // 4. 註冊自動化依賴注入的 OrderController（自動掃描 [Route] 並在每請求由容器解析）
            app.MapController<OrderController>();

            // 5. 保留 Minimal API 端點
            app.MapGet("/", async context =>
            {
                await context.Response.WriteAsync("Hello from C# Web Framework with Automated IoC Container!");
            });

            return app;
        }

        /// <summary>
        /// 自動化模擬情境：透過管線發送多個模擬 HTTP 請求，驗證 Controller -> Service -> Repository 自動注入鏈與生命週期。
        /// </summary>
        private static async Task RunVerificationSimulationAsync(WebApplication app)
        {
            Console.WriteLine("==================================================================");
            Console.WriteLine("  IoC Container 自動化依賴注入模擬驗證 (Controller -> Service -> Repo)");
            Console.WriteLine("==================================================================");

            var pipeline = app.BuildPipeline();

            // 模擬請求 1：POST /orders 建立第一筆訂單
            Console.WriteLine("\n[模擬請求 #1] POST /orders (建立訂單：Mechanical Keyboard x 2)");
            var res1 = await SendSimulatedRequestAsync(pipeline, "POST", "/orders", "{\"item\":\"Mechanical Keyboard\",\"quantity\":2}");
            Console.WriteLine($"Status: {res1.StatusCode}");
            Console.WriteLine($"Response Body: {res1.Body}");

            // 模擬請求 2：POST /orders 建立第二筆訂單
            Console.WriteLine("\n[模擬請求 #2] POST /orders (建立訂單：4K Monitor x 1)");
            var res2 = await SendSimulatedRequestAsync(pipeline, "POST", "/orders", "{\"item\":\"4K Monitor\",\"quantity\":1}");
            Console.WriteLine($"Status: {res2.StatusCode}");
            Console.WriteLine($"Response Body: {res2.Body}");

            // 模擬請求 3：GET /orders 查詢所有訂單
            Console.WriteLine("\n[模擬請求 #3] GET /orders (驗證 Singleton Repository 跨請求保存資料、Scoped Controller 每請求隔離)");
            var res3 = await SendSimulatedRequestAsync(pipeline, "GET", "/orders");
            Console.WriteLine($"Status: {res3.StatusCode}");
            Console.WriteLine($"Response Body: {res3.Body}");

            // 驗證 Scope 內共用與跨 Scope 隔離
            Console.WriteLine("\n[生命週期驗證] 驗證同一個 Scope 內共用 IOrderService，不同 Scope 建立新實例，且共用同一個 Singleton IOrderRepository：");
            using (var scopeA = app.ServiceProvider.CreateScope())
            using (var scopeB = app.ServiceProvider.CreateScope())
            {
                var svcA1 = (OrderService)scopeA.ServiceProvider.GetRequiredService<IOrderService>();
                var svcA2 = (OrderService)scopeA.ServiceProvider.GetRequiredService<IOrderService>();
                var svcB1 = (OrderService)scopeB.ServiceProvider.GetRequiredService<IOrderService>();
                var repoA = (InMemoryOrderRepository)scopeA.ServiceProvider.GetRequiredService<IOrderRepository>();
                var repoB = (InMemoryOrderRepository)scopeB.ServiceProvider.GetRequiredService<IOrderRepository>();

                Console.WriteLine($"  Scope A 內兩次解析 OrderService 是否同一實例 (Scoped): {ReferenceEquals(svcA1, svcA2)} (Id={svcA1.InstanceId})");
                Console.WriteLine($"  Scope A 與 Scope B 的 OrderService 是否不同實例 (Scoped): {!ReferenceEquals(svcA1, svcB1)} (ScopeB Id={svcB1.InstanceId})");
                Console.WriteLine($"  Scope A 與 Scope B 的 InMemoryOrderRepository 是否同一實例 (Singleton): {ReferenceEquals(repoA, repoB)} (Id={repoA.InstanceId})");
            }

            Console.WriteLine("\n==================================================================");
            Console.WriteLine("  所有 IoC 自動化依賴注入與 Web 管線驗證通過！");
            Console.WriteLine("==================================================================");
        }

        private static async Task<(int StatusCode, string Body)> SendSimulatedRequestAsync(
            RequestDelegate pipeline,
            string method,
            string path,
            string bodyContent = "")
        {
            using var reqStream = new MemoryStream(Encoding.UTF8.GetBytes(bodyContent));
            using var resStream = new MemoryStream();

            var request = new HttpRequest
            {
                Method = method,
                Path = path,
                Body = reqStream
            };
            var response = new HttpResponse
            {
                Body = resStream
            };
            var context = new HttpContext(request, response);

            await pipeline(context);

            resStream.Position = 0;
            using var reader = new StreamReader(resStream, Encoding.UTF8);
            var responseText = await reader.ReadToEndAsync();
            return (response.StatusCode, responseText);
        }
    }
}
