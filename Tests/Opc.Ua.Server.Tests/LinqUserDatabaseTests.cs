using System;
using NUnit.Framework;
using Opc.Ua.Server.UserDatabase;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    internal class LinqUserDatabaseTests
    {
        [Test]
        public void CreateInvalidUser()
        {
            //Arrrange
            var usersDb = new LinqUserDatabase();

            //Act+ Assert
            NUnit.Framework.Assert.Throws<ArgumentException>(() =>
                usersDb.CreateUser(string.Empty, "PW", [Role.AuthenticatedUser]));
            NUnit.Framework.Assert.Throws<ArgumentException>(() =>
                usersDb.CreateUser("Name", string.Empty, [Role.AuthenticatedUser]));
        }

        [Test]
        public void DeleteExistingUser()
        {
            //Arrrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser", "PW", [Role.AuthenticatedUser]);
            //Act
            bool result = usersDb.DeleteUser("TestUser");
            //Assert
            Assert.True(result);
        }

        [Test]
        public void DeleteNonExistingUser()
        {
            //Arrrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser", "PW", [Role.AuthenticatedUser]);
            //Act
            bool result = usersDb.DeleteUser("NoTestUser");
            //Assert
            Assert.False(result);
        }

        [Test]
        public void ChangePwOfExistingUser()
        {
            //Arrrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser", "PW", [Role.AuthenticatedUser]);
            //Act
            bool result = usersDb.ChangePassword("TestUser", "PW", "newPW");
            bool login = usersDb.CheckCredentials("TestUser", "newPW");
            bool loginOldPW = usersDb.CheckCredentials("TestUser", "PW");
            //Assert
            Assert.True(result);
            Assert.True(login);
            Assert.False(loginOldPW);
        }

        [Test]
        public void ChangePwOfNonExistingUser()
        {
            //Arrrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser", "PW", [Role.AuthenticatedUser]);
            //Act
            bool result = usersDb.DeleteUser("NoTestUser");
            //Assert
            Assert.False(result);
        }

        [Test]
        public void CheckPWofExistingUser()
        {
            //Arrrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser", "PW", [Role.AuthenticatedUser]);
            //Act
            bool result = usersDb.CheckCredentials("TestUser", "PW");
            bool loginWrongPw = usersDb.CheckCredentials("TestUser", "newPW");
            //Assert
            Assert.True(result);
            Assert.False(loginWrongPw);
        }

        [Test]
        public void CheckPWofNonExistingUser()
        {
            //Arrrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser", "PW", [Role.AuthenticatedUser]);
            //Act
            bool result = usersDb.CheckCredentials("NoTestUser", "PW");
            //Assert
            Assert.False(result);
        }
    }
}
