using System.Collections.Generic;

namespace WebFramework.Sample
{
    /// <summary>
    /// 訂單商業邏輯服務介面（Service 層）。
    /// </summary>
    public interface IOrderService
    {
        IEnumerable<Order> GetAllOrders();
        Order PlaceOrder(string item, int quantity);
    }
}

