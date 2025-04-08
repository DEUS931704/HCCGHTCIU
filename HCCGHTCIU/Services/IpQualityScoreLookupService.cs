// Services/IpQualityScoreLookupService.cs
using HCCGHTCIU.Models;         // 引用模型命名空間
using HCCGHTCIU.Data;           // 引用資料庫相關命名空間
using System;                   // 引用基礎系統命名空間
using System.Threading.Tasks;   // 引用異步任務命名空間
using Microsoft.Extensions.Logging; // 引用日誌相關命名空間
using System.Diagnostics;       // 引用診斷相關命名空間，用於性能測量

namespace HCCGHTCIU.Services
{
    /// <summary>
    /// IP查詢服務的主要實現，整合外部API查詢和本地數據存儲
    /// </summary>
    public class IpQualityScoreLookupService : BaseIpLookupService, IIpLookupService
    {
        private readonly IpQualityScoreService _ipQualityScoreService; // IP品質分數服務

        /// <summary>
        /// 構造函數，通過依賴注入初始化服務
        /// </summary>
        public IpQualityScoreLookupService(
            IpQualityScoreService ipQualityScoreService,
            ApplicationDbContext context,
            ILogger<IpQualityScoreLookupService> logger,
            CacheService cacheService,
            IspTranslationService ispTranslationService)
            : base(context, logger, cacheService, ispTranslationService)
        {
            // 防禦性檢查：確保依賴不為空
            _ipQualityScoreService = ipQualityScoreService ??
                throw new ArgumentNullException(nameof(ipQualityScoreService));
        }

        /// <summary>
        /// 查詢IP地址信息的主要方法
        /// </summary>
        /// <param name="ipAddress">要查詢的IP地址</param>
        /// <returns>IP查詢結果</returns>
        public async Task<IpLookupResult> LookupIpAsync(string ipAddress)
        {
            try
            {
                _logger.LogDebug("開始查詢IP: {IpAddress}", ipAddress);

                // 首先檢查資料庫中是否已存在記錄
                var existingResult = await CheckExistingRecordAsync(ipAddress);
                bool isExistingResultFromDatabase = existingResult != null &&
                                                   existingResult.IspName != "Unknown";

                // 如果是從資料庫找到的真正記錄（不是基本結果）
                if (isExistingResultFromDatabase)
                {
                    _logger.LogInformation("從資料庫返回IP查詢結果：{IpAddress}", ipAddress);
                    return existingResult;
                }

                // 如果不是有效記錄，則從外部API獲取
                _logger.LogInformation("資料庫中沒有有效記錄，將從外部API獲取: {IpAddress}", ipAddress);
                var apiResult = await FetchFromExternalApiAsync(ipAddress);

                // 將新的IP記錄保存到資料庫
                await SaveNewRecordAsync(apiResult);

                return apiResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行IP查詢時發生錯誤：{IpAddress}", ipAddress);
                throw;
            }
        }

        /// <summary>
        /// 從外部API獲取IP信息的具體實現
        /// </summary>
        /// <param name="ipAddress">要查詢的IP地址</param>
        /// <returns>IP查詢結果</returns>
        protected override async Task<IpLookupResult> FetchFromExternalApiAsync(string ipAddress)
        {
            // 性能測量開始
            var stopwatch = Stopwatch.StartNew();

            _logger.LogInformation("正在通過IPQualityScore API查詢IP信息：{IpAddress}", ipAddress);

            // 調用IPQualityScore服務獲取IP信息
            var result = await _ipQualityScoreService.LookupIpAsync(ipAddress);

            stopwatch.Stop();
            _logger.LogInformation("IPQualityScore API查詢完成：{IpAddress}，耗時: {ElapsedMs}ms",
                ipAddress, stopwatch.ElapsedMilliseconds);

            return result;
        }
    }
}