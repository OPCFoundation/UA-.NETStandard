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

#nullable enable

using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Server.UserManagement;
using UserManagementImpl = Opc.Ua.Server.UserManagement.UserManagement;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Unit tests for <see cref="Opc.Ua.Server.UserManagement.UserManagement"/>
    /// covering OPC UA Part 18 §5 compliance: spec result codes, password
    /// validation, self-modify rejection, MustChangePassword bit handling,
    /// and the UserDeactivated event.
    /// </summary>
    [TestFixture]
    [Category("Roles")]
    [Parallelizable]
    public class UserManagementTests
    {
        private static UserManagementImpl CreateManager()
        {
            return new UserManagementImpl(
                new LinqUserDatabase(),
                passwordLength: new Range { Low = 4, High = 64 });
        }

        [Test]
        public void AddUser_HappyPath_Succeeds()
        {
            using var um = CreateManager();
            ServiceResult result = um.AddUser("alice", "secret", UserConfigurationMask.None, "Tester");
            Assert.That(ServiceResult.IsGood(result), Is.True);

            IReadOnlyList<UserManagementDataType> users = um.SnapshotUsers();
            Assert.That(users, Has.Count.EqualTo(1));
            Assert.That(users[0].UserName, Is.EqualTo("alice"));
            Assert.That(users[0].Description, Is.EqualTo("Tester"));
        }

        [Test]
        public void AddUser_DuplicateUserName_ReturnsBadAlreadyExists()
        {
            using var um = CreateManager();
            Assert.That(ServiceResult.IsGood(
                um.AddUser("alice", "secret", UserConfigurationMask.None, string.Empty)), Is.True);
            ServiceResult duplicate = um.AddUser("alice", "secret2", UserConfigurationMask.None, string.Empty);
            Assert.That((StatusCode)duplicate.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadAlreadyExists));
        }

        [Test]
        public void AddUser_PasswordTooShort_ReturnsBadOutOfRange()
        {
            using var um = CreateManager();
            ServiceResult result = um.AddUser("alice", "ab", UserConfigurationMask.None, string.Empty);
            Assert.That((StatusCode)result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
        }

        [Test]
        public void AddUser_PasswordTooLong_ReturnsBadOutOfRange()
        {
            using var um = CreateManager();
            ServiceResult result = um.AddUser("alice", new string('a', 200),
                UserConfigurationMask.None, string.Empty);
            Assert.That((StatusCode)result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
        }

        [Test]
        public void AddUser_MustChangePasswordPlusNoChangeByUser_ReturnsBadConfigurationError()
        {
            using var um = CreateManager();
            ServiceResult result = um.AddUser("alice", "secret",
                UserConfigurationMask.MustChangePassword | UserConfigurationMask.NoChangeByUser,
                string.Empty);
            Assert.That((StatusCode)result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
        }

        [Test]
        public void RemoveUser_NonExistent_ReturnsBadNotFound()
        {
            using var um = CreateManager();
            ServiceResult result = um.RemoveUser("ghost", callingUserName: null);
            Assert.That((StatusCode)result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotFound));
        }

        [Test]
        public void RemoveUser_SelfReference_ReturnsBadInvalidSelfReference()
        {
            using var um = CreateManager();
            Assert.That(ServiceResult.IsGood(
                um.AddUser("alice", "secret", UserConfigurationMask.None, string.Empty)), Is.True);
            ServiceResult result = um.RemoveUser("alice", callingUserName: "alice");
            Assert.That((StatusCode)result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidSelfReference));
        }

        [Test]
        public void ModifyUser_DisableSelfReference_ReturnsBadInvalidSelfReference()
        {
            using var um = CreateManager();
            Assert.That(ServiceResult.IsGood(
                um.AddUser("alice", "secret", UserConfigurationMask.None, string.Empty)), Is.True);
            ServiceResult result = um.ModifyUser(
                "alice",
                modifyPassword: false,
                password: string.Empty,
                modifyUserConfiguration: true,
                userConfiguration: UserConfigurationMask.Disabled,
                modifyDescription: false,
                description: string.Empty,
                callingUserName: "alice");
            Assert.That((StatusCode)result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidSelfReference));
        }

        [Test]
        public void ModifyUser_DisableOther_RaisesUserDeactivatedEvent()
        {
            using var um = CreateManager();
            Assert.That(ServiceResult.IsGood(
                um.AddUser("alice", "secret", UserConfigurationMask.None, string.Empty)), Is.True);

            string? deactivated = null;
            um.UserDeactivated += (_, e) => deactivated = e.UserName;

            ServiceResult result = um.ModifyUser(
                "alice",
                modifyPassword: false,
                password: string.Empty,
                modifyUserConfiguration: true,
                userConfiguration: UserConfigurationMask.Disabled,
                modifyDescription: false,
                description: string.Empty,
                callingUserName: "admin");
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(deactivated, Is.EqualTo("alice"));
            Assert.That(um.IsUserActive("alice"), Is.False);
        }

        [Test]
        public void RemoveUser_RaisesUserDeactivatedEvent()
        {
            using var um = CreateManager();
            Assert.That(ServiceResult.IsGood(
                um.AddUser("alice", "secret", UserConfigurationMask.None, string.Empty)), Is.True);

            string? deactivated = null;
            um.UserDeactivated += (_, e) => deactivated = e.UserName;

            ServiceResult result = um.RemoveUser("alice", callingUserName: "admin");
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(deactivated, Is.EqualTo("alice"));
        }

        [Test]
        public void RemoveUser_NoDeleteFlag_ReturnsBadNotSupported()
        {
            using var um = new UserManagementImpl(
                new LinqUserDatabase(),
                passwordLength: new Range { Low = 4, High = 64 },
                passwordOptions: PasswordOptionsMask.SupportDisableUser
                    | PasswordOptionsMask.SupportDisableDeleteForUser
                    | PasswordOptionsMask.SupportInitialPasswordChange);

            Assert.That(ServiceResult.IsGood(
                um.AddUser("alice", "secret", UserConfigurationMask.NoDelete, string.Empty)), Is.True);
            ServiceResult result = um.RemoveUser("alice", callingUserName: "admin");
            Assert.That((StatusCode)result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
        }

        [Test]
        public void ModifyUser_AdminPasswordReset_PreservesUserRoles()
        {
            // Regression for the password-reset path that previously dropped
            // the user's roles by calling CreateUser with []. The fix
            // snapshots roles via IUserDatabase.GetUserRoles before delete
            // and re-applies them on recreate.
            var db = new LinqUserDatabase();
            db.CreateUser("alice", "secret"u8, [Role.SecurityAdmin, Role.AuthenticatedUser]);

            using var um = new Opc.Ua.Server.UserManagement.UserManagement(
                db, passwordLength: new Range { Low = 4, High = 64 });
            Assert.That(ServiceResult.IsGood(
                um.AddUser("bob", "secret", UserConfigurationMask.None, string.Empty)), Is.True);
            // Bob exists in the manager metadata as well as in the db (added
            // by AddUser); now layer in admin-assigned roles directly on the
            // db (typical pattern for integrators).
            db.DeleteUser("bob");
            db.CreateUser("bob", "secret"u8, [Role.Engineer]);

            ServiceResult result = um.ModifyUser(
                "bob",
                modifyPassword: true,
                password: "newsecret",
                modifyUserConfiguration: false,
                userConfiguration: UserConfigurationMask.None,
                modifyDescription: false,
                description: string.Empty,
                callingUserName: "admin");
            Assert.That(ServiceResult.IsGood(result), Is.True);

            // The Engineer role assignment must have survived the reset.
            ICollection<Role> rolesAfter = db.GetUserRoles("bob");
            Assert.That(rolesAfter, Has.Member(Role.Engineer),
                "Admin password reset should preserve previously assigned roles.");
            Assert.That(db.CheckCredentials("bob", "newsecret"u8), Is.True,
                "New password must be active after the reset.");
        }

        [Test]
        public void ChangePassword_HappyPath_Succeeds()
        {
            using var um = CreateManager();
            Assert.That(ServiceResult.IsGood(
                um.AddUser("alice", "oldpass", UserConfigurationMask.None, string.Empty)), Is.True);
            ServiceResult result = um.ChangePassword("alice", "oldpass", "newpass");
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void ChangePassword_SameAsOld_ReturnsBadAlreadyExists()
        {
            using var um = CreateManager();
            Assert.That(ServiceResult.IsGood(
                um.AddUser("alice", "secret", UserConfigurationMask.None, string.Empty)), Is.True);
            ServiceResult result = um.ChangePassword("alice", "secret", "secret");
            Assert.That((StatusCode)result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadAlreadyExists));
        }

        [Test]
        public void ChangePassword_WrongOldPassword_ReturnsBadIdentityTokenInvalid()
        {
            using var um = CreateManager();
            Assert.That(ServiceResult.IsGood(
                um.AddUser("alice", "secret", UserConfigurationMask.None, string.Empty)), Is.True);
            ServiceResult result = um.ChangePassword("alice", "wrong", "newpass");
            Assert.That((StatusCode)result.StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public void ChangePassword_ClearsMustChangePasswordBit()
        {
            using var um = CreateManager();
            Assert.That(ServiceResult.IsGood(um.AddUser("alice", "secret",
                UserConfigurationMask.MustChangePassword, string.Empty)), Is.True);
            Assert.That(um.MustChangePassword("alice"), Is.True);

            Assert.That(ServiceResult.IsGood(um.ChangePassword("alice", "secret", "newpass")), Is.True);
            Assert.That(um.MustChangePassword("alice"), Is.False);
        }

        [Test]
        public void ChangePassword_NoChangeByUser_ReturnsBadNotSupported()
        {
            using var um = new UserManagementImpl(
                new LinqUserDatabase(),
                passwordLength: new Range { Low = 4, High = 64 },
                passwordOptions: PasswordOptionsMask.SupportDisableUser
                    | PasswordOptionsMask.SupportNoChangeForUser
                    | PasswordOptionsMask.SupportInitialPasswordChange);

            Assert.That(ServiceResult.IsGood(um.AddUser("alice", "secret",
                UserConfigurationMask.NoChangeByUser, string.Empty)), Is.True);
            ServiceResult result = um.ChangePassword("alice", "secret", "newpass");
            Assert.That((StatusCode)result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotSupported));
        }

        [Test]
        public void IsUserActive_DisabledUser_ReturnsFalse()
        {
            using var um = CreateManager();
            Assert.That(ServiceResult.IsGood(um.AddUser("alice", "secret",
                UserConfigurationMask.Disabled, string.Empty)), Is.True);
            Assert.That(um.IsUserActive("alice"), Is.False);
        }

        [Test]
        public void IsUserActive_ActiveUser_ReturnsTrue()
        {
            using var um = CreateManager();
            Assert.That(ServiceResult.IsGood(um.AddUser("alice", "secret",
                UserConfigurationMask.None, string.Empty)), Is.True);
            Assert.That(um.IsUserActive("alice"), Is.True);
        }
    }
}
