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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Client.Tests
{
    [TestFixture]
    [Category("Client")]
    [Category("Session")]
    public sealed class SessionIdentityReviewTests : ClientTestFramework
    {
        public SessionIdentityReviewTests()
            : base(Utils.UriSchemeOpcTcp)
        {
            SingleSession = false;
        }

        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            return base.OneTimeSetUpAsync();
        }

        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UrlOnlyProviderUsesRefreshedEndpointAndSecurityContextAsync(bool managedChannel)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var policies = new SecurityPolicies(Telemetry);
            await using var manager = new ClientChannelManager(
                ClientFixture.Config, Telemetry, securityPolicies: policies);
            var provider = new Mock<IClientIdentityProvider>();
            provider.SetupGet(value => value.ExpiresAt).Returns(DateTime.MaxValue);
            provider.Setup(value => value.CanSatisfyAsync(
                    It.IsAny<UserTokenPolicy>(),
                    It.IsAny<IdentitySelectionContext>(),
                    It.IsAny<CancellationToken>()))
                .Returns((UserTokenPolicy policy, IdentitySelectionContext _, CancellationToken _) =>
                    new ValueTask<CanSatisfyResult>(policy.TokenType == UserTokenType.UserName
                        ? CanSatisfyResult.Yes
                        : CanSatisfyResult.No("UserName required.")));
            provider.Setup(value => value.GetIdentityAsync(
                    It.IsAny<UserTokenPolicy>(),
                    It.IsAny<IdentitySelectionContext>(),
                    It.IsAny<CancellationToken>()))
                .Returns((UserTokenPolicy _, IdentitySelectionContext context, CancellationToken _) =>
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(context.OfferedPolicies.IsEmpty, Is.False);
                        Assert.That(context.EnabledSecurityPolicyUris,
                            Is.EqualTo(ClientFixture.Config.SecurityConfiguration.SupportedSecurityPolicies));
                        Assert.That(context.ClientInstanceCertificateAlgorithm, Is.EqualTo(CertificateKeyAlgorithm.RSA));
                        Assert.That(context.ClientInstanceCertificateKeySize, Is.GreaterThanOrEqualTo(2048));
                        Assert.That(context.SecurityPolicyRegistry, Is.SameAs(policies));
                    });
                    return new ValueTask<IUserIdentity>(new UserIdentity("user1", "password"u8));
                });

            int anonymousAttempts = 0;
            var rejectAnonymous = new Mock<IIdentityAugmenter>();
            rejectAnonymous.Setup(value => value.AugmentAsync(
                    It.IsAny<IUserIdentity>(),
                    It.IsAny<AuthenticationContext>(),
                    It.IsAny<CancellationToken>()))
                .Returns((IUserIdentity identity, AuthenticationContext _, CancellationToken _) =>
                {
                    if (identity.TokenType == UserTokenType.Anonymous)
                    {
                        Interlocked.Increment(ref anonymousAttempts);
                        return new ValueTask<AuthenticationResult>(AuthenticationResult.Reject(
                            new ServiceResult(StatusCodes.BadIdentityTokenRejected)));
                    }
                    return new ValueTask<AuthenticationResult>(AuthenticationResult.Accept(identity));
                });

            ReferenceServer.CurrentInstance.IdentityRegistry.RegisterAugmenter(rejectAnonymous.Object);
            try
            {
                ManagedSessionBuilder builder = new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                    .UseEndpoint(ServerUrl.ToString())
                    .UseSecurityPolicies(policies)
                    .WithIdentityProvider(provider.Object)
                    .WithReconnectPolicy(options => options with { MaxRetries = 1, InitialDelay = TimeSpan.Zero });
                if (managedChannel)
                {
                    builder.WithChannelManager(manager);
                }
                await using ManagedSessionType session = await builder.ConnectAsync(timeout.Token).ConfigureAwait(false);

                Assert.That(session.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
                Assert.That(anonymousAttempts, Is.Zero);
                DataValue value = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, timeout.Token)
                    .ConfigureAwait(false);
                Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
                provider.Verify(value => value.GetIdentityAsync(
                    It.IsAny<UserTokenPolicy>(),
                    It.IsAny<IdentitySelectionContext>(),
                    It.IsAny<CancellationToken>()), Times.Once);
                await session.CloseAsync(timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                ReferenceServer.CurrentInstance.IdentityRegistry.UnregisterAugmenter(rejectAnonymous.Object);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UserNameOnSignUsesEffectiveTokenPolicyAsync(bool explicitNone)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            ArrayOf<EndpointDescription> endpoints = await ClientFixture
                .GetEndpointsAsync(ServerUrl, timeout.Token).ConfigureAwait(false);
            EndpointDescription description = endpoints.ToList().First(endpoint =>
                endpoint.SecurityMode == MessageSecurityMode.Sign &&
                endpoint.SecurityPolicyUri == SecurityPolicies.Basic256Sha256);
            UserTokenPolicy policy = description.UserIdentityTokens.ToList().First(token =>
                token.TokenType == UserTokenType.UserName &&
                string.IsNullOrEmpty(token.SecurityPolicyUri));
            if (explicitNone)
            {
                policy.SecurityPolicyUri = SecurityPolicies.None;
            }
            var endpoint = new ConfiguredEndpoint(
                null,
                description,
                EndpointConfiguration.Create(ClientFixture.Config));
            var identity = new UserIdentity("user1", "password"u8) { PolicyId = policy.PolicyId };
            var factory = new DefaultSessionFactory(Telemetry);

            if (explicitNone)
            {
                ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                    async () => await factory.CreateAsync(
                        ClientFixture.Config, endpoint, false, false, "unencrypted-token",
                        60000, identity, default, timeout.Token).ConfigureAwait(false));
                Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
                return;
            }

            using ISession session = await factory.CreateAsync(
                ClientFixture.Config, endpoint, false, false, "inherited-token",
                60000, identity, default, timeout.Token).ConfigureAwait(false);

            Assert.That(session.Identity.TokenType, Is.EqualTo(UserTokenType.UserName));
            DataValue value = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }
    }
}
