/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using Newtonsoft.Json;
using static System.Net.Mime.MediaTypeNames;

namespace Opc.Ua.Gds.Server.Database.Linq
{
    [Serializable]
    class User
    {
        public Guid ID { get; set; }
        public string UserName { get; set; }
        public string Hash { get; set; }
        public GdsRole GdsRole { get; set; }
    }

    [Serializable]
    public class LinQUsersDatabase : IUsersDatabase, IPasswordHasher
    {
        #region IUsersDatabase
        public virtual void Initialize()
        {
        }

        public bool CreateUser(string userName, string password, GdsRole role)
        {
            if (userName == null|| userName == string.Empty)
            {
                throw new ArgumentNullException(nameof(userName));
            }
            if (password == null || password == string.Empty)
            {
                throw new ArgumentNullException(nameof(password));
            }
            if (//User Exists
                Users.SingleOrDefault(x => x.UserName == userName) != null)
            {
                return false;
            }

            string hash = Hash(password);

            var user = new User { UserName = userName, Hash = hash, GdsRole = role };

            Users.Add(user);

            SaveChanges();

            return true;
        }

        public bool DeleteUser(string userName)
        {
            if (userName == null || userName == string.Empty)
            {
                throw new ArgumentNullException(nameof(userName));
            }

            var user = Users.SingleOrDefault(x => x.UserName == userName);

            if (user == null)
            {
                return false;
            }
            Users.Remove(user);
            return true;
        }

        public bool CheckCredentials(string userName, string password)
        {
            if (userName == null || userName == string.Empty)
            {
                throw new ArgumentNullException(nameof(userName));
            }
            if (password == null || password == string.Empty)
            {
                throw new ArgumentNullException(nameof(password));
            }

            var user = Users.SingleOrDefault(x => x.UserName == userName);

            if (user == null)
            {
                return false;
            }

            return Check(user.Hash, password);
        }

        public GdsRole GetUserRole(string userName)
        {
            if (userName == null || userName == string.Empty)
            {
                throw new ArgumentNullException(nameof(userName));
            }
            var user = Users.SingleOrDefault(x => x.UserName == userName);

            if (user == null)
            {
                throw new ArgumentException("No user found with the UserName " + userName);
            }

            return user.GdsRole;
        }

        public bool ChangePassword(string userName, string oldPassword, string newPassword)
        {
            if (userName == null || userName == string.Empty)
            {
                throw new ArgumentNullException(nameof(userName));
            }
            if (oldPassword == null || oldPassword == string.Empty)
            {
                throw new ArgumentNullException(nameof(oldPassword));
            }
            if (newPassword == null || newPassword == string.Empty)
            {
                throw new ArgumentNullException(nameof(newPassword));
            }

            var user = Users.SingleOrDefault(x => x.UserName == userName);

            if (user == null)
            {
                return false;
            }

            if (Check(user.Hash, oldPassword))
            {

                return true;
            }
            return false;
        }
        #endregion

        #region Public Members
        public virtual void Save()
        {
        }
        #endregion

        #region Private Members
        private void SaveChanges()
        {
            lock (Lock)
            {
                queryCounterResetTime = DateTime.UtcNow;
                // assign IDs to new users
                var queryNewUsers = from x in Users
                                   where x.ID == Guid.Empty
                                   select x;
                if (Users.Count > 0)
                {
                    foreach (var user in queryNewUsers)
                    {
                        user.ID = Guid.NewGuid();
                    }
                }
                Save();
            }
        }
        #endregion

        #region IPasswordHasher
        public string Hash(string password)
        {
#if NETSTANDARD2_0
            using (var algorithm = new Rfc2898DeriveBytes(
              password,
              kSaltSize,
              kIterations))
            {
#else
            using (var algorithm = new Rfc2898DeriveBytes(
              password,
              kSaltSize,
              kIterations,
              HashAlgorithmName.SHA512))
            {
#endif
                var key = Convert.ToBase64String(algorithm.GetBytes(kKeySize));
                var salt = Convert.ToBase64String(algorithm.Salt);

                return $"{kIterations}.{salt}.{key}";
            }
        }

        public bool Check(string hash, string password)
        {
            var separator = new Char[] { '.' };
            var parts = hash.Split(separator, 3);

            if (parts.Length != 3)
            {
                throw new FormatException("Unexpected hash format. " +
                  "Should be formatted as `{iterations}.{salt}.{hash}`");
            }

            var iterations = Convert.ToInt32(parts[0], CultureInfo.InvariantCulture.NumberFormat);
            var salt = Convert.FromBase64String(parts[1]);
            var key = Convert.FromBase64String(parts[2]);

#if NETSTANDARD2_0
            using (var algorithm = new Rfc2898DeriveBytes(
              password,
              salt,
              iterations))
            {
#else
            using (var algorithm = new Rfc2898DeriveBytes(
              password,
              salt,
              iterations,
              HashAlgorithmName.SHA512))
            {
#endif
                var keyToCheck = algorithm.GetBytes(kKeySize);

                var verified = keyToCheck.SequenceEqual(key);

                return verified;
            }
        }
    
#endregion

        #region Internal Members
        [OnDeserialized]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            Lock = new object();
            queryCounterResetTime = DateTime.UtcNow;
        }
        #endregion

        #region Internal Fields
        [NonSerialized]
        internal object Lock = new object();
        [NonSerialized]
        internal DateTime queryCounterResetTime = DateTime.UtcNow;
        [NonSerialized]
        private const int kSaltSize = 16; // 128 bit
        [NonSerialized]
        private const int kIterations = 10000; // 10k
        [NonSerialized]
        private const int kKeySize = 32; // 256 bit
        [JsonProperty]
        internal ICollection<User> Users = new HashSet<User>();
        #endregion
    }
}

