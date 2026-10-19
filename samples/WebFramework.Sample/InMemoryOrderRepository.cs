using System;
using System.Collections.Generic;
using System.Linq;

namespace WebFramework.Sample
{
    /// <summary>
    /// 記憶體內訂單儲存庫實作（由 IoC 容器以 Singleton 生命週期管理並自動注入至 OrderService）。
    /// </summary>
    public class InMemoryOrderRepository : IOrderRepository
    {
        private readonly List<Order> _orders = new List<Order>();
        public Guid InstanceId { get; } = Guid.NewGuid();

        public IEnumerable<Order> FindAll()
        {
            return _orders.ToList();
        }

        public Order Save(Order order)
        {
            if (order == null)
            {
                throw new ArgumentNullException(nameof(order));
            }

            _orders.Add(order);
            return order;
        }
    }
}

