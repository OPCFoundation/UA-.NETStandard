/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using NUnit.Framework;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies that accepted user mutations persist and rejected mutations leave stored credentials unchanged.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Parallelizable(ParallelScope.All)]
    public sealed class UserDatabasePersistenceRegressionTests
    {
        /// <summary>
        /// Verifies that deleting a user or changing a password remains effective after reloading the database.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void SuccessfulUserMutationSurvivesDatabaseReload(bool delete)
        {
            string directory = Path.Combine(Path.GetTempPath(), $"opcua-user-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "users.json");
            try
            {
                var original = new JsonUserDatabase(path);
                Assert.That(original.CreateUser("user", "before"u8, [Role.AuthenticatedUser]), Is.True);
                bool changed = delete
                    ? original.DeleteUser("user")
                    : original.ChangePassword("user", "before"u8, "after"u8);
                Assert.That(changed, Is.True);

                IUserDatabase reloaded = JsonUserDatabase.Load(path, NUnitTelemetryContext.Create());
                Assert.That(reloaded.CheckCredentials("user", "before"u8), Is.False);
                if (delete)
                {
                    Assert.That(reloaded.GetUsers(), Is.Empty);
                }
                else
                {
                    Assert.That(reloaded.CheckCredentials("user", "after"u8), Is.True);
                    Assert.That(reloaded.GetUserRoles("user"), Does.Contain(Role.AuthenticatedUser));
                }
            }
            finally
            {
                File.Delete(path);
                Directory.Delete(directory);
            }
        }

        /// <summary>
        /// Verifies that rejected deletion and password changes preserve both file bytes and reloadable credentials.
        /// </summary>
        [Test]
        public void RejectedUserMutationsPreservePersistedCredentials()
        {
            string directory = Path.Combine(Path.GetTempPath(), $"opcua-user-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "users.json");
            try
            {
                var original = new JsonUserDatabase(path);
                original.CreateUser("user", "before"u8, [Role.AuthenticatedUser]);
                byte[] before = File.ReadAllBytes(path);
                Assert.That(original.DeleteUser("missing"), Is.False);
                Assert.That(original.ChangePassword("user", "wrong"u8, "after"u8), Is.False);
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));

                IUserDatabase reloaded = JsonUserDatabase.Load(path, NUnitTelemetryContext.Create());
                Assert.That(reloaded.CheckCredentials("user", "before"u8), Is.True);
                Assert.That(reloaded.CheckCredentials("user", "after"u8), Is.False);
            }
            finally
            {
                File.Delete(path);
                Directory.Delete(directory);
            }
        }
    }
}
