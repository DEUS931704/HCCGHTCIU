using HCCGHTCIU.Models;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;

namespace HCCGHTCIU.Services
{
    /// <summary>
    /// 增強的快取服務，提供統一的快取管理功能
    /// </summary>
    public class CacheService
    {
        private readonly IMemoryCache _memoryCache; // 記憶體快取
        private readonly ILogger<CacheService> _logger; // 日誌服務
        private readonly IConfiguration _configuration; // 配置服務

        // 用於跟蹤所有快取鍵的集合
        private readonly ConcurrentDictionary<string, DateTime> _allCacheKeys = new ConcurrentDictionary<string, DateTime>();

        // 快取鍵前綴
        private const string IpLookupCacheKeyPrefix = "IP_LOOKUP_";
        private const string RecordCountCacheKey = "RECORD_COUNT";
        private const string LogCountCacheKey = "LOG_COUNT";
        private const string GeneralCachePrefix = "CACHE_";

        // 快取命中統計
        private long _cacheHits = 0;
        private long _cacheMisses = 0;

        /// <summary>
        /// 構造函數
        /// </summary>
        public CacheService(
            IMemoryCache memoryCache,
            ILogger<CacheService> logger,
            IConfiguration configuration)
        {
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// 獲取快取過期時間（分鐘）
        /// </summary>
        private int CacheExpirationMinutes =>
            _configuration.GetValue<int>("Caching:ExpirationMinutes", 60);

        /// <summary>
        /// 獲取IP查詢快取過期時間（分鐘）
        /// </summary>
        private int IpLookupCacheExpirationMinutes =>
            _configuration.GetValue<int>("Caching:IPLookupExpirationMinutes", 60);

        /// <summary>
        /// 獲取統計數據的快取過期時間（分鐘）
        /// </summary>
        private int StatisticsCacheExpirationMinutes =>
            _configuration.GetValue<int>("Caching:StatisticsExpirationMinutes", 5);

        /// <summary>
        /// 通用快取獲取或創建方法，支持任意類型
        /// </summary>
        /// <typeparam name="T">快取資料類型</typeparam>
        /// <param name="key">快取鍵</param>
        /// <param name="factory">資料獲取委託</param>
        /// <param name="expiration">過期時間</param>
        /// <returns>快取的資料</returns>
        public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
        {
            string cacheKey = $"{GeneralCachePrefix}{key}";

            // 嘗試從快取獲取
            if (_memoryCache.TryGetValue(cacheKey, out T cachedValue))
            {
                // 增加命中計數
                Interlocked.Increment(ref _cacheHits);
                _logger.LogDebug("快取命中: {key}", key);
                return cachedValue;
            }

            // 快取未命中，執行工廠方法
            Interlocked.Increment(ref _cacheMisses);
            _logger.LogDebug("快取未命中，正在獲取資料: {key}", key);

            T result = await factory();

            // 設置快取選項
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(expiration ?? TimeSpan.FromMinutes(CacheExpirationMinutes))
                .SetPriority(CacheItemPriority.Normal)
                .RegisterPostEvictionCallback((evictedKey, evictedValue, reason, state) =>
                {
                    // 移除過期的快取鍵
                    if (evictedKey is string keyStr)
                    {
                        _allCacheKeys.TryRemove(keyStr, out _);
                    }
                    _logger.LogDebug("快取項被移除: {key}, 原因: {reason}", evictedKey, reason);
                });

            // 存入快取
            _memoryCache.Set(cacheKey, result, cacheEntryOptions);

            // 儲存快取鍵以便後續管理
            _allCacheKeys[cacheKey] = DateTime.UtcNow;

            _logger.LogDebug("資料已存入快取: {key}", key);
            return result;
        }

        /// <summary>
        /// 根據IP地址獲取或設置IP查詢結果快取
        /// </summary>
        public async Task<IpLookupResult> GetOrCreateIpLookupCacheAsync(
            string ipAddress,
            Func<Task<IpLookupResult>> valueFactory)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                throw new ArgumentException("IP地址不能為空", nameof(ipAddress));
            }

            string cacheKey = $"{IpLookupCacheKeyPrefix}{ipAddress}";

            // 嘗試從快取獲取
            if (_memoryCache.TryGetValue(cacheKey, out IpLookupResult cachedResult))
            {
                Interlocked.Increment(ref _cacheHits);
                _logger.LogDebug("從快取獲取IP查詢結果: {ipAddress}", ipAddress);
                return cachedResult;
            }

            // 快取未命中，執行查詢
            Interlocked.Increment(ref _cacheMisses);
            _logger.LogDebug("IP查詢結果不在快取中，正在查詢: {ipAddress}", ipAddress);

            var result = await valueFactory();

            // 防禦性檢查：確保結果不為空
            if (result == null)
            {
                // 建立一個基本結果而不是返回null
                result = new IpLookupResult
                {
                    IpAddress = ipAddress,
                    LastQueried = DateTime.UtcNow,
                    QueryCount = 1,
                    Country = "Unknown",
                    City = "Unknown",
                    IspName = "Unknown",
                    IspNameEnglish = "Unknown",
                    IsVpn = false,
                    ThreatLevel = 0
                };

                _logger.LogInformation("IP {ipAddress} 查詢結果為空，已建立基本結果", ipAddress);
            }

            // 設置快取選項
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(IpLookupCacheExpirationMinutes))
                .SetPriority(CacheItemPriority.Normal)
                .RegisterPostEvictionCallback((evictedKey, evictedValue, reason, state) =>
                {
                    // 移除過期的快取鍵
                    if (evictedKey is string keyStr)
                    {
                        _allCacheKeys.TryRemove(keyStr, out _);
                    }
                });

            _memoryCache.Set(cacheKey, result, cacheEntryOptions);

            // 儲存快取鍵以便後續管理
            _allCacheKeys[cacheKey] = DateTime.UtcNow;

            _logger.LogDebug("已將IP查詢結果存入快取: {ipAddress}", ipAddress);

            return result;
        }

        /// <summary>
        /// 獲取或設置記錄數量快取
        /// </summary>
        public int GetOrCreateRecordCount(Func<int> valueFactory)
        {
            return _memoryCache.GetOrCreate(RecordCountCacheKey, entry =>
            {
                entry.SetAbsoluteExpiration(TimeSpan.FromMinutes(StatisticsCacheExpirationMinutes))
                     .SetPriority(CacheItemPriority.High) // 高優先級，因為頻繁訪問
                     .RegisterPostEvictionCallback((evictedKey, evictedValue, reason, state) =>
                     {
                         // 移除過期的快取鍵
                         if (evictedKey is string keyStr)
                         {
                             _allCacheKeys.TryRemove(keyStr, out _);
                         }
                     });

                var count = valueFactory();

                // 儲存快取鍵以便後續管理
                _allCacheKeys[RecordCountCacheKey] = DateTime.UtcNow;

                _logger.LogDebug("已將記錄數量存入快取: {count}", count);
                return count;
            });
        }

        /// <summary>
        /// 獲取或設置日誌數量快取
        /// </summary>
        public int GetOrCreateLogCount(Func<int> valueFactory)
        {
            return _memoryCache.GetOrCreate(LogCountCacheKey, entry =>
            {
                entry.SetAbsoluteExpiration(TimeSpan.FromMinutes(StatisticsCacheExpirationMinutes))
                     .SetPriority(CacheItemPriority.High) // 高優先級，因為頻繁訪問
                     .RegisterPostEvictionCallback((evictedKey, evictedValue, reason, state) =>
                     {
                         // 移除過期的快取鍵
                         if (evictedKey is string keyStr)
                         {
                             _allCacheKeys.TryRemove(keyStr, out _);
                         }
                     });

                var count = valueFactory();

                // 儲存快取鍵以便後續管理
                _allCacheKeys[LogCountCacheKey] = DateTime.UtcNow;

                _logger.LogDebug("已將日誌數量存入快取: {count}", count);
                return count;
            });
        }

        /// <summary>
        /// 增加日誌數量快取計數
        /// </summary>
        public void IncrementLogCount()
        {
            if (_memoryCache.TryGetValue(LogCountCacheKey, out int count))
            {
                _memoryCache.Set(LogCountCacheKey, count + 1,
                    new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(TimeSpan.FromMinutes(StatisticsCacheExpirationMinutes))
                        .SetPriority(CacheItemPriority.High));

                _logger.LogDebug("已增加日誌數量快取計數: {count}", count + 1);
            }
        }

        /// <summary>
        /// 增加記錄數量快取計數
        /// </summary>
        public void IncrementRecordCount()
        {
            if (_memoryCache.TryGetValue(RecordCountCacheKey, out int count))
            {
                _memoryCache.Set(RecordCountCacheKey, count + 1,
                    new MemoryCacheEntryOptions()
                        .SetAbsoluteExpiration(TimeSpan.FromMinutes(StatisticsCacheExpirationMinutes))
                        .SetPriority(CacheItemPriority.High));

                _logger.LogDebug("已增加記錄數量快取計數: {count}", count + 1);
            }
        }

        /// <summary>
        /// 清除IP查詢快取
        /// </summary>
        public void ClearIpLookupCache(string ipAddress = null)
        {
            if (string.IsNullOrEmpty(ipAddress))
            {
                // 清除所有IP查詢快取
                int count = 0;
                foreach (var key in _allCacheKeys.Keys.Where(k => k.StartsWith(IpLookupCacheKeyPrefix)).ToList())
                {
                    _memoryCache.Remove(key);
                    _allCacheKeys.TryRemove(key, out _);
                    count++;
                }

                _logger.LogInformation("已清除所有IP查詢快取，共 {count} 項", count);
            }
            else
            {
                // 清除特定IP的快取
                string cacheKey = $"{IpLookupCacheKeyPrefix}{ipAddress}";
                _memoryCache.Remove(cacheKey);
                _allCacheKeys.TryRemove(cacheKey, out _);

                _logger.LogInformation("已清除IP地址 {ipAddress} 的查詢快取", ipAddress);
            }
        }

        /// <summary>
        /// 清除記錄數量快取
        /// </summary>
        public void ClearRecordCount()
        {
            _memoryCache.Remove(RecordCountCacheKey);
            _allCacheKeys.TryRemove(RecordCountCacheKey, out _);
            _logger.LogInformation("已清除記錄數量快取");
        }

        /// <summary>
        /// 清除日誌數量快取
        /// </summary>
        public void ClearLogCount()
        {
            _memoryCache.Remove(LogCountCacheKey);
            _allCacheKeys.TryRemove(LogCountCacheKey, out _);
            _logger.LogInformation("已清除日誌數量快取");
        }

        /// <summary>
        /// 清除指定鍵的快取
        /// </summary>
        public void ClearCache(string key)
        {
            string cacheKey = $"{GeneralCachePrefix}{key}";
            _memoryCache.Remove(cacheKey);
            _allCacheKeys.TryRemove(cacheKey, out _);
            _logger.LogInformation("已清除快取: {key}", key);
        }

        /// <summary>
        /// 釋放一部分快取記憶體
        /// </summary>
        /// <param name="percentage">要釋放的百分比（0.0-1.0）</param>
        public void TrimCache(double percentage = 0.25)
        {
            // 確保百分比在有效範圍內
            percentage = Math.Clamp(percentage, 0.0, 1.0);

            if (_memoryCache is MemoryCache cache)
            {
                // 嘗試釋放指定百分比的快取記憶體
                cache.Compact(percentage);
                _logger.LogInformation("釋放了約 {percentage:P0} 的快取記憶體", percentage);
            }
            else
            {
                _logger.LogWarning("無法執行記憶體整理，_memoryCache 不是 MemoryCache 類型");
            }
        }

        /// <summary>
        /// 獲取快取統計信息
        /// </summary>
        public (long hits, long misses, int keyCount, DateTime oldestEntry) GetCacheStats()
        {
            var oldestEntry = _allCacheKeys.Any()
                ? _allCacheKeys.Values.Min()
                : DateTime.UtcNow;

            return (_cacheHits, _cacheMisses, _allCacheKeys.Count, oldestEntry);
        }

        /// <summary>
        /// 清除所有快取
        /// </summary>
        public void ClearAllCaches()
        {
            foreach (var key in _allCacheKeys.Keys.ToList())
            {
                _memoryCache.Remove(key);
            }

            _allCacheKeys.Clear();
            _logger.LogInformation("已清除所有快取");
        }
    }
}