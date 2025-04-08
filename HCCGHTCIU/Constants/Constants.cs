namespace HCCGHTCIU.Constants
{
    /// <summary>
    /// 系統常量
    /// 集中管理系統中使用的常量值
    /// </summary>
    public static class SessionKeys
    {
        /// <summary>
        /// 用戶 ID 的 Session 鍵
        /// </summary>
        public const string USER_ID = "UserId";

        /// <summary>
        /// 用戶角色的 Session 鍵
        /// </summary>
        public const string USER_ROLE = "UserRole";

        /// <summary>
        /// 最後活動時間的 Session 鍵
        /// </summary>
        public const string LAST_ACTIVITY = "LastActivity";

        /// <summary>
        /// 會話超時時間（分鐘）
        /// </summary>
        public const int TIMEOUT_MINUTES = 30;
    }

    /// <summary>
    /// Cookie 相關常量
    /// </summary>
    public static class CookieKeys
    {
        /// <summary>
        /// 身份驗證 Cookie 名稱
        /// </summary>
        public const string AUTH_COOKIE = "AuthToken";

        /// <summary>
        /// 管理員驗證 Cookie 名稱
        /// </summary>
        public const string ADMIN_AUTH_COOKIE = "AdminAuthenticated";
    }

    /// <summary>
    /// 快取相關常量
    /// </summary>
    public static class CacheKeys
    {
        /// <summary>
        /// IP 查詢快取前綴
        /// </summary>
        public const string IP_LOOKUP_PREFIX = "IP_LOOKUP_";

        /// <summary>
        /// 記錄數量快取鍵
        /// </summary>
        public const string RECORD_COUNT = "RECORD_COUNT";

        /// <summary>
        /// 日誌數量快取鍵
        /// </summary>
        public const string LOG_COUNT = "LOG_COUNT";

        /// <summary>
        /// 一般快取前綴
        /// </summary>
        public const string GENERAL_PREFIX = "CACHE_";
    }

    /// <summary>
    /// 快取過期時間常量
    /// </summary>
    public static class CacheExpiration
    {
        /// <summary>
        /// 一般快取過期時間（分鐘）
        /// </summary>
        public const int DEFAULT_MINUTES = 60;

        /// <summary>
        /// IP 查詢快取過期時間（分鐘）
        /// </summary>
        public const int IP_LOOKUP_MINUTES = 60;

        /// <summary>
        /// 統計數據快取過期時間（分鐘）
        /// </summary>
        public const int STATISTICS_MINUTES = 5;
    }
}