using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WebFramework.Http;
using WebFramework.Routing;

namespace WebFramework.Sample
{
    /// <summary>
    /// 訂單控制器（Controller 層），由 IoC 容器自動解析並注入 IOrderService（進而自動注入 IOrderRepository）。
    /// </summary>
    [Route("/orders")]
    public class OrderController
    {
        private readonly IOrderService _orderService;
        public Guid InstanceId { get; } = Guid.NewGuid();

        public OrderController(IOrderService orderService)
        {
            _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
        }

        [Route("GET", "/")]
        public async Task GetOrders(HttpContext context)
        {
            var orders = _orderService.GetAllOrders().ToList();
            await context.Response.JsonAsync(new
            {
                ControllerInstanceId = InstanceId,
                TotalCount = orders.Count,
                Orders = orders
            });
        }

        [Route("POST", "/")]
        public async Task CreateOrder(HttpContext context)
        {
            var body = await context.Request.ReadBodyAsStringAsync();
            string item = "DefaultItem";
            int quantity = 1;

            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("item", out var itemProp))
                {
                    item = itemProp.GetString() ?? item;
                }
                if (doc.RootElement.TryGetProperty("quantity", out var qtyProp))
                {
                    quantity = qtyProp.GetInt32();
                }
            }

            var created = _orderService.PlaceOrder(item, quantity);
            context.Response.StatusCode = 201;
            await context.Response.JsonAsync(new
            {
                ControllerInstanceId = InstanceId,
                Order = created
            });
        }
    }
}

