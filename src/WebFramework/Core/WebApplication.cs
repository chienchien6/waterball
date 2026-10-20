using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WebFramework.DependencyInjection;
using WebFramework.Filters;
using WebFramework.Http;
using WebFramework.Logging;
using WebFramework.Middleware;
using WebFramework.Routing;
using WebFramework.Server;

namespace WebFramework.Core
{
    /// <summary>
    /// Web 應用程式核心門面類別，負責 IoC 容器註冊、管線組裝、路由註冊與伺服器生命週期。
    /// </summary>
    public class WebApplication
    {
        private IHttpServer? _server;
        private readonly IRouter _router;
        private readonly List<Func<RequestDelegate, RequestDelegate>> _pipeline = new List<Func<RequestDelegate, RequestDelegate>>();
        private DependencyInjection.IServiceProvider? _serviceProvider;

        /// <summary>
        /// 取得應用程式的依賴注入服務註冊表。
        /// </summary>
        public IServiceCollection Services { get; }

        /// <summary>
        /// 取得或建構根容器 IServiceProvider。
        /// </summary>
        public DependencyInjection.IServiceProvider ServiceProvider => _serviceProvider ??= Services.BuildServiceProvider();

        public WebApplication(IRouter? router = null, IHttpServer? server = null, IServiceCollection? services = null)
        {
            Services = services ?? new ServiceCollection();
            Services.AddSingleton<IHttpLogger, ConsoleHttpLogger>();
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
        /// 註冊實作 IMiddleware 的強型別中介軟體（支援透過 IoC 容器自動建構子注入）。
        /// </summary>
        public WebApplication UseMiddleware<TMiddleware>() where TMiddleware : class, IMiddleware
        {
            _pipeline.Add(next =>
            {
                return async context =>
                {
                    var provider = context.RequestServices ?? ServiceProvider;
                    var middleware = provider.GetRequiredService<TMiddleware>();
                    await middleware.InvokeAsync(context, next);
                };
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
        /// 註冊單一 Controller 類別，掃描其標註 [Route] 的方法並由 IoC 容器於每次請求自動解析 Controller 與其深層依賴。
        /// </summary>
        public WebApplication MapController<TController>() where TController : class
        {
            RegisterControllerRoutes(typeof(TController));
            return this;
        }

        /// <summary>
        /// 自動掃描呼叫端組件（或指定組件）中所有以 Controller 結尾或標註 [Route] 的類別並註冊路由。
        /// </summary>
        public WebApplication MapControllers(Assembly? assembly = null)
        {
            var targetAssembly = assembly ?? Assembly.GetCallingAssembly();
            var controllerTypes = targetAssembly
                .GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract &&
                            (t.Name.EndsWith("Controller", StringComparison.Ordinal) ||
                             t.GetCustomAttributes<RouteAttribute>(true).Any()));

            foreach (var controllerType in controllerTypes)
            {
                RegisterControllerRoutes(controllerType);
            }

            return this;
        }

        private void RegisterControllerRoutes(Type controllerType)
        {
            Services.Add(new ServiceDescriptor(controllerType, controllerType, ServiceLifetime.Scoped));

            var classRoute = controllerType.GetCustomAttribute<RouteAttribute>(true);
            var prefix = classRoute != null ? classRoute.Template.TrimEnd('/') : string.Empty;
            var classFilters = controllerType.GetCustomAttributes(true).OfType<IActionFilter>().ToList();

            var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                var methodFilters = method.GetCustomAttributes(true).OfType<IActionFilter>().ToList();
                var actionFilters = classFilters
                    .Concat(methodFilters)
                    .GroupBy(f => f.GetType())
                    .Select(g => g.Last())
                    .ToList();

                var routeAttributes = method.GetCustomAttributes<RouteAttribute>(true);
                foreach (var routeAttr in routeAttributes)
                {
                    var actionPath = routeAttr.Template.StartsWith("/")
                        ? routeAttr.Template
                        : "/" + routeAttr.Template;
                    var fullPattern = string.IsNullOrEmpty(prefix)
                        ? actionPath
                        : (actionPath == "/" ? prefix : prefix + actionPath);

                    _router.AddRoute(routeAttr.Method, fullPattern, async context =>
                    {
                        var provider = context.RequestServices ?? ServiceProvider;
                        var controllerInstance = provider.GetService(controllerType)
                            ?? throw new InvalidOperationException($"無法由 IoC 容器解析 Controller：{controllerType.FullName}");

                        var stopwatch = Stopwatch.StartNew();
                        foreach (var filter in actionFilters)
                        {
                            await filter.OnActionExecutingAsync(context, controllerType.Name, method.Name);
                        }

                        var parameters = method.GetParameters();
                        var args = new object?[parameters.Length];
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            var pType = parameters[i].ParameterType;
                            if (pType == typeof(HttpContext))
                            {
                                args[i] = context;
                            }
                            else
                            {
                                args[i] = provider.GetService(pType);
                            }
                        }

                        var result = method.Invoke(controllerInstance, args);
                        if (result is Task task)
                        {
                            await task;
                        }

                        stopwatch.Stop();
                        for (int i = actionFilters.Count - 1; i >= 0; i--)
                        {
                            await actionFilters[i].OnActionExecutedAsync(
                                context,
                                controllerType.Name,
                                method.Name,
                                stopwatch.ElapsedMilliseconds);
                        }
                    });
                }
            }
        }

        /// <summary>
        /// 組裝包含每請求 IoC Scope 管理、中介軟體與路由終端的完整處理委派。
        /// </summary>
        public RequestDelegate BuildPipeline()
        {
            _serviceProvider = Services.BuildServiceProvider();

            RequestDelegate app = async (context) =>
            {
                var handled = await _router.RouteAsync(context);
                if (!handled)
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("404 Not Found");
                }
            };

            for (int i = _pipeline.Count - 1; i >= 0; i--)
            {
                app = _pipeline[i](app);
            }

            // 最外層為每個 HTTP 請求自動建立獨立的 IServiceScope，結束時自動釋放資源
            var innerApp = app;
            return async (context) =>
            {
                using var scope = _serviceProvider.CreateScope();
                context.RequestServices = scope.ServiceProvider;
                await innerApp(context);
            };
        }

        /// <summary>
        /// 啟動 Web 伺服器並執行請求處理管線。
        /// </summary>
        public async Task RunAsync(string url = "http://localhost:5000/", CancellationToken cancellationToken = default)
        {
            _server ??= new HttpListenerServer(url);
            var app = BuildPipeline();
            await _server.StartAsync(app, cancellationToken);
        }
    }
}
