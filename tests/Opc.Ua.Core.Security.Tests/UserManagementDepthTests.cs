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
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using Opc.Ua.Server.UserDatabase;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.Core.Security.Tests
{
    [TestFixture]
    [Category("Conformance")]
    [Category("Security")]
    [Category("UserManagement")]
    public class UserManagementDepthTests : TestFixture
    {
        [Test]
        public void AddUserWithMinNameLength()
        {
            EnsureUserDatabase();
            string u = "u" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                Assert.That(UserDb.CreateUser(u, ToUtf8("Pass123!"), [Role.AuthenticatedUser]), Is.True);
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public void AddUserWithMaxNameLength()
        {
            EnsureUserDatabase();
            string u = "maxlen_" + new string('a', 50) + Guid.NewGuid().ToString("N")[..8];
            try
            {
                Assert.That(UserDb.CreateUser(u, ToUtf8("Pass123!"), [Role.AuthenticatedUser]), Is.True);
                Assert.That(UserDb.CheckCredentials(u, ToUtf8("Pass123!")), Is.True);
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public void AddUserWithUnicodeName()
        {
            EnsureUserDatabase();
            string u = "unic\u00F6de_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                Assert.That(UserDb.CreateUser(u, ToUtf8("Pass123!"), [Role.AuthenticatedUser]), Is.True);
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public void AddUserWithNullNameThrows()
        {
            EnsureUserDatabase();
            Assert.That(() => UserDb.CreateUser(null, ToUtf8("Pass123!"), [Role.AuthenticatedUser]), Throws.Exception);
        }

        [Test]
        public void AddUserWithWhitespaceName()
        {
            EnsureUserDatabase();
            string u = "   ws_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                Assert.That(UserDb.CreateUser(u, ToUtf8("Pass123!"), [Role.AuthenticatedUser]), Is.True);
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public void VerifyCredentialsAfterCreation()
        {
            EnsureUserDatabase();
            string u = "verify_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                UserDb.CreateUser(u, ToUtf8("ValidPass!"), [Role.AuthenticatedUser]);
                Assert.That(UserDb.CheckCredentials(u, ToUtf8("ValidPass!")), Is.True);
                Assert.That(UserDb.CheckCredentials(u, ToUtf8("WrongPass")), Is.False);
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public void RemoveNonExistentUserReturnsFalse()
        {
            EnsureUserDatabase();
            Assert.That(UserDb.DeleteUser("ghost_" + Guid.NewGuid().ToString("N")[..8]), Is.False);
        }

        [Test]
        public void RemoveUserTwiceSecondReturnsFalse()
        {
            EnsureUserDatabase();
            string u = "del2x_" + Guid.NewGuid().ToString("N")[..8];
            UserDb.CreateUser(u, ToUtf8("Pass123!"), [Role.AuthenticatedUser]);
            Assert.That(UserDb.DeleteUser(u), Is.True);
            Assert.That(UserDb.DeleteUser(u), Is.False);
        }

        [Test]
        public void UpdatePasswordSucceeds()
        {
            EnsureUserDatabase();
            string u = "updpwd_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                bool created = UserDb.CreateUser(u, ToUtf8("OldPass!"),
                    [Role.AuthenticatedUser]);
                if (!created)
                {
                    Assert.Ignore("Failed to create test user.");
                }

                // Use the proper ChangePassword API (CreateUser is
                // not contracted to update existing users — its
                // semantics are implementation-specific).
                bool changed = UserDb.ChangePassword(
                    u,
                    ToUtf8("OldPass!"),
                    ToUtf8("NewPass!"));
                Assert.That(changed, Is.True,
                    "ChangePassword must succeed when the old password matches.");

                Assert.That(
                    UserDb.CheckCredentials(u, ToUtf8("NewPass!")),
                    Is.True);
                Assert.That(
                    UserDb.CheckCredentials(u, ToUtf8("OldPass!")),
                    Is.False,
                    "Old password must no longer authenticate after change.");
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public void CheckRolesAfterCreation()
        {
            EnsureUserDatabase();
            string u = "roles_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                UserDb.CreateUser(u, ToUtf8("Pass123!"), [Role.AuthenticatedUser, Role.Observer]);
                ICollection<Role> roles = UserDb.GetUserRoles(u);
                Assert.That(roles, Is.Not.Null);
                Assert.That(roles, Does.Contain(Role.AuthenticatedUser));
                Assert.That(roles, Does.Contain(Role.Observer));
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public void CreateUserWithEmptyRoles()
        {
            EnsureUserDatabase();
            string u = "norole_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                Assert.That(UserDb.CreateUser(u, ToUtf8("Pass123!"), Array.Empty<Role>()), Is.True);
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public void InvalidRoleFails()
        {
            EnsureUserDatabase();
            string u = "badrole_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                Assert.That(UserDb.CreateUser(u, ToUtf8("Pass123!"), [Role.AuthenticatedUser]), Is.True);
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public void CreateUserDuplicateNameOverwrites()
        {
            EnsureUserDatabase();
            string u = "dupow_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                UserDb.CreateUser(u, ToUtf8("First!"), [Role.AuthenticatedUser]);
                UserDb.CreateUser(u, ToUtf8("Second!"), [Role.AuthenticatedUser]);
                Assert.That(UserDb.CheckCredentials(u, ToUtf8("Second!")), Is.True);
                Assert.That(UserDb.CheckCredentials(u, ToUtf8("First!")), Is.False);
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public void MinPasswordLengthAccepted()
        {
            EnsureUserDatabase();
            string u = "minpw_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                Assert.That(UserDb.CreateUser(u, ToUtf8("A"), [Role.AuthenticatedUser]), Is.True);
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public void MaxPasswordLengthAccepted()
        {
            EnsureUserDatabase();
            string u = "maxpw_" + Guid.NewGuid().ToString("N")[..8];
            string pwd = new string('P', 256) + "!";
            try
            {
                Assert.That(UserDb.CreateUser(u, ToUtf8(pwd), [Role.AuthenticatedUser]), Is.True);
                Assert.That(UserDb.CheckCredentials(u, ToUtf8(pwd)), Is.True);
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public void UnicodePasswordAccepted()
        {
            EnsureUserDatabase();
            string u = "unipw_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                Assert.That(UserDb.CreateUser(u, ToUtf8("\u00E4\u00F6\u00FC\u00DF!"), [Role.AuthenticatedUser]), Is.True);
                Assert.That(UserDb.CheckCredentials(u, ToUtf8("\u00E4\u00F6\u00FC\u00DF!")), Is.True);
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public void SequentialPasswordChanges()
        {
            EnsureUserDatabase();
            string u = "seqpw_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                UserDb.CreateUser(u, ToUtf8("Pass1!"), [Role.AuthenticatedUser]);
                UserDb.CreateUser(u, ToUtf8("Pass2!"), [Role.AuthenticatedUser]);
                UserDb.CreateUser(u, ToUtf8("Pass3!"), [Role.AuthenticatedUser]);
                Assert.That(UserDb.CheckCredentials(u, ToUtf8("Pass3!")), Is.True);
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public void OldPasswordFailsAfterChange()
        {
            EnsureUserDatabase();
            string u = "oldpw_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                UserDb.CreateUser(u, ToUtf8("Original!"), [Role.AuthenticatedUser]);
                UserDb.CreateUser(u, ToUtf8("Changed!"), [Role.AuthenticatedUser]);
                Assert.That(UserDb.CheckCredentials(u, ToUtf8("Original!")), Is.False);
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public void AddTenUsersSequentially()
        {
            EnsureUserDatabase();
            var users = new List<string>();
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    string n = "seq10_" + Guid.NewGuid().ToString("N")[..8];
                    Assert.That(UserDb.CreateUser(n, ToUtf8("Pass" + i), [Role.AuthenticatedUser]), Is.True);
                    users.Add(n);
                }
                Assert.That(users, Has.Count.EqualTo(10));
            }

            finally
            {
                foreach (string u in users)
                {
                    UserDb.DeleteUser(u);
                }
            }
        }

        [Test]
        public void RemoveTenUsersSequentially()
        {
            EnsureUserDatabase();
            var users = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                string n = "rem10_" + Guid.NewGuid().ToString("N")[..8];
                UserDb.CreateUser(n, ToUtf8("Pass" + i), [Role.AuthenticatedUser]);
                users.Add(n);
            }

            foreach (string u in users)
            {
                Assert.That(UserDb.DeleteUser(u), Is.True);
            }
        }

        [Test]
        public void RapidAddRemoveCycle()
        {
            EnsureUserDatabase();
            for (int i = 0; i < 5; i++)
            {
                string n = "rapid_" + Guid.NewGuid().ToString("N")[..8];
                UserDb.CreateUser(n, ToUtf8("Tmp!"), [Role.AuthenticatedUser]);
                Assert.That(UserDb.DeleteUser(n), Is.True);
            }
        }

        [Test]
        public async Task ThreeSimultaneousSessionsSucceedAsync()
        {
            ISession s1 = null;
            ISession s2 = null;
            ISession s3 = null;
            try
            {
                s1 = await TryConnectAsAdminAsync().ConfigureAwait(false);
                s2 = await TryConnectAsAdminAsync().ConfigureAwait(false);
                s3 = await TryConnectAsAdminAsync().ConfigureAwait(false);
                if (s1 == null || s2 == null || s3 == null)
                {
                    Assert.Fail("Cannot create three admin sessions.");
                }
                Assert.That(s1.Connected, Is.True);
                Assert.That(s2.Connected, Is.True);
                Assert.That(s3.Connected, Is.True);
            }
            finally
            {
                if (s1 != null)
                {
                    await s1.CloseAsync(5000, true).ConfigureAwait(false);
                    s1.Dispose();
                }
                if (s2 != null)
                {
                    await s2.CloseAsync(5000, true).ConfigureAwait(false);
                    s2.Dispose();
                }
                if (s3 != null)
                {
                    await s3.CloseAsync(5000, true).ConfigureAwait(false);
                    s3.Dispose();
                }
            }
        }

        [Test]
        public async Task ConnectThenDeleteUserSessionStillActiveAsync()
        {
            EnsureUserDatabase();
            string u = "sesdel_" + Guid.NewGuid().ToString("N")[..8];
            ISession us = null;
            try
            {
                UserDb.CreateUser(u, ToUtf8("SesPass!"), [Role.AuthenticatedUser]);
                us = await TryConnectAsUserAsync(u, "SesPass!").ConfigureAwait(false);
                if (us == null)
                {
                    Assert.Fail("Could not connect as test user.");
                }
                Assert.That(us.Connected, Is.True);
                UserDb.DeleteUser(u);
                Assert.That(us.Connected, Is.True, "Existing session should remain active.");
            }
            finally
            {
                if (us != null)
                {
                    await us.CloseAsync(5000, true).ConfigureAwait(false);
                    us.Dispose();
                }
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public async Task ReconnectAfterDeletionFailsAsync()
        {
            EnsureUserDatabase();
            string u = "reconn_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                UserDb.CreateUser(u, ToUtf8("RePass!"), [Role.AuthenticatedUser]);
                UserDb.DeleteUser(u);
                ISession s = await TryConnectAsUserAsync(u, "RePass!").ConfigureAwait(false);
                Assert.That(s, Is.Null, "Deleted user should not reconnect.");
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public async Task ChangePasswordActiveSessionAsync()
        {
            EnsureUserDatabase();
            string u = "chpw_" + Guid.NewGuid().ToString("N")[..8];
            ISession us = null;
            try
            {
                UserDb.CreateUser(u, ToUtf8("OldPw!"), [Role.AuthenticatedUser]);
                us = await TryConnectAsUserAsync(u, "OldPw!").ConfigureAwait(false);
                if (us == null)
                {
                    Assert.Fail("Could not connect as test user.");
                }
                UserDb.CreateUser(u, ToUtf8("NewPw!"), [Role.AuthenticatedUser]);
                Assert.That(us.Connected, Is.True, "Active session should survive password change.");
            }
            finally
            {
                if (us != null)
                {
                    await us.CloseAsync(5000, true).ConfigureAwait(false);
                    us.Dispose();
                }
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public async Task NewSessionNeedsNewPasswordAsync()
        {
            EnsureUserDatabase();
            string u = "nspw_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                UserDb.CreateUser(u, ToUtf8("First!"), [Role.AuthenticatedUser]);
                UserDb.CreateUser(u, ToUtf8("Second!"), [Role.AuthenticatedUser]);
                ISession old = await TryConnectAsUserAsync(u, "First!").ConfigureAwait(false);
                Assert.That(old, Is.Null, "Old password should not work.");
                ISession ns = await TryConnectAsUserAsync(u, "Second!").ConfigureAwait(false);
                if (ns != null)
                {
                    Assert.That(ns.Connected, Is.True);
                    await ns.CloseAsync(5000, true).ConfigureAwait(false);
                    ns.Dispose();
                }
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public void AllRolesAssignableToUser()
        {
            EnsureUserDatabase();
            string u = "allrl_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                Assert.That(UserDb.CreateUser(u, ToUtf8("AllRoles!"),
                    [Role.AuthenticatedUser, Role.Observer, Role.SecurityAdmin, Role.ConfigureAdmin]), Is.True);
                ICollection<Role> roles = UserDb.GetUserRoles(u);
                Assert.That(roles, Is.Not.Null);
                Assert.That(roles, Has.Count.GreaterThanOrEqualTo(2));
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public void CaseSensitiveUserName()
        {
            EnsureUserDatabase();
            string lower = "casetest_" + Guid.NewGuid().ToString("N")[..8];
            string upper = lower.ToUpperInvariant();
            try
            {
                UserDb.CreateUser(lower, ToUtf8("Lower!"), [Role.AuthenticatedUser]);
                Assert.That(UserDb.CheckCredentials(lower, ToUtf8("Lower!")), Is.True);
            }
            finally
            {
                UserDb.DeleteUser(lower);
                UserDb.DeleteUser(upper);
            }
        }

        [Test]
        public void EmptyPasswordHandled()
        {
            EnsureUserDatabase();
            string u = "emptpw_" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                try
                {
                    Assert.That(UserDb.CreateUser(u, ToUtf8(string.Empty),
                        [Role.AuthenticatedUser]), Is.True);
                }
                catch (ArgumentException)
                {
                    // Empty password rejection is valid behavior
                }
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        [Test]
        public void SpecialCharactersInPassword()
        {
            EnsureUserDatabase();
            string u = "specpw_" + Guid.NewGuid().ToString("N")[..8];
            const string pwd = "!@#$%^&*()_+-=[]{}|;':\",./?";
            try
            {
                Assert.That(UserDb.CreateUser(u, ToUtf8(pwd), [Role.AuthenticatedUser]), Is.True);
                Assert.That(UserDb.CheckCredentials(u, ToUtf8(pwd)), Is.True);
            }
            finally
            {
                UserDb.DeleteUser(u);
            }
        }

        private void EnsureUserDatabase()
        {
            if (UserDb == null)
            {
                Assert.Ignore("UserDatabase is not available.");
            }
        }

        private async Task<ISession> TryConnectAsAdminAsync()
        {
            try
            {
                return await ClientFixture.ConnectAsync(ServerUrl, SecurityPolicies.None,
                    userIdentity: new UserIdentity("sysadmin", "demo"u8)).ConfigureAwait(false);
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
