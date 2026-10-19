using System;

namespace WebFramework.Sample
{
    /// <summary>
    /// 訂單領域實體。
    /// </summary>
    public class Order
    {
        public string Id { get; set; } = string.Empty;
        public string Item { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}

