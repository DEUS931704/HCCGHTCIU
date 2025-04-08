using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace HCCGHTCIU.Services
{
    /// <summary>
    /// ISP 翻譯服務，提供 ISP 名稱的標準化和翻譯功能
    /// </summary>
    public class IspTranslationService
    {
        private readonly Dictionary<string, string> _translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly ILogger<IspTranslationService> _logger;

        public IspTranslationService(ILogger<IspTranslationService> logger)
        {
            _logger = logger;
            InitializeTranslations();

            // 額外的日誌，顯示所有翻譯
            foreach (var translation in _translations)
            {
                _logger.LogInformation($"ISP 翻譯: {translation.Key} -> {translation.Value}");
            }
        }

        /// <summary>
        /// 初始化翻譯字典
        /// </summary>
        private void InitializeTranslations()
        {
            try
            {
                string filePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Data", "IspTranslations.txt");
                if (File.Exists(filePath))
                {
                    string[] lines = File.ReadAllLines(filePath);
                    foreach (string line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("//"))
                            continue;

                        string[] parts = line.Split('\t');
                        if (parts.Length >= 3 &&
                            !string.IsNullOrWhiteSpace(parts[1]) &&
                            !string.IsNullOrWhiteSpace(parts[2]))
                        {
                            string englishName = parts[2].Trim();
                            string chineseName = parts[1].Trim();

                            _translations[englishName] = chineseName;
                        }
                    }
                    _logger.LogInformation("已成功加載 {count} 個 ISP 翻譯", _translations.Count);
                }
                else
                {
                    _logger.LogWarning("找不到 ISP 翻譯檔案: {filePath}", filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加載 ISP 翻譯時發生錯誤");
            }
        }

        /// <summary>
        /// 標準化 ISP 名稱
        /// </summary>
        /// <param name="originalName">原始 ISP 名稱</param>
        /// <param name="hostname">主機名（可選）</param>
        /// <returns>標準化後的 ISP 名稱</returns>
        public (string chineseName, string englishName) StandardizeIspName(
            string originalName,
            string hostname = "")
        {
            if (string.IsNullOrWhiteSpace(originalName))
                return ("未知", "Unknown");

            string chineseName = originalName;
            string englishName = originalName;

            // 直接在翻譯字典中查找
            if (_translations.TryGetValue(originalName, out string translation))
            {
                chineseName = translation;
            }

            // 特殊處理中華電信的子網路
            if (chineseName.Contains("中華電信") && !string.IsNullOrEmpty(hostname))
            {
                if (hostname.Contains("emome", StringComparison.OrdinalIgnoreCase))
                    chineseName += " (emome)";
                else if (hostname.Contains("hinet", StringComparison.OrdinalIgnoreCase))
                    chineseName += " (hinet)";
            }

            _logger.LogInformation(
                "ISP 名稱標準化：原始名稱 = {OriginalName}, 標準化中文名稱 = {ChineseName}",
                originalName,
                chineseName
            );

            return (chineseName, englishName);
        }
    }
}