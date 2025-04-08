using System;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HCCGHTCIU.Services
{
    /// <summary>
    /// API 異常處理服務，提供統一的異常解析和日誌記錄
    /// </summary>
    public class ApiExceptionHandler
    {
        private readonly ILogger<ApiExceptionHandler> _logger;

        /// <summary>
        /// 構造函數
        /// </summary>
        /// <param name="logger">日誌服務</param>
        public ApiExceptionHandler(ILogger<ApiExceptionHandler> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 處理 API 調用過程中可能發生的各類異常
        /// </summary>
        /// <param name="ex">拋出的異常</param>
        /// <returns>異常處理結果(是否已處理, 用戶友好訊息)</returns>
        public (bool IsHandled, string UserMessage) HandleApiException(Exception ex)
        {
            switch (ex)
            {
                // HTTP 請求異常：可能是網絡問題、服務不可用等
                case HttpRequestException httpEx:
                    _logger.LogError(httpEx, "外部服務請求失敗");
                    return (true, "無法連接到外部服務，請稍後重試");

                // JSON 解析異常：返回的數據格式不符合預期
                case JsonException jsonEx:
                    _logger.LogError(jsonEx, "解析 API 響應時發生錯誤");
                    return (true, "服務返回的數據格式異常");

                // 超時異常：請求時間過長
                case TimeoutException timeoutEx:
                    _logger.LogError(timeoutEx, "API 請求超時");
                    return (true, "服務請求超時，請檢查網絡連接");

                // 操作邏輯異常：調用過程中出現不合法的狀態
                case InvalidOperationException opEx:
                    _logger.LogError(opEx, "API 調用邏輯錯誤");
                    return (true, "服務內部發生錯誤");

                // 其他未預期的異常，需要進一步調查
                default:
                    _logger.LogError(ex, "未預期的 API 異常");
                    return (false, "發生未知錯誤");
            }
        }
    }
}