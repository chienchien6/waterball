using System;

namespace WebFramework.DependencyInjection
{
    /// <summary>
    /// 定義依賴注入容器的服務解析與範疇建立契約。
    /// </summary>
    public interface IServiceProvider
    {
        /// <summary>
        /// 解析指定型別的服務實例；若未註冊且無法自動建構則回傳 null。
        /// </summary>
        object? GetService(Type serviceType);

        /// <summary>
        /// 解析指定型別的服務實例；若無法解析則拋出異常。
        /// </summary>
        T GetRequiredService<T>() where T : notnull;

        /// <summary>
        /// 建立單次請求或獨立作業的服務範疇（Scope）。
        /// </summary>
        IServiceScope CreateScope();
    }
}

