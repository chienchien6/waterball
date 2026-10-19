using System;
using System.Collections;
using System.Collections.Generic;

namespace WebFramework.DependencyInjection
{
    /// <summary>
    /// 預設服務註冊表實作。
    /// </summary>
    public class ServiceCollection : IServiceCollection
    {
        private readonly List<ServiceDescriptor> _descriptors = new List<ServiceDescriptor>();

        public IServiceCollection Add(ServiceDescriptor descriptor)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException(nameof(descriptor));
            }

            _descriptors.Add(descriptor);
            return this;
        }

        public IServiceCollection AddSingleton<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService
        {
            return Add(new ServiceDescriptor(typeof(TService), typeof(TImplementation), ServiceLifetime.Singleton));
        }

        public IServiceCollection AddSingleton<TService>() where TService : class
        {
            return Add(new ServiceDescriptor(typeof(TService), typeof(TService), ServiceLifetime.Singleton));
        }

        public IServiceCollection AddSingleton<TService>(TService instance) where TService : class
        {
            return Add(new ServiceDescriptor(typeof(TService), instance));
        }

        public IServiceCollection AddSingleton<TService>(Func<IServiceProvider, TService> factory) where TService : class
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            return Add(new ServiceDescriptor(typeof(TService), sp => factory(sp), ServiceLifetime.Singleton));
        }

        public IServiceCollection AddScoped<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService
        {
            return Add(new ServiceDescriptor(typeof(TService), typeof(TImplementation), ServiceLifetime.Scoped));
        }

        public IServiceCollection AddScoped<TService>() where TService : class
        {
            return Add(new ServiceDescriptor(typeof(TService), typeof(TService), ServiceLifetime.Scoped));
        }

        public IServiceCollection AddTransient<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService
        {
            return Add(new ServiceDescriptor(typeof(TService), typeof(TImplementation), ServiceLifetime.Transient));
        }

        public IServiceCollection AddTransient<TService>() where TService : class
        {
            return Add(new ServiceDescriptor(typeof(TService), typeof(TService), ServiceLifetime.Transient));
        }

        public IServiceProvider BuildServiceProvider()
        {
            return new ServiceProvider(_descriptors);
        }

        public IEnumerator<ServiceDescriptor> GetEnumerator() => _descriptors.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

