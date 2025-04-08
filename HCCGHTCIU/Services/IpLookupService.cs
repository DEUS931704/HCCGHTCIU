using HCCGHTCIU.Constants;
using HCCGHTCIU.Data;
using HCCGHTCIU.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Diagnostics;

namespace HCCGHTCIU.Services
{
    /// <summary>
    /// IP 查詢服務
    /// 簡化版本，合併了原先的多層結構
    /// </summary>
    public class IpLookupService
    {
        private readonly ApplicationDbContext _context; // 資料庫上下文
        private readonly ILogger<IpLookupService> _logger; // 日誌服務
        private readonly CacheService _cacheService; // 快取服務
        private readonly IspTranslationService _ispTranslationService; // ISP 翻譯服務
        private readonly HttpClient _httpClient; // HTTP 客戶端
        private readonly string _apiKey; // API 金鑰
        private readonly IpValidationService _ipValidationService; // IP 驗證服務

        // API 請求超時設置
        private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(5);

        // JSON 序列化選項
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// 構造函數
        /// </summary>
        public IpLookupService(
            ApplicationDbContext context,
            ILogger<IpLookupService> logger,
            CacheService cacheService,
            IspTranslationService ispTranslationService,
            HttpClient httpClient,
            IConfiguration configuration,
            IpValidationService ipValidationService)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _ispTranslationService = ispTranslationService ?? throw new ArgumentNullException(nameof(ispTranslationService));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _ipValidationService = ipValidationService ?? throw new ArgumentNullException(nameof(ipValidationService));

            // 從配置獲取 API 金鑰（使用 IConfiguration）
            _apiKey = configuration["IPQualityScore:ApiKey"] ??
                throw new ArgumentNullException("IPQualityScore API key not found in configuration");

            // 配置 JSON 序列化選項
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            };

            // 配置 HTTP 客戶端
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("HCCGHTCIU/1.0");
        }

        /// <summary>
        /// 查詢 IP 地址
        /// </summary>
        /// <param name="ipAddress">IP 地址</param>
        /// <returns>IP 查詢結果</returns>
        public async Task<IpLookupResult> LookupIpAsync(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                throw new ArgumentException("IP 地址不能為空", nameof(ipAddress));

            try
            {
                _logger.LogDebug("開始查詢 IP: {IpAddress}", ipAddress);

                // 首先檢查是否是有效的 IP 地址
                if (!_ipValidationService.IsValidIpAddress(ipAddress))
                {
                    throw new ArgumentException(_ipValidationService.GetInvalidIpErrorMessage(ipAddress));
                }

                // 檢查是否是保留或特殊 IP
                if (_ipValidationService.IsReservedOrSpecialIp(ipAddress))
                {
                    throw new InvalidOperationException($"'{ipAddress}' 是保留或特殊 IP 地址，無法查詢");
                }

                // 從快取或資料庫中尋找結果
                var existingResult = await CheckExistingRecordAsync(ipAddress);

                // 如果找到有效結果，則直接返回
                if (existingResult != null && existingResult.IspName != "Unknown")
                {
                    _logger.LogInformation("從資料庫返回 IP 查詢結果：{IpAddress}", ipAddress);
                    return existingResult;
                }

                // 否則從外部 API 獲取
                _logger.LogInformation("資料庫中沒有有效記錄，將從外部 API 獲取: {IpAddress}", ipAddress);
                var apiResult = await FetchFromApiAsync(ipAddress);

                // 將新的 IP 記錄保存到資料庫
                await SaveToDbAsync(apiResult);

                return apiResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "執行 IP 查詢時發生錯誤：{IpAddress}", ipAddress);
                throw;
            }
        }

        /// <summary>
        /// 檢查資料庫中是否已有該 IP 的記錄
        /// </summary>
        /// <param name="ipAddress">IP 地址</param>
        /// <returns>IP 查詢結果，如果不存在則返回 null</returns>
        private async Task<IpLookupResult> CheckExistingRecordAsync(string ipAddress)
        {
            // 使用快取服務，避免重複數據庫查詢
            return await _cacheService.GetOrCreateIpLookupCacheAsync(ipAddress, async () =>
            {
                var existingRecord = await _context.IpRecords
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.IpAddress == ipAddress);

                if (existingRecord != null)
                {
                    try
                    {
                        // 更新查詢次數和最後查詢時間
                        await _context.Database.ExecuteSqlRawAsync(
                            "UPDATE IpRecords SET QueryCount = QueryCount + 1, LastQueried = {1} WHERE IpAddress = {0}",
                            ipAddress, DateTime.UtcNow);

                        // 轉換為結果對象
                        return new IpLookupResult
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
                    }
                    catch (DbUpdateException ex)
                    {
                        _logger.LogWarning(ex, "更新 IP 記錄查詢次數時發生資料庫錯誤，但仍返回現有記錄: {IpAddress}", ipAddress);

                        // 即使更新失敗，我們仍然返回現有記錄
                        return new IpLookupResult
                        {
                            IpAddress = existingRecord.IpAddress,
                            IspName = existingRecord.IspName,
                            IspNameEnglish = existingRecord.IspNameEnglish,
                            IsVpn = existingRecord.IsVpn,
                            VpnProvider = existingRecord.VpnProvider,
                            QueryCount = existingRecord.QueryCount,
                            ThreatLevel = existingRecord.ThreatLevel,
                            LastQueried = existingRecord.LastQueried,
                            Country = existingRecord.Country,
                            City = existingRecord.City
                        };
                    }
                }

                return null;
            });
        }

        /// <summary>
        /// 從外部 API 獲取 IP 信息
        /// </summary>
        /// <param name="ipAddress">IP 地址</param>
        /// <returns>IP 查詢結果</returns>
        private async Task<IpLookupResult> FetchFromApiAsync(string ipAddress)
        {
            // 性能測量開始
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // 構建 API URL
                string apiUrl = $"https://www.ipqualityscore.com/api/json/ip/{_apiKey}/{ipAddress}";

                // 發送 HTTP 請求
                _logger.LogDebug("正在向 API 發送請求: {ipAddress}", ipAddress);

                // 使用帶超時的 HTTP 請求
                using var cancellationTokenSource = new System.Threading.CancellationTokenSource(_requestTimeout);
                using var response = await _httpClient.GetAsync(apiUrl, cancellationTokenSource.Token);

                // 檢查響應狀態
                response.EnsureSuccessStatusCode();

                // 讀取響應內容
                var content = await response.Content.ReadAsStreamAsync();
                var ipInfo = await JsonSerializer.DeserializeAsync<JsonElement>(content, _jsonOptions);

                // 驗證 API 響應
                if (!IsApiResponseSuccessful(ipInfo, out string errorMessage))
                {
                    _logger.LogWarning("API 查詢失敗：{errorMessage}", errorMessage);
                    throw new Exception($"IPQualityScore API 錯誤: {errorMessage}");
                }

                // 解析 API 響應
                var result = ParseApiResponse(ipInfo, ipAddress);

                stopwatch.Stop();
                _logger.LogInformation(
                    "IP 查詢完成：IP = {IpAddress}, 耗時 = {ElapsedMs}ms",
                    ipAddress,
                    stopwatch.ElapsedMilliseconds
                );

                return result;
            }
            catch (TaskCanceledException ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "IP 查詢請求超時：{IpAddress}, 已耗時 {ElapsedMs}ms", ipAddress, stopwatch.ElapsedMilliseconds);
                throw new HttpRequestException($"IP 查詢請求超時: {ipAddress}", ex);
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "IP 查詢的 HTTP 請求發生錯誤：{IpAddress}, 已耗時 {ElapsedMs}ms", ipAddress, stopwatch.ElapsedMilliseconds);
                throw;
            }
            catch (JsonException ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "解析 API 響應時發生錯誤：{IpAddress}, 已耗時 {ElapsedMs}ms", ipAddress, stopwatch.ElapsedMilliseconds);
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "執行 IP 查詢時發生未預期的錯誤：{IpAddress}, 已耗時 {ElapsedMs}ms", ipAddress, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        /// <summary>
        /// 檢查 API 響應是否成功
        /// </summary>
        /// <param name="ipInfo">API 響應資料</param>
        /// <param name="errorMessage">錯誤訊息</param>
        /// <returns>是否成功</returns>
        private bool IsApiResponseSuccessful(JsonElement ipInfo, out string errorMessage)
        {
            errorMessage = string.Empty;

            // 檢查是否存在 success 屬性
            if (ipInfo.TryGetProperty("success", out JsonElement successElement))
            {
                bool success = successElement.GetBoolean();

                if (!success)
                {
                    // 嘗試獲取錯誤消息
                    if (ipInfo.TryGetProperty("message", out JsonElement messageElement))
                    {
                        errorMessage = messageElement.GetString() ?? "未知的 API 錯誤";
                    }
                    else
                    {
                        errorMessage = "API 查詢失敗";
                    }
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 解析 API 響應
        /// </summary>
        /// <param name="ipInfo">API 響應資料</param>
        /// <param name="ipAddress">IP 地址</param>
        /// <returns>IP 查詢結果</returns>
        private IpLookupResult ParseApiResponse(JsonElement ipInfo, string ipAddress)
        {
            // 初始化結果對象
            var result = new IpLookupResult
            {
                IpAddress = ipAddress,
                LastQueried = DateTime.UtcNow,
                QueryCount = 1
            };

            // 提取國家和城市信息
            result.Country = GetStringProperty(ipInfo, "country_code", "Unknown");
            result.City = GetStringProperty(ipInfo, "city", "Unknown");

            // 標準化 ISP 名稱
            string originalIspName = GetStringProperty(ipInfo, "ISP", "Unknown");
            string hostname = GetStringProperty(ipInfo, "host", "");

            var (chineseName, englishName) = _ispTranslationService.StandardizeIspName(
                originalIspName,
                hostname
            );

            result.IspName = chineseName;
            result.IspNameEnglish = englishName;

            // 檢測 VPN 和威脅
            result.IsVpn = DetectVpn(ipInfo);
            result.VpnProvider = result.IsVpn ?
                GetStringProperty(ipInfo, "organization", "Unknown VPN Provider") :
                string.Empty;

            // 計算威脅等級
            result.ThreatLevel = CalculateThreatLevel(ipInfo);

            return result;
        }

        /// <summary>
        /// 將 IP 記錄保存到資料庫
        /// </summary>
        /// <param name="result">IP 查詢結果</param>
        /// <returns>Task</returns>
        private async Task SaveToDbAsync(IpLookupResult result)
        {
            // 防禦性檢查
            if (result == null)
                throw new ArgumentNullException(nameof(result), "IP 查詢結果不能為空");

            try
            {
                // 使用事務確保資料完整性
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    // 再次檢查 IP 是否已存在，避免並發衝突
                    var existingRecord = await _context.IpRecords
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r => r.IpAddress == result.IpAddress);

                    if (existingRecord != null)
                    {
                        // IP 記錄在我們準備保存時已被創建，更新查詢次數即可
                        await _context.Database.ExecuteSqlRawAsync(
                            "UPDATE IpRecords SET QueryCount = QueryCount + 1, LastQueried = {1} WHERE IpAddress = {0}",
                            result.IpAddress, DateTime.UtcNow);

                        _logger.LogInformation("發現並更新現有 IP 記錄（併發情況）: {IpAddress}", result.IpAddress);
                    }
                    else
                    {
                        // 創建新的 IP 記錄
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

                        // 新增記錄
                        _context.IpRecords.Add(newRecord);
                        await _context.SaveChangesAsync();

                        _logger.LogInformation("成功建立新 IP 記錄: {IpAddress}", result.IpAddress);
                    }

                    // 提交事務
                    await transaction.CommitAsync();

                    // 更新快取計數
                    _cacheService.IncrementRecordCount();
                }
                catch (Exception ex)
                {
                    // 回滾事務
                    await transaction.RollbackAsync();
                    throw; // 重新拋出異常
                }
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "儲存 IP 記錄時發生資料庫錯誤：{IpAddress}", result.IpAddress);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "儲存 IP 記錄時發生未預期的錯誤：{IpAddress}", result.IpAddress);
                throw;
            }
        }

        /// <summary>
        /// 檢測是否為 VPN
        /// </summary>
        /// <param name="ipInfo">IP 信息</param>
        /// <returns>是否為 VPN</returns>
        private bool DetectVpn(JsonElement ipInfo)
        {
            // 檢查 VPN 和代理標誌
            bool isVpn = false;

            if (ipInfo.TryGetProperty("vpn", out JsonElement vpnElement))
                isVpn = vpnElement.GetBoolean();

            if (!isVpn && ipInfo.TryGetProperty("proxy", out JsonElement proxyElement))
                isVpn = proxyElement.GetBoolean();

            return isVpn;
        }

        /// <summary>
        /// 計算威脅等級
        /// </summary>
        /// <param name="ipInfo">IP 信息</param>
        /// <returns>威脅等級 (0-10)</returns>
        private int CalculateThreatLevel(JsonElement ipInfo)
        {
            int fraudScore = 0;

            if (ipInfo.TryGetProperty("fraud_score", out JsonElement fraudScoreElement))
                fraudScore = fraudScoreElement.GetInt32();

            // 將 0-100 的詐騙分數轉換為 0-10 的威脅等級
            return (int)Math.Round(fraudScore / 10.0);
        }

        /// <summary>
        /// 安全地從 JsonElement 獲取字符串屬性
        /// </summary>
        /// <param name="element">JsonElement</param>
        /// <param name="propertyName">屬性名稱</param>
        /// <param name="defaultValue">默認值</param>
        /// <returns>屬性值</returns>
        private string GetStringProperty(JsonElement element, string propertyName, string defaultValue)
        {
            return element.TryGetProperty(propertyName, out JsonElement propElement) ?
                propElement.GetString() ?? defaultValue :
                defaultValue;
        }
    }
}