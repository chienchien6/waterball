using System;
using System.Collections.Generic;

namespace WebFramework.Sample
{
    /// <summary>
    /// 訂單服務實作，透過建構子自動注入 IOrderRepository。
    /// </summary>
    public class OrderService : IOrderService
    {
        private readonly IOrderRepository _orderRepository;
        public Guid InstanceId { get; } = Guid.NewGuid();

        public OrderService(IOrderRepository orderRepository)
        {
            _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        }

        public IEnumerable<Order> GetAllOrders()
        {
            return _orderRepository.FindAll();
        }

        public Order PlaceOrder(string item, int quantity)
        {
            var order = new Order
            {
                Id = $"ORD-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                Item = item,
                Quantity = quantity,
                CreatedAt = DateTime.UtcNow
            };

            return _orderRepository.Save(order);
        }
    }
}

