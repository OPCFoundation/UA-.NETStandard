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
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Server.UserDatabase;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class LinqUserDatabaseTests
    {
        private static readonly string[] s_createdUsers = ["TestUser1", "TestUser2"];

        /// <summary>
        /// Deterministic PBKDF2-SHA512 vector persisted with 10,000 iterations, generated
        /// offline for the password "Correct.Horse.Battery9!" using the persisted format
        /// `{iterations}.{saltBase64}.{keyBase64}`. Represents a legacy hash created before
        /// the default iteration count was raised to 100,000.
        /// </summary>
        private const string kPbkdf2Vector10000Iterations =
            "10000.AQIDBAUGBwgJCgsMDQ4PEBESExQ=.m11DHtGqddtFRM3j/jT625QRQ94Wr77OQ6JVDB3wkOM=";

        /// <summary>
        /// Deterministic PBKDF2-SHA512 vector persisted with 100,000 iterations, generated
        /// offline for the password "Correct.Horse.Battery9!" using the persisted format
        /// `{iterations}.{saltBase64}.{keyBase64}`. Matches the current default iteration count.
        /// </summary>
        private const string kPbkdf2Vector100000Iterations =
            "100000.FRYXGBkaGxwdHh8gISIjJCUmJyg=.ZTa2BfHDODFt/dLxiIyZQECswP3Rzd3KCmu6TxeE3Wo=";

        [Test]
        public void CreateInvalidUser()
        {
            // Arrange
            var usersDb = new LinqUserDatabase();

            // Act + Assert
            Assert.Throws<ArgumentException>(
                () => usersDb.CreateUser(null, "PW"u8, [Role.AuthenticatedUser]));
            Assert.Throws<ArgumentException>(
                () => usersDb.CreateUser("Name", null, [Role.AuthenticatedUser]));
            Assert.Throws<ArgumentException>(
                () => usersDb.CreateUser(string.Empty, "PW"u8, [Role.AuthenticatedUser]));
            Assert.Throws<ArgumentException>(
                () => usersDb.CreateUser("Name", ""u8, [Role.AuthenticatedUser]));
        }

        [Test]
        public void DeleteExistingUser()
        {
            // Arrrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser", "PW"u8, [Role.AuthenticatedUser]);

            // Act
            bool result = usersDb.DeleteUser("TestUser");

            // Assert
            Assert.That(result, Is.True);
        }

        [Test]
        public void DeleteNonExistingUser()
        {
            // Arrrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser", "PW"u8, [Role.AuthenticatedUser]);

            // Act
            bool result = usersDb.DeleteUser("NoTestUser");

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void ChangePwOfExistingUser()
        {
            // Arrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser", "PW"u8, [Role.AuthenticatedUser]);

            // Act
            bool result = usersDb.ChangePassword("TestUser", "PW"u8, "newPW"u8);
            bool login = usersDb.CheckCredentials("TestUser", "newPW"u8);
            bool loginOldPW = usersDb.CheckCredentials("TestUser", "PW"u8);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(login, Is.True);
            Assert.That(loginOldPW, Is.False);
        }

        [Test]
        public void ChangePwOfNonExistingUser()
        {
            // Arrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser", "PW"u8, [Role.AuthenticatedUser]);

            // Act
            bool result = usersDb.DeleteUser("NoTestUser");

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void CheckPWofExistingUser()
        {
            // Arrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser", "PW"u8, [Role.AuthenticatedUser]);

            // Act
            bool result = usersDb.CheckCredentials("TestUser", "PW"u8);
            bool loginWrongPw = usersDb.CheckCredentials("TestUser", "newPW"u8);

            // Assert
            Assert.That(result, Is.True);
            Assert.That(loginWrongPw, Is.False);
        }

        [Test]
        public void CheckPWofNonExistingUser()
        {
            // Arrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser", "PW"u8, [Role.AuthenticatedUser]);

            // Act
            bool result = usersDb.CheckCredentials("NoTestUser", "PW"u8);

            // Assert
            Assert.That(result, Is.False);
        }

        [Test]
        public void GetUsersReturnsCreatedUsers()
        {
            // Arrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser1", "PW"u8, [Role.AuthenticatedUser]);
            usersDb.CreateUser("TestUser2", "PW2"u8, [Role.Engineer]);

            // Act
            IReadOnlyList<UserManagementDataType> users = usersDb.GetUsers();

            // Assert
            Assert.That(users.Select(user => user.UserName), Is.EquivalentTo(s_createdUsers));
            Assert.That(users.All(user => user.UserConfiguration == (uint)UserConfigurationMask.None), Is.True);
            Assert.That(users.Select(user => user.Description), Is.All.Empty);
        }

        [Test]
        public void CheckCredentialsSucceedsForPersistedPbkdf2VectorWith10000Iterations()
        {
            // Arrange
            var usersDb = new LinqUserDatabase
            {
                Users =
                [
                    new LinqUserDatabase.User
                    {
                        ID = Guid.NewGuid(),
                        UserName = "LegacyUser10000",
                        Hash = kPbkdf2Vector10000Iterations,
                        Roles = [Role.AuthenticatedUser]
                    }
                ]
            };

            // Act
            bool correctPassword = usersDb.CheckCredentials(
                "LegacyUser10000",
                "Correct.Horse.Battery9!"u8);
            bool wrongPassword = usersDb.CheckCredentials(
                "LegacyUser10000",
                "Wrong.Horse.Battery9!"u8);

            // Assert
            Assert.That(correctPassword, Is.True);
            Assert.That(wrongPassword, Is.False);
        }

        [Test]
        public void CheckCredentialsSucceedsForPersistedPbkdf2VectorWith100000Iterations()
        {
            // Arrange
            var usersDb = new LinqUserDatabase
            {
                Users =
                [
                    new LinqUserDatabase.User
                    {
                        ID = Guid.NewGuid(),
                        UserName = "CurrentUser100000",
                        Hash = kPbkdf2Vector100000Iterations,
                        Roles = [Role.AuthenticatedUser]
                    }
                ]
            };

            // Act
            bool correctPassword = usersDb.CheckCredentials(
                "CurrentUser100000",
                "Correct.Horse.Battery9!"u8);
            bool wrongPassword = usersDb.CheckCredentials(
                "CurrentUser100000",
                "Wrong.Horse.Battery9!"u8);

            // Assert
            Assert.That(correctPassword, Is.True);
            Assert.That(wrongPassword, Is.False);
        }

        [Theory]
        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("100000000")]
        [TestCase("99999999999999999999")]
        [TestCase("notanumber")]
        public void CheckCredentialsReturnsFalseForHashWithInvalidIterationCount(string iterations)
        {
            // Arrange - reuse the valid salt/key segments from a known-good vector but
            // replace the iteration count with an out-of-range or unparsable value.
            string[] parts = kPbkdf2Vector100000Iterations.Split('.');
            string corruptHash = $"{iterations}.{parts[1]}.{parts[2]}";
            var usersDb = new LinqUserDatabase
            {
                Users =
                [
                    new LinqUserDatabase.User
                    {
                        ID = Guid.NewGuid(),
                        UserName = "CorruptIterationsUser",
                        Hash = corruptHash,
                        Roles = [Role.AuthenticatedUser]
                    }
                ]
            };

            // Act
            bool result = usersDb.CheckCredentials(
                "CorruptIterationsUser",
                "Correct.Horse.Battery9!"u8);

            // Assert - fail closed, never throw.
            Assert.That(result, Is.False);
        }

        [Test]
        public void CheckCredentialsReturnsFalseForHashWithEmptyKey()
        {
            // Arrange - a persisted hash with an empty derived key segment.
            string[] parts = kPbkdf2Vector100000Iterations.Split('.');
            string corruptHash = $"{parts[0]}.{parts[1]}.";
            var usersDb = new LinqUserDatabase
            {
                Users =
                [
                    new LinqUserDatabase.User
                    {
                        ID = Guid.NewGuid(),
                        UserName = "EmptyKeyUser",
                        Hash = corruptHash,
                        Roles = [Role.AuthenticatedUser]
                    }
                ]
            };

            // Act
            bool result = usersDb.CheckCredentials(
                "EmptyKeyUser",
                "Correct.Horse.Battery9!"u8);

            // Assert - fail closed, never throw.
            Assert.That(result, Is.False);
        }
    }
}
