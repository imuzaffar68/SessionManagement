using System;
using System.Security.Cryptography;
using System.Text;

namespace SessionManagement.Security
{
    /// <summary>
    /// Provides secure password hashing and verification using BCrypt-like implementation
    /// Install BCrypt.Net-Next NuGet package for production use
    /// </summary>
    public static class AuthenticationHelper
    {
        private const int WorkFactor = 12; // BCrypt work factor (2^12 iterations)
        private const int SaltSize = 16;

        #region BCrypt-Style Password Hashing

        /// <summary>
        /// Hashes a password using PBKDF2 with SHA256 (BCrypt alternative for .NET)
        /// For production, use BCrypt.Net.BCrypt.HashPassword() from BCrypt.Net-Next package
        /// </summary>
        public static string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentNullException(nameof(password));

            // Generate a random salt
            byte[] salt = GenerateSalt();

            // Hash the password with the salt
            byte[] hash = HashPasswordWithSalt(password, salt);

            // Combine salt and hash for storage
            byte[] hashBytes = new byte[salt.Length + hash.Length];
            Array.Copy(salt, 0, hashBytes, 0, salt.Length);
            Array.Copy(hash, 0, hashBytes, salt.Length, hash.Length);

            // Convert to Base64 for storage
            return Convert.ToBase64String(hashBytes);
        }

        /// <summary>
        /// Verifies a password against a stored hash
        /// For production, use BCrypt.Net.BCrypt.Verify() from BCrypt.Net-Next package
        /// </summary>
        public static bool VerifyPassword(string password, string storedHash)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
                return false;

            try
            {
                // Decode the stored hash
                byte[] hashBytes = Convert.FromBase64String(storedHash);

                // Extract the salt (first 16 bytes)
                byte[] salt = new byte[SaltSize];
                Array.Copy(hashBytes, 0, salt, 0, SaltSize);

                // Extract the hash (remaining bytes)
                byte[] storedPasswordHash = new byte[hashBytes.Length - SaltSize];
                Array.Copy(hashBytes, SaltSize, storedPasswordHash, 0, storedPasswordHash.Length);

                // Hash the input password with the extracted salt
                byte[] computedHash = HashPasswordWithSalt(password, salt);

                // Compare the hashes
                return CompareHashes(storedPasswordHash, computedHash);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Password verification error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Helper Methods

        private static byte[] GenerateSalt()
        {
            byte[] salt = new byte[SaltSize];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(salt);
            }
            return salt;
        }

        private static byte[] HashPasswordWithSalt(string password, byte[] salt)
        {
            // Use PBKDF2 with SHA256
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(32); // 256 bits
            }
        }

        private static bool CompareHashes(byte[] hash1, byte[] hash2)
        {
            if (hash1.Length != hash2.Length)
                return false;

            // Use constant-time comparison to prevent timing attacks
            int result = 0;
            for (int i = 0; i < hash1.Length; i++)
            {
                result |= hash1[i] ^ hash2[i];
            }
            return result == 0;
        }

        #endregion

        #region BCrypt.Net Integration Instructions

        /*
         * PRODUCTION IMPLEMENTATION USING BCrypt.Net-Next:
         * 
         * 1. Install NuGet Package:
         *    Install-Package BCrypt.Net-Next
         * 
         * 2. Replace HashPassword method with:
         *    public static string HashPassword(string password)
         *    {
         *        return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
         *    }
         * 
         * 3. Replace VerifyPassword method with:
         *    public static bool VerifyPassword(string password, string storedHash)
         *    {
         *        try
         *        {
         *            return BCrypt.Net.BCrypt.Verify(password, storedHash);
         *        }
         *        catch
         *        {
         *            return false;
         *        }
         *    }
         */

        #endregion

        #region Additional Security Utilities

        /// <summary>
        /// Generates a random session token
        /// </summary>
        public static string GenerateSessionToken()
        {
            byte[] tokenBytes = new byte[32];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(tokenBytes);
            }
            return Convert.ToBase64String(tokenBytes);
        }

        /// <summary>
        /// Validates password strength
        /// </summary>
        public static bool IsPasswordStrong(string password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < 8)
                return false;

            bool hasUpper = false;
            bool hasLower = false;
            bool hasDigit = false;
            bool hasSpecial = false;

            foreach (char c in password)
            {
                if (char.IsUpper(c)) hasUpper = true;
                else if (char.IsLower(c)) hasLower = true;
                else if (char.IsDigit(c)) hasDigit = true;
                else if (!char.IsLetterOrDigit(c)) hasSpecial = true;
            }

            // Require at least 3 out of 4 categories
            int categoriesCount = (hasUpper ? 1 : 0) + (hasLower ? 1 : 0) +
                                 (hasDigit ? 1 : 0) + (hasSpecial ? 1 : 0);
            return categoriesCount >= 3;
        }

        /// <summary>
        /// Generates a cryptographically secure random string
        /// </summary>
        public static string GenerateRandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            byte[] data = new byte[length];

            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetBytes(data);
            }

            StringBuilder result = new StringBuilder(length);
            foreach (byte b in data)
            {
                result.Append(chars[b % chars.Length]);
            }
            return result.ToString();
        }

        #endregion

        #region Testing Utilities

        /// <summary>
        /// Quick test of password hashing functionality
        /// </summary>
        public static bool TestPasswordHashing()
        {
            try
            {
                string testPassword = "TestPassword123!";
                string hash = HashPassword(testPassword);

                bool validPassword = VerifyPassword(testPassword, hash);
                bool invalidPassword = !VerifyPassword("WrongPassword", hash);

                return validPassword && invalidPassword;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }

    /// <summary>
    /// Helper class for secure credential storage
    /// </summary>
    public class UserCredentials
    {
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime? LastPasswordChange { get; set; }

        public UserCredentials()
        {
            CreatedDate = DateTime.Now;
        }

        /// <summary>
        /// Creates credentials with hashed password
        /// </summary>
        public static UserCredentials Create(string username, string plainPassword)
        {
            return new UserCredentials
            {
                Username = username,
                PasswordHash = AuthenticationHelper.HashPassword(plainPassword),
                CreatedDate = DateTime.Now,
                LastPasswordChange = DateTime.Now
            };
        }

        /// <summary>
        /// Verifies credentials
        /// </summary>
        public bool Verify(string plainPassword)
        {
            return AuthenticationHelper.VerifyPassword(plainPassword, PasswordHash);
        }
    }
}