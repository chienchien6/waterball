using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace WebFramework.DependencyInjection
{
    /// <summary>
    /// 預設 IoC 容器實作，支援建構子自動遞迴注入、三種生命週期管理與循環依賴偵測。
    /// </summary>
    public class ServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, ServiceDescriptor> _descriptors;
        private readonly ConcurrentDictionary<Type, object> _singletons;
        private readonly ConcurrentDictionary<Type, object> _scopedInstances = new ConcurrentDictionary<Type, object>();
        private readonly List<IDisposable> _transientDisposables = new List<IDisposable>();
        private readonly ServiceProvider _root;

        public ServiceProvider(IEnumerable<ServiceDescriptor> descriptors)
        {
            if (descriptors == null)
            {
                throw new ArgumentNullException(nameof(descriptors));
            }

            _descriptors = new Dictionary<Type, ServiceDescriptor>();
            _singletons = new ConcurrentDictionary<Type, object>();
            _root = this;

            foreach (var descriptor in descriptors)
            {
                _descriptors[descriptor.ServiceType] = descriptor;
                if (descriptor.ImplementationInstance != null)
                {
                    _singletons[descriptor.ServiceType] = descriptor.ImplementationInstance;
                }
            }
        }

        private ServiceProvider(ServiceProvider root)
        {
            _root = root;
            _descriptors = root._descriptors;
            _singletons = root._singletons;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == null)
            {
                throw new ArgumentNullException(nameof(serviceType));
            }

            return Resolve(serviceType, new HashSet<Type>());
        }

        public T GetRequiredService<T>() where T : notnull
        {
            var service = GetService(typeof(T));
            if (service == null)
            {
                throw new InvalidOperationException($"無法解析未註冊的服務型別：{typeof(T).FullName}。");
            }

            return (T)service;
        }

        public IServiceScope CreateScope()
        {
            return new ServiceScope(new ServiceProvider(_root));
        }

        private object? Resolve(Type serviceType, HashSet<Type> callSiteChain)
        {
            if (serviceType == typeof(IServiceProvider))
            {
                return this;
            }

            if (!callSiteChain.Add(serviceType))
            {
                var chainText = string.Join(" -> ", callSiteChain.Select(t => t.Name)) + " -> " + serviceType.Name;
                throw new InvalidOperationException($"偵測到循環依賴：{chainText}");
            }

            try
            {
                if (_descriptors.TryGetValue(serviceType, out var descriptor))
                {
                    switch (descriptor.Lifetime)
                    {
                        case ServiceLifetime.Singleton:
                            return _root._singletons.GetOrAdd(
                                serviceType,
                                _ => _root.CreateFromDescriptor(descriptor, callSiteChain));

                        case ServiceLifetime.Scoped:
                            return _scopedInstances.GetOrAdd(
                                serviceType,
                                _ => CreateFromDescriptor(descriptor, callSiteChain));

                        case ServiceLifetime.Transient:
                            var instance = CreateFromDescriptor(descriptor, callSiteChain);
                            TrackDisposable(instance);
                            return instance;

                        default:
                            throw new NotSupportedException($"不支援的生命週期：{descriptor.Lifetime}");
                    }
                }

                // 若為未顯式註冊的具體類別（如 Controller 或 Middleware），支援自動建構並注入其依賴
                if (!serviceType.IsAbstract && !serviceType.IsInterface)
                {
                    var instance = CreateInstance(serviceType, callSiteChain);
                    TrackDisposable(instance);
                    return instance;
                }

                return null;
            }
            finally
            {
                callSiteChain.Remove(serviceType);
            }
        }

        private object CreateFromDescriptor(ServiceDescriptor descriptor, HashSet<Type> callSiteChain)
        {
            if (descriptor.ImplementationInstance != null)
            {
                return descriptor.ImplementationInstance;
            }

            if (descriptor.Factory != null)
            {
                return descriptor.Factory(this);
            }

            if (descriptor.ImplementationType != null)
            {
                return CreateInstance(descriptor.ImplementationType, callSiteChain);
            }

            throw new InvalidOperationException($"服務描述缺少實作型別或工廠：{descriptor.ServiceType.FullName}");
        }

        private object CreateInstance(Type implementationType, HashSet<Type> callSiteChain)
        {
            var constructors = implementationType
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderByDescending(c => c.GetParameters().Length)
                .ToArray();

            if (constructors.Length == 0)
            {
                throw new InvalidOperationException($"型別 {implementationType.FullName} 沒有可用的 public 建構子。");
            }

            Exception? lastError = null;
            foreach (var ctor in constructors)
            {
                var parameters = ctor.GetParameters();
                var args = new object?[parameters.Length];
                var matched = true;

                for (int i = 0; i < parameters.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;
                    try
                    {
                        var resolved = Resolve(paramType, callSiteChain);
                        if (resolved == null)
                        {
                            if (parameters[i].HasDefaultValue)
                            {
                                args[i] = parameters[i].DefaultValue;
                            }
                            else
                            {
                                matched = false;
                                lastError = new InvalidOperationException(
                                    $"無法為型別 '{implementationType.FullName}' 的建構子參數 '{parameters[i].Name}' 解析服務型別 '{paramType.FullName}'。");
                                break;
                            }
                        }
                        else
                        {
                            args[i] = resolved;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        throw;
                    }
                }

                if (matched)
                {
                    return ctor.Invoke(args);
                }
            }

            throw lastError ?? new InvalidOperationException($"無法建構型別 {implementationType.FullName}。");
        }

        private void TrackDisposable(object instance)
        {
            if (instance is IDisposable disposable && !ReferenceEquals(instance, this))
            {
                lock (_transientDisposables)
                {
                    _transientDisposables.Add(disposable);
                }
            }
        }

        internal void DisposeScopeInstances()
        {
            foreach (var kvp in _scopedInstances)
            {
                if (kvp.Value is IDisposable disposable && !ReferenceEquals(kvp.Value, this))
                {
                    disposable.Dispose();
                }
            }
            _scopedInstances.Clear();

            lock (_transientDisposables)
            {
                foreach (var disposable in _transientDisposables)
                {
                    disposable.Dispose();
                }
                _transientDisposables.Clear();
            }
        }
    }
}

