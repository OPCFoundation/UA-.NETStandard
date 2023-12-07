using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Database.Linq;

namespace Opc.Ua.Gds.Tests
{
    [TestFixture, Category("GDS")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    internal class LinqUsersDatabaseTests
    {
        #region Test Methods

        [Test]
        public void CreateInvalidUser()
        {
            //Arrrange
            var usersDb = new LinQUsersDatabase();
            
            //Act+ Assert
            Assert.Throws<ArgumentException>(
            () => usersDb.CreateUser("", "PW", Server.GdsRole.ApplicationAdmin));
            Assert.Throws<ArgumentException>(
            () => usersDb.CreateUser("Name", "", Server.GdsRole.ApplicationAdmin));
        }

        [Test]
        public void DeleteExistingUser()
        {
            //Arrrange
            var usersDb = new LinQUsersDatabase();
            usersDb.CreateUser("TestUser", "PW", Server.GdsRole.ApplicationAdmin);
            //Act
            var result = usersDb.DeleteUser("TestUser");
            //Assert
            Assert.True(result);
        }

        [Test]
        public void DeleteNonExistingUser()
        {
            //Arrrange
            var usersDb = new LinQUsersDatabase();
            usersDb.CreateUser("TestUser", "PW", Server.GdsRole.ApplicationAdmin);
            //Act
            var result = usersDb.DeleteUser("NoTestUser");
            //Assert
            Assert.False(result);
        }

        [Test]
        public void ChangePwOfExistingUser()
        {
            //Arrrange
            var usersDb = new LinQUsersDatabase();
            usersDb.CreateUser("TestUser", "PW", Server.GdsRole.ApplicationAdmin);
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
            var usersDb = new LinQUsersDatabase();
            usersDb.CreateUser("TestUser", "PW", Server.GdsRole.ApplicationAdmin);
            //Act
            var result = usersDb.DeleteUser("NoTestUser");
            //Assert
            Assert.False(result);
        }

        [Test]
        public void CheckPWofExistingUser()
        {
            //Arrrange
            var usersDb = new LinQUsersDatabase();
            usersDb.CreateUser("TestUser", "PW", Server.GdsRole.ApplicationAdmin);
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
            var usersDb = new LinQUsersDatabase();
            usersDb.CreateUser("TestUser", "PW", Server.GdsRole.ApplicationAdmin);
            //Act
            var result = usersDb.CheckCredentials("NoTestUser", "PW");
            //Assert
            Assert.False(result);
        }
        #endregion
    }
}
