// Services/BaseIpLookupService.cs
using HCCGHTCIU.Models;         // 引用模型命名空間
using HCCGHTCIU.Data;           // 引用資料庫相關命名空間
using System;                   // 引用系統基礎命名空間
using System.Threading.Tasks;   // 引用異步任務命名空間
using Microsoft.EntityFrameworkCore; // 引用EF Core相關命名空間
using Microsoft.Extensions.Logging; // 引用日誌相關命名空間
using System.Diagnostics;       // 引用診斷相關命名空間，用於性能測量

namespace HCCGHTCIU.Services
{
    /// <summary>
    /// IP 查詢服務的基礎類別，提供通用的查詢、處理和儲存邏輯
    /// </summary>
    public abstract class BaseIpLookupService
    {
        // 受保護的唯讀欄位，用於資料庫操作、日誌記錄、快取管理和 ISP 翻譯
        protected readonly ApplicationDbContext _context;           // 資料庫上下文
        protected readonly ILogger _logger;                         // 日誌記錄器
        protected readonly CacheService _cacheService;              // 快取服務
        protected readonly IspTranslationService _ispTranslationService; // ISP 翻譯服務

        // 設定查詢重試次數和間隔
        private const int MaxRetryAttempts = 2; // 最大重試次數
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1); // 重試間隔

        /// <summary>
        /// 建構函數，通過依賴注入初始化服務所需的元件
        /// </summary>
        public BaseIpLookupService(
            ApplicationDbContext context,
            ILogger logger,
            CacheService cacheService,
            IspTranslationService ispTranslationService)
        {
            // 防禦性檢查：確保所有依賴都不為空
            _context = context ?? throw new ArgumentNullException(nameof(context), "資料庫上下文不能為空");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger), "日誌記錄器不能為空");
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService), "快取服務不能為空");
            _ispTranslationService = ispTranslationService ?? throw new ArgumentNullException(nameof(ispTranslationService), "ISP翻譯服務不能為空");
        }

        /// <summary>
        /// 檢查是否存在已儲存的 IP 記錄，如果存在則更新查詢次數
        /// </summary>
        /// <param name="ipAddress">要查詢的 IP 地址</param>
        /// <returns>找到的 IP 查詢結果，如果不存在則返回 null</returns>
        protected async Task<IpLookupResult> CheckExistingRecordAsync(string ipAddress)
        {
            // 性能測量開始
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // 優化：使用快取服務的高效快取機制
                return await _cacheService.GetOrCreateIpLookupCacheAsync(ipAddress, async () =>
                {
                    // 優化：使用編譯查詢提高性能，減少查詢計劃編譯成本
                    var existingRecord = await _context.FindIpByAddressAsync(ipAddress);

                    if (existingRecord != null)
                    {
                        try
                        {
                            // 更新查詢次數和最後查詢時間
                            // 優化：使用參數化查詢防止SQL注入，提高安全性
                            await _context.Database.ExecuteSqlRawAsync(
                                "UPDATE IpRecords SET QueryCount = QueryCount + 1, LastQueried = {1} WHERE IpAddress = {0}",
                                ipAddress, DateTime.UtcNow);

                            // 將資料庫記錄轉換為結果對象
                            var result = new IpLookupResult
                            {
                                IpAddress = existingRecord.IpAddress,
                                IspName = existingRecord.IspName,
                                IspNameEnglish = existingRecord.IspNameEnglish,
                                IsVpn = existingRecord.IsVpn,
                                VpnProvider = existingRecord.VpnProvider,
                                QueryCount = existingRecord.QueryCount + 1,
                                ThreatLevel = existingRecord.ThreatLevel,
                                LastQueried = DateTime.UtcNow,
                                Country = existingRecord.Country,
                                City = existingRecord.City
                            };

                            stopwatch.Stop();
                            _logger.LogDebug("從資料庫找到並更新IP記錄: {IpAddress}, 耗時: {ElapsedMs}ms",
                                ipAddress, stopwatch.ElapsedMilliseconds);

                            return result;
                        }
                        catch (DbUpdateException ex)
                        {
                            // 資料庫更新錯誤處理
                            _logger.LogWarning(ex, "更新IP記錄查詢次數時發生資料庫錯誤，但仍返回現有記錄: {IpAddress}", ipAddress);

                            // 即使更新失敗，我們仍然返回現有記錄
                            return new IpLookupResult
                            {
                                IpAddress = existingRecord.IpAddress,
                                IspName = existingRecord.IspName,
                                IspNameEnglish = existingRecord.IspNameEnglish,
                                IsVpn = existingRecord.IsVpn,
                                VpnProvider = existingRecord.VpnProvider,
                                QueryCount = existingRecord.QueryCount, // 無法更新，使用原始值
                                ThreatLevel = existingRecord.ThreatLevel,
                                LastQueried = existingRecord.LastQueried, // 無法更新，使用原始值
                                Country = existingRecord.Country,
                                City = existingRecord.City
                            };
                        }
                    }

                    // 資料庫中不存在此IP記錄
                    stopwatch.Stop();
                    _logger.LogDebug("資料庫中未找到IP記錄: {IpAddress}, 查詢耗時: {ElapsedMs}ms",
                        ipAddress, stopwatch.ElapsedMilliseconds);

                    return null;
                });
            }
            catch (Exception ex)
            {
                // 記錄詳細的異常信息
                stopwatch.Stop();
                _logger.LogError(ex, "檢查現有IP記錄時發生錯誤：{IpAddress}, 已耗時: {ElapsedMs}ms",
                    ipAddress, stopwatch.ElapsedMilliseconds);
                throw; // 重新拋出異常以讓上層處理
            }
        }

        /// <summary>
        /// 儲存新的 IP 記錄到資料庫，包含重試邏輯
        /// </summary>
        /// <param name="result">IP 查詢結果</param>
        protected async Task SaveNewRecordAsync(IpLookupResult result)
        {
            // 防禦性檢查：確保結果不為空
            if (result == null)
                throw new ArgumentNullException(nameof(result), "IP查詢結果不能為空");

            // 性能測量開始
            var stopwatch = Stopwatch.StartNew();

            // 實現重試邏輯
            int retryCount = 0;
            bool saved = false;

            while (!saved && retryCount <= MaxRetryAttempts)
            {
                try
                {
                    // 若非首次嘗試，等待一段時間再重試
                    if (retryCount > 0)
                    {
                        await Task.Delay(RetryDelay);
                        _logger.LogWarning("正在第{RetryCount}次嘗試儲存IP記錄: {IpAddress}",
                            retryCount, result.IpAddress);
                    }

                    // 使用事務確保資料完整性
                    using var transaction = await _context.Database.BeginTransactionAsync();

                    try
                    {
                        // 再次檢查IP是否已存在，避免並發衝突
                        var existingRecord = await _context.IpRecords
                            .AsNoTracking()
                            .FirstOrDefaultAsync(r => r.IpAddress == result.IpAddress);

                        if (existingRecord != null)
                        {
                            // IP記錄在我們準備保存時已被創建，更新查詢次數即可
                            await _context.Database.ExecuteSqlRawAsync(
                                "UPDATE IpRecords SET QueryCount = QueryCount + 1, LastQueried = {1} WHERE IpAddress = {0}",
                                result.IpAddress, DateTime.UtcNow);

                            _logger.LogInformation("發現並更新現有IP記錄（併發情況）: {IpAddress}", result.IpAddress);
                        }
                        else
                        {
                            // 創建新的 IP 記錄實體
                            var newRecord = new IpRecord
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

                            // 將新記錄添加到資料庫
                            _context.IpRecords.Add(newRecord);
                            await _context.SaveChangesAsync();

                            _logger.LogInformation("成功建立新IP記錄: {IpAddress}", result.IpAddress);
                        }

                        // 提交事務
                        await transaction.CommitAsync();

                        // 增加快取中的記錄計數
                        _cacheService.IncrementRecordCount();

                        // 標記為已保存成功
                        saved = true;

                        stopwatch.Stop();
                        _logger.LogDebug("儲存IP記錄完成: {IpAddress}, 耗時: {ElapsedMs}ms",
                            result.IpAddress, stopwatch.ElapsedMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        // 回滾事務
                        await transaction.RollbackAsync();
                        throw; // 重新拋出異常以供外層處理
                    }
                }
                catch (DbUpdateException dbEx)
                {
                    retryCount++;
                    if (retryCount > MaxRetryAttempts)
                    {
                        // 已達最大重試次數，記錄失敗並拋出異常
                        stopwatch.Stop();
                        _logger.LogError(dbEx, "儲存IP記錄達到最大重試次數後仍失敗：{IpAddress}, 已耗時: {ElapsedMs}ms",
                            result.IpAddress, stopwatch.ElapsedMilliseconds);
                        throw;
                    }
                    else
                    {
                        // 記錄重試信息
                        _logger.LogWarning(dbEx, "保存IP記錄時發生資料庫錯誤，將重試({RetryCount}/{MaxRetries}): {IpAddress}",
                            retryCount, MaxRetryAttempts, result.IpAddress);
                    }
                }
                catch (Exception ex)
                {
                    // 其他類型的異常直接拋出，不進行重試
                    stopwatch.Stop();
                    _logger.LogError(ex, "儲存IP記錄時發生未預期的錯誤：{IpAddress}, 已耗時: {ElapsedMs}ms",
                        result.IpAddress, stopwatch.ElapsedMilliseconds);
                    throw;
                }
            }
        }

        /// <summary>
        /// 抽象方法：通過外部 API 查詢 IP 信息
        /// 由子類實現具體的 API 調用邏輯
        /// </summary>
        /// <param name="ipAddress">要查詢的 IP 地址</param>
        /// <returns>IP 查詢結果</returns>
        protected abstract Task<IpLookupResult> FetchFromExternalApiAsync(string ipAddress);
    }
}