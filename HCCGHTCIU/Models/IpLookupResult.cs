// Models/IpLookupResult.cs - 定義 IP 查詢結果模型

using System;  // 引用基本系統功能，包括 DateTime

namespace HCCGHTCIU.Models
{
    // IP 查詢結果模型類 (用於返回給視圖)
    public class IpLookupResult
    {
        public string IpAddress { get; set; } = string.Empty;  // IP 地址
        public string IspName { get; set; } = string.Empty;    // ISP 名稱（中文）
        public string IspNameEnglish { get; set; } = string.Empty; // ISP 名稱（英文）
        public bool IsVpn { get; set; }                        // 是否是 VPN
        public string VpnProvider { get; set; } = string.Empty; // VPN 提供商名稱 (如果是 VPN)
        public int QueryCount { get; set; }                     // 查詢次數
        public int ThreatLevel { get; set; }                    // 威脅等級 (0-10)
        public DateTime LastQueried { get; set; }               // 最後查詢時間
        public string Country { get; set; } = string.Empty;     // 國家
        public string City { get; set; } = string.Empty;        // 城市
    }
}