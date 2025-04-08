using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace HCCGHTCIU.Middleware
{
    /// <summary>
    /// 增強的全局異常處理中間件
    /// 提供統一、一致的錯誤處理機制
    /// </summary>
    public class EnhancedExceptionHandlerMiddleware
    {
        private readonly RequestDelegate _next; // 請求處理管道
        private readonly ILogger<EnhancedExceptionHandlerMiddleware> _logger; // 日誌服務
        private readonly IWebHostEnvironment _environment; // 環境信息

        // 異常類型到 HTTP 狀態碼的映射
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
            { typeof(TimeoutException), HttpStatusCode.RequestTimeout },
            { typeof(HttpRequestException), HttpStatusCode.BadGateway }
        };

        /// <summary>
        /// 構造函數
        /// </summary>
        /// <param name="next">請求處理管道</param>
        /// <param name="logger">日誌服務</param>
        /// <param name="environment">環境信息</param>
        public EnhancedExceptionHandlerMiddleware(
            RequestDelegate next,
            ILogger<EnhancedExceptionHandlerMiddleware> logger,
            IWebHostEnvironment environment)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        /// <summary>
        /// 中間件處理方法
        /// </summary>
        /// <param name="context">HTTP 上下文</param>
        /// <returns>Task</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                // 繼續請求管道
                await _next(context);

                // 檢查 404 錯誤
                if (context.Response.StatusCode == 404 && !context.Response.HasStarted)
                {
                    // 記錄 404 錯誤
                    _logger.LogWarning("找不到頁面: {Path}", context.Request.Path);

                    // 如果是 API 請求，返回 JSON 響應
                    if (IsApiRequest(context.Request))
                    {
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(new
                        {
                            error = "找不到請求的資源",
                            path = context.Request.Path,
                            statusCode = 404
                        }));
                    }
                    else
                    {
                        // 對於網頁請求，重定向到錯誤頁面
                        context.Response.Redirect("/Home/Error?statusCode=404");
                    }
                }
            }
            catch (Exception ex)
            {
                // 記錄異常
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
        /// <param name="context">HTTP 上下文</param>
        /// <param name="exception">異常</param>
        /// <returns>Task</returns>
        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            // 獲取請求 ID 用於追蹤
            string requestId = Activity.Current?.Id ?? context.TraceIdentifier;

            // 獲取適當的 HTTP 狀態碼
            HttpStatusCode statusCode = HttpStatusCode.InternalServerError;
            foreach (var mapping in _exceptionStatusCodes)
            {
                if (mapping.Key.IsAssignableFrom(exception.GetType()))
                {
                    statusCode = mapping.Value;
                    break;
                }
            }

            // 設置響應狀態碼
            context.Response.StatusCode = (int)statusCode;

            // 判斷是 API 請求還是網頁請求
            if (IsApiRequest(context.Request))
            {
                // API 請求返回 JSON 響應
                context.Response.ContentType = "application/json";

                // 創建錯誤響應
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

                // 序列化並返回
                await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = _environment.IsDevelopment()
                }));
            }
            else
            {
                // 網頁請求重定向到錯誤頁面
                // 保存錯誤信息到 TempData
                if (context.Session != null && context.Session.IsAvailable)
                {
                    context.Session.SetString("ErrorMessage", GetErrorMessage(exception));
                    context.Session.SetString("ErrorRequestId", requestId);
                    context.Session.SetInt32("ErrorStatusCode", (int)statusCode);
                }

                // 重定向到錯誤頁面
                context.Response.Redirect($"/Home/Error?statusCode={(int)statusCode}&requestId={requestId}");
            }
        }

        /// <summary>
        /// 獲取用戶友好的錯誤消息
        /// </summary>
        /// <param name="exception">異常</param>
        /// <returns>錯誤消息</returns>
        private string GetErrorMessage(Exception exception)
        {
            // 在生產環境中使用通用錯誤消息
            if (!_environment.IsDevelopment())
            {
                return "發生伺服器錯誤。我們已記錄此問題並正在處理。";
            }

            // 在開發環境中顯示詳細錯誤
            return exception switch
            {
                ArgumentException _ => "請求參數無效。",
                UnauthorizedAccessException _ => "您無權訪問此資源。",
                KeyNotFoundException _ => "找不到請求的資源。",
                TimeoutException _ => "請求處理超時。",
                HttpRequestException _ => "訪問外部服務時出錯。",
                _ => $"發生錯誤: {exception.Message}"
            };
        }

        /// <summary>
        /// 判斷是否為 API 請求
        /// </summary>
        /// <param name="request">HTTP 請求</param>
        /// <returns>是否為 API 請求</returns>
        private bool IsApiRequest(HttpRequest request)
        {
            // 檢查 Accept 頭
            bool isJsonRequest = request.Headers["Accept"].Any(h => h?.Contains("application/json") == true);

            // 檢查是否是 AJAX 請求
            bool isAjaxRequest = request.Headers["X-Requested-With"] == "XMLHttpRequest";

            // 檢查路徑是否包含 /api/
            bool isApiPath = request.Path.StartsWithSegments("/api");

            return isJsonRequest || isAjaxRequest || isApiPath;
        }
    }

    /// <summary>
    /// 增強錯誤處理中間件擴展方法
    /// </summary>
    public static class EnhancedExceptionHandlerMiddlewareExtensions
    {
        /// <summary>
        /// 使用增強的全局異常處理中間件
        /// </summary>
        /// <param name="builder">應用程序建造器</param>
        /// <returns>應用程序建造器</returns>
        public static IApplicationBuilder UseEnhancedExceptionHandler(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<EnhancedExceptionHandlerMiddleware>();
        }
    }
}