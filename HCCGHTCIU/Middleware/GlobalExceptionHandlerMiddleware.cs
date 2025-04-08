using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace HCCGHTCIU.Middleware
{
    /// <summary>
    /// 全局異常處理中間件
    /// 統一處理未捕獲的異常，提高系統穩定性
    /// </summary>
    public class GlobalExceptionHandlerMiddleware
    {
        private readonly RequestDelegate _next; // 請求處理管道
        private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger; // 日誌服務
        private readonly IWebHostEnvironment _environment; // 環境信息

        // 自定義異常映射表
        private static readonly Dictionary<Type, HttpStatusCode> _exceptionStatusCodes = new()
        {
            { typeof(ArgumentException), HttpStatusCode.BadRequest },
            { typeof(ArgumentNullException), HttpStatusCode.BadRequest },
            { typeof(ArgumentOutOfRangeException), HttpStatusCode.BadRequest },
            { typeof(InvalidOperationException), HttpStatusCode.BadRequest },
            { typeof(UnauthorizedAccessException), HttpStatusCode.Unauthorized },
            { typeof(FileNotFoundException), HttpStatusCode.NotFound },
            { typeof(DirectoryNotFoundException), HttpStatusCode.NotFound },
            { typeof(KeyNotFoundException), HttpStatusCode.NotFound },
            { typeof(NotImplementedException), HttpStatusCode.NotImplemented },
            { typeof(TimeoutException), HttpStatusCode.RequestTimeout }
        };

        /// <summary>
        /// 構造函數
        /// </summary>
        public GlobalExceptionHandlerMiddleware(
            RequestDelegate next,
            ILogger<GlobalExceptionHandlerMiddleware> logger,
            IWebHostEnvironment environment)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        /// <summary>
        /// 中間件處理方法
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                // 繼續處理請求管道
                await _next(context);
            }
            catch (Exception ex)
            {
                // 記錄異常詳細信息
                _logger.LogError(ex, "未處理的異常: {Message}, URL: {Path}, Method: {Method}",
                    ex.Message,
                    context.Request.Path,
                    context.Request.Method);

                // 處理異常響應
                await HandleExceptionAsync(context, ex);
            }
        }

        /// <summary>
        /// 處理異常響應
        /// </summary>
        private Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            // 獲取請求ID用於追蹤
            string requestId = Activity.Current?.Id ?? context.TraceIdentifier;

            // 根據異常類型設置HTTP狀態碼
            HttpStatusCode statusCode = HttpStatusCode.InternalServerError;

            // 嘗試尋找匹配的狀態碼
            foreach (var mapping in _exceptionStatusCodes)
            {
                if (mapping.Key.IsAssignableFrom(exception.GetType()))
                {
                    statusCode = mapping.Value;
                    break;
                }
            }

            // 設置響應內容類型和狀態碼
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)statusCode;

            // 創建響應對象
            var response = new
            {
                error = GetErrorMessage(exception),
                requestId,
                statusCode = (int)statusCode,
                // 僅在開發環境下返回詳細錯誤信息
                details = _environment.IsDevelopment() ? new
                {
                    message = exception.Message,
                    stackTrace = exception.StackTrace?.Split(Environment.NewLine),
                    source = exception.Source,
                    innerException = exception.InnerException?.Message
                } : null
            };

            // 序列化並返回響應
            return context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = _environment.IsDevelopment()
            }));
        }

        /// <summary>
        /// 獲取用戶友好的錯誤消息
        /// </summary>
        private string GetErrorMessage(Exception exception)
        {
            // 在生產環境中使用通用錯誤消息
            if (!_environment.IsDevelopment())
            {
                return "發生內部伺服器錯誤。我們已記錄此問題並正在處理中。";
            }

            // 在開發環境中顯示詳細錯誤
            return exception switch
            {
                ArgumentException _ => "請求參數無效。",
                UnauthorizedAccessException _ => "您無權訪問此資源。",
                KeyNotFoundException _ => "找不到請求的資源。",
                TimeoutException _ => "請求處理超時。",
                _ => $"發生錯誤: {exception.Message}"
            };
        }
    }

    /// <summary>
    /// 中間件擴展方法
    /// </summary>
    public static class GlobalExceptionHandlerMiddlewareExtensions
    {
        /// <summary>
        /// 添加全局異常處理中間件
        /// </summary>
        public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        }
    }
}