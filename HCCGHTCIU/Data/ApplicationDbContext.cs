using HCCGHTCIU.Models;        // 引用模型命名空間
using Microsoft.EntityFrameworkCore; // 引用 Entity Framework Core
using HCCGHTCIU.Helpers;       // 引用密碼雜湊helper
using System;                  // 基礎功能
using System.Threading;        // 提供線程相關功能
using System.Threading.Tasks;  // 提供異步任務支持
using System.Collections.Generic; // 使用字典和列表
using System.Linq;             // 使用LINQ查詢

namespace HCCGHTCIU.Data
{
    /// <summary>
    /// 應用程式資料庫上下文
    /// 定義資料表結構和關係
    /// </summary>
    public class ApplicationDbContext : DbContext
    {
        // 當前用戶ID的AsyncLocal存儲
        // 用於自動記錄操作用戶
        private static readonly AsyncLocal<int?> _currentUserId = new AsyncLocal<int?>();

        // 獲取或設置當前用戶ID
        public static int? CurrentUserId
        {
            get => _currentUserId.Value;
            set => _currentUserId.Value = value;
        }

        /// <summary>
        /// 構造函數
        /// </summary>
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // 定義資料表
        public DbSet<User> Users { get; set; }
        public DbSet<QueryLog> QueryLogs { get; set; }
        public DbSet<IpRecord> IpRecords { get; set; }

        // 添加審計日誌表
        public DbSet<AuditLog> AuditLogs { get; set; }

        #region 高效能查詢方法

        // 通過IP地址異步查找記錄的方法
        public async Task<IpRecord> FindIpByAddressAsync(string ipAddress)
        {
            return await IpRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.IpAddress == ipAddress);
        }

        // 獲取最常查詢的IP記錄
        public async Task<List<IpRecord>> GetMostQueriedIpsAsync(int count)
        {
            return await IpRecords
                .AsNoTracking()
                .OrderByDescending(r => r.QueryCount)
                .Take(count)
                .ToListAsync();
        }

        // 獲取最近查詢日誌
        public async Task<List<QueryLog>> GetRecentQueriesAsync(int count)
        {
            return await QueryLogs
                .AsNoTracking()
                .OrderByDescending(l => l.QueryTime)
                .Take(count)
                .ToListAsync();
        }

        // 高效刪除查詢日誌的方法
        public async Task<int> DeleteQueryLogsAsync(CancellationToken cancellationToken = default)
        {
            // 清空查詢日誌表
            return await Database.ExecuteSqlRawAsync("DELETE FROM QueryLogs", cancellationToken);
        }

        #endregion

        /// <summary>
        /// 在保存變更前的操作
        /// 用於自動添加審計數據
        /// </summary>
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // 添加審計追蹤
            var entries = ChangeTracker.Entries().Where(e =>
                (e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted) &&
                e.Entity.GetType() != typeof(AuditLog));

            foreach (var entry in entries)
            {
                // 獲取當前時間
                var now = DateTime.UtcNow;

                // 獲取操作類型
                string action;
                switch (entry.State)
                {
                    case EntityState.Added:
                        action = "Create";
                        break;
                    case EntityState.Modified:
                        action = "Update";
                        break;
                    case EntityState.Deleted:
                        action = "Delete";
                        break;
                    default:
                        continue;
                }

                // 創建審計日誌
                AuditLogs.Add(new AuditLog
                {
                    EntityName = entry.Entity.GetType().Name,
                    EntityId = GetEntityId(entry),
                    Action = action,
                    UserId = CurrentUserId,
                    IpAddress = null, // 可以通過HttpContextAccessor獲取，但需要注入
                    Timestamp = now,
                    OldValues = entry.State == EntityState.Modified || entry.State == EntityState.Deleted
                        ? System.Text.Json.JsonSerializer.Serialize(GetOriginalValues(entry))
                        : null,
                    NewValues = entry.State == EntityState.Added || entry.State == EntityState.Modified
                        ? System.Text.Json.JsonSerializer.Serialize(GetCurrentValues(entry))
                        : null
                });
                AuditLogs.Add(new AuditLog { /* ... */ });
            }

            return base.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// 獲取實體的ID
        /// </summary>
        private string GetEntityId(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
        {
            // 嘗試獲取ID屬性
            var idProperty = entry.Properties.FirstOrDefault(p =>
                p.Metadata.Name.Equals("Id", StringComparison.OrdinalIgnoreCase));

            if (idProperty != null)
            {
                return idProperty.CurrentValue?.ToString() ?? "unknown";
            }

            // 嘗試獲取主鍵
            var key = entry.Metadata.FindPrimaryKey();
            if (key != null)
            {
                return string.Join(",", key.Properties
                    .Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? "unknown"));
            }

            return "unknown";
        }

        /// <summary>
        /// 獲取變更前的值
        /// </summary>
        private Dictionary<string, object> GetOriginalValues(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
        {
            var originalValues = new Dictionary<string, object>();

            foreach (var property in entry.Properties)
            {
                if (property.IsModified || entry.State == EntityState.Deleted)
                {
                    originalValues[property.Metadata.Name] = property.OriginalValue;
                }
            }

            return originalValues;
        }

        /// <summary>
        /// 獲取當前值
        /// </summary>
        private Dictionary<string, object> GetCurrentValues(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
        {
            var currentValues = new Dictionary<string, object>();

            foreach (var property in entry.Properties)
            {
                if (property.IsModified || entry.State == EntityState.Added)
                {
                    currentValues[property.Metadata.Name] = property.CurrentValue;
                }
            }

            return currentValues;
        }

        /// <summary>
        /// 配置模型映射和關聯
        /// </summary>
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            #region 初始化預設使用者

            // 初始化預設使用者
            modelBuilder.Entity<User>().HasData(
                new User
                {
                    Id = 1,
                    Username = "ABC",
                    // 使用新的更安全的密碼雜湊方法
                    PasswordHash = PasswordHasher.HashPassword("ABC"),
                    Role = UserRole.User,
                    CreatedAt = DateTime.UtcNow
                },
                new User
                {
                    Id = 2,
                    Username = "ADMIN",
                    // 使用新的更安全的密碼雜湊方法
                    PasswordHash = PasswordHasher.HashPassword("ADMIN"),
                    Role = UserRole.Admin,
                    CreatedAt = DateTime.UtcNow
                }
            );

            #endregion

            #region IP記錄表配置

            // IP記錄表配置
            modelBuilder.Entity<IpRecord>(entity =>
            {
                // 為頻繁查詢的欄位建立索引
                entity.HasIndex(e => e.IpAddress);
                entity.HasIndex(e => e.QueryCount);
                entity.HasIndex(e => e.LastQueried);

                // 配置欄位長度限制
                entity.Property(e => e.IpAddress).HasMaxLength(45).IsRequired();
                entity.Property(e => e.IspName).HasMaxLength(100);
                entity.Property(e => e.IspNameEnglish).HasMaxLength(100);
                entity.Property(e => e.VpnProvider).HasMaxLength(100);
                entity.Property(e => e.Country).HasMaxLength(2);
                entity.Property(e => e.City).HasMaxLength(100);

                // 添加ISP名稱索引
                entity.HasIndex(e => e.IspName);
            });

            #endregion

            #region QueryLog 表配置

            // QueryLog 表配置
            modelBuilder.Entity<QueryLog>(entity =>
            {
                // 為頻繁查詢的欄位建立索引
                entity.HasIndex(e => e.QueryTime);
                entity.HasIndex(e => e.QueriedIpAddress);
                entity.HasIndex(e => e.UserIpAddress);

                // 配置欄位長度限制
                entity.Property(e => e.UserIpAddress).HasMaxLength(45).IsRequired();
                entity.Property(e => e.QueriedIpAddress).HasMaxLength(45).IsRequired();
                entity.Property(e => e.UserAgent).HasMaxLength(500);
                entity.Property(e => e.Referrer).HasMaxLength(500);
            });

            #endregion

            #region AuditLog 表配置

            // 審計日誌表配置
            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.Action);

                entity.Property(e => e.Action).HasMaxLength(50).IsRequired();
                entity.Property(e => e.EntityName).HasMaxLength(50).IsRequired();
                entity.Property(e => e.EntityId).HasMaxLength(50).IsRequired();
                entity.Property(e => e.IpAddress).HasMaxLength(45);
            });

            #endregion
        }
    }
}