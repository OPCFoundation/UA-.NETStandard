using System;
using System.Collections.Generic;
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
        public void GetUserNamesReturnsCreatedUsers()
        {
            // Arrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser1", "PW"u8, [Role.AuthenticatedUser]);
            usersDb.CreateUser("TestUser2", "PW2"u8, [Role.Engineer]);

            // Act
            IReadOnlyList<string> userNames = usersDb.GetUserNames();

            // Assert
            Assert.That(userNames, Is.EquivalentTo(s_createdUsers));
        }
    }
}
