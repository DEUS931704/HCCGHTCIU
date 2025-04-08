using System.Security.Cryptography;     // 引用加密相關命名空間
using System.Text;                    // 引用文本處理相關命名空間
using Microsoft.AspNetCore.Http;      // 引用HTTP相關命名空間

namespace HCCGHTCIU.Helpers
{
    /// <summary>
    /// 身份驗證輔助類，提供身份驗證相關功能
    /// </summary>
    public static class AuthHelper
    {
        // Cookie名稱常數
        private const string AdminAuthCookieName = "AdminAuthenticated";

        /// <summary>
        /// 驗證管理員身份
        /// </summary>
        /// <param name="context">HTTP上下文</param>
        /// <returns>如果是管理員則返回true，否則返回false</returns>
        public static bool IsAdminAuthenticated(HttpContext context)
        {
            return context.Request.Cookies.ContainsKey(AdminAuthCookieName) &&
                   context.Request.Cookies[AdminAuthCookieName] == "true";
        }

        /// <summary>
        /// 設置管理員認證Cookie
        /// </summary>
        /// <param name="context">HTTP上下文</param>
        /// <param name="expireMinutes">過期時間（分鐘）</param>
        public static void SetAdminAuthCookie(HttpContext context, int expireMinutes = 60)
        {
            context.Response.Cookies.Append(AdminAuthCookieName, "true", new CookieOptions
            {
                HttpOnly = true,                 // 防止客戶端JavaScript訪問
                Secure = true,                   // 僅通過HTTPS傳輸
                SameSite = SameSiteMode.Strict,  // 防止跨站請求
                Expires = DateTime.Now.AddMinutes(expireMinutes) // 設置過期時間
            });
        }

        /// <summary>
        /// 刪除管理員認證Cookie
        /// </summary>
        /// <param name="context">HTTP上下文</param>
        public static void ClearAdminAuthCookie(HttpContext context)
        {
            context.Response.Cookies.Delete(AdminAuthCookieName);
        }

        /// <summary>
        /// 生成一個新的隨機密碼
        /// </summary>
        /// <param name="length">密碼長度（默認為10）</param>
        /// <returns>隨機密碼</returns>
        public static string GenerateRandomPassword(int length = 10)
        {
            const string validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890!@#$%^&*()_-+=";
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] bytes = new byte[length];
                rng.GetBytes(bytes);

                var sb = new StringBuilder(length);
                foreach (byte b in bytes)
                {
                    sb.Append(validChars[b % validChars.Length]);
                }
                return sb.ToString();
            }
        }
    }
}