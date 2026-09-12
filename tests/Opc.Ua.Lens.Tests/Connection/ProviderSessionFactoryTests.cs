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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Identity;
using UaLens.Connection;

namespace UaLens.Tests.Connection;

[TestFixture]
public sealed class ProviderSessionFactoryTests
{
    [Test]
    public async Task InitialActivationAndRecreationAcquireSelectedIdentityInsteadOfOpeningAnonymously()
    {
        (ApplicationConfiguration configuration, ConfiguredEndpoint endpoint, ConnectionProfile profile) = Setup();
        var provider = new Mock<IClientIdentityProvider>();
        provider.SetupGet(value => value.SupportedTokenTypes).Returns(new[] { UserTokenType.UserName });
        provider.SetupGet(value => value.SupportedIssuedTokenProfileUris).Returns(Array.Empty<string>());
        provider.Setup(value => value.CanSatisfyAsync(
            It.IsAny<UserTokenPolicy>(), It.IsAny<IdentitySelectionContext>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(CanSatisfyResult.Yes));
        provider.Setup(value => value.GetIdentityAsync(
            It.IsAny<UserTokenPolicy>(), It.IsAny<IdentitySelectionContext>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<IUserIdentity>(
                new UserIdentity(profile.IdentityName!, Guid.NewGuid().ToByteArray())));
        var identities = new ConnectionSessionIdentityProvider(new ProfileIdentityProvider(profile, provider.Object));
        var inner = new Mock<ISessionFactory>();
        var selected = new List<IUserIdentity>();
        inner.Setup(value => value.CreateAsync(
            configuration, endpoint, false, true, "test", 60000,
            It.IsAny<IUserIdentity>(), It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
            .Returns((ApplicationConfiguration _, ConfiguredEndpoint _, bool _, bool _, string _, uint _,
                IUserIdentity identity, ArrayOf<string> _, CancellationToken _) =>
            {
                selected.Add(identity);
                return Task.FromResult(new Mock<ISession>().Object);
            });
        var factory = new ProviderSessionFactory(inner.Object, identities, profile);
        await using (identities.ConfigureAwait(false))
        {
            await factory.CreateAsync(configuration, endpoint, false, true, "test", 60000,
                null, default).ConfigureAwait(false);
            await factory.CreateAsync(configuration, endpoint, false, true, "test", 60000,
                null, default).ConfigureAwait(false);

            Assert.That(selected, Has.Count.EqualTo(2));
            Assert.That(selected[0].TokenType, Is.EqualTo(UserTokenType.UserName));
            Assert.That(selected[0].PolicyId, Is.EqualTo(profile.UserTokenPolicyId));
            Assert.That(selected[1].DisplayName, Is.EqualTo(profile.IdentityName));
            Assert.That(selected[1], Is.Not.SameAs(selected[0]));
            Assert.That(((UserNameIdentityTokenHandler)selected[0].TokenHandler).DecryptedPassword, Is.Not.Empty);
        }
        Assert.That(((UserNameIdentityTokenHandler)selected[0].TokenHandler).DecryptedPassword, Is.Null);
        Assert.That(((UserNameIdentityTokenHandler)selected[1].TokenHandler).DecryptedPassword, Is.Null);
    }

    [Test]
    public async Task MaterialReturnedAfterCancellationIsReleasedExactlyOnceAndNeverInstalled()
    {
        (ApplicationConfiguration configuration, ConfiguredEndpoint endpoint, ConnectionProfile profile) = Setup();
        using var cancellation = new CancellationTokenSource();
        var provider = new Mock<IClientIdentityProvider>();
        var identity = new Mock<IUserIdentity>();
        identity.As<IAsyncDisposable>().Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        provider.Setup(value => value.GetIdentityAsync(
            It.IsAny<UserTokenPolicy>(), It.IsAny<IdentitySelectionContext>(), It.IsAny<CancellationToken>()))
            .Returns(async (UserTokenPolicy _, IdentitySelectionContext _, CancellationToken _) =>
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                return identity.Object;
            });
        var owner = new ConnectionSessionIdentityProvider(provider.Object);
        await using (owner.ConfigureAwait(false))
        {
            var context = new IdentitySelectionContext(
                endpoint.Description, endpoint.Description.UserIdentityTokens,
                configuration.CreateMessageContext(), new[] { profile.SecurityPolicyUri });
            await Assert.ThatAsync(async () => await owner.GetIdentityAsync(
                endpoint.Description.UserIdentityTokens[0], context, cancellation.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            identity.As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Once);
        }
        identity.As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Once);
    }

    [Test]
    public async Task MismatchedEndpointCannotTriggerProviderAcquisitionOrNetworkCreation()
    {
        (ApplicationConfiguration configuration, ConfiguredEndpoint endpoint, ConnectionProfile profile) = Setup();
        endpoint.Description.SecurityPolicyUri = SecurityPolicies.None;
        endpoint.Description.SecurityMode = MessageSecurityMode.None;
        var provider = new Mock<IClientIdentityProvider>(MockBehavior.Strict);
        var inner = new Mock<ISessionFactory>(MockBehavior.Strict);
        var factory = new ProviderSessionFactory(inner.Object, provider.Object, profile);

        await Assert.ThatAsync(() => factory.CreateAsync(
            configuration, endpoint, false, true, "test", 60000, null, default),
            Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
        provider.VerifyNoOtherCalls();
        inner.VerifyNoOtherCalls();
    }

    [Test]
    public async Task DisposingSessionIdentityOwnerDoesNotDisposeItsConfiguredProvider()
    {
        var provider = new Mock<IClientIdentityProvider>();
        provider.As<IAsyncDisposable>().Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var owner = new ConnectionSessionIdentityProvider(provider.Object);

        await owner.DisposeAsync().ConfigureAwait(false);
        await owner.DisposeAsync().ConfigureAwait(false);

        provider.As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Never);
    }

    [Test]
    public async Task CleanupFailureStillReleasesOtherOwnedIdentitiesAndIsObservedByRepeatedDisposal()
    {
        (ApplicationConfiguration configuration, ConfiguredEndpoint endpoint, _) = Setup();
        var first = new Mock<IUserIdentity>();
        first.As<IAsyncDisposable>().Setup(value => value.DisposeAsync())
            .Returns(() => ValueTask.FromException(new InvalidOperationException("Identity cleanup failed.")));
        var second = new Mock<IUserIdentity>();
        second.As<IAsyncDisposable>().Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var provider = new Mock<IClientIdentityProvider>();
        provider.SetupSequence(value => value.GetIdentityAsync(
            It.IsAny<UserTokenPolicy>(), It.IsAny<IdentitySelectionContext>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(first.Object))
            .Returns(() => ValueTask.FromResult(second.Object));
        var owner = new ConnectionSessionIdentityProvider(provider.Object);
        var context = new IdentitySelectionContext(
            endpoint.Description, endpoint.Description.UserIdentityTokens,
            configuration.CreateMessageContext(), new[] { SecurityPolicies.Basic256Sha256 });
        await owner.GetIdentityAsync(endpoint.Description.UserIdentityTokens[0], context).ConfigureAwait(false);
        await owner.GetIdentityAsync(endpoint.Description.UserIdentityTokens[0], context).ConfigureAwait(false);

        await Assert.ThatAsync(async () => await owner.DisposeAsync().ConfigureAwait(false),
            Throws.InvalidOperationException).ConfigureAwait(false);
        await Assert.ThatAsync(async () => await owner.DisposeAsync().ConfigureAwait(false),
            Throws.InvalidOperationException).ConfigureAwait(false);

        first.As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Once);
        second.As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Once);
    }

    private static (
        ApplicationConfiguration Configuration,
        ConfiguredEndpoint Endpoint,
        ConnectionProfile Profile) Setup()
    {
        var telemetry = new Mock<ITelemetryContext>();
        telemetry.SetupGet(value => value.LoggerFactory).Returns(NullLoggerFactory.Instance);
        var policy = new UserTokenPolicy(UserTokenType.UserName)
        {
            PolicyId = "operator",
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
        };
        var endpoint = new EndpointDescription("opc.tcp://localhost:4840/authenticated-only")
        {
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            UserIdentityTokens = [policy]
        };
        var configuration = new ApplicationConfiguration(telemetry.Object)
        {
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificates =
                [
                    new CertificateIdentifier { CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType }
                ]
            }
        };
        return (configuration, new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(configuration)),
            ConnectionProfile.Create(endpoint, policy, SubscriptionEngineKind.ChannelV2, "operator"));
    }
}
