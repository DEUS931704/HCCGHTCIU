using HCCGHTCIU.Constants;
using HCCGHTCIU.Data;
using HCCGHTCIU.Models;
using Microsoft.EntityFrameworkCore;

namespace HCCGHTCIU.Services
{
    /// <summary>
    /// IP 查詢協調服務
    /// 處理所有與 IP 查詢相關的業務邏輯
    /// </summary>
    public class IpQueryService
    {
        private readonly ApplicationDbContext _context;         // 數據庫上下文
        private readonly ILogger<IpQueryService> _logger;       // 日誌服務
        private readonly IpLookupService _ipLookupService;      // IP 查詢服務
        private readonly IpValidationService _ipValidationService; // IP 驗證服務
        private readonly CacheService _cacheService;            // 快取服務
        private readonly IHttpContextAccessor _httpContextAccessor; // HTTP 上下文訪問器

        /// <summary>
        /// 構造函數
        /// </summary>
        public IpQueryService(
            ApplicationDbContext context,
            ILogger<IpQueryService> logger,
            IpLookupService ipLookupService,
            IpValidationService ipValidationService,
            CacheService cacheService,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ipLookupService = ipLookupService ?? throw new ArgumentNullException(nameof(ipLookupService));
            _ipValidationService = ipValidationService ?? throw new ArgumentNullException(nameof(ipValidationService));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        }

        /// <summary>
        /// 查詢 IP 信息
        /// </summary>
        /// <param name="ipAddress">要查詢的 IP 地址</param>
        /// <returns>IP 查詢結果</returns>
        public async Task<IpLookupResult> LookupIpAsync(string ipAddress)
        {
            // 如果 IP 為空，使用用戶當前 IP
            ipAddress = ResolveIpAddress(ipAddress);

            // 驗證 IP 格式
            ValidateIpAddress(ipAddress);

            try
            {
                // 使用 IP 查詢服務
                var result = await _ipLookupService.LookupIpAsync(ipAddress);

                // 記錄查詢日誌
                await LogQueryAsync(ipAddress);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"查詢 IP 時發生錯誤: {ipAddress}");
                throw;
            }
        }

        /// <summary>
        /// 解析 IP 地址，如果為空則使用當前用戶的 IP
        /// </summary>
        /// <param name="ipAddress">提供的 IP 地址</param>
        /// <returns>解析後的 IP 地址</returns>
        private string ResolveIpAddress(string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress))
            {
                ipAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
                _logger.LogInformation($"使用用戶當前 IP: {ipAddress}");
            }
            return ipAddress;
        }

        /// <summary>
        /// 驗證 IP 地址格式和類型
        /// </summary>
        /// <param name="ipAddress">要驗證的 IP 地址</param>
        private void ValidateIpAddress(string ipAddress)
        {
            // 驗證 IP 格式
            if (!_ipValidationService.IsValidIpAddress(ipAddress))
            {
                string errorMessage = _ipValidationService.GetInvalidIpErrorMessage(ipAddress);
                _logger.LogWarning($"無效的 IP 格式: {ipAddress}");
                throw new ArgumentException(errorMessage);
            }

            // 檢查是否為保留 IP
            if (_ipValidationService.IsReservedOrSpecialIp(ipAddress))
            {
                string errorMessage = $"'{ipAddress}' 是保留或特殊 IP 地址，無法查詢";
                _logger.LogWarning(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }
        }

        /// <summary>
        /// 獲取查詢日誌
        /// </summary>
        /// <param name="page">頁碼</param>
        /// <param name="pageSize">每頁記錄數</param>
        /// <returns>查詢日誌列表和分頁信息</returns>
        public async Task<(List<QueryLog> Logs, int TotalPages, int TotalCount)> GetQueryLogsAsync(int page, int pageSize)
        {
            // 使用快取提高性能
            string cacheKey = $"QueryLogs_Page{page}_Size{pageSize}";
            return await _cacheService.GetOrCreateAsync(cacheKey, async () =>
            {
                return await FetchPaginatedLogsAsync(page, pageSize);
            }, TimeSpan.FromMinutes(1)); // 快取 1 分鐘
        }

        /// <summary>
        /// 獲取分頁查詢日誌
        /// </summary>
        /// <param name="page">頁碼</param>
        /// <param name="pageSize">每頁記錄數</param>
        /// <returns>日誌列表和分頁信息</returns>
        private async Task<(List<QueryLog> Logs, int TotalPages, int TotalCount)> FetchPaginatedLogsAsync(int page, int pageSize)
        {
            // 計算總記錄數和總頁數
            int totalLogs = await _context.QueryLogs.CountAsync();
            int totalPages = (int)Math.Ceiling(totalLogs / (double)pageSize);

            // 調整頁碼範圍
            page = Math.Max(1, Math.Min(page, totalPages > 0 ? totalPages : 1));

            // 獲取當前頁的記錄
            var logs = await _context.QueryLogs
                .OrderByDescending(l => l.QueryTime)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (logs, totalPages, totalLogs);
        }

        /// <summary>
        /// 清除所有查詢日誌
        /// </summary>
        /// <returns>是否成功</returns>
        public async Task<bool> ClearQueryLogsAsync()
        {
            try
            {
                // 使用直接 SQL 執行，提高效率
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM QueryLogs");

                // 清除快取
                _cacheService.ClearLogCount();

                _logger.LogInformation("成功清除所有查詢日誌");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清除查詢日誌時發生錯誤");
                return false;
            }
        }

        /// <summary>
        /// 記錄 IP 查詢
        /// </summary>
        /// <param name="queriedIpAddress">被查詢的 IP 地址</param>
        private async Task LogQueryAsync(string queriedIpAddress)
        {
            try
            {
                var queryLog = CreateQueryLogEntry(queriedIpAddress);

                _context.QueryLogs.Add(queryLog);
                await _context.SaveChangesAsync();

                // 更新快取
                _cacheService.IncrementLogCount();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"記錄查詢日誌時發生錯誤: {queriedIpAddress}");
                // 不拋出異常，避免影響主要功能
            }
        }

        /// <summary>
        /// 創建查詢日誌條目
        /// </summary>
        /// <param name="queriedIpAddress">被查詢的IP地址</param>
        /// <returns>查詢日誌對象</returns>
        private QueryLog CreateQueryLogEntry(string queriedIpAddress)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            string userIpAddress = httpContext?.Connection.RemoteIpAddress?.ToString() ?? "Unknown";

            return new QueryLog
            {
                UserIpAddress = userIpAddress,
                QueriedIpAddress = queriedIpAddress,
                QueryTime = DateTime.UtcNow.AddHours(8), // UTC+8
                UserAgent = httpContext?.Request.Headers.UserAgent.ToString() ?? "",
                Referrer = httpContext?.Request.Headers.Referer.ToString() ?? ""
            };
        }

        /// <summary>
        /// 獲取系統統計信息
        /// </summary>
        /// <returns>統計信息</returns>
        public async Task<(int RecordCount, int LogCount, DateTime StartupTime)> GetSystemStatsAsync()
        {
            try
            {
                // 獲取記錄數
                int recordCount = _cacheService.GetOrCreateRecordCount(() => _context.IpRecords.Count());

                // 獲取日誌數
                int logCount = _cacheService.GetOrCreateLogCount(() => _context.QueryLogs.Count());

                // 系統啟動時間（這裡使用當前時間，實際應該從某處獲取真實的啟動時間）
                DateTime startupTime = DateTime.Now;

                return (recordCount, logCount, startupTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "獲取系統統計信息失敗");
                throw;
            }
        }
    }
}