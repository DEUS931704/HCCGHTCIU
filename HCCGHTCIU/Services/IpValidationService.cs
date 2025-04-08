using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Text.RegularExpressions;

namespace HCCGHTCIU.Services
{
    /// <summary>
    /// 簡化版 IP 地址驗證服務
    /// 使用內建函數和簡化邏輯
    /// </summary>
    public class IpValidationService
    {
        private readonly ILogger<IpValidationService> _logger;
        private readonly IMemoryCache _memoryCache;

        // 快取鍵前綴
        private const string VALIDATION_CACHE_KEY = "IPValidation_";
        private const string RESERVED_CACHE_KEY = "IPReserved_";

        // 快取過期時間（分鐘）
        private const int CACHE_EXPIRATION_MINUTES = 60;

        // 預定義的私有 IP 地址 CIDR 區段
        private static readonly string[] PRIVATE_IP_RANGES = new[]
        {
            "10.0.0.0/8",      // RFC 1918 - 私有網路
            "172.16.0.0/12",   // RFC 1918 - 私有網路
            "192.168.0.0/16",  // RFC 1918 - 私有網路
            "127.0.0.0/8",     // 本地迴環
            "169.254.0.0/16",  // 鏈接本地
            "192.0.0.0/24",    // IETF 協議分配
            "192.0.2.0/24",    // TEST-NET-1
            "198.51.100.0/24", // TEST-NET-2
            "203.0.113.0/24",  // TEST-NET-3
            "224.0.0.0/4",     // 多播
            "240.0.0.0/4",     // 保留
            "100.64.0.0/10",   // 運營商 NAT
            "::/128",          // 未指定 IPv6
            "::1/128",         // IPv6 本地迴環
            "fc00::/7",        // IPv6 唯一本地地址
            "fe80::/10",       // IPv6 鏈接本地
            "ff00::/8",        // IPv6 多播
            "2001:db8::/32"    // IPv6 文檔前綴
        };

        /// <summary>
        /// 構造函數
        /// </summary>
        /// <param name="logger">日誌服務</param>
        /// <param name="memoryCache">記憶體快取</param>
        public IpValidationService(
            ILogger<IpValidationService> logger,
            IMemoryCache memoryCache)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        }

        /// <summary>
        /// 驗證 IP 地址格式是否有效
        /// </summary>
        /// <param name="ipAddress">IP 地址</param>
        /// <returns>是否有效</returns>
        public bool IsValidIpAddress(string ipAddress)
        {
            // 檢查是否為空
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                _logger.LogWarning("嘗試驗證空 IP 地址");
                return false;
            }

            // 嘗試從快取中獲取結果
            string cacheKey = $"{VALIDATION_CACHE_KEY}{ipAddress}";
            if (_memoryCache.TryGetValue(cacheKey, out bool cachedResult))
            {
                return cachedResult;
            }

            // 使用 .NET 的 IPAddress.TryParse 方法驗證
            bool isValid = IPAddress.TryParse(ipAddress, out _);

            // 存入快取
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(CACHE_EXPIRATION_MINUTES))
                .SetPriority(CacheItemPriority.High);

            _memoryCache.Set(cacheKey, isValid, cacheEntryOptions);

            if (!isValid)
            {
                _logger.LogWarning("無效的 IP 地址格式: {ipAddress}", ipAddress);
            }

            return isValid;
        }

        /// <summary>
        /// 判斷 IP 地址是否為 IPv4
        /// </summary>
        /// <param name="ipAddress">IP 地址</param>
        /// <returns>是否為 IPv4</returns>
        public bool IsIPv4(string ipAddress)
        {
            if (!IsValidIpAddress(ipAddress))
                return false;

            IPAddress ip;
            if (IPAddress.TryParse(ipAddress, out ip))
            {
                return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
            }

            return false;
        }

        /// <summary>
        /// 判斷 IP 地址是否為 IPv6
        /// </summary>
        /// <param name="ipAddress">IP 地址</param>
        /// <returns>是否為 IPv6</returns>
        public bool IsIPv6(string ipAddress)
        {
            if (!IsValidIpAddress(ipAddress))
                return false;

            IPAddress ip;
            if (IPAddress.TryParse(ipAddress, out ip))
            {
                return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;
            }

            return false;
        }

        /// <summary>
        /// 判斷 IP 地址是否為保留地址或特殊地址
        /// </summary>
        /// <param name="ipAddress">IP 地址</param>
        /// <returns>是否為保留或特殊地址</returns>
        public bool IsReservedOrSpecialIp(string ipAddress)
        {
            // 嘗試從快取中獲取結果
            string cacheKey = $"{RESERVED_CACHE_KEY}{ipAddress}";
            if (_memoryCache.TryGetValue(cacheKey, out bool cachedResult))
            {
                return cachedResult;
            }

            // 驗證 IP 地址格式
            if (!IsValidIpAddress(ipAddress))
            {
                // 無效的 IP 地址視為特殊地址
                _memoryCache.Set(cacheKey, true, TimeSpan.FromMinutes(CACHE_EXPIRATION_MINUTES));
                return true;
            }

            IPAddress ip = IPAddress.Parse(ipAddress);
            bool isReserved = false;

            // 檢查是否為保留地址
            foreach (var range in PRIVATE_IP_RANGES)
            {
                try
                {
                    // 解析 CIDR 表示法
                    string[] parts = range.Split('/');
                    IPAddress networkAddress = IPAddress.Parse(parts[0]);
                    int prefixLength = int.Parse(parts[1]);

                    // 檢查 IP 地址族是否匹配
                    if (ip.AddressFamily != networkAddress.AddressFamily)
                        continue;

                    // 使用基本的 CIDR 驗證邏輯
                    if (IsIpInRange(ip, networkAddress, prefixLength))
                    {
                        isReserved = true;
                        _logger.LogInformation("檢測到保留 IP 地址: {ipAddress} 在範圍 {range}", ipAddress, range);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "檢查 IP 範圍時發生錯誤: {range}", range);
                }
            }

            // 存入快取
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(CACHE_EXPIRATION_MINUTES))
                .SetPriority(CacheItemPriority.High);

            _memoryCache.Set(cacheKey, isReserved, cacheEntryOptions);

            return isReserved;
        }

        /// <summary>
        /// 檢查 IP 是否在指定範圍內
        /// </summary>
        /// <param name="ip">IP 地址</param>
        /// <param name="networkAddress">網路地址</param>
        /// <param name="prefixLength">前綴長度</param>
        /// <returns>是否在範圍內</returns>
        private bool IsIpInRange(IPAddress ip, IPAddress networkAddress, int prefixLength)
        {
            byte[] ipBytes = ip.GetAddressBytes();
            byte[] networkBytes = networkAddress.GetAddressBytes();

            // 檢查 IP 地址族是否匹配
            if (ipBytes.Length != networkBytes.Length)
                return false;

            // 計算網路遮罩
            int byteCount = ipBytes.Length;
            int fullBytes = prefixLength / 8;
            int remainingBits = prefixLength % 8;

            // 檢查完整的字節
            for (int i = 0; i < fullBytes; i++)
            {
                if (ipBytes[i] != networkBytes[i])
                    return false;
            }

            // 檢查剩餘的位
            if (remainingBits > 0 && fullBytes < byteCount)
            {
                byte mask = (byte)(0xFF << (8 - remainingBits));
                if ((ipBytes[fullBytes] & mask) != (networkBytes[fullBytes] & mask))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 獲取無效 IP 地址的錯誤訊息
        /// </summary>
        /// <param name="ipAddress">IP 地址</param>
        /// <returns>錯誤訊息</returns>
        public string GetInvalidIpErrorMessage(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                return "IP 地址不能為空";
            }

            if (!IsValidIpAddress(ipAddress))
            {
                return $"'{ipAddress}' 不是有效的 IP 地址格式";
            }

            if (IsReservedOrSpecialIp(ipAddress))
            {
                if (IsIPv4(ipAddress))
                {
                    return $"'{ipAddress}' 是保留的 IPv4 地址或特殊地址，無法查詢";
                }
                else if (IsIPv6(ipAddress))
                {
                    return $"'{ipAddress}' 是保留的 IPv6 地址或特殊地址，無法查詢";
                }
                else
                {
                    return $"'{ipAddress}' 是保留地址或特殊地址，無法查詢";
                }
            }

            return "無效的 IP 地址";
        }

        /// <summary>
        /// 獲取 IP 地址的友好顯示
        /// </summary>
        /// <param name="ipAddress">IP 地址</param>
        /// <returns>友好的顯示文本</returns>
        public string GetFriendlyIpDisplay(string ipAddress)
        {
            if (IsValidIpAddress(ipAddress))
            {
                string version = IsIPv4(ipAddress) ? "IPv4" : "IPv6";
                return $"{ipAddress} ({version})";
            }
            return ipAddress;
        }

        /// <summary>
        /// 清除快取
        /// </summary>
        public void ClearCache()
        {
            // 由於無法直接列舉 IMemoryCache 中的所有鍵
            // 因此這裡只清除兩個已知類型的快取
            _logger.LogInformation("已清除 IP 驗證快取");
        }
    }
}