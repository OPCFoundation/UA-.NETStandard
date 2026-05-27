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
using System.Linq;
using NUnit.Framework;
using Opc.Ua.Server.UserDatabase;
using UserManagementImpl = Opc.Ua.Server.UserManagement.UserManagement;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Unit tests for <see cref="UserManagement.UserManagement"/>
    /// covering OPC UA Part 18 §5 compliance: spec result codes, password
    /// validation, self-modify rejection, MustChangePassword bit handling,
    /// and the UserDeactivated event.
    /// </summary>
    [TestFixture]
    [Category("Roles")]
    [Parallelizable]
    public class UserManagementTests
    {
        private static readonly string[] s_defaultUserNames = ["alice", "bob"];

        private static UserManagementImpl CreateManager()
        {
            return new UserManagementImpl(
                new LinqUserDatabase(),
                passwordLength: new Range { Low = 4, High = 64 });
        }

        [Test]
        public void AddUser_HappyPath_Succeeds()
        {
            using UserManagementImpl um = CreateManager();
            ServiceResult result = um.AddUser("alice", "secret", UserConfigurationMask.None, "Tester");
            Assert.That(ServiceResult.IsGood(result), Is.True);

            IReadOnlyList<UserManagementDataType> users = um.SnapshotUsers();
            Assert.That(users, Has.Count.EqualTo(1));
            Assert.That(users[0].UserName, Is.EqualTo("alice"));
            Assert.That(users[0].Description, Is.EqualTo("Tester"));
        }

        [Test]
        public void SnapshotUsers_StartsWithAllDatabaseUsers()
        {
            var database = new LinqUserDatabase();
            Assert.That(database.CreateUser("alice", "secret"u8, [Role.AuthenticatedUser]), Is.True);
            Assert.That(database.CreateUser("bob", "secret2"u8, [Role.AuthenticatedUser]), Is.True);

            using var um = new UserManagementImpl(database, passwordLength: new Range { Low = 4, High = 64 });

            IReadOnlyList<UserManagementDataType> users = um.SnapshotUsers();
            Assert.That(users.Select(u => u.UserName), Is.EquivalentTo(s_defaultUserNames));
            Assert.That(users.All(u => u.UserConfiguration == (uint)UserConfigurationMask.None), Is.True);
        }

        [Test]
        public void SnapshotUsers_StartsWithAllCustomDatabaseUsers()
        {
            var database = new TestUserDatabase(s_defaultUserNames);

            using var um = new UserManagementImpl(database, passwordLength: new Range { Low = 4, High = 64 });

            IReadOnlyList<UserManagementDataType> users = um.SnapshotUsers();
            Assert.That(users.Select(u => u.UserName), Is.EquivalentTo(s_defaultUserNames));
            Assert.That(users.All(u => u.UserConfiguration == (uint)UserConfigurationMask.None), Is.True);
        }

        [Test]
        public void AddUser_DuplicateUserName_ReturnsBadAlreadyExists()
        {
            using UserManagementImpl um = CreateManager();
            Assert.That(ServiceResult.IsGood(
                um.AddUser("alice", "secret", UserConfigurationMask.None, string.Empty)), Is.True);
            ServiceResult duplicate = um.AddUser("alice", "secret2", UserConfigurationMask.None, string.Empty);
            Assert.That(duplicate.StatusCode, Is.EqualTo(StatusCodes.BadAlreadyExists));
        }

        [Test]
        public void AddUser_PasswordTooShort_ReturnsBadOutOfRange()
        {
            using UserManagementImpl um = CreateManager();
            ServiceResult result = um.AddUser("alice", "ab", UserConfigurationMask.None, string.Empty);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        [Test]
        public void AddUser_PasswordTooLong_ReturnsBadOutOfRange()
        {
            using UserManagementImpl um = CreateManager();
            ServiceResult result = um.AddUser("alice", new string('a', 200),
                UserConfigurationMask.None, string.Empty);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        [Test]
        public void AddUser_MustChangePasswordPlusNoChangeByUser_ReturnsBadConfigurationError()
        {
            using UserManagementImpl um = CreateManager();
            ServiceResult result = um.AddUser("alice", "secret",
                UserConfigurationMask.MustChangePassword | UserConfigurationMask.NoChangeByUser,
                string.Empty);
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public void RemoveUser_NonExistent_ReturnsBadNotFound()
        {
            using UserManagementImpl um = CreateManager();
            ServiceResult result = um.RemoveUser("ghost", callingUserName: null);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public void RemoveUser_SelfReference_ReturnsBadInvalidSelfReference()
        {
            using UserManagementImpl um = CreateManager();
            Assert.That(ServiceResult.IsGood(
                um.AddUser("alice", "secret", UserConfigurationMask.None, string.Empty)), Is.True);
            ServiceResult result = um.RemoveUser("alice", callingUserName: "alice");
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidSelfReference));
        }

        [Test]
        public void ModifyUser_DisableSelfReference_ReturnsBadInvalidSelfReference()
        {
            using UserManagementImpl um = CreateManager();
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
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadInvalidSelfReference));
        }

        [Test]
        public void ModifyUser_DisableOther_RaisesUserDeactivatedEvent()
        {
            using UserManagementImpl um = CreateManager();
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
            using UserManagementImpl um = CreateManager();
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
                passwordOptions: PasswordOptionsMask.SupportDisableUser |
                    PasswordOptionsMask.SupportDisableDeleteForUser |
                    PasswordOptionsMask.SupportInitialPasswordChange);

            Assert.That(ServiceResult.IsGood(
                um.AddUser("alice", "secret", UserConfigurationMask.NoDelete, string.Empty)), Is.True);
            ServiceResult result = um.RemoveUser("alice", callingUserName: "admin");
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
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

            using var um = new UserManagementImpl(
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
            using UserManagementImpl um = CreateManager();
            Assert.That(ServiceResult.IsGood(
                um.AddUser("alice", "oldpass", UserConfigurationMask.None, string.Empty)), Is.True);
            ServiceResult result = um.ChangePassword("alice", "oldpass", "newpass");
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void ChangePassword_SameAsOld_ReturnsBadAlreadyExists()
        {
            using UserManagementImpl um = CreateManager();
            Assert.That(ServiceResult.IsGood(
                um.AddUser("alice", "secret", UserConfigurationMask.None, string.Empty)), Is.True);
            ServiceResult result = um.ChangePassword("alice", "secret", "secret");
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadAlreadyExists));
        }

        [Test]
        public void ChangePassword_WrongOldPassword_ReturnsBadIdentityTokenInvalid()
        {
            using UserManagementImpl um = CreateManager();
            Assert.That(ServiceResult.IsGood(
                um.AddUser("alice", "secret", UserConfigurationMask.None, string.Empty)), Is.True);
            ServiceResult result = um.ChangePassword("alice", "wrong", "newpass");
            Assert.That(result.StatusCode,
                Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public void ChangePassword_ClearsMustChangePasswordBit()
        {
            using UserManagementImpl um = CreateManager();
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
                passwordOptions: PasswordOptionsMask.SupportDisableUser |
                    PasswordOptionsMask.SupportNoChangeForUser |
                    PasswordOptionsMask.SupportInitialPasswordChange);

            Assert.That(ServiceResult.IsGood(um.AddUser("alice", "secret",
                UserConfigurationMask.NoChangeByUser, string.Empty)), Is.True);
            ServiceResult result = um.ChangePassword("alice", "secret", "newpass");
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
        }

        [Test]
        public void IsUserActive_DisabledUser_ReturnsFalse()
        {
            using UserManagementImpl um = CreateManager();
            Assert.That(ServiceResult.IsGood(um.AddUser("alice", "secret",
                UserConfigurationMask.Disabled, string.Empty)), Is.True);
            Assert.That(um.IsUserActive("alice"), Is.False);
        }

        [Test]
        public void IsUserActive_ActiveUser_ReturnsTrue()
        {
            using UserManagementImpl um = CreateManager();
            Assert.That(ServiceResult.IsGood(um.AddUser("alice", "secret",
                UserConfigurationMask.None, string.Empty)), Is.True);
            Assert.That(um.IsUserActive("alice"), Is.True);
        }

        // ----------------------------------------------------------------
        // Gap 11: Password length exact boundaries
        // ----------------------------------------------------------------

        [TestCase(3, ExpectedResult = false, TestName = "Password just below min length is rejected")]
        [TestCase(4, ExpectedResult = true, TestName = "Password exactly at min length is accepted")]
        [TestCase(5, ExpectedResult = true, TestName = "Password just above min length is accepted")]
        public bool AddUser_PasswordLengthAtLowerBoundary_RespectsRange(int length)
        {
            using var um = new UserManagementImpl(
                new LinqUserDatabase(),
                passwordLength: new Range { Low = 4, High = 64 });
            ServiceResult result = um.AddUser("alice", new string('a', length),
                UserConfigurationMask.None, string.Empty);
            return ServiceResult.IsGood(result);
        }

        [TestCase(63, ExpectedResult = true, TestName = "Password just below max length is accepted")]
        [TestCase(64, ExpectedResult = true, TestName = "Password exactly at max length is accepted")]
        [TestCase(65, ExpectedResult = false, TestName = "Password just above max length is rejected")]
        public bool AddUser_PasswordLengthAtUpperBoundary_RespectsRange(int length)
        {
            using var um = new UserManagementImpl(
                new LinqUserDatabase(),
                passwordLength: new Range { Low = 4, High = 64 });
            ServiceResult result = um.AddUser("alice", new string('a', length),
                UserConfigurationMask.None, string.Empty);
            return ServiceResult.IsGood(result);
        }

        // ----------------------------------------------------------------
        // Gap 10: Password complexity (Upper / Lower / Digit / Special)
        // ----------------------------------------------------------------

        [Test]
        public void AddUser_RequiresUpperCase_PasswordWithoutUpper_ReturnsBadOutOfRange()
        {
            using var um = new UserManagementImpl(
                new LinqUserDatabase(),
                passwordLength: new Range { Low = 4, High = 64 },
                passwordOptions: PasswordOptionsMask.RequiresUpperCaseCharacters);
            ServiceResult result = um.AddUser("alice", "alllower1!",
                UserConfigurationMask.None, string.Empty);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        [Test]
        public void AddUser_RequiresUpperCase_PasswordWithUpper_Succeeds()
        {
            using var um = new UserManagementImpl(
                new LinqUserDatabase(),
                passwordLength: new Range { Low = 4, High = 64 },
                passwordOptions: PasswordOptionsMask.RequiresUpperCaseCharacters);
            ServiceResult result = um.AddUser("alice", "HasUpper",
                UserConfigurationMask.None, string.Empty);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void AddUser_RequiresLowerCase_PasswordWithoutLower_ReturnsBadOutOfRange()
        {
            using var um = new UserManagementImpl(
                new LinqUserDatabase(),
                passwordLength: new Range { Low = 4, High = 64 },
                passwordOptions: PasswordOptionsMask.RequiresLowerCaseCharacters);
            ServiceResult result = um.AddUser("alice", "ALLUPPER1!",
                UserConfigurationMask.None, string.Empty);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        [Test]
        public void AddUser_RequiresLowerCase_PasswordWithLower_Succeeds()
        {
            using var um = new UserManagementImpl(
                new LinqUserDatabase(),
                passwordLength: new Range { Low = 4, High = 64 },
                passwordOptions: PasswordOptionsMask.RequiresLowerCaseCharacters);
            ServiceResult result = um.AddUser("alice", "haslower",
                UserConfigurationMask.None, string.Empty);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void AddUser_RequiresDigit_PasswordWithoutDigit_ReturnsBadOutOfRange()
        {
            using var um = new UserManagementImpl(
                new LinqUserDatabase(),
                passwordLength: new Range { Low = 4, High = 64 },
                passwordOptions: PasswordOptionsMask.RequiresDigitCharacters);
            ServiceResult result = um.AddUser("alice", "NoDigits",
                UserConfigurationMask.None, string.Empty);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        [Test]
        public void AddUser_RequiresDigit_PasswordWithDigit_Succeeds()
        {
            using var um = new UserManagementImpl(
                new LinqUserDatabase(),
                passwordLength: new Range { Low = 4, High = 64 },
                passwordOptions: PasswordOptionsMask.RequiresDigitCharacters);
            ServiceResult result = um.AddUser("alice", "HasDigit1",
                UserConfigurationMask.None, string.Empty);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        [Test]
        public void AddUser_RequiresSpecial_PasswordWithoutSpecial_ReturnsBadOutOfRange()
        {
            using var um = new UserManagementImpl(
                new LinqUserDatabase(),
                passwordLength: new Range { Low = 4, High = 64 },
                passwordOptions: PasswordOptionsMask.RequiresSpecialCharacters);
            ServiceResult result = um.AddUser("alice", "OnlyAlphaNumeric1",
                UserConfigurationMask.None, string.Empty);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
        }

        [Test]
        public void AddUser_RequiresSpecial_PasswordWithSpecial_Succeeds()
        {
            using var um = new UserManagementImpl(
                new LinqUserDatabase(),
                passwordLength: new Range { Low = 4, High = 64 },
                passwordOptions: PasswordOptionsMask.RequiresSpecialCharacters);
            ServiceResult result = um.AddUser("alice", "Has!Special",
                UserConfigurationMask.None, string.Empty);
            Assert.That(ServiceResult.IsGood(result), Is.True);
        }

        private sealed class TestUserDatabase : IUserDatabase
        {
            private readonly IReadOnlyList<string> m_userNames;

            public TestUserDatabase(IReadOnlyList<string> userNames)
            {
                m_userNames = userNames;
            }

            public bool CreateUser(string userName, System.ReadOnlySpan<byte> password, ICollection<Role> roles)
            {
                throw new System.NotSupportedException();
            }

            public bool DeleteUser(string userName)
            {
                throw new System.NotSupportedException();
            }

            public bool CheckCredentials(string userName, System.ReadOnlySpan<byte> password)
            {
                throw new System.NotSupportedException();
            }

            public ICollection<Role> GetUserRoles(string userName)
            {
                throw new System.NotSupportedException();
            }

            public IReadOnlyList<string> GetUserNames()
            {
                return m_userNames;
            }

            public bool ChangePassword(
                string userName,
                System.ReadOnlySpan<byte> oldPassword,
                System.ReadOnlySpan<byte> newPassword)
            {
                throw new System.NotSupportedException();
            }
        }
    }
}
