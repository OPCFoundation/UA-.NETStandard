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
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.UserDatabase;
namespace Opc.Ua.Conformance.Tests.Security
{
    /// <summary>
    /// compliance tests for User Management via the IUserDatabase
    /// exposed through the ReferenceServer.
    /// Tests exercise the user database directly through the server's public API
    /// and verify credential changes take effect for OPC UA sessions.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Security")]
    [Category("UserManagement")]
    public class UserManagementTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "001")]
        public void UserDatabaseIsAvailable()
        {
            Assert.That(UserDb, Is.Not.Null,
                "ReferenceServer should expose a UserDatabase.");
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "002")]
        public void DefaultUsersExistInDatabase()
        {
            EnsureUserDatabase();

            Assert.That(
                UserDb.CheckCredentials("sysadmin", ToUtf8("demo")), Is.True,
                "sysadmin/demo should be valid.");
            Assert.That(
                UserDb.CheckCredentials("user1", ToUtf8("password")), Is.True,
                "user1/password should be valid.");
            Assert.That(
                UserDb.CheckCredentials("user2", ToUtf8("password1")), Is.True,
                "user2/password1 should be valid.");
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "003")]
        public void SysadminHasSecurityAdminRole()
        {
            EnsureUserDatabase();

            ICollection<Role> roles = UserDb.GetUserRoles("sysadmin");
            Assert.That(roles, Is.Not.Null);
            Assert.That(roles, Does.Contain(Role.SecurityAdmin));
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "004")]
        public void RegularUserHasAuthenticatedRole()
        {
            EnsureUserDatabase();

            ICollection<Role> roles = UserDb.GetUserRoles("user1");
            Assert.That(roles, Is.Not.Null);
            Assert.That(roles, Does.Contain(Role.AuthenticatedUser));
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "005")]
        public void AddUserWithValidNameAndPassword()
        {
            EnsureUserDatabase();

            string testUser = "testuser_add_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                bool result = UserDb.CreateUser(
                    testUser, ToUtf8("TestPass123!"),
                    [Role.AuthenticatedUser]);
                Assert.That(result, Is.True, "CreateUser should return true.");
            }
            finally
            {
                UserDb.DeleteUser(testUser);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "006")]
        public void AddUserThenCheckCredentials()
        {
            EnsureUserDatabase();

            string testUser = "testuser_cred_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                UserDb.CreateUser(
                    testUser, ToUtf8("SecurePass!"),
                    [Role.AuthenticatedUser]);

                bool valid = UserDb.CheckCredentials(testUser, ToUtf8("SecurePass!"));
                Assert.That(valid, Is.True,
                    "New user should be able to authenticate.");
            }
            finally
            {
                UserDb.DeleteUser(testUser);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "007")]
        public void AddUserWithDuplicateNameUpdatesUser()
        {
            EnsureUserDatabase();

            string testUser = "testuser_dup_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                bool first = UserDb.CreateUser(
                    testUser, ToUtf8("Pass1"),
                    [Role.AuthenticatedUser]);
                Assert.That(first, Is.True);

                // Second creation with same name updates (returns false)
                bool second = UserDb.CreateUser(
                    testUser, ToUtf8("Pass2"),
                    [Role.AuthenticatedUser]);
                Assert.That(second, Is.False,
                    "Duplicate CreateUser should return false (updated).");

                // New password should work
                Assert.That(
                    UserDb.CheckCredentials(testUser, ToUtf8("Pass2")), Is.True);
            }
            finally
            {
                UserDb.DeleteUser(testUser);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "008")]
        public void AddUserWithEmptyNameThrows()
        {
            EnsureUserDatabase();

            Assert.Throws<ArgumentException>(
                () => UserDb.CreateUser(string.Empty, ToUtf8("pass"),
                    [Role.AuthenticatedUser]));
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "009")]
        public void AddUserWithEmptyPasswordThrows()
        {
            EnsureUserDatabase();

            Assert.Throws<ArgumentException>(
                () => UserDb.CreateUser("emptypass_user",
                    [],
                    [Role.AuthenticatedUser]));
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "010")]
        public void AddUserWithSpecificRoles()
        {
            EnsureUserDatabase();

            string testUser = "testuser_roles_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                var roles = new List<Role>
                {
                    Role.AuthenticatedUser,
                    Role.SecurityAdmin
                };
                UserDb.CreateUser(testUser, ToUtf8("RolePass!"), roles);

                ICollection<Role> retrievedRoles = UserDb.GetUserRoles(testUser);
                Assert.That(retrievedRoles, Does.Contain(Role.AuthenticatedUser));
                Assert.That(retrievedRoles, Does.Contain(Role.SecurityAdmin));
            }
            finally
            {
                UserDb.DeleteUser(testUser);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "011")]
        public void AddUserWithMaxLengthPassword()
        {
            EnsureUserDatabase();

            string testUser = "testuser_long_" + Guid.NewGuid().ToString("N")[..8];
            string longPassword = new('A', 256);
            try
            {
                bool result = UserDb.CreateUser(
                    testUser, ToUtf8(longPassword),
                    [Role.AuthenticatedUser]);
                Assert.That(result, Is.True);

                Assert.That(
                    UserDb.CheckCredentials(testUser, ToUtf8(longPassword)),
                    Is.True);
            }
            finally
            {
                UserDb.DeleteUser(testUser);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "012")]
        public void AddUserWithSpecialCharactersInNameAndPassword()
        {
            EnsureUserDatabase();

            string testUser = "user@#$_" + Guid.NewGuid().ToString("N")[..8];
            const string specialPassword = "p@$$w0rd!#%^&*()";
            try
            {
                bool result = UserDb.CreateUser(
                    testUser, ToUtf8(specialPassword),
                    [Role.AuthenticatedUser]);
                Assert.That(result, Is.True);

                Assert.That(
                    UserDb.CheckCredentials(testUser, ToUtf8(specialPassword)),
                    Is.True);
            }
            finally
            {
                UserDb.DeleteUser(testUser);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "013")]
        public void RemoveUserSucceeds()
        {
            EnsureUserDatabase();

            string testUser = "testuser_rm_" + Guid.NewGuid().ToString("N")[..8];
            UserDb.CreateUser(
                testUser, ToUtf8("ToDelete!"),
                [Role.AuthenticatedUser]);

            bool deleted = UserDb.DeleteUser(testUser);
            Assert.That(deleted, Is.True, "DeleteUser should return true.");
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "014")]
        public void RemoveUserVerifyCanNoLongerAuthenticate()
        {
            EnsureUserDatabase();

            string testUser = "testuser_rm2_" + Guid.NewGuid().ToString("N")[..8];
            UserDb.CreateUser(
                testUser, ToUtf8("ToDelete!"),
                [Role.AuthenticatedUser]);

            UserDb.DeleteUser(testUser);

            bool valid = UserDb.CheckCredentials(testUser, ToUtf8("ToDelete!"));
            Assert.That(valid, Is.False,
                "Deleted user should not be able to authenticate.");
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "015")]
        public void RemoveNonExistentUserReturnsFalse()
        {
            EnsureUserDatabase();

            bool result = UserDb.DeleteUser("nonexistent_user_xyz_12345");
            Assert.That(result, Is.False,
                "Deleting non-existent user should return false.");
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "016")]
        public void ChangePasswordSucceeds()
        {
            EnsureUserDatabase();

            string testUser = "testuser_cp_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                UserDb.CreateUser(
                    testUser, ToUtf8("OldPass!"),
                    [Role.AuthenticatedUser]);

                bool changed = UserDb.ChangePassword(
                    testUser, ToUtf8("OldPass!"), ToUtf8("NewPass!"));
                Assert.That(changed, Is.True,
                    "ChangePassword should return true.");
            }
            finally
            {
                UserDb.DeleteUser(testUser);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "017")]
        public void ChangePasswordVerifyOldNoLongerWorks()
        {
            EnsureUserDatabase();

            string testUser = "testuser_cp2_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                UserDb.CreateUser(
                    testUser, ToUtf8("OldPass!"),
                    [Role.AuthenticatedUser]);

                UserDb.ChangePassword(
                    testUser, ToUtf8("OldPass!"), ToUtf8("NewPass!"));

                Assert.That(
                    UserDb.CheckCredentials(testUser, ToUtf8("OldPass!")),
                    Is.False,
                    "Old password should no longer work.");
            }
            finally
            {
                UserDb.DeleteUser(testUser);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "018")]
        public void ChangePasswordVerifyNewWorks()
        {
            EnsureUserDatabase();

            string testUser = "testuser_cp3_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                UserDb.CreateUser(
                    testUser, ToUtf8("OldPass!"),
                    [Role.AuthenticatedUser]);

                UserDb.ChangePassword(
                    testUser, ToUtf8("OldPass!"), ToUtf8("NewPass!"));

                Assert.That(
                    UserDb.CheckCredentials(testUser, ToUtf8("NewPass!")),
                    Is.True,
                    "New password should work.");
            }
            finally
            {
                UserDb.DeleteUser(testUser);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "019")]
        public void ChangePasswordWithWrongOldPasswordFails()
        {
            EnsureUserDatabase();

            string testUser = "testuser_cp4_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                UserDb.CreateUser(
                    testUser, ToUtf8("CorrectPass!"),
                    [Role.AuthenticatedUser]);

                bool changed = UserDb.ChangePassword(
                    testUser, ToUtf8("WrongPass!"), ToUtf8("NewPass!"));
                Assert.That(changed, Is.False,
                    "ChangePassword with wrong old password should fail.");
            }
            finally
            {
                UserDb.DeleteUser(testUser);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "020")]
        public void ChangePasswordForNonExistentUserFails()
        {
            EnsureUserDatabase();

            bool changed = UserDb.ChangePassword(
                "nonexistent_cp_user", ToUtf8("old"), ToUtf8("new"));
            Assert.That(changed, Is.False,
                "ChangePassword for non-existent user should fail.");
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "021")]
        public async Task AddUserThenConnectWithNewCredentialsAsync()
        {
            EnsureUserDatabase();

            string testUser = "testuser_sess_" + Guid.NewGuid().ToString("N")[..8];
            const string testPassword = "SessionPass123!";
            try
            {
                UserDb.CreateUser(
                    testUser, ToUtf8(testPassword),
                    [Role.AuthenticatedUser]);

                using ISession session = await TryConnectAsUserAsync(
                    testUser, testPassword).ConfigureAwait(false);
                if (session == null)
                {
                    Assert.Fail(
                        "Username token not available on this endpoint.");
                }
                Assert.That(session.Connected, Is.True,
                    "Should connect with newly created user.");
            }
            finally
            {
                UserDb.DeleteUser(testUser);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "022")]
        public async Task RemoveUserThenConnectionFailsAsync()
        {
            EnsureUserDatabase();

            string testUser = "testuser_sess2_" + Guid.NewGuid().ToString("N")[..8];
            const string testPassword = "SessionPass123!";

            UserDb.CreateUser(
                testUser, ToUtf8(testPassword),
                [Role.AuthenticatedUser]);

            // Verify can connect first
            using (ISession session = await TryConnectAsUserAsync(
                testUser, testPassword).ConfigureAwait(false))
            {
                if (session == null)
                {
                    UserDb.DeleteUser(testUser);
                    Assert.Fail(
                        "Username token not available on this endpoint.");
                }
                Assert.That(session.Connected, Is.True);
            }

            // Delete and verify connection fails
            UserDb.DeleteUser(testUser);

            using ISession failSession = await TryConnectAsUserAsync(
                testUser, testPassword).ConfigureAwait(false);
            Assert.That(failSession, Is.Null,
                "Should not connect with deleted user.");
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "023")]
        public async Task ChangePasswordThenReconnectWithNewPasswordAsync()
        {
            EnsureUserDatabase();

            string testUser = "testuser_sess3_" + Guid.NewGuid().ToString("N")[..8];
            const string oldPassword = "OldSessionPass!";
            const string newPassword = "NewSessionPass!";
            try
            {
                UserDb.CreateUser(
                    testUser, ToUtf8(oldPassword),
                    [Role.AuthenticatedUser]);

                UserDb.ChangePassword(
                    testUser, ToUtf8(oldPassword), ToUtf8(newPassword));

                using ISession session = await TryConnectAsUserAsync(
                    testUser, newPassword).ConfigureAwait(false);
                if (session == null)
                {
                    Assert.Fail(
                        "Username token not available on this endpoint.");
                }
                Assert.That(session.Connected, Is.True,
                    "Should connect with new password.");
            }
            finally
            {
                UserDb.DeleteUser(testUser);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "024")]
        public async Task AddUserDisconnectReconnectAsync()
        {
            EnsureUserDatabase();

            string testUser = "testuser_recon_" + Guid.NewGuid().ToString("N")[..8];
            const string testPassword = "ReconPass!";
            try
            {
                UserDb.CreateUser(
                    testUser, ToUtf8(testPassword),
                    [Role.AuthenticatedUser]);

                // First connection
                ISession session1 = await TryConnectAsUserAsync(
                    testUser, testPassword).ConfigureAwait(false);
                if (session1 == null)
                {
                    Assert.Fail(
                        "Username token not available on this endpoint.");
                }
                Assert.That(session1.Connected, Is.True);
                await session1.CloseAsync(5000, true).ConfigureAwait(false);
                session1.Dispose();

                // Second connection
                using ISession session2 = await TryConnectAsUserAsync(
                    testUser, testPassword).ConfigureAwait(false);
                if (session2 == null)
                {
                    Assert.Fail(
                        "Username token not available on this endpoint.");
                }
                Assert.That(session2.Connected, Is.True,
                    "Should be able to reconnect after disconnect.");
            }
            finally
            {
                UserDb.DeleteUser(testUser);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "025")]
        public async Task AdminCanStillConnectAfterUserOperationsAsync()
        {
            EnsureUserDatabase();

            string testUser = "testuser_admin_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                // Perform some user operations
                UserDb.CreateUser(
                    testUser, ToUtf8("pass"),
                    [Role.AuthenticatedUser]);
                UserDb.DeleteUser(testUser);

                // Admin should still work
                using ISession session = await TryConnectAsAdminAsync()
                    .ConfigureAwait(false);
                if (session == null)
                {
                    Assert.Fail(
                        "Admin session not available on this endpoint.");
                }
                Assert.That(session.Connected, Is.True);
            }
            finally
            {
                UserDb.DeleteUser(testUser);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "026")]
        public void MultipleAddRemoveCycles()
        {
            EnsureUserDatabase();

            string testUser = "testuser_cycle_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    bool created = UserDb.CreateUser(
                        testUser, ToUtf8($"Pass{i}!"),
                        [Role.AuthenticatedUser]);
                    // First iteration is true (new), subsequent are false (update)
                    if (i == 0)
                    {
                        Assert.That(created, Is.True);
                    }

                    Assert.That(
                        UserDb.CheckCredentials(testUser, ToUtf8($"Pass{i}!")),
                        Is.True);

                    UserDb.DeleteUser(testUser);

                    Assert.That(
                        UserDb.CheckCredentials(testUser, ToUtf8($"Pass{i}!")),
                        Is.False);
                }
            }
            finally
            {
                UserDb.DeleteUser(testUser);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "027")]
        public void GetUserRolesForNonExistentUserThrows()
        {
            EnsureUserDatabase();

            Assert.Throws<ArgumentException>(
                () => UserDb.GetUserRoles("nonexistent_roles_xyz"));
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "028")]
        public void ChangePasswordWithEmptyOldPasswordThrows()
        {
            EnsureUserDatabase();

            string testUser = "testuser_emptyold_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                UserDb.CreateUser(
                    testUser, ToUtf8("Pass!"),
                    [Role.AuthenticatedUser]);

                Assert.Throws<ArgumentException>(
                    () => UserDb.ChangePassword(
                        testUser,
                        [],
                        ToUtf8("NewPass!")));
            }
            finally
            {
                UserDb.DeleteUser(testUser);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "029")]
        public void ChangePasswordWithEmptyNewPasswordThrows()
        {
            EnsureUserDatabase();

            string testUser = "testuser_emptynew_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                UserDb.CreateUser(
                    testUser, ToUtf8("Pass!"),
                    [Role.AuthenticatedUser]);

                Assert.Throws<ArgumentException>(
                    () => UserDb.ChangePassword(
                        testUser,
                        ToUtf8("Pass!"),
                        []));
            }
            finally
            {
                UserDb.DeleteUser(testUser);
            }
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "030")]
        public void CheckCredentialsWithWrongPasswordReturnsFalse()
        {
            EnsureUserDatabase();

            Assert.That(
                UserDb.CheckCredentials("sysadmin", ToUtf8("wrongpassword")),
                Is.False,
                "Wrong password should return false.");
        }

        [Test]
        [Property("ConformanceUnit", "Security User Management Server")]
        [Property("Tag", "031")]
        public void CheckCredentialsWithNonExistentUserReturnsFalse()
        {
            EnsureUserDatabase();

            Assert.That(
                UserDb.CheckCredentials(
                    "totally_nonexistent_user_xyz",
                    ToUtf8("anypass")),
                Is.False,
                "Non-existent user should return false.");
        }

        private void EnsureUserDatabase()
        {
            if (UserDb == null)
            {
                Assert.Ignore("UserDatabase is not available on this server.");
            }
        }

        private async Task<ISession> TryConnectAsAdminAsync()
        {
            try
            {
                return await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.None,
                        userIdentity: new UserIdentity("sysadmin", "demo"u8))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                return null;
            }
        }

        private async Task<ISession> TryConnectAsUserAsync(string userName, string password)
        {
            // Bypass the ClientFixture retry loop: this helper is invoked from tests
            // that intentionally use bad credentials and expect failure. Retrying 25 times
            // floods the server with bad auth attempts and triggers user-lockout (which
            // then cascades to BadUserAccessDenied in every subsequent test). Resolve the
            // endpoint once and call the non-retrying ConnectAsync overload.
            try
            {
                ConfiguredEndpoint endpoint = await ClientFixture
                    .GetEndpointAsync(ServerUrl, SecurityPolicies.None)
                    .ConfigureAwait(false);
                return await ClientFixture
                    .ConnectAsync(endpoint,
                        new UserIdentity(userName, Encoding.UTF8.GetBytes(password)))
                    .ConfigureAwait(false);
            }
            catch (ServiceResultException)
            {
                return null;
            }
        }

        private static byte[] ToUtf8(string s)
        {
            return Encoding.UTF8.GetBytes(s);
        }

        private IUserDatabase UserDb => ReferenceServer?.UserDatabase;
    }
}
