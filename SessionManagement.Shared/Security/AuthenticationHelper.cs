using BCrypt.Net;
using System;
using System.Security.Cryptography;
using System.Text;

namespace SessionManagement.Security
{
    /// <summary>
    /// Provides secure password hashing and verification using BCrypt
    /// Uses BCrypt.Net-Next NuGet package for industry-standard security
    /// </summary>
    public static class AuthenticationHelper
    {
        private const int WorkFactor = 12; // BCrypt work factor (2^12 iterations)

        #region BCrypt Password Hashing

        /// <summary>
        /// Hashes a password using BCrypt algorithm
        /// </summary>
        public static string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentNullException(nameof(password));

            // Use BCrypt.Net for secure hashing
            return BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
        }

        /// <summary>
        /// Verifies a password against a BCrypt hash
        /// </summary>
        public static bool VerifyPassword(string password, string storedHash)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
                return false;

            try
            {
                return BCrypt.Net.BCrypt.Verify(password, storedHash);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Password verification error: {ex.Message}");
                return false;
            }
        }

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