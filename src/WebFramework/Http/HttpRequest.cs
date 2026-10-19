using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace WebFramework.Http
{
    /// <summary>
    /// 抽象化 HTTP 請求資訊。
    /// </summary>
    public class HttpRequest
    {
        public string Method { get; set; } = "GET";
        public string Path { get; set; } = "/";
        public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public IDictionary<string, string> Query { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Stream Body { get; set; } = Stream.Null;

        public async Task<string> ReadBodyAsStringAsync()
        {
            if (Body == null || !Body.CanRead)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(Body, Encoding.UTF8, leaveOpen: true);
            return await reader.ReadToEndAsync();
        }
    }
}

