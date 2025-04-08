using HCCGHTCIU.Data;
using HCCGHTCIU.Helpers;
using HCCGHTCIU.Models;
using Microsoft.EntityFrameworkCore;

namespace HCCGHTCIU.Services
{
    /// <summary>
    /// 用戶認證服務
    /// 處理用戶認證和用戶管理相關功能
    /// </summary>
    public class AuthService
    {
        private readonly ApplicationDbContext _context; // 數據庫上下文
        private readonly ILogger<AuthService> _logger; // 日誌服務

        // 登入失敗嘗試記錄（用於防止暴力破解）
        private readonly Dictionary<string, (int Count, DateTime LastAttempt)> _failedLoginAttempts
            = new Dictionary<string, (int, DateTime)>();

        // 鎖定閾值和時間
        private const int MaxFailedAttempts = 5;
        private const int LockoutMinutes = 15;

        /// <summary>
        /// 構造函數
        /// </summary>
        public AuthService(
            ApplicationDbContext context,
            ILogger<AuthService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 認證用戶
        /// </summary>
        /// <param name="username">用戶名</param>
        /// <param name="password">密碼</param>
        /// <returns>若認證成功則返回用戶對象，否則返回 null</returns>
        public async Task<User> AuthenticateUser(string username, string password)
        {
            // 驗證輸入
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                _logger.LogWarning("嘗試使用空用戶名或密碼登入");
                return null;
            }

            try
            {
                // 檢查是否被鎖定
                if (IsUserLockedOut(username))
                {
                    _logger.LogWarning($"用戶 {username} 因多次登入失敗已被鎖定");
                    return null;
                }

                // 查詢用戶
                var user = await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Username == username);

                // 用戶不存在
                if (user == null)
                {
                    _logger.LogWarning($"用戶 {username} 不存在");
                    RecordFailedLoginAttempt(username);
                    return null;
                }

                // 驗證密碼
                if (PasswordHasher.VerifyPassword(password, user.PasswordHash))
                {
                    _logger.LogInformation($"用戶 {username} 認證成功");
                    ResetFailedLoginAttempts(username);
                    return user;
                }
                else
                {
                    _logger.LogWarning($"用戶 {username} 密碼驗證失敗");
                    RecordFailedLoginAttempt(username);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"認證過程發生錯誤：{username}");
                throw;
            }
        }

        /// <summary>
        /// 獲取所有用戶
        /// </summary>
        /// <returns>用戶列表</returns>
        public async Task<List<User>> GetAllUsersAsync()
        {
            try
            {
                return await _context.Users
                    .AsNoTracking()
                    .OrderBy(u => u.Id)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "獲取用戶列表失敗");
                throw;
            }
        }

        /// <summary>
        /// 添加新用戶
        /// </summary>
        /// <param name="username">用戶名</param>
        /// <param name="password">密碼</param>
        /// <param name="role">用戶角色</param>
        /// <returns>是否添加成功</returns>
        public async Task<bool> AddUserAsync(string username, string password, UserRole role)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("用戶名和密碼不能為空");
            }

            try
            {
                // 檢查用戶名是否已存在
                bool userExists = await _context.Users.AnyAsync(u => u.Username == username);
                if (userExists)
                {
                    _logger.LogWarning($"嘗試添加已存在的用戶名: {username}");
                    return false;
                }

                // 創建新用戶
                var newUser = new User
                {
                    Username = username,
                    PasswordHash = PasswordHasher.HashPassword(password),
                    Role = role,
                    CreatedAt = DateTime.UtcNow
                };

                // 添加到數據庫
                _context.Users.Add(newUser);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"新用戶添加成功: {username}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"添加用戶失敗: {username}");
                throw;
            }
        }

        /// <summary>
        /// 刪除用戶
        /// </summary>
        /// <param name="userId">用戶 ID</param>
        /// <returns>是否刪除成功</returns>
        public async Task<bool> DeleteUserAsync(int userId)
        {
            try
            {
                // 查找用戶
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning($"嘗試刪除不存在的用戶 ID: {userId}");
                    return false;
                }

                // 不允許刪除最後一個管理員
                if (user.Role == UserRole.Admin)
                {
                    int adminCount = await _context.Users.CountAsync(u => u.Role == UserRole.Admin);
                    if (adminCount <= 1)
                    {
                        _logger.LogWarning($"嘗試刪除最後一個管理員: {userId}");
                        return false;
                    }
                }

                // 刪除用戶
                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"用戶刪除成功: {userId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"刪除用戶失敗: {userId}");
                throw;
            }
        }

        /// <summary>
        /// 修改用戶密碼
        /// </summary>
        /// <param name="userId">用戶 ID</param>
        /// <param name="newPassword">新密碼</param>
        /// <returns>是否修改成功</returns>
        public async Task<bool> ChangePasswordAsync(int userId, string newPassword)
        {
            if (string.IsNullOrEmpty(newPassword))
            {
                throw new ArgumentException("新密碼不能為空");
            }

            try
            {
                // 查找用戶
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning($"嘗試修改不存在的用戶密碼 ID: {userId}");
                    return false;
                }

                // 更新密碼
                user.PasswordHash = PasswordHasher.HashPassword(newPassword);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"用戶密碼修改成功: {userId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"修改用戶密碼失敗: {userId}");
                throw;
            }
        }

        #region 登入嘗試管理

        /// <summary>
        /// 記錄失敗的登入嘗試
        /// </summary>
        /// <param name="username">用戶名</param>
        private void RecordFailedLoginAttempt(string username)
        {
            lock (_failedLoginAttempts)
            {
                if (_failedLoginAttempts.TryGetValue(username, out var attempts))
                {
                    _failedLoginAttempts[username] = (attempts.Count + 1, DateTime.UtcNow);
                }
                else
                {
                    _failedLoginAttempts[username] = (1, DateTime.UtcNow);
                }

                // 記錄警告
                if (_failedLoginAttempts[username].Count >= MaxFailedAttempts)
                {
                    _logger.LogWarning($"用戶 {username} 已達到最大登入失敗次數，暫時鎖定");
                }
            }
        }

        /// <summary>
        /// 重置失敗的登入嘗試
        /// </summary>
        /// <param name="username">用戶名</param>
        private void ResetFailedLoginAttempts(string username)
        {
            lock (_failedLoginAttempts)
            {
                if (_failedLoginAttempts.ContainsKey(username))
                {
                    _failedLoginAttempts.Remove(username);
                }
            }
        }

        /// <summary>
        /// 檢查用戶是否被鎖定
        /// </summary>
        /// <param name="username">用戶名</param>
        /// <returns>是否被鎖定</returns>
        private bool IsUserLockedOut(string username)
        {
            lock (_failedLoginAttempts)
            {
                if (_failedLoginAttempts.TryGetValue(username, out var attempts))
                {
                    // 檢查是否超過最大嘗試次數
                    if (attempts.Count >= MaxFailedAttempts)
                    {
                        // 檢查鎖定時間是否已過
                        var lockoutEnd = attempts.LastAttempt.AddMinutes(LockoutMinutes);
                        if (DateTime.UtcNow < lockoutEnd)
                        {
                            return true;
                        }
                        else
                        {
                            // 鎖定時間已過，重置計數
                            _failedLoginAttempts.Remove(username);
                            return false;
                        }
                    }
                }
                return false;
            }
        }

        #endregion
    }
}