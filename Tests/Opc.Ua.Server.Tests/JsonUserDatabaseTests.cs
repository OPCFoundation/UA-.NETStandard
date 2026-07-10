/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class JsonUserDatabaseTests
    {
        [Test]
        public void LoadRejectsNullFileName()
        {
            Assert.That(
                () => JsonUserDatabase.Load(null!, NUnitTelemetryContext.Create()),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void LoadMissingFileReturnsEmptyDatabaseWithFileName()
        {
            string fileName = CreateDatabasePath();

            IUserDatabase database = JsonUserDatabase.Load(fileName, NUnitTelemetryContext.Create());

            var jsonDatabase = (JsonUserDatabase)database;
            Assert.That(jsonDatabase.FileName, Is.EqualTo(fileName));
            Assert.That(jsonDatabase.GetUsers(), Is.Empty);
        }

        [Test]
        public void SaveAndLoadRoundTripsUsersCredentialsAndRoles()
        {
            string fileName = CreateDatabasePath();
            var database = new JsonUserDatabase(fileName);
            database.CreateUser("alice", "secret"u8, [Role.AuthenticatedUser, Role.Engineer]);

            IUserDatabase loaded = JsonUserDatabase.Load(fileName, NUnitTelemetryContext.Create());

            Assert.That(((JsonUserDatabase)loaded).FileName, Is.EqualTo(fileName));
            Assert.That(loaded.GetUsers().Single().UserName, Is.EqualTo("alice"));
            Assert.That(loaded.CheckCredentials("alice", "secret"u8), Is.True);
            Assert.That(loaded.CheckCredentials("alice", "wrong"u8), Is.False);
            Assert.That(loaded.GetUserRoles("alice"), Has.Count.EqualTo(2));
            Assert.That(loaded.GetUserRoles("alice"), Does.Contain(Role.AuthenticatedUser));
            Assert.That(loaded.GetUserRoles("alice"), Does.Contain(Role.Engineer));
        }

        [Test]
        public void LoadExistingEmptyJsonPreservesFileName()
        {
            string fileName = CreateDatabasePath();
            File.WriteAllText(fileName, "{\"users\":[]}");

            IUserDatabase loaded = JsonUserDatabase.Load(fileName, NUnitTelemetryContext.Create());

            Assert.That(((JsonUserDatabase)loaded).FileName, Is.EqualTo(fileName));
            Assert.That(loaded.GetUsers().Select(user => user.UserName), Is.EqualTo(Array.Empty<string>()));
        }

        [Test]
        public void LoadInvalidJsonLogsAndReturnsEmptyDatabase()
        {
            string fileName = CreateDatabasePath();
            File.WriteAllText(fileName, "{ invalid json }");

            IUserDatabase loaded = JsonUserDatabase.Load(fileName, NUnitTelemetryContext.Create());

            Assert.That(((JsonUserDatabase)loaded).FileName, Is.EqualTo(fileName));
            Assert.That(loaded.GetUsers(), Is.Empty);
        }

        private static string CreateDatabasePath()
        {
            string directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "JsonUserDatabaseTests");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        }
    }
}
