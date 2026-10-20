using System;
using System.IO;
using System.Text;
using WebFramework.Http;

namespace WebFramework.Logging
{
    /// <summary>
    /// 預設主控台 HTTP 請求／回應日誌記錄器。
    /// </summary>
    public class ConsoleHttpLogger : IHttpLogger
    {
        public void LogRequest(HttpContext context, string controllerName, string actionName)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var req = context.Request;
            var bodyPreview = ReadSeekableStreamPreview(req.Body);
            var bodySegment = string.IsNullOrEmpty(bodyPreview) ? string.Empty : $" | Body: {bodyPreview}";

            Console.WriteLine(
                $"[HttpLogging][Request]  --> [{DateTime.Now:HH:mm:ss.fff}] {req.Method} {req.Path} -> {controllerName}.{actionName}{bodySegment}");
        }

        public void LogResponse(HttpContext context, string controllerName, string actionName, long elapsedMs)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var req = context.Request;
            var res = context.Response;
            var bodyPreview = ReadSeekableStreamPreview(res.Body);
            var bodySegment = string.IsNullOrEmpty(bodyPreview) ? string.Empty : $" | Body: {bodyPreview}";

            Console.WriteLine(
                $"[HttpLogging][Response] <-- [{DateTime.Now:HH:mm:ss.fff}] {req.Method} {req.Path} <- {controllerName}.{actionName} | Status: {res.StatusCode} ({elapsedMs}ms){bodySegment}");
        }

        private static string ReadSeekableStreamPreview(Stream? stream)
        {
            if (stream == null || !stream.CanRead || !stream.CanSeek || stream.Length == 0)
            {
                return string.Empty;
            }

            var originalPosition = stream.Position;
            try
            {
                stream.Position = 0;
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                return reader.ReadToEnd();
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }
    }
}

