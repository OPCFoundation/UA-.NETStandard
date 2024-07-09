using System;
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.UserDatabase;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Server.Tests
{
    [TestFixture, Category("Server")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    internal class LinqUserDatabaseTests
    {
        #region Test Methods

        [Test]
        public void CreateInvalidUser()
        {
            //Arrrange
            var usersDb = new LinqUserDatabase();

            //Act+ Assert
            Assert.Throws<ArgumentException>(
            () => usersDb.CreateUser("", "PW", new List<Role> { Role.AuthenticatedUser }));
            Assert.Throws<ArgumentException>(
            () => usersDb.CreateUser("Name", "", new List<Role> { Role.AuthenticatedUser }));
        }

        [Test]
        public void DeleteExistingUser()
        {
            //Arrrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser", "PW", new List<Role> { Role.AuthenticatedUser });
            //Act
            var result = usersDb.DeleteUser("TestUser");
            //Assert
            Assert.True(result);
        }

        [Test]
        public void DeleteNonExistingUser()
        {
            //Arrrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser", "PW", new List<Role> { Role.AuthenticatedUser });
            //Act
            var result = usersDb.DeleteUser("NoTestUser");
            //Assert
            Assert.False(result);
        }

        [Test]
        public void ChangePwOfExistingUser()
        {
            //Arrrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser", "PW", new List<Role> { Role.AuthenticatedUser });
            //Act
            var result = usersDb.ChangePassword("TestUser", "PW", "newPW");
            var login = usersDb.CheckCredentials("TestUser", "newPW");
            var loginOldPW = usersDb.CheckCredentials("TestUser", "PW");
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
            usersDb.CreateUser("TestUser", "PW", new List<Role> { Role.AuthenticatedUser });
            //Act
            var result = usersDb.DeleteUser("NoTestUser");
            //Assert
            Assert.False(result);
        }

        [Test]
        public void CheckPWofExistingUser()
        {
            //Arrrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser", "PW", new List<Role> { Role.AuthenticatedUser });
            //Act
            var result = usersDb.CheckCredentials("TestUser", "PW");
            var loginWrongPw = usersDb.CheckCredentials("TestUser", "newPW");
            //Assert
            Assert.True(result);
            Assert.False(loginWrongPw);
        }

        [Test]
        public void CheckPWofNonExistingUser()
        {
            //Arrrange
            var usersDb = new LinqUserDatabase();
            usersDb.CreateUser("TestUser", "PW", new List<Role> { Role.AuthenticatedUser });
            //Act
            var result = usersDb.CheckCredentials("NoTestUser", "PW");
            //Assert
            Assert.False(result);
        }
        #endregion
    }
}
