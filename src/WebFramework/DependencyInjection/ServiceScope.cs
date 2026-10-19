using System;

namespace WebFramework.DependencyInjection
{
    /// <summary>
    /// 實作單次請求或作業範疇（Scope），負責持有範疇 ServiceProvider 並於結束時釋放資源。
    /// </summary>
    public class ServiceScope : IServiceScope
    {
        private readonly ServiceProvider _scopedProvider;
        private bool _disposed;

        public IServiceProvider ServiceProvider => _scopedProvider;

        public ServiceScope(ServiceProvider scopedProvider)
        {
            _scopedProvider = scopedProvider ?? throw new ArgumentNullException(nameof(scopedProvider));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _scopedProvider.DisposeScopeInstances();
        }
    }
}

