控制器文件

HomeController.cs - 主控制器，處理所有用戶請求，包含 IP 查詢、管理頁面、區塊鏈分析等功能

服務文件 (Services 資料夾)

IpValidationService.cs - 新增，提供 IP 地址格式驗證和特殊 IP 檢測功能
CacheService.cs - 新增，統一管理系統快取，提高性能
IIpLookupService.cs - 原有，IP 查詢服務介面
IpQualityScoreLookupService.cs - 原有，實現 IP 查詢服務的主要類別
IpQualityScoreService.cs - 原有，與 IPQualityScore API 交互的服務
IpStackLookupService.cs - 原有，與 IPStack API 交互的服務
DatabaseInitializer.cs - 原有，資料庫初始化服務

模型文件 (Models 資料夾)

IpRecord.cs - 原有，IP 記錄資料庫模型
IpLookupResult.cs - 原有，IP 查詢結果視圖模型
QueryLog.cs - 原有，查詢日誌模型
ErrorViewModel.cs - 原有，錯誤視圖模型

資料庫相關 (Data 資料夾)

ApplicationDbContext.cs - 原有，資料庫上下文，定義資料表結構

輔助類和特性 (Helpers 與 Attributes 資料夾)

AuthHelper.cs - 新增於 Helpers 資料夾，提供身份驗證相關功能
AdminAuthAttribute.cs - 新增於 Attributes 資料夾，用於保護管理頁面的授權特性

視圖文件 (Views 資料夾)

Views/Home/Admin.cshtml - 更新，管理頁面，加入 CSRF 保護和登出功能
Views/Home/AdminLogin.cshtml - 更新，管理員登入頁面，加入 CSRF 保護
Views/Home/IpLookup.cshtml - 更新，IP 查詢頁面，加入 CSRF 保護
Views/Home/QueryLogs.cshtml - 更新，查詢日誌頁面，加入分頁功能和 CSRF 保護
Views/Home/Result.cshtml - 原有，IP 查詢結果頁面
Views/Home/Blockchain.cshtml - 原有，區塊鏈功能頁面
Views/Home/Privacy.cshtml - 原有，隱私政策頁面

配置文件

appsettings.json - 更新，包含資料庫連接字串、API 金鑰、管理員憑證等配置
Program.cs - 更新，應用程式入口點，添加了服務註冊、中間件配置和錯誤處理

其他資源

wwwroot/css/site.css - 原有，網站樣式表
wwwroot/js/site.js - 原有，網站 JavaScript 檔案
