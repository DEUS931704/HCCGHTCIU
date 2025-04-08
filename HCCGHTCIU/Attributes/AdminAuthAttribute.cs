using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using HCCGHTCIU.Models;

namespace HCCGHTCIU.Attributes
{
    /// <summary>
    /// 管理員授權特性，用於控制管理頁面的訪問
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class AdminAuthAttribute : TypeFilterAttribute
    {
        public AdminAuthAttribute() : base(typeof(AdminAuthFilter))
        {
        }

        /// <summary>
        /// 管理員授權過濾器實現
        /// </summary>
        private class AdminAuthFilter : IAuthorizationFilter
        {
            private readonly ILogger<AdminAuthFilter> _logger;

            public AdminAuthFilter(ILogger<AdminAuthFilter> logger)
            {
                _logger = logger;
            }

            public void OnAuthorization(AuthorizationFilterContext context)
            {
                // 檢查是否為管理員
                var userRole = context.HttpContext.Session.GetString("UserRole");

                if (userRole != UserRole.Admin.ToString())
                {
                    // 非管理員，重定向到登入頁
                    _logger.LogWarning("非管理員嘗試訪問管理頁面");
                    context.Result = new RedirectToActionResult("Index", "Home", null);
                }
            }
        }
    }
}