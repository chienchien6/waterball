using System.Collections.Generic;

namespace WebFramework.Sample
{
    /// <summary>
    /// 訂單資料存取介面（Repository 層）。
    /// </summary>
    public interface IOrderRepository
    {
        IEnumerable<Order> FindAll();
        Order Save(Order order);
    }
}

