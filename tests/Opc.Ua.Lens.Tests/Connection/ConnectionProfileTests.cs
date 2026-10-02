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
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
public sealed class ConnectionProfileTests
{
    [Test]
    public async Task ASerializedProfileRestoresThroughASecretReferenceWithoutSerializingCredentials()
    {
        (EndpointDescription endpoint, ConnectionProfile profile) = CreateUserNameProfile();
        var store = new InMemorySecretStore();
        var reference = new SecretIdentifier("reference-only", store.StoreType);
        profile = profile with { CredentialReference = reference };
        byte[] password = Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N"));
        await store.SetAsync(reference, password).ConfigureAwait(false);

        string json = JsonSerializer.Serialize(profile, ConnectionProfileJsonContext.Default.ConnectionProfile);
        ConnectionProfile restored = JsonSerializer.Deserialize(
            json, ConnectionProfileJsonContext.Default.ConnectionProfile)!;
        var resolver = new ProfileCredentialProvider(new SecretRegistry(store));
        IClientIdentityProvider provider = await resolver
            .GetAsync(restored, CancellationToken.None)
            .ConfigureAwait(false);
        var pinned = new ProfileIdentityProvider(restored, provider);
        IUserIdentity identity = await pinned
            .AcquireIdentityAsync(endpoint, CreateMessageContext())
            .ConfigureAwait(false);

        Assert.That(restored, Is.EqualTo(profile));
        Assert.That(json, Does.Not.Contain(Encoding.UTF8.GetString(password)));
        Assert.That(json, Does.Not.Contain("TokenHandler").And.Not.Contain("ServerCertificate"));
        Assert.That(json, Does.Contain("reference-only"));
        Assert.That(identity.TokenType, Is.EqualTo(UserTokenType.UserName));
        Assert.That(identity.PolicyId, Is.EqualTo(profile.UserTokenPolicyId));
        Assert.That(identity.DisplayName, Is.EqualTo(profile.IdentityName));
        Assert.That(identity.TokenHandler, Is.TypeOf<UserNameIdentityTokenHandler>());
        Assert.That(((UserNameIdentityTokenHandler)identity.TokenHandler).DecryptedPassword, Is.EqualTo(password));
        await store.RemoveAsync(reference).ConfigureAwait(false);
    }

    [Test]
    public async Task MissingSecretReferenceDoesNotFallBackToAnonymous()
    {
        (EndpointDescription endpoint, ConnectionProfile profile) = CreateUserNameProfile();
        profile = profile with
        {
            CredentialReference = new SecretIdentifier("missing", InMemorySecretStore.DefaultStoreType)
        };
        var resolver = new ProfileCredentialProvider(new SecretRegistry(new InMemorySecretStore()));
        IClientIdentityProvider provider = await resolver
            .GetAsync(profile, CancellationToken.None)
            .ConfigureAwait(false);

        await Assert.ThatAsync(
            async () => await new ProfileIdentityProvider(profile, provider)
                .AcquireIdentityAsync(endpoint, CreateMessageContext()).ConfigureAwait(false),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);
    }

    [Test]
    public async Task AProfileWithoutRecoverableCredentialsExplicitlyRequiresReacquisition()
    {
        (_, ConnectionProfile profile) = CreateUserNameProfile();
        var resolver = new ProfileCredentialProvider();

        await Assert.ThatAsync(
            async () => await resolver.GetAsync(profile, CancellationToken.None).ConfigureAwait(false),
            Throws.InstanceOf<CredentialsRequiredException>()
                .With.Property(nameof(CredentialsRequiredException.Profile)).EqualTo(profile)).ConfigureAwait(false);
    }

    [Test]
    public async Task LegacyUsernameIdentityIsCopiedToAProviderNotReusedByASession()
    {
        (EndpointDescription endpoint, ConnectionProfile profile) = CreateUserNameProfile();
        byte[] password = Guid.NewGuid().ToByteArray();
        var original = new UserIdentity(profile.IdentityName!, password)
        {
            PolicyId = profile.UserTokenPolicyId
        };
        ConnectionCredentials credentials = await ConnectionCredentials
            .FromIdentityAsync(profile, original, CancellationToken.None)
            .ConfigureAwait(false);
        await using (credentials.ConfigureAwait(false))
        {
            IUserIdentity first = await credentials.Provider
                .AcquireIdentityAsync(endpoint, CreateMessageContext())
                .ConfigureAwait(false);
            IUserIdentity second = await credentials.Provider
                .AcquireIdentityAsync(endpoint, CreateMessageContext())
                .ConfigureAwait(false);

            Assert.That(first, Is.Not.SameAs(original));
            Assert.That(second, Is.Not.SameAs(first));
            ((UserNameIdentityTokenHandler)first.TokenHandler).DecryptedPassword!.AsSpan().Clear();
            Assert.That(((UserNameIdentityTokenHandler)second.TokenHandler).DecryptedPassword, Is.EqualTo(password));
            Assert.That(second.PolicyId, Is.EqualTo(profile.UserTokenPolicyId));
        }

        await Assert.ThatAsync(
            async () => await credentials.Provider
                .AcquireIdentityAsync(endpoint, CreateMessageContext()).ConfigureAwait(false),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);
    }

    [Test]
    public async Task AProviderCannotSubstituteAnotherUserOrAnonymousPolicy()
    {
        (EndpointDescription endpoint, ConnectionProfile profile) = CreateUserNameProfile();
        endpoint.UserIdentityTokens += new UserTokenPolicy(UserTokenType.Anonymous)
        {
            PolicyId = "anonymous"
        };
        var pinnedAnonymous = new ProfileIdentityProvider(profile, new AnonymousIdentityProvider());

        await Assert.ThatAsync(
            async () => await pinnedAnonymous
                .AcquireIdentityAsync(endpoint, CreateMessageContext()).ConfigureAwait(false),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);

        var store = new InMemorySecretStore();
        var reference = new SecretIdentifier("user", store.StoreType);
        await store.SetAsync(reference, Guid.NewGuid().ToByteArray()).ConfigureAwait(false);
        var wrongUser = new ProfileIdentityProvider(
            profile,
            new UserNamePasswordIdentityProvider("different-user", new SecretRegistry(store), reference));
        await Assert.ThatAsync(
            async () => await wrongUser.AcquireIdentityAsync(endpoint, CreateMessageContext()).ConfigureAwait(false),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);
        await store.RemoveAsync(reference).ConfigureAwait(false);
    }

    [Test]
    public async Task AProviderCannotSelectAWeakerUserTokenPolicyWithTheSameId()
    {
        (EndpointDescription endpoint, ConnectionProfile profile) = CreateUserNameProfile();
        endpoint.UserIdentityTokens[0].SecurityPolicyUri = SecurityPolicies.None;
        var provider = new Mock<IClientIdentityProvider>(MockBehavior.Strict);
        var pinned = new ProfileIdentityProvider(profile, provider.Object);

        await Assert.ThatAsync(
            async () => await pinned.AcquireIdentityAsync(endpoint, CreateMessageContext()).ConfigureAwait(false),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);
        provider.VerifyNoOtherCalls();
    }

    [Test]
    public async Task ADisplayLabelCannotHideADifferentUsernameInTheActualToken()
    {
        (EndpointDescription endpoint, ConnectionProfile profile) = CreateUserNameProfile();
        var identity = new UserIdentity("other-user", Guid.NewGuid().ToByteArray())
        {
            DisplayName = profile.IdentityName!
        };
        var inner = new Mock<IClientIdentityProvider>();
        inner.Setup(value => value.CanSatisfyAsync(
            It.IsAny<UserTokenPolicy>(),
            It.IsAny<IdentitySelectionContext>(),
            It.IsAny<CancellationToken>())).Returns(() => ValueTask.FromResult(CanSatisfyResult.Yes));
        inner.Setup(value => value.GetIdentityAsync(
            It.IsAny<UserTokenPolicy>(),
            It.IsAny<IdentitySelectionContext>(),
            It.IsAny<CancellationToken>())).Returns(() => ValueTask.FromResult<IUserIdentity>(identity));
        var pinned = new ProfileIdentityProvider(profile, inner.Object);

        await Assert.ThatAsync(
            async () => await pinned.AcquireIdentityAsync(endpoint, CreateMessageContext()).ConfigureAwait(false),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);
    }

    [Test]
    public void EndpointIdentityPreservesCaseSensitivePaths()
    {
        Assert.That(ConnectionProfile.EndpointUrlsMatch(
            "opc.tcp://SERVER:4840/FactoryA", "opc.tcp://server:4840/FactoryA"), Is.True);
        Assert.That(ConnectionProfile.EndpointUrlsMatch(
            "opc.tcp://server:4840/FactoryA", "opc.tcp://server:4840/factorya"), Is.False);
        Assert.That(ConnectionProfile.EndpointUrlsMatch(
            "opc.tcp://server:4840/FactoryA", "opc.tcp://server:4841/FactoryA"), Is.False);
    }

    [Test]
    public void ProfileRejectsEmbeddedCredentialsAndInconsistentSecurity()
    {
        (_, ConnectionProfile profile) = CreateUserNameProfile();
        Assert.That(
            () => (profile with { EndpointUrl = "opc.tcp://user:password@localhost:4840/" }).Validate(),
            Throws.ArgumentException);
        Assert.That(
            () => (profile with { SecurityMode = MessageSecurityMode.None }).Validate(),
            Throws.ArgumentException);
        Assert.That(
            () => (profile with { SecurityPolicyUri = SecurityPolicies.None }).Validate(),
            Throws.ArgumentException);
    }

    [Test]
    public void InitialFailuresReturnToTheCoordinatorButConnectedSessionsRetainStackRetry()
    {
        var policy = new InitialConnectPolicy();
        Assert.That(policy.GetNextDelay(0), Is.Null);
        Assert.That(policy.TryGetNextDelay(0, StatusCodes.BadCertificateInvalid, null, out TimeSpan? delay), Is.True);
        Assert.That(delay, Is.Null);

        policy.Reset();

        Assert.That(policy.GetNextDelay(0), Is.GreaterThan(TimeSpan.Zero));
        Assert.That(policy.TryGetNextDelay(0, StatusCodes.BadServerTooBusy, null, out delay), Is.True);
        Assert.That(delay, Is.GreaterThan(TimeSpan.Zero));
    }

    [Test]
    public void AutomaticFailoverCannotCarryATrustDecisionToAnotherEndpoint()
    {
        (EndpointDescription endpoint, ConnectionProfile profile) = CreateUserNameProfile();
        var current = new ConfiguredEndpoint(null, endpoint, null);
        var other = new ConfiguredEndpoint(null, (EndpointDescription)endpoint.Clone(), null);
        other.Description.EndpointUrl = "opc.tcp://localhost:4901/other";
        var information = new ServerRedundancyInfo { Mode = RedundancySupport.Hot };
        var inner = new Mock<IServerRedundancyHandler>();
        inner.Setup(value => value.SelectFailoverTarget(information, current)).Returns(other);
        var handler = new ProfileRedundancyHandler(profile, inner.Object);

        Assert.That(
            () => handler.SelectFailoverTarget(information, current),
            Throws.InstanceOf<ServiceResultException>());
    }

    private static ServiceMessageContext CreateMessageContext()
    {
        var telemetry = new Mock<ITelemetryContext>();
        telemetry.SetupGet(value => value.LoggerFactory).Returns(NullLoggerFactory.Instance);
        return ServiceMessageContext.Create(telemetry.Object);
    }

    private static (EndpointDescription Endpoint, ConnectionProfile Profile) CreateUserNameProfile()
    {
        var policy = new UserTokenPolicy(UserTokenType.UserName)
        {
            PolicyId = "username",
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
        };
        var endpoint = new EndpointDescription("opc.tcp://localhost:4850/primary")
        {
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            TransportProfileUri = Profiles.UaTcpTransport,
            UserIdentityTokens = [policy]
        };
        return (endpoint, ConnectionProfile.Create(
            endpoint, policy, SubscriptionEngineKind.ChannelV2, "engineer"));
    }
}

[JsonSerializable(typeof(ConnectionProfile))]
internal sealed partial class ConnectionProfileJsonContext : JsonSerializerContext
{
}
