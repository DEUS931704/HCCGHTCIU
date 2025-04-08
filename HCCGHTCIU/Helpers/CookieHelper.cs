using HCCGHTCIU.Constants;
using Microsoft.AspNetCore.Http;
using System;

namespace HCCGHTCIU.Helpers
{
    /// <summary>
    /// Cookie 輔助類
    /// 提供統一的 Cookie 操作方法
    /// </summary>
    public static class CookieHelper
    {
        /// <summary>
        /// 設置安全的 Cookie
        /// </summary>
        /// <param name="context">HTTP 上下文</param>
        /// <param name="key">Cookie 名稱</param>
        /// <param name="value">Cookie 值</param>
        /// <param name="expireMinutes">過期時間（分鐘）</param>
        public static void SetSecureCookie(HttpContext context, string key, string value, int expireMinutes = 60)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context), "HTTP 上下文不能為空");

            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Cookie 名稱不能為空", nameof(key));

            // 配置安全的 Cookie 選項
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,                 // 防止客戶端 JavaScript 訪問
                Secure = true,                   // 僅通過 HTTPS 傳輸
                SameSite = SameSiteMode.Strict,  // 防止跨站請求
                Expires = DateTime.Now.AddMinutes(expireMinutes) // 設置過期時間
            };

            // 添加 Cookie
            context.Response.Cookies.Append(key, value, cookieOptions);
        }

        /// <summary>
        /// 設置認證 Cookie
        /// </summary>
        /// <param name="context">HTTP 上下文</param>
        /// <param name="expireMinutes">過期時間（分鐘）</param>
        public static void SetAuthCookie(HttpContext context, int expireMinutes = 60)
        {
            // 使用 GUID 作為令牌值
            SetSecureCookie(context, CookieKeys.AUTH_COOKIE, Guid.NewGuid().ToString(), expireMinutes);
        }

        /// <summary>
        /// 設置管理員認證 Cookie
        /// </summary>
        /// <param name="context">HTTP 上下文</param>
        /// <param name="expireMinutes">過期時間（分鐘）</param>
        public static void SetAdminAuthCookie(HttpContext context, int expireMinutes = 60)
        {
            // 檢查用戶是否有管理員權限
            var userRole = context.Session.GetString(SessionKeys.USER_ROLE);
            if (userRole != HCCGHTCIU.Models.UserRole.Admin.ToString())
            {
                throw new UnauthorizedAccessException("只有管理員可以設置管理員認證 Cookie");
            }

            SetSecureCookie(context, CookieKeys.ADMIN_AUTH_COOKIE, "true", expireMinutes);
        }

        /// <summary>
        /// 刪除 Cookie
        /// </summary>
        /// <param name="context">HTTP 上下文</param>
        /// <param name="key">Cookie 名稱</param>
        public static void DeleteCookie(HttpContext context, string key)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context), "HTTP 上下文不能為空");

            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Cookie 名稱不能為空", nameof(key));

            context.Response.Cookies.Delete(key);
        }

        /// <summary>
        /// 刪除認證 Cookie
        /// </summary>
        /// <param name="context">HTTP 上下文</param>
        public static void DeleteAuthCookie(HttpContext context)
        {
            DeleteCookie(context, CookieKeys.AUTH_COOKIE);
        }

        /// <summary>
        /// 刪除管理員認證 Cookie
        /// </summary>
        /// <param name="context">HTTP 上下文</param>
        public static void DeleteAdminAuthCookie(HttpContext context)
        {
            // 檢查是否已登入
            if (context.Session.GetInt32(SessionKeys.USER_ID) == null)
            {
                throw new UnauthorizedAccessException("用戶未登入");
            }

            // 檢查用戶是否有管理員權限
            var userRole = context.Session.GetString(SessionKeys.USER_ROLE);
            if (userRole != HCCGHTCIU.Models.UserRole.Admin.ToString())
            {
                throw new UnauthorizedAccessException("只有管理員可以刪除管理員認證 Cookie");
            }

            DeleteCookie(context, CookieKeys.ADMIN_AUTH_COOKIE);
        }

        /// <summary>
        /// 檢查是否有管理員認證
        /// </summary>
        /// <param name="context">HTTP 上下文</param>
        /// <returns>是否有管理員認證</returns>
        public static bool IsAdminAuthenticated(HttpContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context), "HTTP 上下文不能為空");

            return context.Request.Cookies.ContainsKey(CookieKeys.ADMIN_AUTH_COOKIE) &&
                   context.Request.Cookies[CookieKeys.ADMIN_AUTH_COOKIE] == "true";
        }
    }
}