using HCCGHTCIU.Data;
using Microsoft.EntityFrameworkCore;

namespace HCCGHTCIU.Services
{
    /// <summary>
    /// 資料庫初始化服務
    /// </summary>
    public class DatabaseInitializer
    {
        private readonly IServiceProvider _serviceProvider; // 服務提供者
        private readonly ILogger<DatabaseInitializer> _logger; // 日誌服務
        private readonly IWebHostEnvironment _environment; // 環境信息
        private readonly IConfiguration _configuration; // 配置服務

        /// <summary>
        /// 構造函數
        /// </summary>
        public DatabaseInitializer(
            IServiceProvider serviceProvider,
            ILogger<DatabaseInitializer> logger,
            IWebHostEnvironment environment,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        /// <summary>
        /// 初始化資料庫
        /// </summary>
        public async Task InitializeAsync()
        {
            // 建立服務範圍
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                _logger.LogInformation("開始初始化資料庫...");

                // 檢查是否啟用自動遷移（從配置檢索）
                bool enableAutoMigration = _configuration.GetValue<bool>("Database:EnableAutoMigration", true);

                // 檢查是否強制重建資料庫（從配置檢索，僅開發環境有效）
                bool forceRecreate = _environment.IsDevelopment() &&
                                    _configuration.GetValue<bool>("Database:ForceRecreate", false);

                // 檢查資料庫連接
                bool dbExists = await context.Database.CanConnectAsync();

                if (forceRecreate)
                {
                    // 在開發環境且配置為強制重建時，刪除並重建資料庫
                    _logger.LogInformation("開發環境: 強制重建資料庫...");
                    await context.Database.EnsureDeletedAsync();
                    await context.Database.EnsureCreatedAsync();
                    _logger.LogInformation("資料庫已重建");
                }
                else if (!dbExists)
                {
                    // 資料庫不存在，創建新資料庫
                    _logger.LogInformation("資料庫不存在，創建新資料庫...");

                    if (enableAutoMigration)
                    {
                        // 使用 Migration 創建資料庫
                        await context.Database.MigrateAsync();
                        _logger.LogInformation("已使用遷移創建資料庫");
                    }
                    else
                    {
                        // 使用 EnsureCreated 創建資料庫（不支持後續遷移）
                        await context.Database.EnsureCreatedAsync();
                        _logger.LogInformation("已創建資料庫（不使用遷移）");
                    }
                }
                else if (_environment.IsDevelopment() && enableAutoMigration)
                {
                    // 開發環境且啟用自動遷移，應用未應用的遷移
                    _logger.LogInformation("開發環境: 應用資料庫遷移...");
                    await context.Database.MigrateAsync();
                    _logger.LogInformation("已完成資料庫遷移");
                }
                else if (enableAutoMigration)
                {
                    // 生產環境，如果啟用自動遷移，檢查並應用未應用的遷移
                    var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
                    if (pendingMigrations.Any())
                    {
                        _logger.LogInformation("生產環境: 發現 {count} 個待應用的遷移，正在應用...", pendingMigrations.Count());
                        await context.Database.MigrateAsync();
                        _logger.LogInformation("已完成資料庫遷移");
                    }
                    else
                    {
                        _logger.LogInformation("資料庫結構已是最新");
                    }
                }
                else
                {
                    // 生產環境，未啟用自動遷移，僅檢查資料庫是否兼容
                    _logger.LogInformation("生產環境: 自動遷移已禁用，檢查資料庫兼容性...");

                    // 確保表結構正確
                    await EnsureTablesExistAsync(context);
                }

                _logger.LogInformation("資料庫初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化資料庫時發生錯誤");
                throw;
            }
        }

        /// <summary>
        /// 確保表存在
        /// </summary>
        private async Task EnsureTablesExistAsync(ApplicationDbContext context)
        {
            try
            {
                // 嘗試執行簡單查詢以確認基本功能正常
                int userCount = await context.Users.CountAsync();
                _logger.LogInformation($"用戶表記錄數: {userCount}");

                // 檢查是否能查詢 QueryLogs 表
                int logCount = await context.QueryLogs.CountAsync();
                _logger.LogInformation($"查詢日誌表記錄數: {logCount}");

                // 檢查是否能查詢 IpRecords 表
                int recordCount = await context.IpRecords.CountAsync();
                _logger.LogInformation($"IP記錄表記錄數: {recordCount}");

                // 檢查是否能查詢 AuditLogs 表
                int auditCount = await context.AuditLogs.CountAsync();
                _logger.LogInformation($"審計日誌表記錄數: {auditCount}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "檢查資料表時出錯");

                // 判斷是否為 SQLite 資料庫
                bool isSqlite = context.Database.ProviderName?.Contains("Sqlite") == true;

                if (isSqlite)
                {
                    // SQLite 資料庫處理
                    _logger.LogWarning("檢測到 SQLite 資料庫，嘗試檢查表結構");
                    await CheckSqliteTablesAsync(context);
                }
                else
                {
                    // 其他資料庫，嘗試使用通用方法確保資料庫創建
                    _logger.LogWarning("嘗試重新創建資料庫結構");
                    await context.Database.EnsureCreatedAsync();
                }
            }
        }

        /// <summary>
        /// 檢查 SQLite 資料表結構
        /// </summary>
        private async Task CheckSqliteTablesAsync(ApplicationDbContext context)
        {
            try
            {
                // 檢查資料表是否存在
                var tableNames = new[] { "Users", "QueryLogs", "IpRecords", "AuditLogs" };
                foreach (var tableName in tableNames)
                {
                    // 使用 SQLite 特定的 SQL 查詢檢查表是否存在
                    var sql = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{tableName}'";
                    var exists = await context.Database.ExecuteSqlRawAsync(sql) > 0;

                    if (!exists)
                    {
                        _logger.LogWarning($"資料表 {tableName} 不存在");
                    }
                    else
                    {
                        _logger.LogInformation($"資料表 {tableName} 存在");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "檢查 SQLite 資料表時出錯");
                throw;
            }
        }
    }
}