using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using WebFramework.Core;
using WebFramework.Http;
using WebFramework.Middleware;

namespace WebFramework.Sample
{
    /// <summary>
    /// 自訂日誌與請求計時中介軟體。
    /// </summary>
    public class RequestLoggingMiddleware : IMiddleware
    {
        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            var stopwatch = Stopwatch.StartNew();
            Console.WriteLine($"--> [{DateTime.Now:HH:mm:ss}] Incoming Request: {context.Request.Method} {context.Request.Path}");

            await next(context);

            stopwatch.Stop();
            Console.WriteLine($"<-- [{DateTime.Now:HH:mm:ss}] Completed {context.Request.Method} {context.Request.Path} - Status: {context.Response.StatusCode} in {stopwatch.ElapsedMilliseconds}ms");
        }
    }

    internal class Program
    {
        private static async Task Main(string[] args)
        {
            var app = new WebApplication();

            // 1. 註冊強型別中介軟體 (日誌計時)
            app.UseMiddleware<RequestLoggingMiddleware>();

            // 2. 註冊內聯中介軟體 (安全標頭)
            app.Use(async (context, next) =>
            {
                context.Response.Headers["X-Framework"] = "CSharp-MiniWebFramework";
                await next();
            });

            // 3. 註冊首頁路由
            app.MapGet("/", async (context) =>
            {
                await context.Response.WriteAsync("Hello from C# Web Framework!");
            });

            // 4. 註冊具名動態路由 (如 /users/{id})
            app.MapGet("/users/{id}", async (context) =>
            {
                var routeValues = context.Items["RouteValues"] as IDictionary<string, string>;
                var userId = routeValues != null && routeValues.TryGetValue("id", out var id) ? id : "unknown";

                await context.Response.JsonAsync(new
                {
                    UserId = userId,
                    Name = $"User_{userId}",
                    CreatedTime = DateTime.UtcNow
                });
            });

            // 5. 註冊 POST 路由處理請求主體與 JSON 回傳
            app.MapPost("/api/echo", async (context) =>
            {
                var bodyText = await context.Request.ReadBodyAsStringAsync();
                await context.Response.JsonAsync(new
                {
                    Message = "Data received successfully",
                    ReceivedBody = bodyText,
                    Length = bodyText.Length
                });
            });

            // 啟動伺服器
            Console.WriteLine("Starting Web Application on http://localhost:5000/ ...");
            await app.RunAsync("http://localhost:5000/");
        }
    }
}

