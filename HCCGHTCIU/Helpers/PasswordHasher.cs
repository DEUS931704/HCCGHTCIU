using System;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace HCCGHTCIU.Helpers
{
    /// <summary>
    /// 提供安全的密碼雜湊和驗證功能
    /// 使用PBKDF2演算法替換不安全的SHA256
    /// </summary>
    public static class PasswordHasher
    {
        // 迭代次數，增加雜湊計算成本以防止暴力破解
        private const int IterationCount = 10000;

        // 輸出雜湊長度
        private const int NumBytesRequested = 32;

        // 鹽值長度
        private const int SaltSize = 16;

        /// <summary>
        /// 使用PBKDF2演算法對密碼進行雜湊
        /// </summary>
        /// <param name="password">待雜湊的明文密碼</param>
        /// <returns>Base64編碼的雜湊結果（包含鹽值）</returns>
        public static string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentNullException(nameof(password), "密碼不能為空");
            }

            // 生成隨機鹽值
            byte[] salt = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            // 使用PBKDF2演算法執行雜湊
            byte[] hash = KeyDerivation.Pbkdf2(
                password: password,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: IterationCount,
                numBytesRequested: NumBytesRequested);

            // 組合鹽值和雜湊結果
            // 格式: [16字節鹽值][32字節雜湊]
            byte[] result = new byte[SaltSize + NumBytesRequested];
            Buffer.BlockCopy(salt, 0, result, 0, SaltSize);
            Buffer.BlockCopy(hash, 0, result, SaltSize, NumBytesRequested);

            // 轉換為Base64字符串以便存儲
            return Convert.ToBase64String(result);
        }

        /// <summary>
        /// 驗證密碼是否與存儲的雜湊匹配
        /// </summary>
        /// <param name="password">待驗證的明文密碼</param>
        /// <param name="storedHash">存儲的密碼雜湊（包含鹽值）</param>
        /// <returns>密碼是否匹配</returns>
        public static bool VerifyPassword(string password, string storedHash)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
            {
                return false;
            }

            try
            {
                // 解碼存儲的雜湊值
                byte[] hashBytes = Convert.FromBase64String(storedHash);

                // 確保雜湊長度正確
                if (hashBytes.Length != SaltSize + NumBytesRequested)
                {
                    return false;
                }

                // 提取鹽值
                byte[] salt = new byte[SaltSize];
                Buffer.BlockCopy(hashBytes, 0, salt, 0, SaltSize);

                // 提取原始雜湊值
                byte[] originalHash = new byte[NumBytesRequested];
                Buffer.BlockCopy(hashBytes, SaltSize, originalHash, 0, NumBytesRequested);

                // 使用相同的鹽值和方法計算輸入密碼的雜湊
                byte[] newHash = KeyDerivation.Pbkdf2(
                    password: password,
                    salt: salt,
                    prf: KeyDerivationPrf.HMACSHA256,
                    iterationCount: IterationCount,
                    numBytesRequested: NumBytesRequested);

                // 使用固定時間比較，防止計時攻擊
                return CryptographicOperations.FixedTimeEquals(originalHash, newHash);
            }
            catch
            {
                // 如果發生任何異常（例如Base64解碼錯誤），視為驗證失敗
                return false;
            }
        }

        /// <summary>
        /// 生成安全的隨機密碼
        /// </summary>
        /// <param name="length">密碼長度</param>
        /// <returns>隨機生成的密碼</returns>
        public static string GenerateRandomPassword(int length = 16)
        {
            // 確保密碼長度至少為8位
            length = Math.Max(8, length);

            // 包含字母、數字和特殊字符的字符集
            const string validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890!@#$%^&*()-_=+[]{}|;:,.<>?";

            // 生成隨機字節
            byte[] bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            // 將隨機字節轉換為字符
            char[] chars = new char[length];
            for (int i = 0; i < length; i++)
            {
                chars[i] = validChars[bytes[i] % validChars.Length];
            }

            return new string(chars);
        }
    }
}