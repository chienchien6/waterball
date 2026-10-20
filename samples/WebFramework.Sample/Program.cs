using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebFramework.Core;
using WebFramework.Http;

namespace WebFramework.Sample
{
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

            // 2. 註冊內聯中介軟體
            app.Use(async (context, next) =>
            {
                context.Response.Headers["X-Framework"] = "CSharp-MiniWebFramework-IoC";
                await next();
            });

            // 3. 註冊掛有 [HttpLogging] 裝飾器的 OrderController（自動掃描 [Route] 與 [HttpLogging] 並在每請求執行）
            app.MapController<OrderController>();

            // 4. 保留 Minimal API 端點（對照組：未掛 [HttpLogging] 裝飾器）
            app.MapGet("/", async context =>
            {
                await context.Response.WriteAsync("Hello from C# Web Framework with Automated IoC Container!");
            });

            return app;
        }

        /// <summary>
        /// 自動化模擬情境：透過管線發送多個模擬 HTTP 請求，驗證 [HttpLogging] 裝飾器於 Request 進入與 Response 輸出時自動寫入日誌，以及三層自動依賴注入。
        /// </summary>
        private static async Task RunVerificationSimulationAsync(WebApplication app)
        {
            Console.WriteLine("==================================================================");
            Console.WriteLine("  [HttpLogging] 裝飾器與 IoC 自動化依賴注入模擬驗證");
            Console.WriteLine("==================================================================");

            var pipeline = app.BuildPipeline();

            // 模擬請求 1：POST /orders 建立第一筆訂單
            Console.WriteLine("\n[模擬請求 #1] POST /orders (建立訂單：Mechanical Keyboard x 2)");
            var res1 = await SendSimulatedRequestAsync(pipeline, "POST", "/orders", "{\"item\":\"Mechanical Keyboard\",\"quantity\":2}");
            Console.WriteLine($"Client Received -> Status: {res1.StatusCode}, Body: {res1.Body}");

            // 模擬請求 2：POST /orders 建立第二筆訂單
            Console.WriteLine("\n[模擬請求 #2] POST /orders (建立訂單：4K Monitor x 1)");
            var res2 = await SendSimulatedRequestAsync(pipeline, "POST", "/orders", "{\"item\":\"4K Monitor\",\"quantity\":1}");
            Console.WriteLine($"Client Received -> Status: {res2.StatusCode}, Body: {res2.Body}");

            // 模擬請求 3：GET /orders 查詢所有訂單
            Console.WriteLine("\n[模擬請求 #3] GET /orders (查詢訂單清單)");
            var res3 = await SendSimulatedRequestAsync(pipeline, "GET", "/orders");
            Console.WriteLine($"Client Received -> Status: {res3.StatusCode}, Body: {res3.Body}");

            // 模擬請求 4：GET / (未掛載 [HttpLogging] 的一般端點，不會輸出 [HttpLogging] 日誌)
            Console.WriteLine("\n[模擬請求 #4] GET / (對照組：未掛載 [HttpLogging] 的端點)");
            var res4 = await SendSimulatedRequestAsync(pipeline, "GET", "/");
            Console.WriteLine($"Client Received -> Status: {res4.StatusCode}, Body: {res4.Body}");

            Console.WriteLine("\n==================================================================");
            Console.WriteLine("  [HttpLogging] 請求／回應雙時間點日誌與 IoC 依賴注入驗證通過！");
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
