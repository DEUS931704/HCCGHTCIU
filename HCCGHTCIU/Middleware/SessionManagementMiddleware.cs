using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;

namespace HCCGHTCIU.Middleware
{
    /// <summary>
    /// Session 管理中間件
    /// 集中處理 Session 的過期檢查和活動時間更新
    /// </summary>
    public class SessionManagementMiddleware
    {
        private readonly RequestDelegate _next; // 下一個請求處理委託
        private readonly ILogger<SessionManagementMiddleware> _logger; // 日誌服務

        // 會話相關常量，從 Constants 類中獲取
        private const string SESSION_USER_ID = "UserId"; // 用戶ID的Session鍵
        private const string SESSION_USER_ROLE = "UserRole"; // 用戶角色的Session鍵
        private const string SESSION_LAST_ACTIVITY = "LastActivity"; // 最後活動時間的Session鍵
        private const int SESSION_TIMEOUT_MINUTES = 30; // 會話超時時間（分鐘）

        /// <summary>
        /// 構造函數
        /// </summary>
        /// <param name="next">下一個請求處理委託</param>
        /// <param name="logger">日誌服務</param>
        public SessionManagementMiddleware(RequestDelegate next, ILogger<SessionManagementMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 請求處理方法
        /// </summary>
        /// <param name="context">HTTP上下文</param>
        /// <returns>Task</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            // 檢查是否為需要身份驗證的路徑
            if (RequiresAuthentication(context.Request.Path))
            {
                // 檢查會話是否過期
                if (IsSessionExpired(context))
                {
                    _logger.LogInformation("用戶會話已過期，重定向至登入頁面");
                    context.Response.Redirect("/Home/Index"); // 重定向至登入頁面
                    return;
                }

                // 更新會話活動時間
                UpdateSessionActivity(context);
            }

            // 繼續處理請求
            await _next(context);
        }

        /// <summary>
        /// 檢查路徑是否需要身份驗證
        /// </summary>
        /// <param name="path">請求路徑</param>
        /// <returns>是否需要身份驗證</returns>
        private bool RequiresAuthentication(PathString path)
        {
            // 需要身份驗證的路徑列表
            string[] authenticatedPaths = new[] {
                "/Home/UserDashboard",
                "/Home/AdminDashboard",
                "/Home/IpLookup",
                "/Home/Lookup",
                "/Home/Blockchain",
                "/Home/QueryLogs",
                "/Home/ClearQueryLogs",
                "/Home/ManageUsers"
            };

            foreach (var authPath in authenticatedPaths)
            {
                if (path.StartsWithSegments(authPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 檢查會話是否過期
        /// </summary>
        /// <param name="context">HTTP上下文</param>
        /// <returns>會話是否已過期</returns>
        private bool IsSessionExpired(HttpContext context)
        {
            // 檢查用戶 ID 是否存在
            if (context.Session.GetInt32(SESSION_USER_ID) == null)
            {
                return true; // 沒有用戶 ID，視為會話過期
            }

            // 檢查最後活動時間
            string lastActivityStr = context.Session.GetString(SESSION_LAST_ACTIVITY);
            if (string.IsNullOrEmpty(lastActivityStr))
            {
                return true; // 沒有最後活動時間，視為會話過期
            }

            // 解析最後活動時間
            if (long.TryParse(lastActivityStr, out long lastActivity))
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                // 檢查是否超過超時時間
                return (now - lastActivity) > (SESSION_TIMEOUT_MINUTES * 60);
            }

            return true; // 解析失敗，視為會話過期
        }

        /// <summary>
        /// 更新會話活動時間
        /// </summary>
        /// <param name="context">HTTP上下文</param>
        private void UpdateSessionActivity(HttpContext context)
        {
            if (context.Session.GetInt32(SESSION_USER_ID) != null)
            {
                // 使用 Unix 時間戳記錄，節省儲存空間
                context.Session.SetString(SESSION_LAST_ACTIVITY,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
                _logger.LogDebug("已更新用戶會話活動時間");
            }
        }
    }

    /// <summary>
    /// Session 管理中間件擴展方法
    /// </summary>
    public static class SessionManagementMiddlewareExtensions
    {
        /// <summary>
        /// 添加 Session 管理中間件到應用程序管道
        /// </summary>
        /// <param name="builder">應用程序建造器</param>
        /// <returns>應用程序建造器</returns>
        public static IApplicationBuilder UseSessionManagement(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<SessionManagementMiddleware>();
        }
    }
}