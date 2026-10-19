using System;

namespace WebFramework.DependencyInjection
{
    /// <summary>
    /// 代表單一請求或獨立生命週期範疇的服務容器，於結束時釋放範疇內實例。
    /// </summary>
    public interface IServiceScope : IDisposable
    {
        IServiceProvider ServiceProvider { get; }
    }
}

