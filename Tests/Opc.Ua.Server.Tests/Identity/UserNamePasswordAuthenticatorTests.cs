/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
 *
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

#nullable disable

using System.Text;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Server.UserDatabase;
using Opc.Ua.Tests;
using UserManagementFacade = Opc.Ua.Server.UserManagement.UserManagement;

namespace Opc.Ua.Server.Tests.Identity
{
    [TestFixture]
    [Category("Identity")]
    public class UserNamePasswordAuthenticatorTests
    {
        [Test]
        public async Task AuthenticateAsyncValidCredentialsAccepted()
        {
            UserNamePasswordAuthenticator authenticator = CreateAuthenticator(out _, out _);

            AuthenticationResult result = await authenticator.AuthenticateAsync(
                CreateContext(new UserNameIdentityTokenHandler("alice", Encoding.UTF8.GetBytes("password"))))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(result.Identity, Is.Not.Null);
            Assert.That(result.Identity.DisplayName, Is.EqualTo("alice"));
            Assert.That(result.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
        }

        [Test]
        public async Task AuthenticateAsyncEmptyUserNameRejectedAsInvalid()
        {
            UserNamePasswordAuthenticator authenticator = CreateAuthenticator(out _, out _);

            AuthenticationResult result = await authenticator.AuthenticateAsync(
                CreateContext(new UserNameIdentityTokenHandler(string.Empty, Encoding.UTF8.GetBytes("password"))))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task AuthenticateAsyncEmptyPasswordRejectedAsInvalid()
        {
            UserNamePasswordAuthenticator authenticator = CreateAuthenticator(out _, out _);

            AuthenticationResult result = await authenticator.AuthenticateAsync(
                CreateContext(new UserNameIdentityTokenHandler("alice", System.Array.Empty<byte>())))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task AuthenticateAsyncWrongPasswordRejectedAsAccessDenied()
        {
            UserNamePasswordAuthenticator authenticator = CreateAuthenticator(out _, out _);

            AuthenticationResult result = await authenticator.AuthenticateAsync(
                CreateContext(new UserNameIdentityTokenHandler("alice", Encoding.UTF8.GetBytes("wrongpass"))))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
            Assert.That(result.Error.Code, Is.EqualTo((uint)StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public async Task AuthenticateAsyncAnonymousTokenReturnsNotHandled()
        {
            UserNamePasswordAuthenticator authenticator = CreateAuthenticator(out _, out _);

            AuthenticationResult result = await authenticator.AuthenticateAsync(
                CreateContext(new AnonymousIdentityTokenHandler()))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.NotHandled));
        }

        [Test]
        public async Task AuthenticateAsyncMustChangePasswordAcceptedAndActivationStatusHonoured()
        {
            var userDatabase = new LinqUserDatabase();
            var userManagement = new UserManagementFacade(userDatabase);
            ServiceResult addResult = userManagement.AddUser(
                "alice",
                "password",
                UserConfigurationMask.MustChangePassword,
                string.Empty);
            Assert.That(ServiceResult.IsGood(addResult), Is.True);

            var authenticator = new UserNamePasswordAuthenticator(
                userDatabase,
                userManagement,
                NUnitTelemetryContext.Create());

            AuthenticationResult result = await authenticator.AuthenticateAsync(
                CreateContext(new UserNameIdentityTokenHandler("alice", Encoding.UTF8.GetBytes("password"))))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));

            var server = new Mock<IServerInternal>();
            server.Setup(s => s.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            server.Setup(s => s.UserManagement).Returns(userManagement);

            using var manager = new TestableSessionManager(server.Object, CreateConfiguration());
            ServiceResult activationStatus = manager.PublicComputeActivationStatus(result.Identity);

            Assert.That(activationStatus.Code, Is.EqualTo((uint)StatusCodes.GoodPasswordChangeRequired));
        }

        private static UserNamePasswordAuthenticator CreateAuthenticator(
            out LinqUserDatabase userDatabase,
            out UserManagementFacade userManagement)
        {
            userDatabase = new LinqUserDatabase();
            userManagement = new UserManagementFacade(userDatabase);
            ServiceResult addResult = userManagement.AddUser("alice", "password", 0, string.Empty);
            Assert.That(ServiceResult.IsGood(addResult), Is.True);
            return new UserNamePasswordAuthenticator(
                userDatabase,
                userManagement,
                NUnitTelemetryContext.Create());
        }

        private static AuthenticationContext CreateContext(IUserIdentityTokenHandler handler)
        {
            return new AuthenticationContext(
                handler,
                new UserTokenPolicy { TokenType = handler.TokenType, PolicyId = "policy" },
                new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
        }

        private static ApplicationConfiguration CreateConfiguration()
        {
            return new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MinSessionTimeout = 1000,
                    MaxSessionTimeout = 3600000,
                    MaxSessionCount = 100,
                    MaxRequestAge = 60000,
                    MaxBrowseContinuationPoints = 10,
                    MaxHistoryContinuationPoints = 10
                }
            };
        }
    }
}
