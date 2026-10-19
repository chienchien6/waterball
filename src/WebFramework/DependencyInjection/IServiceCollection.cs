using System;
using System.Collections.Generic;

namespace WebFramework.DependencyInjection
{
    /// <summary>
    /// 定義服務註冊表介面，支援 Singleton、Scoped 與 Transient 註冊並建構容器。
    /// </summary>
    public interface IServiceCollection : IEnumerable<ServiceDescriptor>
    {
        IServiceCollection Add(ServiceDescriptor descriptor);
        IServiceCollection AddSingleton<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService;
        IServiceCollection AddSingleton<TService>() where TService : class;
        IServiceCollection AddSingleton<TService>(TService instance) where TService : class;
        IServiceCollection AddSingleton<TService>(Func<IServiceProvider, TService> factory) where TService : class;
        IServiceCollection AddScoped<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService;
        IServiceCollection AddScoped<TService>() where TService : class;
        IServiceCollection AddTransient<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService;
        IServiceCollection AddTransient<TService>() where TService : class;
        IServiceProvider BuildServiceProvider();
    }
}

