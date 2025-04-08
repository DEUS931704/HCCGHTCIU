using System;
using System.ComponentModel.DataAnnotations;

namespace HCCGHTCIU.Models
{
    /// <summary>
    /// 審計日誌模型
    /// 用於記錄系統中的所有關鍵操作
    /// </summary>
    public class AuditLog
    {
        /// <summary>
        /// 審計日誌ID
        /// </summary>
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// 操作者ID（可為空，表示系統操作）
        /// </summary>
        public int? UserId { get; set; }

        /// <summary>
        /// 操作者IP地址
        /// </summary>
        [StringLength(45)]
        public string? IpAddress { get; set; }

        /// <summary>
        /// 操作類型（Create, Update, Delete, Login, Logout, Query等）
        /// </summary>
        [Required]
        [StringLength(50)]
        public string Action { get; set; }

        /// <summary>
        /// 實體名稱（表名）
        /// </summary>
        [Required]
        [StringLength(50)]
        public string EntityName { get; set; }

        /// <summary>
        /// 實體ID
        /// </summary>
        [Required]
        [StringLength(50)]
        public string EntityId { get; set; }

        /// <summary>
        /// 操作時間（UTC）
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 修改前的值（JSON格式）
        /// </summary>
        public string? OldValues { get; set; }

        /// <summary>
        /// 修改後的值（JSON格式）
        /// </summary>
        public string? NewValues { get; set; }

        /// <summary>
        /// 附加數據（如查詢參數等，JSON格式）
        /// </summary>
        public string? AdditionalData { get; set; }
    }
}