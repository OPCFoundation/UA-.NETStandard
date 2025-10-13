/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;

namespace Opc.Ua.Server.UserDatabase
{
    /// <summary>
    /// Implementation of a serializable user database using a concurrent dictionary for users.
    /// </summary>
    [DataContract(Namespace = Namespaces.UserDatabase)]
    public class LinqUserDatabase : IUserDatabase
    {
        /// <summary>
        /// 128 bit
        /// </summary>
        private const int kSaltSize = 16;

        /// <summary>
        /// 100k
        /// </summary>
        private const int kIterations = 100_000;

        /// <summary>
        /// 256 bit
        /// </summary>
        private const int kKeySize = 32;

        /// <summary>
        /// The representation of a user in the Linq database.
        /// </summary>
        [DataContract(Namespace = Namespaces.UserDatabase)]
        public class User
        {
            /// <summary>
            /// A guid to distinguish users.
            /// </summary>
            [DataMember(Name = "Id", IsRequired = true, Order = 10)]
            public Guid ID { get; set; }

            /// <summary>
            /// The user name.
            /// </summary>
            [DataMember(Name = "UserName", IsRequired = true, Order = 20)]
            public string UserName { get; set; }

            /// <summary>
            /// The hashed password.
            /// </summary>
            [DataMember(Name = "Hash", IsRequired = true, Order = 30)]
            public string Hash { get; set; }

            /// <summary>
            /// The associated roles with the user.
            /// </summary>
            [DataMember(Name = "Roles", IsRequired = false, Order = 40)]
            public ICollection<Role> Roles { get; set; }
        }

        /// <summary>
        /// The constructor.
        /// </summary>
        public LinqUserDatabase()
        {
            Initialize();
        }

        /// <inheritdoc/>
        public bool CreateUser(string userName, ReadOnlySpan<byte> password, ICollection<Role> roles)
        {
            if (string.IsNullOrEmpty(userName))
            {
                throw new ArgumentException("UserName cannot be empty.", nameof(userName));
            }

            if (Utils.Utf8IsNullOrEmpty(password))
            {
                throw new ArgumentException("Password cannot be empty.", nameof(password));
            }

            string hash = LinqUserDatabase.Hash(password);

            bool added = true;
            User newUser = m_users.AddOrUpdate(userName,
                (key) => new User
                {
                    ID = Guid.NewGuid(),
                    UserName = userName,
                    Hash = hash,
                    Roles = roles
                },
                (key, value) =>
                {
                    added = false;
                    value.Hash = hash;
                    value.Roles = roles;
                    return value;
                });

            SaveChanges();

            return added;
        }

        /// <inheritdoc/>
        public bool DeleteUser(string userName)
        {
            if (string.IsNullOrEmpty(userName))
            {
                throw new ArgumentException("UserName cannot be empty.", nameof(userName));
            }

            return m_users.TryRemove(userName, out _);
        }

        /// <inheritdoc/>
        public bool CheckCredentials(string userName, ReadOnlySpan<byte> password)
        {
            if (string.IsNullOrEmpty(userName))
            {
                throw new ArgumentException("UserName cannot be empty.", nameof(userName));
            }

            if (Utils.Utf8IsNullOrEmpty(password))
            {
                throw new ArgumentException("Password cannot be empty.", nameof(password));
            }

            if (!m_users.TryGetValue(userName, out User user))
            {
                return false;
            }

            return LinqUserDatabase.Check(user.Hash, password);
        }

        /// <inheritdoc/>
        public ICollection<Role> GetUserRoles(string userName)
        {
            if (string.IsNullOrEmpty(userName))
            {
                throw new ArgumentException("UserName cannot be empty.", nameof(userName));
            }

            if (!m_users.TryGetValue(userName, out User user))
            {
                throw new ArgumentException("No user found with the UserName " + userName);
            }

            return user.Roles;
        }

        /// <inheritdoc/>
        public bool ChangePassword(string userName, ReadOnlySpan<byte> oldPassword, ReadOnlySpan<byte> newPassword)
        {
            if (string.IsNullOrEmpty(userName))
            {
                throw new ArgumentException("UserName cannot be empty.", nameof(userName));
            }

            if (Utils.Utf8IsNullOrEmpty(oldPassword))
            {
                throw new ArgumentException(
                    "Current Password cannot be empty.",
                    nameof(oldPassword));
            }

            if (Utils.Utf8IsNullOrEmpty(newPassword))
            {
                throw new ArgumentException("New Password cannot be empty.", nameof(newPassword));
            }

            if (!m_users.TryGetValue(userName, out User user))
            {
                return false;
            }

            if (LinqUserDatabase.Check(user.Hash, oldPassword))
            {
                user.Hash = LinqUserDatabase.Hash(newPassword);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Persists the changes to the users database.
        /// </summary>
        protected virtual void Save()
        {
        }

        /// <summary>
        /// Users in the database.
        /// </summary>
        [DataMember(Name = "Users", IsRequired = true, Order = 10)]
        public User[] Users
        {
            get => [.. m_users.Values];
            set
            {
                foreach (User user in value)
                {
                    m_users.TryAdd(user.UserName, user);
                }
            }
        }

        /// <summary>
        /// Initializes the database.
        /// </summary>
        private void Initialize()
        {
            m_users = new ConcurrentDictionary<string, User>();
        }

        private void SaveChanges()
        {
            Save();
        }

        private static string Hash(ReadOnlySpan<byte> password)
        {
            byte[] tmpPassword = password.ToArray();
            try
            {
                byte[] salt = new byte[kSaltSize + sizeof(uint)];

#if NETSTANDARD2_0 || NETFRAMEWORK
                using (var random = RandomNumberGenerator.Create())
                {
                    random.GetNonZeroBytes(salt);
                }
#else
                RandomNumberGenerator.Fill(salt.AsSpan(0, kSaltSize));
#endif

#if NETSTANDARD2_0 || NET462
#pragma warning disable CA5379 // Ensure Key Derivation Function algorithm is sufficiently strong
                using var algorithm = new Rfc2898DeriveBytes(
                    tmpPassword,
                    salt,
                    kIterations);
#pragma warning restore CA5379 // Ensure Key Derivation Function algorithm is sufficiently strong
#else
                using var algorithm = new Rfc2898DeriveBytes(
                    tmpPassword,
                    salt,
                    kIterations,
                    HashAlgorithmName.SHA512);
#endif
                string keyBase64 = Convert.ToBase64String(algorithm.GetBytes(kKeySize));
                string saltBase64 = Convert.ToBase64String(algorithm.Salt);
                return $"{kIterations}.{saltBase64}.{keyBase64}";
            }
            finally
            {
                Array.Clear(tmpPassword, 0, tmpPassword.Length);
            }
        }

        private static bool Check(string hash, ReadOnlySpan<byte> password)
        {
#if NET6_0_OR_GREATER
            string[] parts = hash.Split('.', 3, StringSplitOptions.TrimEntries);
#else
            char[] separator = ['.'];
            string[] parts = hash.Split(separator, 3);
#endif

            if (parts.Length != 3)
            {
                throw new FormatException(
                    "Unexpected hash format. Should be formatted as `{iterations}.{salt}.{hash}`");
            }

            int iterations = Convert.ToInt32(parts[0], CultureInfo.InvariantCulture.NumberFormat);
            byte[] salt = Convert.FromBase64String(parts[1]);
            byte[] key = Convert.FromBase64String(parts[2]);
            byte[] tmpPassword = password.ToArray();
            try
            {
#if NETSTANDARD2_0 || NET462
#pragma warning disable CA5379 // Ensure Key Derivation Function algorithm is sufficiently strong
                using var algorithm = new Rfc2898DeriveBytes(
                    tmpPassword,
                    salt,
                    iterations);
#pragma warning restore CA5379 // Ensure Key Derivation Function algorithm is sufficiently strong
#else
                using var algorithm = new Rfc2898DeriveBytes(
                    tmpPassword,
                    salt,
                    iterations,
                    HashAlgorithmName.SHA512);
#endif
                byte[] keyToCheck = algorithm.GetBytes(kKeySize);

                return keyToCheck.SequenceEqual(key);
            }
            finally
            {
                Array.Clear(tmpPassword, 0, tmpPassword.Length);
            }
        }

        [OnDeserialized]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            Initialize();
        }

        private ConcurrentDictionary<string, User> m_users;
    }

    /// <summary>
    /// Defines constants for all namespaces.
    /// </summary>
    public static class Namespaces
    {
        /// <summary>
        /// The URI for the UserDatabase namespace.
        /// </summary>
        public const string UserDatabase = "http://opcfoundation.org/UA/UserDatabase/";
    }
}
