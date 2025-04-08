using HCCGHTCIU.Attributes;
using HCCGHTCIU.Models;
using HCCGHTCIU.Services;
using HCCGHTCIU.Helpers;
using HCCGHTCIU.Constants;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace HCCGHTCIU.Controllers
{
    /// <summary>
    /// 主控制器，處理網站主要功能
    /// </summary>
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger; // 日誌服務
        private readonly AuthService _authService; // 認證服務
        private readonly IpQueryService _ipQueryService; // IP 查詢服務
        private readonly CacheService _cacheService; // 快取服務

        /// <summary>
        /// 構造函數
        /// </summary>
        public HomeController(
            ILogger<HomeController> logger,
            AuthService authService,
            IpQueryService ipQueryService,
            CacheService cacheService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _ipQueryService = ipQueryService ?? throw new ArgumentNullException(nameof(ipQueryService));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        }

        /// <summary>
        /// 首頁，預設為登入頁
        /// </summary>
        public IActionResult Index()
        {
            // 檢查是否已登入
            var userId = HttpContext.Session.GetInt32(SessionKeys.USER_ID);
            if (userId.HasValue)
            {
                // 根據用戶角色重定向到相應儀表板
                var userRole = HttpContext.Session.GetString(SessionKeys.USER_ROLE);
                return userRole == UserRole.Admin.ToString()
                    ? RedirectToAction("AdminDashboard")
                    : RedirectToAction("UserDashboard");
            }
            // 未登入，顯示登入頁
            return View("Login");
        }

        /// <summary>
        /// 登入處理
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password)
        {
            // 參數驗證
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                _logger.LogWarning("使用者嘗試使用空用戶名或密碼登入");
                TempData["LoginError"] = "請輸入用戶名和密碼";
                return RedirectToAction("Index");
            }

            try
            {
                // 記錄 IP 地址，用於安全審計
                string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                _logger.LogInformation($"登入嘗試 - 用戶: {username}, IP: {ipAddress}");

                // 使用認證服務驗證用戶
                var user = await _authService.AuthenticateUser(username, password);

                if (user != null)
                {
                    // 設置 Session
                    HttpContext.Session.SetInt32(SessionKeys.USER_ID, user.Id);
                    HttpContext.Session.SetString(SessionKeys.USER_ROLE, user.Role.ToString());
                    HttpContext.Session.SetString(SessionKeys.LAST_ACTIVITY, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

                    _logger.LogInformation($"用戶 {username} 登入成功，IP: {ipAddress}");

                    // 設置安全的認證 Cookie
                    CookieHelper.SetAuthCookie(HttpContext, 60);

                    // 根據角色重定向
                    return user.Role == UserRole.Admin
                        ? RedirectToAction("AdminDashboard")
                        : RedirectToAction("UserDashboard");
                }
                else
                {
                    _logger.LogWarning($"登入失敗 - 用戶: {username}, IP: {ipAddress}");
                    TempData["LoginError"] = "帳號或密碼錯誤";
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"登入過程發生錯誤 - 用戶: {username}");
                TempData["LoginError"] = "系統錯誤，請稍後再試";
                return RedirectToAction("Index");
            }
        }

        /// <summary>
        /// 登出處理
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            try
            {
                // 記錄用戶信息用於日誌
                var userId = HttpContext.Session.GetInt32(SessionKeys.USER_ID);
                var userRole = HttpContext.Session.GetString(SessionKeys.USER_ROLE);

                // 清除 Session
                HttpContext.Session.Clear();

                // 清除認證 Cookie
                CookieHelper.DeleteAuthCookie(HttpContext);

                // 記錄登出操作
                _logger.LogInformation($"用戶登出成功 - ID: {userId}, 角色: {userRole}");

                // 返回首頁
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "登出過程發生錯誤");
                return RedirectToAction("Index");
            }
        }

        /// <summary>
        /// 用戶儀表板
        /// </summary>
        [UserAuth]
        public IActionResult UserDashboard()
        {
            return View();
        }

        /// <summary>
        /// 管理員儀表板
        /// </summary>
        [AdminAuth]
        public async Task<IActionResult> AdminDashboard()
        {
            // 獲取系統統計數據
            var stats = await _ipQueryService.GetSystemStatsAsync();

            ViewBag.RecordCount = stats.RecordCount;
            ViewBag.LogCount = stats.LogCount;
            ViewBag.StartupTime = stats.StartupTime.ToString("yyyy-MM-dd HH:mm:ss");

            return View();
        }

        /// <summary>
        /// IP 查詢頁面
        /// </summary>
        [UserAuth]
        public IActionResult IpLookup()
        {
            return View();
        }

        /// <summary>
        /// IP 查詢處理
        /// </summary>
        [HttpPost]
        [UserAuth]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Lookup(string ipAddress)
        {
            try
            {
                // 使用 IP 查詢服務
                var result = await _ipQueryService.LookupIpAsync(ipAddress);
                return View("Result", result);
            }
            catch (ArgumentException ex)
            {
                // IP 格式無效
                ModelState.AddModelError("", ex.Message);
                return View("IpLookup");
            }
            catch (InvalidOperationException ex)
            {
                // IP 為保留地址等
                ModelState.AddModelError("", ex.Message);
                return View("IpLookup");
            }
            catch (Exception ex)
            {
                // 其他未預期的錯誤
                _logger.LogError(ex, $"IP 查詢失敗: {ipAddress}");
                ModelState.AddModelError("", $"查詢失敗: {ex.Message}");
                return View("IpLookup");
            }
        }

        /// <summary>
        /// 區塊鏈分析頁面
        /// </summary>
        [UserAuth]
        public IActionResult Blockchain()
        {
            return View();
        }

        /// <summary>
        /// 查詢日誌頁面
        /// </summary>
        [HttpGet]
        [AdminAuth]
        public async Task<IActionResult> QueryLogs(int page = 1)
        {
            // 獲取查詢日誌
            const int pageSize = 20;
            var (logs, totalPages, totalLogs) = await _ipQueryService.GetQueryLogsAsync(page, pageSize);

            // 設置分頁信息
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalLogs = totalLogs;
            ViewBag.PageSize = pageSize;

            return View(logs);
        }

        /// <summary>
        /// 清除查詢日誌
        /// </summary>
        [HttpPost]
        [AdminAuth]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearQueryLogs()
        {
            try
            {
                // 使用服務清除日誌
                bool success = await _ipQueryService.ClearQueryLogsAsync();

                if (success)
                {
                    TempData["SuccessMessage"] = "成功清除查詢日誌";
                }
                else
                {
                    TempData["ErrorMessage"] = "清除日誌失敗";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清除查詢日誌失敗");
                TempData["ErrorMessage"] = $"清除日誌失敗: {ex.Message}";
            }

            return RedirectToAction("AdminDashboard");
        }

        /// <summary>
        /// 用戶管理頁面
        /// </summary>
        [AdminAuth]
        public async Task<IActionResult> ManageUsers()
        {
            // 獲取用戶列表（由服務提供）
            var users = await _authService.GetAllUsersAsync();
            return View(users);
        }

        /// <summary>
        /// 添加用戶
        /// </summary>
        [HttpPost]
        [AdminAuth]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddUser(string username, string password, UserRole role)
        {
            try
            {
                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    TempData["ErrorMessage"] = "用戶名和密碼不能為空";
                    return RedirectToAction("ManageUsers");
                }

                // 添加用戶
                bool success = await _authService.AddUserAsync(username, password, role);

                if (success)
                {
                    TempData["SuccessMessage"] = "用戶添加成功";
                }
                else
                {
                    TempData["ErrorMessage"] = "用戶添加失敗，可能用戶名已存在";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加用戶失敗");
                TempData["ErrorMessage"] = $"添加用戶失敗: {ex.Message}";
            }

            return RedirectToAction("ManageUsers");
        }

        /// <summary>
        /// 刪除用戶
        /// </summary>
        [HttpPost]
        [AdminAuth]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(int id)
        {
            try
            {
                // 不允許刪除自己
                var currentUserId = HttpContext.Session.GetInt32(SessionKeys.USER_ID);
                if (currentUserId == id)
                {
                    TempData["ErrorMessage"] = "不能刪除當前登入的用戶";
                    return RedirectToAction("ManageUsers");
                }

                // 刪除用戶
                bool success = await _authService.DeleteUserAsync(id);

                if (success)
                {
                    TempData["SuccessMessage"] = "用戶刪除成功";
                }
                else
                {
                    TempData["ErrorMessage"] = "用戶刪除失敗，用戶可能不存在";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刪除用戶失敗");
                TempData["ErrorMessage"] = $"刪除用戶失敗: {ex.Message}";
            }

            return RedirectToAction("ManageUsers");
        }

        /// <summary>
        /// 隱私政策頁面
        /// </summary>
        public IActionResult Privacy()
        {
            return View();
        }

        /// <summary>
        /// 錯誤頁面
        /// </summary>
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
        }
    }
}