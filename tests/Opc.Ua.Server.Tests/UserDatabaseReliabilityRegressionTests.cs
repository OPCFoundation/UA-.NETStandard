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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Tests;
using UserManagementFacade = Opc.Ua.Server.UserManagement.UserManagement;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Server")]
    public sealed class UserDatabaseReliabilityRegressionTests
    {
        [Test]
        public void FailedSnapshotWritePreservesThePreviousCommittedDatabase()
        {
            using var files = new DatabaseFiles();
            var initial = new JsonUserDatabase(files.FileName);
            initial.CreateUser("alice", "credential"u8, [Role.AuthenticatedUser]);
            byte[] committed = File.ReadAllBytes(files.FileName);
            var failing = new JsonUserDatabase(files.FileName, (path, bytes) =>
            {
                using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                stream.Write(bytes, 0, Math.Min(bytes.Length, 12));
                throw new IOException("controlled incomplete write");
            })
            {
                Users = initial.Users
            };
            Assert.Throws<IOException>(() => failing.CreateUser("bob", "credential"u8, [Role.Operator]));
            Assert.That(File.ReadAllBytes(files.FileName), Is.EqualTo(committed));
            IUserDatabase reloaded = JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create());
            Assert.That(reloaded.CheckCredentials("alice", "credential"u8), Is.True);
            Assert.That(reloaded.GetUsers().Select(user => user.UserName), Is.EqualTo(s_oneUser));
            Assert.That(Directory.GetFiles(files.DirectoryName), Has.Length.EqualTo(1));
        }

        [Test]
        public void SnapshotBecomesVisibleOnlyAfterTheCompleteWrite()
        {
            using var files = new DatabaseFiles();
            var initial = new JsonUserDatabase(files.FileName);
            initial.CreateUser("alice", "credential"u8, [Role.AuthenticatedUser]);
            byte[] committed = File.ReadAllBytes(files.FileName);
            bool oldVersionStayedIntact = false;
            var database = new JsonUserDatabase(files.FileName, (path, bytes) =>
            {
                File.WriteAllBytes(path, bytes);
                oldVersionStayedIntact = File.ReadAllBytes(files.FileName).SequenceEqual(committed);
            })
            {
                Users = initial.Users
            };
            database.CreateUser("bob", "credential"u8, [Role.Operator]);
            Assert.That(oldVersionStayedIntact, Is.True);
            IUserDatabase loaded = JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create());
            Assert.That(loaded.GetUsers().Select(user => user.UserName),
                Is.EquivalentTo(s_twoUsers));
            Assert.That(loaded.CheckCredentials("bob", "credential"u8), Is.True);
            Assert.That(Directory.GetFiles(files.DirectoryName), Has.Length.EqualTo(1));
        }

        [TestCase("{")]
        [TestCase("null")]
        public void CorruptOrNullDatabaseDoesNotBecomeAnEmptySuccessfulLoad(string json)
        {
            using var files = new DatabaseFiles();
            File.WriteAllText(files.FileName, json);
            Assert.Throws<JsonException>(() => JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create()));
            Assert.That(File.ReadAllText(files.FileName), Is.EqualTo(json));
        }

        [Test]
        public void InaccessibleDatabaseDoesNotBecomeAnEmptySuccessfulLoad()
        {
            using var files = new DatabaseFiles();
            var database = new JsonUserDatabase(files.FileName);
            database.CreateUser("alice", "credential"u8, []);
            using var locked = new FileStream(files.FileName, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Throws<IOException>(() => JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create()));
        }

        [TestCase("accepted")]
        [TestCase("rejected")]
        [TestCase("failure")]
        public void DerivedVerificationKeyIsClearedForSuccessRejectionAndFailure(string outcome)
        {
            byte[] observed = null;
            var database = new LinqUserDatabase(key =>
            {
                observed = key;
                if (outcome == "failure")
                {
                    throw new InvalidOperationException("controlled observer failure");
                }
            });
            database.CreateUser("alice", "credential"u8, []);
            if (outcome == "failure")
            {
                Assert.Throws<InvalidOperationException>(() => database.CheckCredentials("alice", "credential"u8));
            }
            else
            {
                Assert.That(database.CheckCredentials(
                    "alice", outcome == "accepted" ? "credential"u8 : "incorrect"u8),
                    Is.EqualTo(outcome == "accepted"));
            }
            Assert.That(observed, Has.Length.EqualTo(32));
            Assert.That(Array.TrueForAll(observed, value => value == 0), Is.True);
        }

        [TestCase(1)]
        [TestCase(31)]
        [TestCase(33)]
        public void MalformedDerivedKeyLengthCannotAuthenticateUsingAPrefix(int length)
        {
            var valid = new LinqUserDatabase();
            valid.CreateUser("alice", "credential"u8, []);
            string[] fields = valid.Users.Single().Hash.Split('.');
            byte[] key = Convert.FromBase64String(fields[2]);
            byte[] malformed = new byte[length];
            Array.Copy(key, malformed, Math.Min(key.Length, malformed.Length));
            var database = new LinqUserDatabase
            {
                Users =
                [
                    new LinqUserDatabase.User
                    {
                        ID = Guid.NewGuid(),
                        UserName = "alice",
                        Roles = [],
                        Hash = fields[0] + "." + fields[1] + "." + Convert.ToBase64String(malformed)
                    }
                ]
            };
            Assert.That(database.CheckCredentials("alice", "credential"u8), Is.False);
        }

        [Test]
        public void UserSnapshotsRemainStableAcrossSubsequentCredentialAndRoleUpdates()
        {
            var database = new LinqUserDatabase();
            database.CreateUser("alice", "credential"u8, [Role.AuthenticatedUser]);
            LinqUserDatabase.User[] snapshot = database.Users;
            string originalHash = snapshot[0].Hash;
            Guid originalId = snapshot[0].ID;
            database.CreateUser("alice", "replacement"u8, [Role.Operator]);
            Assert.That(snapshot[0].Hash, Is.EqualTo(originalHash));
            Assert.That(snapshot[0].Roles, Has.Count.EqualTo(1));
            Assert.That(snapshot[0].Roles.Single(), Is.EqualTo(Role.AuthenticatedUser));
            Assert.That(database.Users[0].ID, Is.EqualTo(originalId));
            Assert.That(database.CheckCredentials("alice", "replacement"u8), Is.True);
            Assert.That(database.GetUserRoles("alice").Single(), Is.EqualTo(Role.Operator));
        }

        [Test]
        public async Task ConcurrentUserUpdatesLeaveACompleteReloadableSnapshotAsync()
        {
            using var files = new DatabaseFiles();
            var database = new JsonUserDatabase(files.FileName);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var updates = new Task[8];
            for (int i = 0; i < updates.Length; i++)
            {
                int index = i;
                updates[i] = Task.Run(async () =>
                {
                    await release.Task.ConfigureAwait(false);
                    database.CreateUser("user-" + index, "credential"u8, [Role.AuthenticatedUser]);
                });
            }
            release.SetResult(true);
            await Task.WhenAll(updates).ConfigureAwait(false);
            IUserDatabase loaded = JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create());
            Assert.That(loaded.GetUsers(), Has.Count.EqualTo(updates.Length));
            for (int i = 0; i < updates.Length; i++)
            {
                Assert.That(loaded.CheckCredentials("user-" + i, "credential"u8), Is.True);
            }
            Assert.That(Directory.GetFiles(files.DirectoryName), Has.Length.EqualTo(1));
        }

        [TestCase("active")]
        [TestCase("inactive")]
        [TestCase("unknown")]
        public async Task UnknownAndInactiveUsersExecuteCredentialVerificationAsync(string state)
        {
            var derived = new List<byte[]>();
            var database = new LinqUserDatabase(derived.Add);
            using var management = new UserManagementFacade(database);
            management.AddUser("alice", "credential",
                state == "inactive" ? UserConfigurationMask.Disabled : UserConfigurationMask.None, string.Empty);
            var authenticator = new UserNamePasswordAuthenticator(database, management, NUnitTelemetryContext.Create());
            var handler = new UserNameIdentityTokenHandler(state == "unknown" ? "nobody" : "alice", "credential"u8);
            var context = new AuthenticationContext(
                handler,
                new UserTokenPolicy { TokenType = UserTokenType.UserName, PolicyId = "username" },
                new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
            AuthenticationResult result = await authenticator.AuthenticateAsync(context).ConfigureAwait(false);
            Assert.That(derived, Has.Count.EqualTo(1));
            Assert.That(derived[0], Has.Length.EqualTo(32));
            Assert.That(Array.TrueForAll(derived[0], value => value == 0), Is.True);
            Assert.That(result.Outcome,
                Is.EqualTo(state == "active" ? AuthenticationOutcome.Accepted : AuthenticationOutcome.Rejected));
            if (state != "active")
            {
                Assert.That(result.Error.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
            CryptoUtils.ZeroMemory(handler.DecryptedPassword);
        }

        private static readonly string[] s_oneUser = ["alice"];
        private static readonly string[] s_twoUsers = ["alice", "bob"];

        private sealed class DatabaseFiles : IDisposable
        {
            public DatabaseFiles()
            {
                DirectoryName = Path.Combine(Path.GetTempPath(), "UserDatabaseRegression-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(DirectoryName);
            }

            public string DirectoryName { get; }
            public string FileName => Path.Combine(DirectoryName, "users.json");

            public void Dispose()
            {
                Directory.Delete(DirectoryName, recursive: true);
            }
        }
    }
}
