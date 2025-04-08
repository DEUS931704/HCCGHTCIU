// Services/IpQualityScoreService.cs
using HCCGHTCIU.Models;         // 引用模型命名空間
using System;                   // 引用基礎系統命名空間
using System.Net.Http;          // 引用HTTP相關命名空間
using System.Text.Json;         // 引用JSON處理相關命名空間
using System.Threading.Tasks;   // 引用異步任務命名空間
using Microsoft.Extensions.Logging; // 引用日誌相關命名空間
using Microsoft.Extensions.Configuration; // 引用配置相關命名空間
using System.Diagnostics;       // 引用診斷相關命名空間，用於性能測量
using System.IO;                // 引用IO相關命名空間，用於流處理
using System.Threading;         // 引用線程相關命名空間，用於取消令牌
using System.Net.Http.Headers;  // 引用HTTP頭相關命名空間

namespace HCCGHTCIU.Services
{
    /// <summary>
    /// IPQualityScore IP查詢服務，提供全面的IP信息查詢和風險評估
    /// </summary>
    public class IpQualityScoreService
    {
        private readonly HttpClient _httpClient;               // HTTP客戶端
        private readonly string _apiKey;                      // API密鑰
        private readonly ILogger<IpQualityScoreService> _logger; // 日誌服務
        private readonly IspTranslationService _ispTranslationService; // ISP翻譯服務
        private readonly JsonSerializerOptions _jsonOptions;   // JSON序列化選項

        // API請求超時設置
        private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// 構造函數
        /// </summary>
        public IpQualityScoreService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<IpQualityScoreService> logger,
            IspTranslationService ispTranslationService)
        {
            // 防禦性檢查：確保依賴不為空
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _ispTranslationService = ispTranslationService ??
                throw new ArgumentNullException(nameof(ispTranslationService));

            // 從配置獲取API密鑰
            _apiKey = configuration["IPQualityScore:ApiKey"] ??
                throw new ArgumentNullException("IPQualityScore API key not found in configuration");

            // 優化：配置JSON序列化選項，減少記憶體使用
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,    // 屬性名稱不區分大小寫
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull, // 忽略空值
                WriteIndented = false                 // 不格式化輸出，減少大小
            };

            // 配置HTTP客戶端
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("HCCGHTCIU/1.0");
        }

        /// <summary>
        /// 執行IP查詢並返回詳細結果
        /// </summary>
        /// <param name="ipAddress">要查詢的IP地址</param>
        /// <returns>IP查詢結果</returns>
        public async Task<IpLookupResult> LookupIpAsync(string ipAddress)
        {
            // 防禦性檢查：確保IP地址不為空
            if (string.IsNullOrWhiteSpace(ipAddress))
                throw new ArgumentException("IP地址不能為空", nameof(ipAddress));

            // 性能測量開始
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // 構建API URL
                string apiUrl = $"https://www.ipqualityscore.com/api/json/ip/{_apiKey}/{ipAddress}";

                // 創建取消令牌，設置超時時間
                using var cancellationTokenSource = new CancellationTokenSource(_requestTimeout);
                var cancellationToken = cancellationTokenSource.Token;

                // 發送HTTP請求並讀取響應
                _logger.LogDebug("正在向API發送請求: {ipAddress}", ipAddress);

                // 優化：使用GetStreamAsync而非GetStringAsync，減少記憶體使用
                using var responseStream = await _httpClient.GetStreamAsync(apiUrl, cancellationToken);

                // 優化：直接從流中反序列化，而非先轉為字符串
                var ipInfo = await JsonSerializer.DeserializeAsync<JsonElement>(responseStream, _jsonOptions, cancellationToken);

                // 測量API回應時間
                var apiResponseTime = stopwatch.ElapsedMilliseconds;
                _logger.LogDebug("API回應時間: {ResponseTimeMs}ms for {ipAddress}", apiResponseTime, ipAddress);

                // 驗證API響應是否成功
                if (!IsApiResponseSuccessful(ipInfo, out string errorMessage))
                {
                    _logger.LogWarning("API查詢失敗：{errorMessage}", errorMessage);
                    throw new Exception($"IPQualityScore API錯誤: {errorMessage}");
                }

                // 解析和標準化IP信息
                var result = ParseIpQualityScoreResponse(ipInfo, ipAddress);

                stopwatch.Stop();
                _logger.LogInformation(
                    "IP查詢完成：IP = {IpAddress}, 耗時 = {ElapsedMs}ms",
                    ipAddress,
                    stopwatch.ElapsedMilliseconds
                );

                return result;
            }
            catch (TaskCanceledException ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "IP查詢請求超時：{IpAddress}, 已耗時 {ElapsedMs}ms", ipAddress, stopwatch.ElapsedMilliseconds);
                throw new HttpRequestException($"IP查詢請求超時: {ipAddress}", ex);
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "IP查詢的HTTP請求發生錯誤：{IpAddress}, 已耗時 {ElapsedMs}ms", ipAddress, stopwatch.ElapsedMilliseconds);
                throw;
            }
            catch (JsonException ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "解析IPQualityScore API響應時發生錯誤：{IpAddress}, 已耗時 {ElapsedMs}ms", ipAddress, stopwatch.ElapsedMilliseconds);
                throw;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "執行IP查詢時發生未預期的錯誤：{IpAddress}, 已耗時 {ElapsedMs}ms", ipAddress, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }

        /// <summary>
        /// 驗證API響應是否成功
        /// </summary>
        private bool IsApiResponseSuccessful(JsonElement ipInfo, out string errorMessage)
        {
            errorMessage = string.Empty;

            // 檢查是否存在success屬性
            if (ipInfo.TryGetProperty("success", out JsonElement successElement))
            {
                bool success = successElement.GetBoolean();

                if (!success)
                {
                    // 嘗試獲取錯誤消息
                    if (ipInfo.TryGetProperty("message", out JsonElement messageElement))
                    {
                        errorMessage = messageElement.GetString() ?? "未知的API錯誤";
                    }
                    else
                    {
                        errorMessage = "API查詢失敗";
                    }
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 解析IPQualityScore API響應
        /// </summary>
        private IpLookupResult ParseIpQualityScoreResponse(JsonElement ipInfo, string ipAddress)
        {
            // 性能測量開始
            var stopwatch = Stopwatch.StartNew();

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

            // 標準化ISP名稱
            string originalIspName = GetStringProperty(ipInfo, "ISP", "Unknown");
            string hostname = GetStringProperty(ipInfo, "host", "");

            var (chineseName, englishName) = _ispTranslationService.StandardizeIspName(
                originalIspName,
                hostname
            );

            result.IspName = chineseName;
            result.IspNameEnglish = englishName;

            // 檢測VPN和威脅
            result.IsVpn = DetectVpn(ipInfo);
            result.VpnProvider = result.IsVpn ?
                GetStringProperty(ipInfo, "organization", "Unknown VPN Provider") :
                string.Empty;

            // 計算威脅等級
            result.ThreatLevel = CalculateThreatLevel(ipInfo);

            stopwatch.Stop();
            _logger.LogDebug(
                "解析API回應完成：IP = {IpAddress}, 耗時 = {ElapsedMs}ms",
                ipAddress,
                stopwatch.ElapsedMilliseconds
            );

            return result;
        }

        /// <summary>
        /// 檢測是否為VPN
        /// </summary>
        private bool DetectVpn(JsonElement ipInfo)
        {
            // 檢查VPN和代理標誌
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
        private int CalculateThreatLevel(JsonElement ipInfo)
        {
            int fraudScore = 0;

            if (ipInfo.TryGetProperty("fraud_score", out JsonElement fraudScoreElement))
                fraudScore = fraudScoreElement.GetInt32();

            // 將0-100的詐騙分數轉換為0-10的威脅等級
            return (int)Math.Round(fraudScore / 10.0);
        }

        /// <summary>
        /// 安全地從JsonElement獲取字符串屬性
        /// </summary>
        private string GetStringProperty(JsonElement element, string propertyName, string defaultValue)
        {
            return element.TryGetProperty(propertyName, out JsonElement propElement) ?
                propElement.GetString() ?? defaultValue :
                defaultValue;
        }
    }
}