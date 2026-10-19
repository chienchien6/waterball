using System;
using System.Collections.Generic;

namespace WebFramework.Http
{
    /// <summary>
    /// 封裝單次 HTTP 請求與回應的上下文環境。
    /// </summary>
    public class HttpContext
    {
        public HttpRequest Request { get; }
        public HttpResponse Response { get; }
        public IDictionary<string, object> Items { get; }

        public HttpContext()
        {
            Request = new HttpRequest();
            Response = new HttpResponse();
            Items = new Dictionary<string, object>();
        }

        public HttpContext(HttpRequest request, HttpResponse response)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Response = response ?? throw new ArgumentNullException(nameof(response));
            Items = new Dictionary<string, object>();
        }
    }
}

