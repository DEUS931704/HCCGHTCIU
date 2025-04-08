// Models/IpRecord.cs - 定義 IP 記錄資料庫模型

using System;  // 引用基本系統功能，包括 DateTime

namespace HCCGHTCIU.Models
{
    // IP 記錄資料庫模型類
    public class IpRecord
    {
        public int Id { get; set; }                           // 記錄 ID (主鍵)
        public string IpAddress { get; set; } = string.Empty; // IP 地址
        public int QueryCount { get; set; } = 1;              // 查詢次數，默認為 1
        public int ThreatLevel { get; set; } = 0;             // 威脅等級 (0-10)
        public DateTime LastQueried { get; set; } = DateTime.UtcNow; // 最後查詢時間，默認為當前 UTC 時間
        public string IspName { get; set; } = string.Empty;    // ISP 名稱（中文）
        public string IspNameEnglish { get; set; } = string.Empty; // ISP 名稱（英文）
        public bool IsVpn { get; set; }                       // 是否是 VPN
        public string VpnProvider { get; set; } = string.Empty; // VPN 提供商名稱 (如果是 VPN)
        public string Country { get; set; } = string.Empty;    // 國家
        public string City { get; set; } = string.Empty;       // 城市
    }
}