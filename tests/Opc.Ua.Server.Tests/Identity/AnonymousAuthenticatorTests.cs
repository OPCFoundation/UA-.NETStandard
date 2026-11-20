// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Identity
{
    [TestFixture]
    [Category("Identity")]
    public class AnonymousAuthenticatorTests
    {
        [Test]
        public async Task AuthenticateAsyncAnonymousTokenAccepted()
        {
            var authenticator = new AnonymousAuthenticator();

            AuthenticationResult result = await authenticator.AuthenticateAsync(
                CreateContext(new AnonymousIdentityTokenHandler()))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(result.Identity, Is.Not.Null);
            Assert.That(result.Identity.TokenType, Is.EqualTo(UserTokenType.Anonymous));
        }

        [Test]
        public async Task AuthenticateAsyncDifferentTokenReturnsNotHandled()
        {
            var authenticator = new AnonymousAuthenticator();

            AuthenticationResult result = await authenticator.AuthenticateAsync(
                CreateContext(new UserNameIdentityTokenHandler("alice", Encoding.UTF8.GetBytes("password"))))
                .ConfigureAwait(false);

            Assert.That(result.Outcome, Is.EqualTo(AuthenticationOutcome.NotHandled));
        }

        private static AuthenticationContext CreateContext(IUserIdentityTokenHandler handler)
        {
            return new AuthenticationContext(
                handler,
                new UserTokenPolicy { TokenType = handler.TokenType, PolicyId = "policy" },
                new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
        }
    }
}
