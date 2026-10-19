using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace WebFramework.Http
{
    /// <summary>
    /// 抽象化 HTTP 回應操作。
    /// </summary>
    public class HttpResponse
    {
        public int StatusCode { get; set; } = 200;
        public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Stream Body { get; set; } = Stream.Null;

        public async Task WriteAsync(string content)
        {
            if (content == null)
            {
                return;
            }

            if (!Headers.ContainsKey("Content-Type"))
            {
                Headers["Content-Type"] = "text/plain; charset=utf-8";
            }

            byte[] bytes = Encoding.UTF8.GetBytes(content);
            Headers["Content-Length"] = bytes.Length.ToString();
            await Body.WriteAsync(bytes, 0, bytes.Length);
            await Body.FlushAsync();
        }

        public async Task JsonAsync(object data)
        {
            Headers["Content-Type"] = "application/json; charset=utf-8";
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            byte[] bytes = Encoding.UTF8.GetBytes(json);
            Headers["Content-Length"] = bytes.Length.ToString();
            await Body.WriteAsync(bytes, 0, bytes.Length);
            await Body.FlushAsync();
        }
    }
}

