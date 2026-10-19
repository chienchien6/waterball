## 類別設計

```mermaid
classDiagram
    class ServiceLifetime {
        <<enumeration>>
        Singleton
        Scoped
        Transient
    }
    class ServiceDescriptor {
        +Type ServiceType
        +Type ImplementationType
        +object ImplementationInstance
        +Func~IServiceProvider, object~ Factory
        +ServiceLifetime Lifetime
    }
    class IServiceCollection {
        <<interface>>
        +Add(ServiceDescriptor descriptor) IServiceCollection
        +AddSingleton~TService, TImplementation~() IServiceCollection
        +AddScoped~TService, TImplementation~() IServiceCollection
        +AddTransient~TService, TImplementation~() IServiceCollection
        +BuildServiceProvider() IServiceProvider
    }
    class ServiceCollection {
        -List~ServiceDescriptor~ _descriptors
        +Add(ServiceDescriptor descriptor) IServiceCollection
        +AddSingleton~TService, TImplementation~() IServiceCollection
        +AddScoped~TService, TImplementation~() IServiceCollection
        +AddTransient~TService, TImplementation~() IServiceCollection
        +BuildServiceProvider() IServiceProvider
    }
    class IServiceProvider {
        <<interface>>
        +GetService(Type serviceType) object
        +GetRequiredService~T~() T
        +CreateScope() IServiceScope
    }
    class IServiceScope {
        <<interface>>
        +IServiceProvider ServiceProvider
        +Dispose()
    }
    class ServiceProvider {
        -Dictionary~Type, ServiceDescriptor~ _descriptors
        -ConcurrentDictionary~Type, object~ _singletons
        -ConcurrentDictionary~Type, object~ _scopedInstances
        -ServiceProvider _root
        +GetService(Type serviceType) object
        +GetRequiredService~T~() T
        +CreateScope() IServiceScope
        -Resolve(Type serviceType, HashSet~Type~ callSiteChain) object
        -CreateInstance(Type implementationType, HashSet~Type~ callSiteChain) object
    }
    class ServiceScope {
        +IServiceProvider ServiceProvider
        +Dispose()
    }
    class RouteAttribute {
        +string Template
        +string Method
    }
    class HttpContext {
        +HttpRequest Request
        +HttpResponse Response
        +IDictionary~string, object~ Items
        +IServiceProvider RequestServices
    }
    class WebApplication {
        +IServiceCollection Services
        -IServiceProvider _serviceProvider
        -IHttpServer _server
        -IRouter _router
        +UseMiddleware~TMiddleware~() WebApplication
        +MapController~TController~() WebApplication
        +MapControllers() WebApplication
        +RunAsync(string url) Task
    }
    class OrderController {
        -IOrderService _orderService
        +GetOrders(HttpContext context) Task
        +CreateOrder(HttpContext context) Task
    }
    class IOrderService {
        <<interface>>
        +GetAllOrders() IEnumerable~Order~
        +PlaceOrder(string item, int quantity) Order
    }
    class OrderService {
        -IOrderRepository _orderRepository
        +GetAllOrders() IEnumerable~Order~
        +PlaceOrder(string item, int quantity) Order
    }
    class IOrderRepository {
        <<interface>>
        +FindAll() IEnumerable~Order~
        +Save(Order order) Order
    }
    class InMemoryOrderRepository {
        -List~Order~ _orders
        +FindAll() IEnumerable~Order~
        +Save(Order order) Order
    }

    IServiceCollection <|.. ServiceCollection : 實作
    ServiceCollection *-- ServiceDescriptor : 聚合註冊資訊
    ServiceDescriptor *-- ServiceLifetime : 定義生命週期
    IServiceProvider <|.. ServiceProvider : 實作
    IServiceScope <|.. ServiceScope : 實作
    ServiceCollection ..> ServiceProvider : 建立
    ServiceProvider ..> ServiceDescriptor : 依據描述解析與建構
    ServiceProvider ..> ServiceScope : 建立請求範疇
    ServiceScope *-- ServiceProvider : 持有範疇容器
    HttpContext ..> IServiceProvider : 提供 RequestServices
    WebApplication *-- IServiceCollection : 擁有服務註冊表
    WebApplication ..> IServiceProvider : 建立容器與每請求 Scope
    WebApplication ..> RouteAttribute : 反射掃描 Controller 路由
    WebApplication ..> OrderController : 透過容器自動解析實例
    OrderController ..> IOrderService : 建構子注入
    IOrderService <|.. OrderService : 實作
    OrderService ..> IOrderRepository : 建構子注入
    IOrderRepository <|.. InMemoryOrderRepository : 實作
```

## 設計說明

- 類別職責：
  - `ServiceLifetime` 與 `ServiceDescriptor`：描述服務的抽象型別、具體實作型別、工廠委派與生命週期（`Singleton` 應用程式單例、`Scoped` 每請求單例、`Transient` 每次解析新建）。
  - `IServiceCollection` 與 `ServiceCollection`：提供流暢的依賴註冊 API（`AddSingleton`、`AddScoped`、`AddTransient`），並在啟動時建構出 `IServiceProvider`。
  - `IServiceProvider`、`ServiceProvider` 與 `IServiceScope`、`ServiceScope`：IoC 核心引擎。負責依型別遞迴分析最匹配建構子（Constructor Injection）、自動解析整條依賴鏈、偵測循環依賴，並管理每個 HTTP 請求獨立的 `Scoped` 實例生命週期與資源釋放。
  - `RouteAttribute`：標註於 Controller 類別與 Action 方法上的路由與 HTTP 方法屬性（如 `[Route("GET", "/orders")]`）。
  - `HttpContext` 與 `WebApplication`：擴充既有框架，在 `WebApplication` 暴露 `Services` 供註冊依賴；每次收到 HTTP 請求時自動建立 `IServiceScope` 並掛載至 `HttpContext.RequestServices`；支援 `MapController<TController>()` / `MapControllers()`，在路由命中時由容器自動實例化 Controller 及其深層依賴（Service、Repository），同時支援 `IMiddleware` 建構子注入。
  - `OrderController`、`IOrderService` / `OrderService`、`IOrderRepository` / `InMemoryOrderRepository`：驗證三層自動注入（Controller → Service → Repository）的示範情境類別。
- 關係與設計意圖：
  - `ServiceCollection` 組合多筆 `ServiceDescriptor`，並在 `BuildServiceProvider()` 時交由 `ServiceProvider` 建立快取與解析索引，分離「註冊期」與「解析期」職責。
  - `ServiceProvider` 透過反射遞迴解析建構子參數，使 `OrderController` 只需宣告依賴 `IOrderService`、`OrderService` 只需宣告依賴 `IOrderRepository`，無須手動 `new` 任何實作類別；同時利用 `callSiteChain` 偵測並攔截循環依賴（如 A → B → A）。
  - `WebApplication` 透過每次請求建立 `IServiceScope`，確保 `Scoped` 服務（如 `OrderService`）在同一個請求內共用實例、不同請求間彼此隔離，而 `Singleton`（如 `InMemoryOrderRepository`）則跨請求保存資料。
- 實作順序：
  1. 實作 `ServiceLifetime` 與 `ServiceDescriptor`（無外部相依的基礎資料模型）。
  2. 定義 `IServiceProvider`、`IServiceScope`、`IServiceCollection` 介面。
  3. 實作 `ServiceCollection`、`ServiceProvider` 與 `ServiceScope`（含遞迴建構子注入、未註冊具體類別自動建構、循環依賴偵測與生命週期快取）。
  4. 實作 `RouteAttribute`（含 `HttpGetAttribute`、`HttpPostAttribute` 便利屬性），並擴充 `HttpContext`（新增 `RequestServices`）與 `WebApplication`（新增 `Services`、支援 `IMiddleware` 依賴注入、Request Scope 自動管理與 `MapController` 路由綁定）。
  5. 於範例專案實作 `InMemoryOrderRepository` → `OrderService` → `OrderController` 三層架構，並實際啟動或測試驗證完整注入鏈與請求隔離行為。

