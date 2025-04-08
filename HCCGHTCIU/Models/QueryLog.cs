// Models/QueryLog.cs - 定義查詢日誌模型

using System;

namespace HCCGHTCIU.Models
{
    // 查詢日誌模型類，記錄用戶查詢IP的行為
    public class QueryLog
    {
        public int Id { get; set; }                          // 記錄 ID (主鍵)
        public string UserIpAddress { get; set; } = string.Empty; // 用戶的 IP 地址
        public string QueriedIpAddress { get; set; } = string.Empty; // 被查詢的 IP 地址
        public DateTime QueryTime { get; set; } = DateTime.UtcNow.AddHours(8); // 查詢時間（UTC+8）
        public string UserAgent { get; set; } = string.Empty; // 用戶瀏覽器信息
        public string Referrer { get; set; } = string.Empty;  // 來源頁面
    }
}