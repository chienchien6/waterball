namespace WebFramework.DependencyInjection
{
    /// <summary>
    /// 定義依賴注入容器中服務實例的生命週期。
    /// </summary>
    public enum ServiceLifetime
    {
        /// <summary>
        /// 應用程式生命週期內共用單一實例。
        /// </summary>
        Singleton,

        /// <summary>
        /// 單次請求範疇（Scope）內共用單一實例，不同請求彼此隔離。
        /// </summary>
        Scoped,

        /// <summary>
        /// 每次解析皆建立全新實例。
        /// </summary>
        Transient
    }
}

