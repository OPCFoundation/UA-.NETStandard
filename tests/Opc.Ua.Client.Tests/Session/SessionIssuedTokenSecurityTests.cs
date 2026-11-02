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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Identity;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [Category("Session")]
    [Category("Security")]
    public sealed class SessionIssuedTokenSecurityTests
    {
        [TestCase(MessageSecurityMode.None, SecurityPolicies.None)]
        [TestCase(MessageSecurityMode.Sign, SecurityPolicies.None)]
        [TestCase(MessageSecurityMode.None, "None")]
        [TestCase(MessageSecurityMode.Sign, "None")]
        public void ValidateIssuedTokenPolicyRejectsNonePolicyWithoutSignAndEncrypt(
            MessageSecurityMode channelSecurityMode,
            string tokenSecurityPolicy)
        {
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                Session.ValidateIssuedTokenPolicy(
                    CreateIdentity(Profiles.JwtUserToken, "issued"),
                    CreatePolicy(tokenSecurityPolicy),
                    SecurityPolicies.None,
                    channelSecurityMode,
                    SecurityPolicies.Default))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void ValidateIssuedTokenPolicyRejectsInheritedNonePolicy()
        {
            UserTokenPolicy policy = CreatePolicy(securityPolicyUri: null);

            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                Session.ValidateIssuedTokenPolicy(
                    CreateIdentity(Profiles.JwtUserToken, "issued"),
                    policy,
                    SecurityPolicies.None,
                    MessageSecurityMode.Sign,
                    SecurityPolicies.Default))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [TestCase(MessageSecurityMode.None)]
        [TestCase(MessageSecurityMode.Sign)]
        public void ValidateIssuedTokenPolicyAllowsSupportedEncryptionOnUnsecuredChannel(
            MessageSecurityMode channelSecurityMode)
        {
            string effectivePolicyUri = Session.ValidateIssuedTokenPolicy(
                CreateIdentity(Profiles.JwtUserToken, "issued"),
                CreatePolicy(SecurityPolicies.Basic256Sha256),
                SecurityPolicies.None,
                channelSecurityMode,
                SecurityPolicies.Default);

            Assert.That(effectivePolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
        }

        [TestCase(SecurityPolicies.None)]
        [TestCase("None")]
        public void ValidateIssuedTokenPolicyAllowsNoneOnSignAndEncryptChannel(string tokenSecurityPolicy)
        {
            string effectivePolicyUri = Session.ValidateIssuedTokenPolicy(
                CreateIdentity(Profiles.JwtUserToken, "issued"),
                CreatePolicy(tokenSecurityPolicy),
                SecurityPolicies.None,
                MessageSecurityMode.SignAndEncrypt,
                SecurityPolicies.Default);

            Assert.That(effectivePolicyUri, Is.EqualTo(tokenSecurityPolicy));
        }

        [Test]
        public void ValidateIssuedTokenPolicyRejectsUnknownTokenSecurityPolicy()
        {
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                Session.ValidateIssuedTokenPolicy(
                    CreateIdentity(Profiles.JwtUserToken, "issued"),
                    CreatePolicy("urn:test:unknown-security-policy"),
                    SecurityPolicies.None,
                    MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicies.Default))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void ValidateIssuedTokenPolicyRejectsChangedIssuedTokenProfile()
        {
            ServiceResultException exception = Assert.Throws<ServiceResultException>(() =>
                Session.ValidateIssuedTokenPolicy(
                    CreateIdentity(Profiles.JwtUserToken, "issued"),
                    CreatePolicy(SecurityPolicies.None, Namespaces.OpcUa + "UserToken#SAML"),
                    SecurityPolicies.None,
                    MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicies.Default))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
        }

        [Test]
        public async Task OpenAsyncRejectsUnencryptedIssuedTokenBeforeCreateSession()
        {
            using SessionMock session = CreateSession(CreateEndpoint(
                MessageSecurityMode.None,
                SecurityPolicies.None));

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await session.OpenAsync(
                    "issued-token",
                    60000,
                    CreateIdentity(Profiles.JwtUserToken, "issued"),
                    default,
                    false,
                    false,
                    CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            session.Channel.Verify(channel => channel.SendRequestAsync(
                It.IsAny<CreateSessionRequest>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task UpdateIdentityAsyncRejectsUnencryptedIssuedTokenBeforeActivation()
        {
            using SessionMock session = CreateSession(CreateEndpoint(
                MessageSecurityMode.None,
                SecurityPolicies.None));
            session.SetConnected();

            var provider = new Mock<IClientIdentityProvider>();
            provider.SetupGet(value => value.SupportedTokenTypes)
                .Returns([UserTokenType.IssuedToken]);
            provider.SetupGet(value => value.SupportedIssuedTokenProfileUris)
                .Returns([Profiles.JwtUserToken]);
            provider.SetupGet(value => value.ExpiresAt).Returns(DateTime.MaxValue);
            provider.Setup(value => value.CanSatisfyAsync(
                    It.IsAny<UserTokenPolicy>(),
                    It.IsAny<IdentitySelectionContext>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CanSatisfyResult>(CanSatisfyResult.Yes));
            provider.Setup(value => value.GetIdentityAsync(
                    It.IsAny<UserTokenPolicy>(),
                    It.IsAny<IdentitySelectionContext>(),
                    It.IsAny<CancellationToken>()))
                .Returns((UserTokenPolicy policy, IdentitySelectionContext _, CancellationToken _) =>
                {
                    IUserIdentity identity = CreateIdentity(Profiles.JwtUserToken, policy.PolicyId!);
                    return new ValueTask<IUserIdentity>(identity);
                });

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await session.UpdateIdentityAsync(provider.Object).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            session.Channel.Verify(channel => channel.SendRequestAsync(
                It.IsAny<ActivateSessionRequest>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ReactivationRejectsChangedIssuedTokenProfileBeforeActivation()
        {
            using SessionMock session = CreateSession(CreateEndpoint(
                MessageSecurityMode.None,
                SecurityPolicies.None,
                SecurityPolicies.Basic256Sha256,
                Namespaces.OpcUa + "UserToken#SAML"));
            session.SetConnected();

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await session.UpdateSessionAsync(
                    CreateIdentity(Profiles.JwtUserToken, "issued"),
                    default,
                    CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadIdentityTokenInvalid));
            session.Channel.Verify(channel => channel.SendRequestAsync(
                It.IsAny<ActivateSessionRequest>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ReactivationRejectsDowngradedInheritedPolicyBeforeActivation()
        {
            using SessionMock session = CreateSession(CreateEndpoint(
                MessageSecurityMode.None,
                SecurityPolicies.None,
                tokenSecurityPolicyUri: null));
            session.SetConnected();

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await session.UpdateSessionAsync(
                    CreateIdentity(Profiles.JwtUserToken, "issued"),
                    default,
                    CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false))!;

            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            session.Channel.Verify(channel => channel.SendRequestAsync(
                It.IsAny<ActivateSessionRequest>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        private static SessionMock CreateSession(EndpointDescription endpoint)
        {
            return SessionMock.Create(endpoint);
        }

        private static UserIdentity CreateIdentity(string profileUri, string policyId)
        {
            return new UserIdentity([1], profileUri)
            {
                PolicyId = policyId
            };
        }

        private static UserTokenPolicy CreatePolicy(
            string? securityPolicyUri,
            string issuedTokenType = Profiles.JwtUserToken)
        {
            return new UserTokenPolicy
            {
                PolicyId = "issued",
                TokenType = UserTokenType.IssuedToken,
                IssuedTokenType = issuedTokenType,
                SecurityPolicyUri = securityPolicyUri
            };
        }

        private static EndpointDescription CreateEndpoint(
            MessageSecurityMode securityMode,
            string securityPolicyUri,
            string? tokenSecurityPolicyUri = SecurityPolicies.None,
            string issuedTokenType = Profiles.JwtUserToken)
        {
            return new EndpointDescription
            {
                SecurityMode = securityMode,
                SecurityPolicyUri = securityPolicyUri,
                EndpointUrl = "opc.tcp://localhost:4840",
                UserIdentityTokens =
                [
                    CreatePolicy(tokenSecurityPolicyUri, issuedTokenType)
                ]
            };
        }
    }
}
