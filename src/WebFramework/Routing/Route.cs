using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WebFramework.Http;

namespace WebFramework.Routing
{
    /// <summary>
    /// 代表單一路徑比對規則與對應處理常式。
    /// </summary>
    public class Route
    {
        private readonly Regex _regex;
        private readonly List<string> _parameterNames = new List<string>();

        public string Method { get; }
        public string Pattern { get; }
        public RequestDelegate Handler { get; }

        public Route(string method, string pattern, RequestDelegate handler)
        {
            Method = method?.ToUpperInvariant() ?? throw new ArgumentNullException(nameof(method));
            Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));

            _regex = CompilePattern(pattern, _parameterNames);
        }

        public bool Match(string method, string path, IDictionary<string, string> routeValues)
        {
            if (!string.Equals(Method, method, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var match = _regex.Match(path ?? string.Empty);
            if (!match.Success)
            {
                return false;
            }

            if (routeValues != null)
            {
                foreach (var paramName in _parameterNames)
                {
                    var group = match.Groups[paramName];
                    if (group.Success)
                    {
                        routeValues[paramName] = Uri.UnescapeDataString(group.Value);
                    }
                }
            }

            return true;
        }

        private static Regex CompilePattern(string pattern, List<string> parameterNames)
        {
            // 將類似 "/users/{id}/orders/{orderId}" 轉為正規表達式
            var normalizedPattern = pattern.Trim('/');
            if (string.IsNullOrEmpty(normalizedPattern))
            {
                return new Regex("^/?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            }

            var segments = normalizedPattern.Split('/');
            var regexPattern = "^";

            foreach (var segment in segments)
            {
                regexPattern += "/";
                if (segment.StartsWith("{") && segment.EndsWith("}"))
                {
                    var paramName = segment.Substring(1, segment.Length - 2);
                    parameterNames.Add(paramName);
                    regexPattern += $"(?<{paramName}>[^/]+)";
                }
                else
                {
                    regexPattern += Regex.Escape(segment);
                }
            }

            regexPattern += "/?$";
            return new Regex(regexPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }
    }
}

