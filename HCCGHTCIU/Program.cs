using AspNetCoreRateLimit;
using HCCGHTCIU.Data;
using HCCGHTCIU.Services;
using HCCGHTCIU.Helpers;
using HCCGHTCIU.Middleware;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Text.Encodings.Web;
using System.Text.Unicode;

var builder = WebApplication.CreateBuilder(args);

#region 服務註冊

// 設定編碼，支持中文字符
builder.Services.AddSingleton(HtmlEncoder.Create(UnicodeRanges.All));

// 添加控制器服務，並配置模型驗證
builder.Services.AddControllersWithViews(options =>
{
    // 全域 CSRF 驗證
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

// 配置 SQLite 資料庫
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    // 使用 SQLite 作為資料庫
    options.UseSqlite(
        builder.Configuration.GetConnectionString("SqliteConnection") ?? "Data Source=IpLookup.db",
        sqliteOptions =>
        {
            sqliteOptions.MigrationsAssembly("HCCGHTCIU");
        }
    );

    // 開發環境啟用敏感資料記錄
    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
});

// 添加 HTTP 上下文訪問器
builder.Services.AddHttpContextAccessor();

// 添加記憶體快取服務
builder.Services.AddMemoryCache();

// 配置速率限制服務
builder.Services.Configure<IpRateLimitOptions>(
    builder.Configuration.GetSection("IpRateLimiting")
);
builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
builder.Services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
builder.Services.AddInMemoryRateLimiting();

// 添加 Session 服務
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = ".HCCGHTCIU.Session";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

// 註冊數據庫初始化器為作用域服務
builder.Services.AddScoped<DatabaseInitializer>();

// 註冊自訂服務
builder.Services.AddScoped<AuthService>();      // 身份驗證服務
builder.Services.AddScoped<CacheService>();     // 快取服務
builder.Services.AddScoped<IpValidationService>(); // IP 驗證服務
builder.Services.AddScoped<IpLookupService>();  // IP 查詢服務
builder.Services.AddScoped<IspTranslationService>(); // ISP 翻譯服務
builder.Services.AddScoped<IpQueryService>();   // IP 查詢協調服務

// 配置 HTTP 客戶端
builder.Services.AddHttpClient<IpLookupService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Add("User-Agent", "HCCGHTCIU/1.0");
});

// 添加日誌服務
builder.Services.AddLogging(configure =>
{
    configure.AddConsole();
    configure.AddDebug();
    configure.SetMinimumLevel(LogLevel.Information);
});

#endregion

var app = builder.Build();

#region 中間件配置

// 配置異常處理中間件
if (!app.Environment.IsDevelopment())
{
    // 生產環境使用增強的全局異常處理
    app.UseEnhancedExceptionHandler();
    app.UseHsts();
}
else
{
    // 開發環境使用開發者異常頁面
    app.UseDeveloperExceptionPage();
}

// 啟用 HTTPS 重定向
app.UseHttpsRedirection();

// 啟用靜態文件服務
app.UseStaticFiles();

// 啟用路由
app.UseRouting();

// 啟用速率限制
app.UseIpRateLimiting();

// 啟用 Session
app.UseSession();

// 啟用會話管理中間件
app.UseSessionManagement();

// 啟用授權
app.UseAuthorization();

// 配置預設路由
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

#endregion

#region 應用程序初始化

// 初始化資料庫
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var databaseInitializer = services.GetRequiredService<DatabaseInitializer>();
        await databaseInitializer.InitializeAsync();
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("資料庫初始化成功");
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "資料庫初始化失敗");
    }
}

#endregion

// 啟動應用程式
app.Run();