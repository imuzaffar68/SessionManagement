using BCrypt.Net;
using System;

namespace SessionManagement.Security
{
    /// <summary>
    /// Utility to generate password hashes for database seed data
    /// Uses BCrypt for secure password hashing
    /// </summary>
    public class PasswordHashGenerator
    {
        public static void Main(string[] args)
        {
            // Seed account credentials — regenerate hashes here if SQL needs to be re-seeded
            var passwords = new[]
            {
                new { Username = "Admin",   Password = "Admin@123456"   },
                new { Username = "sukaina", Password = "Sukaina@123"    },
                new { Username = "bisma",   Password = "Bisma@123"      },
                new { Username = "jannat",  Password = "Jannat@123"     },
                new { Username = "adan",    Password = "Adan@123"       }
            };

            Console.WriteLine("Password Hash Generation for SessionManagement Database");
            Console.WriteLine("========================================================\n");

            foreach (var user in passwords)
            {
                var hash = BCrypt.Net.BCrypt.HashPassword(user.Password);
                Console.WriteLine($"Username: {user.Username}");
                Console.WriteLine($"Password: {user.Password}");
                Console.WriteLine($"Hash: {hash}");
                Console.WriteLine($"SQL: ('{user.Username}', '{hash}', ...)");
                Console.WriteLine();
            }

            Console.WriteLine("\nCopy the hashes above and update the SQL seed data in developer\\SessionManagement.sql");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        /// <summary>
        /// Verify a plaintext password against a bcrypt hash
        /// </summary>
        public static bool VerifyPassword(string plainTextPassword, string hash)
        {
            return BCrypt.Net.BCrypt.Verify(plainTextPassword, hash);
        }

        /// <summary>
        /// Hash a password using bcrypt
        /// </summary>
        public static string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }
    }
}

