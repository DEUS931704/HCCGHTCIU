using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace HCCGHTCIU.Services
{
    /// <summary>
    /// IP 地址驗證服務
    /// 提供 IP 地址格式驗證和分類功能
    /// </summary>
    public class IpValidationService
    {
        private readonly ILogger<IpValidationService> _logger;    // 日誌服務
        private readonly IMemoryCache _memoryCache;               // 記憶體快取

        // 快取鍵前綴
        private const string VALIDATION_CACHE_KEY = "IPValidation_";
        private const string RESERVED_CACHE_KEY = "IPReserved_";

        // 快取過期時間（分鐘）
        private const int CACHE_EXPIRATION_MINUTES = 60;

        // IP 地址分類緩存
        private readonly ConcurrentDictionary<string, bool> _validationCache = new();
        private readonly ConcurrentDictionary<string, bool> _reservedCache = new();

        // 預定義的私有 IP 地址 CIDR 區段
        private static readonly List<(IPAddress NetworkAddress, int PrefixLength)> _privateIpRanges;

        // 靜態構造函數，初始化 IP 範圍
        static IpValidationService()
        {
            _privateIpRanges = new List<(IPAddress, int)>();

            // IPv4 範圍
            InitializeIpRange("10.0.0.0/8");       // RFC 1918 - 私有網路
            InitializeIpRange("172.16.0.0/12");    // RFC 1918 - 私有網路
            InitializeIpRange("192.168.0.0/16");   // RFC 1918 - 私有網路
            InitializeIpRange("127.0.0.0/8");      // 本地迴環
            InitializeIpRange("169.254.0.0/16");   // 鏈接本地
            InitializeIpRange("192.0.0.0/24");     // IETF 協議分配
            InitializeIpRange("192.0.2.0/24");     // TEST-NET-1
            InitializeIpRange("198.51.100.0/24");  // TEST-NET-2
            InitializeIpRange("203.0.113.0/24");   // TEST-NET-3
            InitializeIpRange("224.0.0.0/4");      // 多播
            InitializeIpRange("240.0.0.0/4");      // 保留
            InitializeIpRange("100.64.0.0/10");    // 運營商 NAT

            // IPv6 範圍
            InitializeIpRange("::/128");           // 未指定 IPv6
            InitializeIpRange("::1/128");          // IPv6 本地迴環
            InitializeIpRange("fc00::/7");         // IPv6 唯一本地地址
            InitializeIpRange("fe80::/10");        // IPv6 鏈接本地
            InitializeIpRange("ff00::/8");         // IPv6 多播
            InitializeIpRange("2001:db8::/32");    // IPv6 文檔前綴
        }

        /// <summary>
        /// 初始化 IP 範圍，解析 CIDR 表示法
        /// </summary>
        /// <param name="cidr">CIDR 表示法的 IP 範圍</param>
        private static void InitializeIpRange(string cidr)
        {
            try
            {
                string[] parts = cidr.Split('/');
                if (parts.Length == 2 &&
                    IPAddress.TryParse(parts[0], out var networkAddress) &&
                    int.TryParse(parts[1], out var prefixLength))
                {
                    _privateIpRanges.Add((networkAddress, prefixLength));
                }
            }
            catch
            {
                // 解析失敗時安靜失敗，不影響其他範圍的初始化
            }
        }

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
            if (_validationCache.TryGetValue(ipAddress, out bool cachedResult))
            {
                return cachedResult;
            }

            // 嘗試從記憶體快取獲取
            string cacheKey = $"{VALIDATION_CACHE_KEY}{ipAddress}";
            if (_memoryCache.TryGetValue(cacheKey, out bool memoryCachedResult))
            {
                // 更新本地快取
                _validationCache[ipAddress] = memoryCachedResult;
                return memoryCachedResult;
            }

            // 使用 .NET 的 IPAddress.TryParse 方法驗證
            bool isValid = IPAddress.TryParse(ipAddress, out _);

            // 存入本地快取
            _validationCache[ipAddress] = isValid;

            // 存入記憶體快取
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
            // 嘗試從本地快取獲取結果
            if (_reservedCache.TryGetValue(ipAddress, out bool localCachedResult))
            {
                return localCachedResult;
            }

            // 嘗試從記憶體快取獲取結果
            string cacheKey = $"{RESERVED_CACHE_KEY}{ipAddress}";
            if (_memoryCache.TryGetValue(cacheKey, out bool cachedResult))
            {
                // 更新本地快取
                _reservedCache[ipAddress] = cachedResult;
                return cachedResult;
            }

            // 驗證 IP 地址格式
            if (!IsValidIpAddress(ipAddress))
            {
                // 無效的 IP 地址視為特殊地址
                StoreReservedIpResult(ipAddress, true);
                return true;
            }

            IPAddress ip = IPAddress.Parse(ipAddress);
            bool isReserved = CheckIfIpIsInPrivateRanges(ip);

            // 存儲結果到快取
            StoreReservedIpResult(ipAddress, isReserved);

            return isReserved;
        }

        /// <summary>
        /// 儲存保留 IP 檢查結果到快取
        /// </summary>
        /// <param name="ipAddress">IP 地址</param>
        /// <param name="isReserved">是否為保留 IP</param>
        private void StoreReservedIpResult(string ipAddress, bool isReserved)
        {
            // 更新本地快取
            _reservedCache[ipAddress] = isReserved;

            // 存入記憶體快取
            string cacheKey = $"{RESERVED_CACHE_KEY}{ipAddress}";
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(CACHE_EXPIRATION_MINUTES))
                .SetPriority(CacheItemPriority.High);

            _memoryCache.Set(cacheKey, isReserved, cacheEntryOptions);

            if (isReserved)
            {
                _logger.LogInformation("檢測到保留 IP 地址: {ipAddress}", ipAddress);
            }
        }

        /// <summary>
        /// 檢查 IP 地址是否在預定義的私有範圍內
        /// </summary>
        /// <param name="ip">IP 地址</param>
        /// <returns>是否在私有範圍內</returns>
        private bool CheckIfIpIsInPrivateRanges(IPAddress ip)
        {
            byte[] ipBytes = ip.GetAddressBytes();

            // 檢查每個預定義範圍
            foreach (var (networkAddress, prefixLength) in _privateIpRanges)
            {
                // 檢查 IP 地址族是否匹配
                if (ip.AddressFamily != networkAddress.AddressFamily)
                    continue;

                // 檢查是否在範圍內
                if (IsIpInRange(ipBytes, networkAddress.GetAddressBytes(), prefixLength))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 檢查 IP 是否在指定範圍內
        /// </summary>
        /// <param name="ipBytes">IP 地址位元組</param>
        /// <param name="networkBytes">網路地址位元組</param>
        /// <param name="prefixLength">前綴長度</param>
        /// <returns>是否在範圍內</returns>
        private bool IsIpInRange(byte[] ipBytes, byte[] networkBytes, int prefixLength)
        {
            // 檢查位元組長度是否匹配
            if (ipBytes.Length != networkBytes.Length)
                return false;

            // 計算網路遮罩
            int fullBytes = prefixLength / 8;
            int remainingBits = prefixLength % 8;

            // 檢查完整的字節
            for (int i = 0; i < fullBytes && i < ipBytes.Length; i++)
            {
                if (ipBytes[i] != networkBytes[i])
                    return false;
            }

            // 檢查剩餘的位
            if (remainingBits > 0 && fullBytes < ipBytes.Length)
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
                string ipType = DetermineIpType(ipAddress);
                return $"'{ipAddress}' 是{ipType}，無法查詢";
            }

            return "無效的 IP 地址";
        }

        /// <summary>
        /// 確定 IP 地址的類型描述
        /// </summary>
        /// <param name="ipAddress">IP 地址</param>
        /// <returns>IP 類型描述</returns>
        private string DetermineIpType(string ipAddress)
        {
            if (IsIPv4(ipAddress))
            {
                return "保留的 IPv4 地址或特殊地址";
            }
            else if (IsIPv6(ipAddress))
            {
                return "保留的 IPv6 地址或特殊地址";
            }
            else
            {
                return "保留地址或特殊地址";
            }
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
            // 清除本地快取
            _validationCache.Clear();
            _reservedCache.Clear();

            _logger.LogInformation("已清除 IP 驗證快取");
        }
    }
}