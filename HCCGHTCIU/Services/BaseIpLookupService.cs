using HCCGHTCIU.Models;
using HCCGHTCIU.Data;
using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Threading;

namespace HCCGHTCIU.Services
{
    /// <summary>
    /// IP 查詢服務的基礎類別
    /// 提供通用的查詢、處理和儲存邏輯
    /// </summary>
    public abstract class BaseIpLookupService
    {
        // 受保護的唯讀欄位
        protected readonly ApplicationDbContext _context;           // 資料庫上下文
        protected readonly ILogger _logger;                         // 日誌記錄器
        protected readonly CacheService _cacheService;              // 快取服務
        protected readonly IspTranslationService _ispTranslationService; // ISP 翻譯服務

        // 資料庫操作重試設定
        private const int MaxRetryAttempts = 2;                     // 最大重試次數
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1); // 重試間隔

        /// <summary>
        /// 建構函數
        /// </summary>
        protected BaseIpLookupService(
            ApplicationDbContext context,
            ILogger logger,
            CacheService cacheService,
            IspTranslationService ispTranslationService)
        {
            // 參數驗證
            _context = context ?? throw new ArgumentNullException(nameof(context), "資料庫上下文不能為空");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger), "日誌記錄器不能為空");
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService), "快取服務不能為空");
            _ispTranslationService = ispTranslationService ?? throw new ArgumentNullException(nameof(ispTranslationService), "ISP翻譯服務不能為空");
        }

        /// <summary>
        /// 根據 IP 地址查詢信息
        /// </summary>
        /// <param name="ipAddress">IP 地址</param>
        /// <returns>IP 查詢結果</returns>
        public async Task<IpLookupResult> LookupIpAsync(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                throw new ArgumentException("IP 地址不能為空", nameof(ipAddress));

            _logger.LogDebug("開始查詢 IP: {IpAddress}", ipAddress);

            // 檢查現有記錄
            var existingResult = await CheckExistingRecordAsync(ipAddress);
            bool isExistingResultValid = IsResultValid(existingResult);

            // 如果找到有效結果，直接返回
            if (isExistingResultValid)
            {
                _logger.LogInformation("從資料庫返回 IP 查詢結果: {IpAddress}", ipAddress);
                return existingResult;
            }

            // 否則從外部 API 獲取
            _logger.LogInformation("資料庫中沒有有效記錄，從外部 API 獲取: {IpAddress}", ipAddress);
            var apiResult = await FetchFromExternalApiAsync(ipAddress);

            // 儲存新記錄
            await SaveNewRecordAsync(apiResult);

            return apiResult;
        }

        /// <summary>
        /// 檢查結果是否有效
        /// </summary>
        private bool IsResultValid(IpLookupResult result)
        {
            return result != null && result.IspName != "Unknown";
        }

        /// <summary>
        /// 檢查是否存在已儲存的 IP 記錄
        /// </summary>
        protected async Task<IpLookupResult> CheckExistingRecordAsync(string ipAddress)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // 使用快取服務
                return await _cacheService.GetOrCreateIpLookupCacheAsync(ipAddress, async () =>
                {
                    // 查詢資料庫
                    var existingRecord = await FindIpRecordAsync(ipAddress);

                    if (existingRecord != null)
                    {
                        try
                        {
                            // 更新查詢次數和時間
                            await UpdateQueryCountAsync(ipAddress);

                            // 轉換為結果對象
                            var result = ConvertToLookupResult(existingRecord, true);

                            stopwatch.Stop();
                            _logger.LogDebug("從資料庫找到並更新 IP 記錄: {IpAddress}, 耗時: {ElapsedMs}ms",
                                ipAddress, stopwatch.ElapsedMilliseconds);

                            return result;
                        }
                        catch (DbUpdateException ex)
                        {
                            _logger.LogWarning(ex, "更新 IP 記錄查詢次數時發生錯誤，但仍返回現有記錄: {IpAddress}", ipAddress);

                            // 返回現有記錄但不增加查詢次數
                            return ConvertToLookupResult(existingRecord, false);
                        }
                    }

                    // 記錄未找到
                    stopwatch.Stop();
                    _logger.LogDebug("資料庫中未找到 IP 記錄: {IpAddress}, 查詢耗時: {ElapsedMs}ms",
                        ipAddress, stopwatch.ElapsedMilliseconds);

                    return null;
                });
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "檢查現有 IP 記錄時發生錯誤: {IpAddress}, 已耗時: {ElapsedMs}ms",
                    ipAddress, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        /// <summary>
        /// 從資料庫查詢 IP 記錄
        /// </summary>
        private async Task<IpRecord> FindIpRecordAsync(string ipAddress)
        {
            return await _context.FindIpByAddressAsync(ipAddress);
        }

        /// <summary>
        /// 更新 IP 記錄的查詢次數
        /// </summary>
        private async Task UpdateQueryCountAsync(string ipAddress)
        {
            await _context.Database.ExecuteSqlRawAsync(
                "UPDATE IpRecords SET QueryCount = QueryCount + 1, LastQueried = {1} WHERE IpAddress = {0}",
                ipAddress, DateTime.UtcNow);
        }

        /// <summary>
        /// 將資料庫記錄轉換為查詢結果
        /// </summary>
        private IpLookupResult ConvertToLookupResult(IpRecord record, bool incrementQueryCount)
        {
            return new IpLookupResult
            {
                IpAddress = record.IpAddress,
                IspName = record.IspName,
                IspNameEnglish = record.IspNameEnglish,
                IsVpn = record.IsVpn,
                VpnProvider = record.VpnProvider,
                QueryCount = incrementQueryCount ? record.QueryCount + 1 : record.QueryCount,
                ThreatLevel = record.ThreatLevel,
                LastQueried = incrementQueryCount ? DateTime.UtcNow : record.LastQueried,
                Country = record.Country,
                City = record.City
            };
        }

        /// <summary>
        /// 儲存新的 IP 記錄到資料庫
        /// </summary>
        protected async Task SaveNewRecordAsync(IpLookupResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result), "IP 查詢結果不能為空");

            var stopwatch = Stopwatch.StartNew();
            int retryCount = 0;
            bool saved = false;

            while (!saved && retryCount <= MaxRetryAttempts)
            {
                try
                {
                    // 如果非首次嘗試，等待一段時間再重試
                    if (retryCount > 0)
                    {
                        await Task.Delay(RetryDelay);
                        _logger.LogWarning("正在第 {RetryCount} 次嘗試儲存 IP 記錄: {IpAddress}",
                            retryCount, result.IpAddress);
                    }

                    // 使用事務確保資料完整性
                    using var transaction = await _context.Database.BeginTransactionAsync();

                    try
                    {
                        // 再次檢查 IP 是否已存在
                        var existingRecord = await _context.IpRecords
                            .AsNoTracking()
                            .FirstOrDefaultAsync(r => r.IpAddress == result.IpAddress);

                        if (existingRecord != null)
                        {
                            // 更新查詢次數
                            await UpdateQueryCountAsync(result.IpAddress);
                            _logger.LogInformation("發現並更新現有 IP 記錄(併發情況): {IpAddress}", result.IpAddress);
                        }
                        else
                        {
                            // 創建新記錄
                            var newRecord = CreateIpRecord(result);
                            _context.IpRecords.Add(newRecord);
                            await _context.SaveChangesAsync();
                            _logger.LogInformation("成功建立新 IP 記錄: {IpAddress}", result.IpAddress);
                        }

                        // 提交事務
                        await transaction.CommitAsync();

                        // 更新快取計數
                        _cacheService.IncrementRecordCount();

                        saved = true;

                        stopwatch.Stop();
                        _logger.LogDebug("儲存 IP 記錄完成: {IpAddress}, 耗時: {ElapsedMs}ms",
                            result.IpAddress, stopwatch.ElapsedMilliseconds);
                    }
                    catch (Exception)
                    {
                        // 回滾事務
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
                catch (DbUpdateException dbEx)
                {
                    retryCount++;
                    if (retryCount > MaxRetryAttempts)
                    {
                        stopwatch.Stop();
                        _logger.LogError(dbEx, "儲存 IP 記錄達到最大重試次數後仍失敗: {IpAddress}, 已耗時: {ElapsedMs}ms",
                            result.IpAddress, stopwatch.ElapsedMilliseconds);
                        throw;
                    }
                    else
                    {
                        _logger.LogWarning(dbEx, "儲存 IP 記錄時發生資料庫錯誤，將重試({RetryCount}/{MaxRetries}): {IpAddress}",
                            retryCount, MaxRetryAttempts, result.IpAddress);
                    }
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    _logger.LogError(ex, "儲存 IP 記錄時發生未預期的錯誤: {IpAddress}, 已耗時: {ElapsedMs}ms",
                        result.IpAddress, stopwatch.ElapsedMilliseconds);
                    throw;
                }
            }
        }

        /// <summary>
        /// 創建新的 IP 記錄實體
        /// </summary>
        private IpRecord CreateIpRecord(IpLookupResult result)
        {
            return new IpRecord
            {
                IpAddress = result.IpAddress,
                IspName = result.IspName,
                IspNameEnglish = result.IspNameEnglish,
                IsVpn = result.IsVpn,
                VpnProvider = result.VpnProvider,
                QueryCount = 1,
                ThreatLevel = result.ThreatLevel,
                LastQueried = DateTime.UtcNow,
                Country = result.Country,
                City = result.City
            };
        }

        /// <summary>
        /// 從外部 API 獲取 IP 信息
        /// 由子類實現具體邏輯
        /// </summary>
        protected abstract Task<IpLookupResult> FetchFromExternalApiAsync(string ipAddress);
    }
}