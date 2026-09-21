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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Tests;
using UserManagementFacade = Opc.Ua.Server.UserManagement.UserManagement;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies atomic user persistence, stable snapshots, and reliable credential verification cleanup.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    public sealed class UserDatabaseReliabilityRegressionTests
    {
        [Test]
        public void FailedUserCreationDoesNotPersistCredentialsWithoutRequestedFlags()
        {
            using var files = new DatabaseFiles();
            var initial = new JsonUserDatabase(files.FileName);
            Assert.That(initial.CreateUser("alice", "credential"u8, [Role.AuthenticatedUser]), Is.True);
            byte[] committed = File.ReadAllBytes(files.FileName);
            const UserConfigurationMask configuration =
                UserConfigurationMask.Disabled | UserConfigurationMask.MustChangePassword;
            var database = new JsonUserDatabase(files.FileName, (path, bytes) =>
            {
                File.WriteAllBytes(path, bytes);
                using var snapshot = JsonDocument.Parse(bytes);
                if (snapshot.RootElement.GetProperty("users").EnumerateArray().Any(user =>
                    user.GetProperty("UserName").GetString() == "bob" &&
                    user.GetProperty("UserConfiguration").GetUInt32() == (uint)configuration))
                {
                    throw new IOException("controlled metadata snapshot failure");
                }
            })
            {
                Users = initial.Users
            };
            using var management = new UserManagementFacade(database);

            IOException failure = Assert.Throws<IOException>(() =>
                management.AddUser("bob", "initial-credential", configuration, "Pending approval"));
            Assert.That(failure.Message, Is.EqualTo("controlled metadata snapshot failure"));

            IUserDatabase reloaded = JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create());
            Assert.That(reloaded.GetUsers().Select(user => user.UserName), Is.EqualTo(s_oneUser));
            Assert.That(reloaded.CheckCredentials("bob", "initial-credential"u8), Is.False);
            Assert.That(reloaded.CheckCredentials("alice", "credential"u8), Is.True);
            Assert.That(File.ReadAllBytes(files.FileName), Is.EqualTo(committed));
            Assert.That(database.GetUsers().Select(user => user.UserName), Is.EqualTo(s_oneUser));
            Assert.That(database.CheckCredentials("bob", "initial-credential"u8), Is.False);
            Assert.That(management.SnapshotUsers().Select(user => user.UserName), Is.EqualTo(s_oneUser));
            Assert.That(Directory.GetFiles(files.DirectoryName), Has.Length.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedUserModificationPreservesPasswordAndMetadataAfterRestart(bool resetPassword)
        {
            using var files = new DatabaseFiles();
            var initial = new JsonUserDatabase(files.FileName);
            Assert.That(initial.CreateUser("alice", "credential"u8, [Role.SecurityAdmin]), Is.True);
            Assert.That(initial.UpdateUserMetadata(
                "alice", UserConfigurationMask.MustChangePassword, "Original restriction"), Is.True);
            byte[] committed = File.ReadAllBytes(files.FileName);
            var database = new JsonUserDatabase(files.FileName, (path, bytes) =>
            {
                File.WriteAllBytes(path, bytes);
                using var snapshot = JsonDocument.Parse(bytes);
                if (snapshot.RootElement.GetProperty("users").EnumerateArray().Any(user =>
                    user.GetProperty("Description").GetString() == "Reset pending approval"))
                {
                    throw new IOException("controlled modification failure");
                }
            })
            {
                Users = initial.Users
            };
            using var management = new UserManagementFacade(database);
            bool deactivated = false;
            management.UserDeactivated += (_, _) => deactivated = true;

            IOException failure = Assert.Throws<IOException>(() => management.ModifyUser(
                "alice",
                modifyPassword: resetPassword,
                password: "replacement-credential",
                modifyUserConfiguration: true,
                userConfiguration: UserConfigurationMask.Disabled | UserConfigurationMask.MustChangePassword,
                modifyDescription: true,
                description: "Reset pending approval",
                callingUserName: "admin"));
            Assert.That(failure.Message, Is.EqualTo("controlled modification failure"));

            IUserDatabase reloaded = JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create());
            AssertOriginalUser(reloaded);
            Assert.That(File.ReadAllBytes(files.FileName), Is.EqualTo(committed));
            AssertOriginalUser(database);
            Assert.That(database.Users.Single().ID, Is.EqualTo(initial.Users.Single().ID));
            Assert.That(database.Users.Single().Hash, Is.EqualTo(initial.Users.Single().Hash));
            Assert.That(management.IsUserActive("alice"), Is.True);
            Assert.That(management.MustChangePassword("alice"), Is.True);
            Assert.That(management.SnapshotUsers().Single().Description, Is.EqualTo("Original restriction"));
            Assert.That(deactivated, Is.False);

            Assert.That(database.CreateUser("bob", "other-credential"u8, [Role.Operator]), Is.True);
            AssertOriginalUser(JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create()));
            Assert.That(Directory.GetFiles(files.DirectoryName), Has.Length.EqualTo(1));
        }

        [Test]
        public void FailedPasswordChangePreservesPasswordAndRequiredChangeFlagAfterRestart()
        {
            using var files = new DatabaseFiles();
            var initial = new JsonUserDatabase(files.FileName);
            Assert.That(initial.CreateUser("alice", "credential"u8, [Role.SecurityAdmin]), Is.True);
            Assert.That(initial.UpdateUserMetadata(
                "alice", UserConfigurationMask.MustChangePassword, "Original restriction"), Is.True);
            byte[] committed = File.ReadAllBytes(files.FileName);
            var database = new JsonUserDatabase(files.FileName, (path, bytes) =>
            {
                File.WriteAllBytes(path, bytes);
                using var snapshot = JsonDocument.Parse(bytes);
                if (snapshot.RootElement.GetProperty("users").EnumerateArray().Any(user =>
                    user.GetProperty("UserName").GetString() == "alice" &&
                    user.GetProperty("UserConfiguration").GetUInt32() == 0))
                {
                    throw new IOException("controlled password change failure");
                }
            })
            {
                Users = initial.Users
            };
            using var management = new UserManagementFacade(database);

            IOException failure = Assert.Throws<IOException>(() =>
                management.ChangePassword("alice", "credential", "replacement-credential"));
            Assert.That(failure.Message, Is.EqualTo("controlled password change failure"));

            AssertOriginalUser(JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create()));
            Assert.That(File.ReadAllBytes(files.FileName), Is.EqualTo(committed));
            AssertOriginalUser(database);
            Assert.That(management.MustChangePassword("alice"), Is.True);
            Assert.That(management.IsUserActive("alice"), Is.True);
            Assert.That(management.SnapshotUsers().Single().Description, Is.EqualTo("Original restriction"));
            Assert.That(Directory.GetFiles(files.DirectoryName), Has.Length.EqualTo(1));
        }

        [Test]
        public async Task CreateUserCommitsFlagsBeforeValidCredentialAuthenticationAsync()
        {
            using var files = new DatabaseFiles();
            int writes = 0;
            var database = new JsonUserDatabase(files.FileName, (path, bytes) =>
            {
                writes++;
                File.WriteAllBytes(path, bytes);
            });
            using var management = new UserManagementFacade(database);
            ServiceResult added = management.AddUser(
                "bob",
                "credential",
                UserConfigurationMask.Disabled | UserConfigurationMask.MustChangePassword,
                "Pending approval");
            Assert.That(ServiceResult.IsGood(added), Is.True);
            Assert.That(writes, Is.EqualTo(1));

            IUserDatabase reloaded = JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create());
            Assert.That(reloaded.CheckCredentials("bob", "credential"u8), Is.True);
            Assert.That(reloaded.CheckCredentials("bob", "incorrect"u8), Is.False);
            UserManagementDataType user = reloaded.GetUsers().Single();
            Assert.That(user.UserName, Is.EqualTo("bob"));
            Assert.That(user.UserConfiguration,
                Is.EqualTo((uint)(UserConfigurationMask.Disabled | UserConfigurationMask.MustChangePassword)));
            Assert.That(user.Description, Is.EqualTo("Pending approval"));
            using var restarted = new UserManagementFacade(reloaded);
            Assert.That(restarted.IsUserActive("bob"), Is.False);
            Assert.That(restarted.MustChangePassword("bob"), Is.True);

            var authenticator = new UserNamePasswordAuthenticator(reloaded, restarted, NUnitTelemetryContext.Create());
            var handler = new UserNameIdentityTokenHandler("bob", "credential"u8);
            try
            {
                var context = new AuthenticationContext(
                    handler,
                    new UserTokenPolicy { TokenType = UserTokenType.UserName, PolicyId = "username" },
                    new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                    ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
                AuthenticationResult disabled = await authenticator.AuthenticateAsync(context).ConfigureAwait(false);
                Assert.That(disabled.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
                Assert.That(disabled.Error.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));

                ServiceResult enabled = restarted.ModifyUser(
                    "bob", false, string.Empty, true, UserConfigurationMask.None, false, string.Empty, "admin");
                Assert.That(ServiceResult.IsGood(enabled), Is.True);
                AuthenticationResult active = await authenticator.AuthenticateAsync(context).ConfigureAwait(false);
                Assert.That(active.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            }
            finally
            {
                CryptoUtils.ZeroMemory(handler.DecryptedPassword);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PasswordResetCommitsCredentialsAndEffectiveMetadataInOneWrite(bool modifyMetadata)
        {
            using var files = new DatabaseFiles();
            var initial = new JsonUserDatabase(files.FileName);
            Assert.That(initial.CreateUser("alice", "credential"u8, [Role.SecurityAdmin]), Is.True);
            Assert.That(initial.UpdateUserMetadata(
                "alice", UserConfigurationMask.MustChangePassword, "Original restriction"), Is.True);
            int writes = 0;
            var database = new JsonUserDatabase(files.FileName, (path, bytes) =>
            {
                writes++;
                File.WriteAllBytes(path, bytes);
            })
            {
                Users = initial.Users
            };
            using var management = new UserManagementFacade(database);
            int deactivated = 0;
            management.UserDeactivated += (_, _) => deactivated++;

            ServiceResult result = management.ModifyUser(
                "alice",
                modifyPassword: true,
                password: "replacement-credential",
                modifyUserConfiguration: modifyMetadata,
                userConfiguration: UserConfigurationMask.Disabled | UserConfigurationMask.MustChangePassword,
                modifyDescription: modifyMetadata,
                description: "Reset pending approval",
                callingUserName: "admin");
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(writes, Is.EqualTo(1));
            Assert.That(deactivated, Is.EqualTo(modifyMetadata ? 1 : 0));
            Assert.That(database.Users.Single().ID, Is.EqualTo(initial.Users.Single().ID));

            IUserDatabase reloaded = JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create());
            Assert.That(reloaded.CheckCredentials("alice", "credential"u8), Is.False);
            Assert.That(reloaded.CheckCredentials("alice", "replacement-credential"u8), Is.True);
            Assert.That(reloaded.GetUserRoles("alice").Single(), Is.EqualTo(Role.SecurityAdmin));
            UserManagementDataType user = reloaded.GetUsers().Single();
            Assert.That(user.UserConfiguration, Is.EqualTo((uint)(modifyMetadata
                ? UserConfigurationMask.Disabled | UserConfigurationMask.MustChangePassword
                : UserConfigurationMask.MustChangePassword)));
            Assert.That(user.Description,
                Is.EqualTo(modifyMetadata ? "Reset pending approval" : "Original restriction"));
            Assert.That(management.SnapshotUsers().Single().UserConfiguration, Is.EqualTo(user.UserConfiguration));
            Assert.That(management.SnapshotUsers().Single().Description, Is.EqualTo(user.Description));
        }

        [Test]
        public void PasswordChangeCommitsCredentialsAndClearsOnlyRequiredChangeFlagInOneWrite()
        {
            using var files = new DatabaseFiles();
            var initial = new JsonUserDatabase(files.FileName);
            Assert.That(initial.CreateUser("alice", "credential"u8, [Role.SecurityAdmin]), Is.True);
            Assert.That(initial.UpdateUserMetadata(
                "alice",
                UserConfigurationMask.Disabled |
                UserConfigurationMask.NoDelete |
                UserConfigurationMask.MustChangePassword,
                "Original restriction"), Is.True);
            int writes = 0;
            var database = new JsonUserDatabase(files.FileName, (path, bytes) =>
            {
                writes++;
                File.WriteAllBytes(path, bytes);
            })
            {
                Users = initial.Users
            };
            using var management = new UserManagementFacade(database);

            ServiceResult rejected = management.ChangePassword("alice", "incorrect", "replacement-credential");
            Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
            Assert.That(writes, Is.Zero);
            Assert.That(database.CheckCredentials("alice", "credential"u8), Is.True);
            Assert.That(management.MustChangePassword("alice"), Is.True);

            ServiceResult changed = management.ChangePassword("alice", "credential", "replacement-credential");
            Assert.That(ServiceResult.IsGood(changed), Is.True);
            Assert.That(writes, Is.EqualTo(1));
            Assert.That(database.Users.Single().ID, Is.EqualTo(initial.Users.Single().ID));
            Assert.That(management.IsUserActive("alice"), Is.False);
            Assert.That(management.MustChangePassword("alice"), Is.False);

            IUserDatabase reloaded = JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create());
            Assert.That(reloaded.CheckCredentials("alice", "credential"u8), Is.False);
            Assert.That(reloaded.CheckCredentials("alice", "replacement-credential"u8), Is.True);
            Assert.That(reloaded.GetUserRoles("alice").Single(), Is.EqualTo(Role.SecurityAdmin));
            UserManagementDataType user = reloaded.GetUsers().Single();
            Assert.That(user.UserConfiguration,
                Is.EqualTo((uint)(UserConfigurationMask.Disabled | UserConfigurationMask.NoDelete)));
            Assert.That(user.Description, Is.EqualTo("Original restriction"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PasswordVerificationDoesNotBlockUnrelatedAuthenticationAsync(bool correctPassword)
        {
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int verifications = 0;
            var database = new LinqUserDatabase(_ =>
            {
                if (Interlocked.Increment(ref verifications) == 1)
                {
                    entered.TrySetResult(true);
                    release.Wait();
                }
            });
            Assert.That(database.CreateUser(
                "alice", "credential"u8, [Role.AuthenticatedUser],
                UserConfigurationMask.MustChangePassword, "Pending change"), Is.True);
            Assert.That(database.CreateUser("bob", "other-credential"u8, [Role.Operator]), Is.True);
            using var management = new UserManagementFacade(database);
            var change = Task.Run(() =>
            {
                ServiceResult result = management.ChangePassword(
                    "alice", correctPassword ? "credential" : "incorrect", "replacement-credential");
                Assert.That(result.StatusCode,
                    Is.EqualTo(correctPassword ? StatusCodes.Good : StatusCodes.BadIdentityTokenInvalid));
            });
            Task authentication = Task.CompletedTask;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                authentication = Task.Run(async () =>
                    await AuthenticateUnrelatedUserAsync(database, management).ConfigureAwait(false));
                await authentication.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(release.IsSet, Is.False,
                    "Authentication must finish while the other user's verification is still paused.");
            }
            finally
            {
                release.Set();
                await Task.WhenAll(change, authentication).ConfigureAwait(false);
            }
            Assert.That(management.MustChangePassword("alice"), Is.EqualTo(!correctPassword));
            Assert.That(database.CheckCredentials("alice", "replacement-credential"u8), Is.EqualTo(correctPassword));
            Assert.That(database.CheckCredentials("alice", "credential"u8), Is.EqualTo(!correctPassword));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PasswordPersistenceDoesNotBlockAuthenticationOrExposeTentativeCredentialsAsync(bool failSave)
        {
            using var files = new DatabaseFiles();
            var initial = new JsonUserDatabase(files.FileName);
            Assert.That(initial.CreateUser(
                "alice", "credential"u8, [Role.AuthenticatedUser],
                UserConfigurationMask.MustChangePassword, "Pending change"), Is.True);
            Assert.That(initial.CreateUser("bob", "other-credential"u8, [Role.Operator]), Is.True);
            byte[] committed = File.ReadAllBytes(files.FileName);
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int writes = 0;
            var database = new JsonUserDatabase(files.FileName, (path, bytes) =>
            {
                Interlocked.Increment(ref writes);
                File.WriteAllBytes(path, bytes);
                entered.TrySetResult(true);
                release.Wait();
                if (failSave)
                {
                    throw new IOException("controlled password persistence failure");
                }
            })
            {
                Users = initial.Users
            };
            using var management = new UserManagementFacade(database);
            var change = Task.Run(() =>
            {
                if (failSave)
                {
                    Assert.That(
                        () => management.ChangePassword("alice", "credential", "replacement-credential"),
                        Throws.TypeOf<IOException>().With.Message.EqualTo("controlled password persistence failure"));
                }
                else
                {
                    Assert.That(
                        ServiceResult.IsGood(
                            management.ChangePassword("alice", "credential", "replacement-credential")),
                        Is.True);
                }
            });
            Task<(bool OldValid, bool NewValid, bool MustChange)> authentication =
                Task.FromResult((false, false, false));
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                authentication = Task.Run(async () =>
                {
                    await AuthenticateUnrelatedUserAsync(database, management).ConfigureAwait(false);
                    return (
                        database.CheckCredentials("alice", "credential"u8),
                        database.CheckCredentials("alice", "replacement-credential"u8),
                        management.MustChangePassword("alice"));
                });
                (bool oldValid, bool newValid, bool mustChange) = await authentication
                    .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(oldValid, Is.True);
                Assert.That(newValid, Is.False);
                Assert.That(mustChange, Is.True);
                Assert.That(release.IsSet, Is.False);
                Assert.That(File.ReadAllBytes(files.FileName).SequenceEqual(committed), Is.True);
            }
            finally
            {
                release.Set();
                await Task.WhenAll(change, authentication).ConfigureAwait(false);
            }
            Assert.That(writes, Is.EqualTo(1));
            Assert.That(database.CheckCredentials("alice", "credential"u8), Is.EqualTo(failSave));
            Assert.That(database.CheckCredentials("alice", "replacement-credential"u8), Is.EqualTo(!failSave));
            Assert.That(management.MustChangePassword("alice"), Is.EqualTo(failSave));
            IUserDatabase reloaded = JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create());
            Assert.That(reloaded.CheckCredentials("alice", "credential"u8), Is.EqualTo(failSave));
            Assert.That(reloaded.CheckCredentials("alice", "replacement-credential"u8), Is.EqualTo(!failSave));
            Assert.That(reloaded.GetUsers().Single(user => user.UserName == "alice").UserConfiguration,
                Is.EqualTo((uint)(failSave ? UserConfigurationMask.MustChangePassword : UserConfigurationMask.None)));
            Assert.That(Directory.GetFiles(files.DirectoryName), Has.Length.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PasswordChangeRejectsVerifierReplacedDuringVerification(bool resetPassword)
        {
            Action onVerified = null!;
            var database = new LinqUserDatabase(_ => onVerified?.Invoke());
            Assert.That(database.CreateUser(
                "alice", "credential"u8, [Role.SecurityAdmin],
                UserConfigurationMask.MustChangePassword, "Original restriction"), Is.True);
            onVerified = () =>
            {
                onVerified = null!;
                bool changed = resetPassword
                    ? database.ResetPassword(
                        "alice", "concurrent-credential"u8,
                        UserConfigurationMask.MustChangePassword, "Concurrent reset")
                    : database.ChangePassword("alice", "credential"u8, "concurrent-credential"u8);
                Assert.That(changed, Is.True);
            };

            Assert.That(database.ChangePassword("alice", "credential"u8, "replacement-credential"u8), Is.False);
            Assert.That(database.CheckCredentials("alice", "concurrent-credential"u8), Is.True);
            Assert.That(database.CheckCredentials("alice", "credential"u8), Is.False);
            Assert.That(database.CheckCredentials("alice", "replacement-credential"u8), Is.False);
            Assert.That(database.GetUserRoles("alice").Single(), Is.EqualTo(Role.SecurityAdmin));
            UserManagementDataType user = database.GetUsers().Single();
            Assert.That(user.UserConfiguration, Is.EqualTo((uint)(resetPassword
                ? UserConfigurationMask.MustChangePassword
                : UserConfigurationMask.None)));
            Assert.That(user.Description, Is.EqualTo(resetPassword ? "Concurrent reset" : "Original restriction"));
            Assert.That(database.Users.Single().Hash.Split('.')[0], Is.EqualTo("100000"));
        }

        [Test]
        public void PasswordChangePreservesMetadataCommittedDuringVerification()
        {
            Action onVerified = null!;
            var database = new LinqUserDatabase(_ => onVerified?.Invoke());
            Assert.That(database.CreateUser(
                "alice", "credential"u8, [Role.SecurityAdmin],
                UserConfigurationMask.MustChangePassword, "Original restriction"), Is.True);
            onVerified = () =>
            {
                onVerified = null!;
                Assert.That(database.UpdateUserMetadata(
                    "alice",
                    UserConfigurationMask.Disabled |
                    UserConfigurationMask.NoDelete |
                    UserConfigurationMask.MustChangePassword,
                    "Concurrent restriction"), Is.True);
            };

            Assert.That(database.ChangePassword("alice", "credential"u8, "replacement-credential"u8), Is.True);
            Assert.That(database.CheckCredentials("alice", "credential"u8), Is.False);
            Assert.That(database.CheckCredentials("alice", "replacement-credential"u8), Is.True);
            UserManagementDataType user = database.GetUsers().Single();
            Assert.That(user.UserConfiguration,
                Is.EqualTo((uint)(UserConfigurationMask.Disabled | UserConfigurationMask.NoDelete)));
            Assert.That(user.Description, Is.EqualTo("Concurrent restriction"));
        }

        [Test]
        public async Task ConcurrentPasswordChangesCommitOnlyOneCapturedVerifierAsync()
        {
            using var release = new ManualResetEventSlim();
            var bothVerified = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int verifications = 0;
            var database = new LinqUserDatabase(_ =>
            {
                int count = Interlocked.Increment(ref verifications);
                if (count <= 2)
                {
                    if (count == 2)
                    {
                        bothVerified.TrySetResult(true);
                    }
                    release.Wait();
                }
            });
            Assert.That(database.CreateUser(
                "alice", "credential"u8, [Role.SecurityAdmin],
                UserConfigurationMask.MustChangePassword, "Pending change"), Is.True);
            Task<bool> first = Task.Run(() =>
                database.ChangePassword("alice", "credential"u8, "first-replacement"u8));
            Task<bool> second = Task.Run(() =>
                database.ChangePassword("alice", "credential"u8, "second-replacement"u8));
            try
            {
                await bothVerified.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            finally
            {
                release.Set();
                await Task.WhenAll(first, second).ConfigureAwait(false);
            }

            bool[] accepted = await Task.WhenAll(first, second).ConfigureAwait(false);
            Assert.That(accepted.Count(value => value), Is.EqualTo(1));
            Assert.That(database.CheckCredentials("alice", "credential"u8), Is.False);
            Assert.That(database.CheckCredentials("alice", "first-replacement"u8), Is.EqualTo(accepted[0]));
            Assert.That(database.CheckCredentials("alice", "second-replacement"u8), Is.EqualTo(accepted[1]));
            Assert.That(database.GetUsers().Single().UserConfiguration, Is.Zero);
            Assert.That(database.GetUserRoles("alice").Single(), Is.EqualTo(Role.SecurityAdmin));
        }

        [Test]
        public async Task QueuedAdministrativeResetPreservesRequiredChangeWithoutBlockingOtherUsersAsync()
        {
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resetStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int verifications = 0;
            var database = new LinqUserDatabase(_ =>
            {
                if (Interlocked.Increment(ref verifications) == 1)
                {
                    entered.TrySetResult(true);
                    release.Wait();
                }
            });
            Assert.That(database.CreateUser(
                "alice", "credential"u8, [Role.SecurityAdmin],
                UserConfigurationMask.MustChangePassword, "Pending change"), Is.True);
            Assert.That(database.CreateUser("bob", "other-credential"u8, [Role.Operator]), Is.True);
            using var management = new UserManagementFacade(database);
            Task<ServiceResult> change = Task.Run(() =>
                management.ChangePassword("alice", "credential", "replacement-credential"));
            Task reset = Task.CompletedTask;
            Task authentication = Task.CompletedTask;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                reset = Task.Run(() =>
                {
                    resetStarted.TrySetResult(true);
                    ServiceResult result = management.ModifyUser(
                        "alice", true, "admin-replacement", true, UserConfigurationMask.MustChangePassword,
                        true, "Administrator reset", "admin");
                    Assert.That(ServiceResult.IsGood(result), Is.True);
                });
                await resetStarted.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                authentication = Task.Run(async () =>
                    await AuthenticateUnrelatedUserAsync(database, management).ConfigureAwait(false));
                await authentication.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(release.IsSet, Is.False);
            }
            finally
            {
                release.Set();
                await Task.WhenAll(change, reset, authentication).ConfigureAwait(false);
            }

            Assert.That(ServiceResult.IsGood(await change.ConfigureAwait(false)), Is.True);
            Assert.That(management.MustChangePassword("alice"), Is.True);
            Assert.That(database.CheckCredentials("alice", "admin-replacement"u8), Is.True);
            Assert.That(database.CheckCredentials("alice", "replacement-credential"u8), Is.False);
            UserManagementDataType persisted = database.GetUsers().Single(user => user.UserName == "alice");
            UserManagementDataType published = management.SnapshotUsers().Single(user => user.UserName == "alice");
            Assert.That(persisted.UserConfiguration, Is.EqualTo((uint)UserConfigurationMask.MustChangePassword));
            Assert.That(published.UserConfiguration, Is.EqualTo(persisted.UserConfiguration));
            Assert.That(published.Description, Is.EqualTo("Administrator reset"));
        }

        [TestCase("create")]
        [TestCase("overwrite")]
        [TestCase("delete")]
        [TestCase("password")]
        [TestCase("metadata")]
        public void FailedSaveCannotLeakIntoALaterSuccessfulWrite(string operation)
        {
            using var files = new DatabaseFiles();
            var initial = new JsonUserDatabase(files.FileName);
            Assert.That(initial.CreateUser("alice", "credential"u8, [Role.SecurityAdmin]), Is.True);
            Assert.That(initial.UpdateUserMetadata(
                "alice", UserConfigurationMask.MustChangePassword, "Original restriction"), Is.True);
            byte[] committed = File.ReadAllBytes(files.FileName);
            int writes = 0;
            var database = new JsonUserDatabase(files.FileName, (path, bytes) =>
            {
                if (++writes == 1)
                {
                    using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    stream.Write(bytes, 0, Math.Min(bytes.Length, 12));
                    throw new IOException("controlled partial snapshot failure");
                }
                File.WriteAllBytes(path, bytes);
            })
            {
                Users = initial.Users
            };
            Action mutate = operation switch
            {
                "create" => () => database.CreateUser("failed-user", "replacement-credential"u8, [Role.Operator]),
                "overwrite" => () => database.CreateUser("alice", "replacement-credential"u8, [Role.Operator]),
                "delete" => () => database.DeleteUser("alice"),
                "password" => () => database.ChangePassword("alice", "credential"u8, "replacement-credential"u8),
                "metadata" => () => database.UpdateUserMetadata("alice", UserConfigurationMask.Disabled, "Failed"),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };

            IOException failure = Assert.Throws<IOException>(() => mutate());
            Assert.That(failure.Message, Is.EqualTo("controlled partial snapshot failure"));
            Assert.That(writes, Is.EqualTo(1));
            Assert.That(File.ReadAllBytes(files.FileName), Is.EqualTo(committed));
            Assert.That(database.GetUsers().Select(user => user.UserName), Is.EqualTo(s_oneUser));
            AssertOriginalUser(database);
            AssertOriginalUser(JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create()));

            Assert.That(database.CreateUser("bob", "other-credential"u8, [Role.Operator]), Is.True);
            Assert.That(writes, Is.EqualTo(2));
            IUserDatabase reloaded = JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create());
            AssertOriginalUser(reloaded);
            Assert.That(reloaded.GetUsers().Select(user => user.UserName), Is.EquivalentTo(s_twoUsers));
            Assert.That(reloaded.CheckCredentials("bob", "other-credential"u8), Is.True);
            Assert.That(Directory.GetFiles(files.DirectoryName), Has.Length.EqualTo(1));
        }

        [Test]
        public void CustomUserDatabaseRetainsUserManagementCompatibility()
        {
            var database = new CustomUserDatabase();
            Assert.That(database.CreateUser("alice", "credential"u8, [Role.SecurityAdmin]), Is.True);
            using var management = new UserManagementFacade(database);

            ServiceResult reset = management.ModifyUser(
                "alice", true, "replacement-credential", true, UserConfigurationMask.MustChangePassword,
                true, "Custom description", "admin");
            Assert.That(ServiceResult.IsGood(reset), Is.True);
            Assert.That(database.CheckCredentials("alice", "credential"u8), Is.False);
            Assert.That(database.CheckCredentials("alice", "replacement-credential"u8), Is.True);
            Assert.That(database.GetUserRoles("alice").Single(), Is.EqualTo(Role.SecurityAdmin));
            Assert.That(management.MustChangePassword("alice"), Is.True);
            Assert.That(management.SnapshotUsers().Single().Description, Is.EqualTo("Custom description"));

            ServiceResult changed = management.ChangePassword(
                "alice", "replacement-credential", "third-credential");
            Assert.That(ServiceResult.IsGood(changed), Is.True);
            Assert.That(database.CheckCredentials("alice", "third-credential"u8), Is.True);
            Assert.That(management.MustChangePassword("alice"), Is.False);
            Assert.That(ServiceResult.IsGood(
                management.AddUser("bob", "credential", UserConfigurationMask.Disabled, "Custom user")), Is.True);
            Assert.That(management.IsUserActive("bob"), Is.False);
            Assert.That(ServiceResult.IsGood(management.RemoveUser("bob", "admin")), Is.True);
            Assert.That(database.CheckCredentials("bob", "credential"u8), Is.False);
        }

        [Test]
        public void DuplicateAtomicCreationPreservesThePreviousRecordWithoutWriting()
        {
            using var files = new DatabaseFiles();
            var initial = new JsonUserDatabase(files.FileName);
            Assert.That(initial.CreateUser("alice", "credential"u8, [Role.SecurityAdmin]), Is.True);
            Assert.That(initial.UpdateUserMetadata(
                "alice", UserConfigurationMask.MustChangePassword, "Original restriction"), Is.True);
            byte[] committed = File.ReadAllBytes(files.FileName);
            int writes = 0;
            var database = new JsonUserDatabase(files.FileName, (path, bytes) =>
            {
                writes++;
                File.WriteAllBytes(path, bytes);
            })
            {
                Users = initial.Users
            };
            Assert.That(database.CreateUser(
                "alice", "replacement-credential"u8, [Role.Operator],
                UserConfigurationMask.Disabled, "Replacement"), Is.False);
            Assert.That(writes, Is.Zero);
            Assert.That(File.ReadAllBytes(files.FileName), Is.EqualTo(committed));
            AssertOriginalUser(database);
            AssertOriginalUser(JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create()));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RejectedUserModificationDoesNotPublishMetadata(bool resetPassword)
        {
            using var files = new DatabaseFiles();
            int writes = 0;
            var database = new JsonUserDatabase(files.FileName, (path, bytes) =>
            {
                writes++;
                File.WriteAllBytes(path, bytes);
            });
            Assert.That(database.CreateUser("alice", "credential"u8, [Role.SecurityAdmin]), Is.True);
            Assert.That(database.UpdateUserMetadata(
                "alice", UserConfigurationMask.MustChangePassword, "Original restriction"), Is.True);
            using var management = new UserManagementFacade(database);
            Assert.That(database.DeleteUser("alice"), Is.True);
            writes = 0;
            byte[] committed = File.ReadAllBytes(files.FileName);
            bool deactivated = false;
            management.UserDeactivated += (_, _) => deactivated = true;

            ServiceResult result = management.ModifyUser(
                "alice", resetPassword, "replacement-credential", true, UserConfigurationMask.Disabled,
                true, "Rejected update", "admin");
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That(writes, Is.Zero);
            Assert.That(deactivated, Is.False);
            Assert.That(File.ReadAllBytes(files.FileName), Is.EqualTo(committed));
            Assert.That(database.GetUsers(), Is.Empty);
            Assert.That(JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create()).GetUsers(), Is.Empty);
            Assert.That(management.IsUserActive("alice"), Is.True);
            Assert.That(management.MustChangePassword("alice"), Is.True);
            Assert.That(management.SnapshotUsers().Single().Description, Is.EqualTo("Original restriction"));
        }

        /// <summary>
        /// Verifies that a partial snapshot-write failure preserves the committed database
        /// and removes temporary files.
        /// </summary>
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

        /// <summary>
        /// Verifies that readers retain the old database until the complete replacement snapshot is ready.
        /// </summary>
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

        /// <summary>
        /// Verifies that malformed or null JSON throws without rewriting the database as an empty successful load.
        /// </summary>
        [TestCase("{")]
        [TestCase("null")]
        public void CorruptOrNullDatabaseDoesNotBecomeAnEmptySuccessfulLoad(string json)
        {
            using var files = new DatabaseFiles();
            File.WriteAllText(files.FileName, json);
            Assert.Throws<JsonException>(() => JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create()));
            Assert.That(File.ReadAllText(files.FileName), Is.EqualTo(json));
        }

        /// <summary>
        /// Verifies that an exclusively locked database reports an I/O failure instead of silently loading no users.
        /// </summary>
        [Test]
        public void InaccessibleDatabaseDoesNotBecomeAnEmptySuccessfulLoad()
        {
            using var files = new DatabaseFiles();
            var database = new JsonUserDatabase(files.FileName);
            database.CreateUser("alice", "credential"u8, []);
            using var locked = new FileStream(files.FileName, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Throws<IOException>(() => JsonUserDatabase.Load(files.FileName, NUnitTelemetryContext.Create()));
        }

        /// <summary>
        /// Verifies that temporary derived credential keys are cleared after acceptance, rejection, or failure.
        /// </summary>
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

        /// <summary>
        /// Verifies that a stored derived key with an invalid length cannot authenticate by matching only a prefix.
        /// </summary>
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

        /// <summary>
        /// Verifies that updating credentials and roles preserves earlier snapshots and the user's stable identity.
        /// </summary>
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

        /// <summary>
        /// Verifies that concurrent user creations persist one complete snapshot containing every accepted credential.
        /// </summary>
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

        /// <summary>
        /// Verifies that unknown and inactive users still derive and clear a verification key before rejection.
        /// </summary>
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
            var authenticator = new UserNamePasswordAuthenticator(
                database, management, NUnitTelemetryContext.Create());
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

        private static async Task AuthenticateUnrelatedUserAsync(
            IUserDatabase database,
            UserManagementFacade management)
        {
            Assert.That(management.MustChangePassword("bob"), Is.False);
            var authenticator = new UserNamePasswordAuthenticator(
                database, management, NUnitTelemetryContext.Create());
            var handler = new UserNameIdentityTokenHandler("bob", "other-credential"u8);
            try
            {
                var context = new AuthenticationContext(
                    handler,
                    new UserTokenPolicy { TokenType = UserTokenType.UserName, PolicyId = "username" },
                    new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                    ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
                AuthenticationResult authenticated = await authenticator.AuthenticateAsync(context)
                    .ConfigureAwait(false);
                Assert.That(authenticated.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
                Assert.That(database.GetUserRoles("bob").Single(), Is.EqualTo(Role.Operator));
            }
            finally
            {
                CryptoUtils.ZeroMemory(handler.DecryptedPassword);
            }
        }

        private static void AssertOriginalUser(IUserDatabase database)
        {
            Assert.That(database.CheckCredentials("alice", "credential"u8), Is.True);
            Assert.That(database.CheckCredentials("alice", "replacement-credential"u8), Is.False);
            Assert.That(database.GetUserRoles("alice").Single(), Is.EqualTo(Role.SecurityAdmin));
            UserManagementDataType user = database.GetUsers().Single(user => user.UserName == "alice");
            Assert.That(user.UserConfiguration, Is.EqualTo((uint)UserConfigurationMask.MustChangePassword));
            Assert.That(user.Description, Is.EqualTo("Original restriction"));
        }

        /// <summary>
        /// Defines the users expected when a failed write leaves the original snapshot committed.
        /// </summary>
        private static readonly string[] s_oneUser = ["alice"];

        /// <summary>
        /// Defines the users expected after a complete replacement snapshot is committed.
        /// </summary>
        private static readonly string[] s_twoUsers = ["alice", "bob"];

        private sealed class CustomUserDatabase : IUserDatabase
        {
            /// <inheritdoc/>
            public bool CreateUser(string userName, ReadOnlySpan<byte> password, ICollection<Role> roles)
            {
                return m_database.CreateUser(userName, password, roles);
            }

            /// <inheritdoc/>
            public bool DeleteUser(string userName)
            {
                return m_database.DeleteUser(userName);
            }

            /// <inheritdoc/>
            public bool CheckCredentials(string userName, ReadOnlySpan<byte> password)
            {
                return m_database.CheckCredentials(userName, password);
            }

            /// <inheritdoc/>
            public ICollection<Role> GetUserRoles(string userName)
            {
                return m_database.GetUserRoles(userName);
            }

            /// <inheritdoc/>
            public IReadOnlyList<UserManagementDataType> GetUsers()
            {
                return m_database.GetUsers();
            }

            /// <inheritdoc/>
            public bool ChangePassword(
                string userName,
                ReadOnlySpan<byte> oldPassword,
                ReadOnlySpan<byte> newPassword)
            {
                return m_database.ChangePassword(userName, oldPassword, newPassword);
            }

            /// <inheritdoc/>
            public bool CreateUser(
                string userName,
                ReadOnlySpan<byte> password,
                ArrayOf<Role> roles,
                UserConfigurationMask userConfiguration,
                string description)
            {
                return m_database.CreateUser(userName, password, roles, userConfiguration, description);
            }

            /// <inheritdoc/>
            public bool ResetPassword(
                string userName,
                ReadOnlySpan<byte> newPassword,
                UserConfigurationMask userConfiguration,
                string description)
            {
                return m_database.ResetPassword(userName, newPassword, userConfiguration, description);
            }

            /// <inheritdoc/>
            public bool UpdateUserMetadata(
                string userName,
                UserConfigurationMask userConfiguration,
                string description)
            {
                return m_database.UpdateUserMetadata(userName, userConfiguration, description);
            }

            private readonly LinqUserDatabase m_database = new();
        }

        /// <summary>
        /// Owns an isolated directory for the committed database and any transient snapshot files.
        /// </summary>
        private sealed class DatabaseFiles : IDisposable
        {
            /// <summary>
            /// Creates a unique temporary directory for one persistence scenario.
            /// </summary>
            public DatabaseFiles()
            {
                DirectoryName = Path.Combine(
                    Path.GetTempPath(), "UserDatabaseRegression-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(DirectoryName);
            }

            /// <summary>
            /// Gets the isolated directory used to check for leftover temporary files.
            /// </summary>
            public string DirectoryName { get; }

            /// <summary>
            /// Gets the path of the committed user database.
            /// </summary>
            public string FileName => Path.Combine(DirectoryName, "users.json");

            /// <summary>
            /// Removes the isolated directory and any database or temporary files created by the test.
            /// </summary>
            public void Dispose()
            {
                Directory.Delete(DirectoryName, recursive: true);
            }
        }
    }
}
