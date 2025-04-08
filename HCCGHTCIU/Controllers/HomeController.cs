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
    /// �D����A�B�z�����D�n�\��
    /// </summary>
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger; // ��x�A��
        private readonly AuthService _authService; // �{�ҪA��
        private readonly IpQueryService _ipQueryService; // IP �d�ߪA��
        private readonly CacheService _cacheService; // �֨��A��

        /// <summary>
        /// �c�y���
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
        /// �����A�w�]���n�J��
        /// </summary>
        public IActionResult Index()
        {
            // �ˬd�O�_�w�n�J
            var userId = HttpContext.Session.GetInt32(SessionKeys.USER_ID);
            if (userId.HasValue)
            {
                // �ھڥΤᨤ�⭫�w�V���������O
                var userRole = HttpContext.Session.GetString(SessionKeys.USER_ROLE);
                return userRole == UserRole.Admin.ToString()
                    ? RedirectToAction("AdminDashboard")
                    : RedirectToAction("UserDashboard");
            }
            // ���n�J�A��ܵn�J��
            return View("Login");
        }

        /// <summary>
        /// �n�J�B�z
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password)
        {
            // �Ѽ�����
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                _logger.LogWarning("�ϥΪ̹��ըϥΪťΤ�W�αK�X�n�J");
                TempData["LoginError"] = "�п�J�Τ�W�M�K�X";
                return RedirectToAction("Index");
            }

            try
            {
                // �O�� IP �a�}�A�Ω�w���f�p
                string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                _logger.LogInformation($"�n�J���� - �Τ�: {username}, IP: {ipAddress}");

                // �ϥλ{�ҪA�����ҥΤ�
                var user = await _authService.AuthenticateUser(username, password);

                if (user != null)
                {
                    // �]�m Session
                    HttpContext.Session.SetInt32(SessionKeys.USER_ID, user.Id);
                    HttpContext.Session.SetString(SessionKeys.USER_ROLE, user.Role.ToString());
                    HttpContext.Session.SetString(SessionKeys.LAST_ACTIVITY, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

                    _logger.LogInformation($"�Τ� {username} �n�J���\�AIP: {ipAddress}");

                    // �]�m�w�����{�� Cookie
                    CookieHelper.SetAuthCookie(HttpContext, 60);

                    // �ھڨ��⭫�w�V
                    return user.Role == UserRole.Admin
                        ? RedirectToAction("AdminDashboard")
                        : RedirectToAction("UserDashboard");
                }
                else
                {
                    _logger.LogWarning($"�n�J���� - �Τ�: {username}, IP: {ipAddress}");
                    TempData["LoginError"] = "�b���αK�X���~";
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"�n�J�L�{�o�Ϳ��~ - �Τ�: {username}");
                TempData["LoginError"] = "�t�ο��~�A�еy��A��";
                return RedirectToAction("Index");
            }
        }

        /// <summary>
        /// �n�X�B�z
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            try
            {
                // �O���Τ�H���Ω��x
                var userId = HttpContext.Session.GetInt32(SessionKeys.USER_ID);
                var userRole = HttpContext.Session.GetString(SessionKeys.USER_ROLE);

                // �M�� Session
                HttpContext.Session.Clear();

                // �M���{�� Cookie
                CookieHelper.DeleteAuthCookie(HttpContext);

                // �O���n�X�ާ@
                _logger.LogInformation($"�Τ�n�X���\ - ID: {userId}, ����: {userRole}");

                // ��^����
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "�n�X�L�{�o�Ϳ��~");
                return RedirectToAction("Index");
            }
        }

        /// <summary>
        /// �Τ����O
        /// </summary>
        [UserAuth]
        public IActionResult UserDashboard()
        {
            return View();
        }

        /// <summary>
        /// �޲z������O
        /// </summary>
        [AdminAuth]
        public async Task<IActionResult> AdminDashboard()
        {
            // ����t�βέp�ƾ�
            var stats = await _ipQueryService.GetSystemStatsAsync();

            ViewBag.RecordCount = stats.RecordCount;
            ViewBag.LogCount = stats.LogCount;
            ViewBag.StartupTime = stats.StartupTime.ToString("yyyy-MM-dd HH:mm:ss");

            return View();
        }

        /// <summary>
        /// IP �d�߭���
        /// </summary>
        [UserAuth]
        public IActionResult IpLookup()
        {
            return View();
        }

        /// <summary>
        /// IP �d�߳B�z
        /// </summary>
        [HttpPost]
        [UserAuth]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Lookup(string ipAddress)
        {
            try
            {
                // �ϥ� IP �d�ߪA��
                var result = await _ipQueryService.LookupIpAsync(ipAddress);
                return View("Result", result);
            }
            catch (ArgumentException ex)
            {
                // IP �榡�L��
                ModelState.AddModelError("", ex.Message);
                return View("IpLookup");
            }
            catch (InvalidOperationException ex)
            {
                // IP ���O�d�a�}��
                ModelState.AddModelError("", ex.Message);
                return View("IpLookup");
            }
            catch (Exception ex)
            {
                // ��L���w�������~
                _logger.LogError(ex, $"IP �d�ߥ���: {ipAddress}");
                ModelState.AddModelError("", $"�d�ߥ���: {ex.Message}");
                return View("IpLookup");
            }
        }

        /// <summary>
        /// �϶�����R����
        /// </summary>
        [UserAuth]
        public IActionResult Blockchain()
        {
            return View();
        }

        /// <summary>
        /// �d�ߤ�x����
        /// </summary>
        [HttpGet]
        [AdminAuth]
        public async Task<IActionResult> QueryLogs(int page = 1)
        {
            // ����d�ߤ�x
            const int pageSize = 20;
            var (logs, totalPages, totalLogs) = await _ipQueryService.GetQueryLogsAsync(page, pageSize);

            // �]�m�����H��
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalLogs = totalLogs;
            ViewBag.PageSize = pageSize;

            return View(logs);
        }

        /// <summary>
        /// �M���d�ߤ�x
        /// </summary>
        [HttpPost]
        [AdminAuth]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearQueryLogs()
        {
            try
            {
                // �ϥΪA�ȲM����x
                bool success = await _ipQueryService.ClearQueryLogsAsync();

                if (success)
                {
                    TempData["SuccessMessage"] = "���\�M���d�ߤ�x";
                }
                else
                {
                    TempData["ErrorMessage"] = "�M����x����";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "�M���d�ߤ�x����");
                TempData["ErrorMessage"] = $"�M����x����: {ex.Message}";
            }

            return RedirectToAction("AdminDashboard");
        }

        /// <summary>
        /// �Τ�޲z����
        /// </summary>
        [AdminAuth]
        public async Task<IActionResult> ManageUsers()
        {
            // ����Τ�C��]�ѪA�ȴ��ѡ^
            var users = await _authService.GetAllUsersAsync();
            return View(users);
        }

        /// <summary>
        /// �K�[�Τ�
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
                    TempData["ErrorMessage"] = "�Τ�W�M�K�X���ର��";
                    return RedirectToAction("ManageUsers");
                }

                // �K�[�Τ�
                bool success = await _authService.AddUserAsync(username, password, role);

                if (success)
                {
                    TempData["SuccessMessage"] = "�Τ�K�[���\";
                }
                else
                {
                    TempData["ErrorMessage"] = "�Τ�K�[���ѡA�i��Τ�W�w�s�b";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "�K�[�Τᥢ��");
                TempData["ErrorMessage"] = $"�K�[�Τᥢ��: {ex.Message}";
            }

            return RedirectToAction("ManageUsers");
        }

        /// <summary>
        /// �R���Τ�
        /// </summary>
        [HttpPost]
        [AdminAuth]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(int id)
        {
            try
            {
                // �����\�R���ۤv
                var currentUserId = HttpContext.Session.GetInt32(SessionKeys.USER_ID);
                if (currentUserId == id)
                {
                    TempData["ErrorMessage"] = "����R����e�n�J���Τ�";
                    return RedirectToAction("ManageUsers");
                }

                // �R���Τ�
                bool success = await _authService.DeleteUserAsync(id);

                if (success)
                {
                    TempData["SuccessMessage"] = "�Τ�R�����\";
                }
                else
                {
                    TempData["ErrorMessage"] = "�Τ�R�����ѡA�Τ�i�ण�s�b";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "�R���Τᥢ��");
                TempData["ErrorMessage"] = $"�R���Τᥢ��: {ex.Message}";
            }

            return RedirectToAction("ManageUsers");
        }

        /// <summary>
        /// ���p�F������
        /// </summary>
        public IActionResult Privacy()
        {
            return View();
        }

        /// <summary>
        /// ���~����
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