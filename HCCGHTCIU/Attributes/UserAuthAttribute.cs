using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using HCCGHTCIU.Models;

namespace HCCGHTCIU.Attributes
{
    /// <summary>
    /// 用戶授權特性，用於控制需要登入的頁面訪問
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class UserAuthAttribute : TypeFilterAttribute
    {
        public UserAuthAttribute() : base(typeof(UserAuthFilter))
        {
        }

        /// <summary>
        /// 用戶授權過濾器實現
        /// </summary>
        private class UserAuthFilter : IAuthorizationFilter
        {
            private readonly ILogger<UserAuthFilter> _logger;

            public UserAuthFilter(ILogger<UserAuthFilter> logger)
            {
                _logger = logger;
            }

            public void OnAuthorization(AuthorizationFilterContext context)
            {
                // 檢查是否已登入
                var userId = context.HttpContext.Session.GetInt32("UserId");

                if (!userId.HasValue)
                {
                    // 未登入，重定向到登入頁
                    _logger.LogWarning("未授權的頁面訪問嘗試");
                    context.Result = new RedirectToActionResult("Index", "Home", null);
                }
            }
        }
    }
}