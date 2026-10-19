using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using WebFramework.Http;

namespace WebFramework.Server
{
    /// <summary>
    /// 基於 .NET 原生 HttpListener 的 HTTP 伺服器實作。
    /// </summary>
    public class HttpListenerServer : IHttpServer
    {
        private readonly HttpListener _listener;
        private readonly string _prefix;
        private bool _isRunning;

        public HttpListenerServer(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                throw new ArgumentException("Prefix cannot be null or empty.", nameof(prefix));
            }

            _prefix = prefix.EndsWith("/") ? prefix : prefix + "/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(_prefix);
        }

        public async Task StartAsync(RequestDelegate handler, CancellationToken cancellationToken = default)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _listener.Start();
            _isRunning = true;
            Console.WriteLine($"[HttpListenerServer] Listening on {_prefix}...");

            using (cancellationToken.Register(() =>
            {
                try
                {
                    _isRunning = false;
                    _listener.Stop();
                }
                catch
                {
                    // 忽略停止時的異常
                }
            }))
            {
                while (_isRunning && !cancellationToken.IsCancellationRequested)
                {
                    HttpListenerContext rawContext;
                    try
                    {
                        rawContext = await _listener.GetContextAsync();
                    }
                    catch (HttpListenerException) when (!isRunning())
                    {
                        break;
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    // 非同步處理各個連線
                    _ = ProcessRequestAsync(rawContext, handler);
                }
            }

            bool isRunning() => _isRunning && _listener.IsListening;
        }

        private async Task ProcessRequestAsync(HttpListenerContext rawContext, RequestDelegate handler)
        {
            try
            {
                var request = new HttpRequest
                {
                    Method = rawContext.Request.HttpMethod,
                    Path = rawContext.Request.Url?.AbsolutePath ?? "/",
                    Body = rawContext.Request.InputStream
                };

                // 填入 Headers
                foreach (string? key in rawContext.Request.Headers.AllKeys)
                {
                    if (key != null && rawContext.Request.Headers[key] is string headerVal)
                    {
                        request.Headers[key] = headerVal;
                    }
                }

                // 填入 Query 參數
                var queryString = rawContext.Request.QueryString;
                foreach (string? key in queryString.AllKeys)
                {
                    if (key != null && queryString[key] is string queryVal)
                    {
                        request.Query[key] = queryVal;
                    }
                }

                // 準備 Response
                using var responseBodyStream = new MemoryStream();
                var response = new HttpResponse
                {
                    StatusCode = 200,
                    Body = responseBodyStream
                };

                var context = new HttpContext(request, response);

                // 執行管線
                await handler(context);

                // 將 HttpResponse 寫回 rawContext.Response
                rawContext.Response.StatusCode = response.StatusCode;
                foreach (var header in response.Headers)
                {
                    try
                    {
                        if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                        {
                            rawContext.Response.ContentType = header.Value;
                        }
                        else if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                        {
                            if (long.TryParse(header.Value, out var len))
                            {
                                rawContext.Response.ContentLength64 = len;
                            }
                        }
                        else
                        {
                            rawContext.Response.Headers[header.Key] = header.Value;
                        }
                    }
                    catch
                    {
                        // 忽略限制欄位異常
                    }
                }

                responseBodyStream.Position = 0;
                await responseBodyStream.CopyToAsync(rawContext.Response.OutputStream);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HttpListenerServer] Error processing request: {ex}");
                try
                {
                    rawContext.Response.StatusCode = 500;
                }
                catch
                {
                    // 忽略連線已中斷
                }
            }
            finally
            {
                try
                {
                    rawContext.Response.OutputStream.Close();
                    rawContext.Response.Close();
                }
                catch
                {
                    // 忽略關閉異常
                }
            }
        }

        public Task StopAsync()
        {
            _isRunning = false;
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch
            {
                // 忽略停止異常
            }
            return Task.CompletedTask;
        }
    }
}

