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
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Identity;
using Opc.Ua.Security.Certificates;
using UaLens.Connection;
using UaLens.Views;

namespace UaLens.Tests.Connection;

[TestFixture]
public sealed class ConfiguredIdentityProviderTests
{
    [Test]
    public void AuthenticatedEndpointRootRemainsExpandableWithoutInventingAnonymousAccess()
    {
        (EndpointDescription endpoint, _) = CertificateProfile();
        ArrayOf<PickerNode> primary = EndpointPickerDialog.CreateNodes([endpoint], allowConfiguredIdentities: true);
        ArrayOf<PickerNode> legacy = EndpointPickerDialog.CreateNodes([endpoint], allowConfiguredIdentities: false);

        Assert.That(primary[0].IsSelectable, Is.False);
        Assert.That(primary[0].CanInteract, Is.True, "The tree root must not disable its authenticated children.");
        Assert.That(primary[0].Children[0].IsSelectable, Is.True);
        Assert.That(primary[0].Children[0].TokenPolicy!.TokenType, Is.EqualTo(UserTokenType.Certificate));
        Assert.That(legacy[0].IsSelectable, Is.False);
        Assert.That(legacy[0].Children[0].IsSelectable, Is.False);
    }

    [Test]
    public async Task ApplicationKeyFactoryCannotBorrowToolsManagerBeforeValidationOrCleanup()
    {
        var telemetry = new Mock<ITelemetryContext>();
        telemetry.SetupGet(value => value.LoggerFactory).Returns(NullLoggerFactory.Instance);
        var manager = new Mock<ICertificateManager>();
        manager.As<IAsyncDisposable>().Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        var application = new ApplicationConfiguration(telemetry.Object)
        {
            CertificateManager = manager.Object,
            SecurityConfiguration = new SecurityConfiguration()
        };
        var certificates = new Mock<ICertificateProvider>(MockBehavior.Strict);
        var source = new ConfiguredCertificateSource(
            "application-key", "Configured application key", new CertificateIdentifier(),
            certificates.Object, [PasswordSource()], CryptoPurpose.ApplicationInstanceKey);
        var reference = new CertificateIdentityReference
        {
            SourceId = source.Id,
            PasswordSourceId = "device-pin",
            SubjectName = "CN=Application"
        };
        var catalog = new ConnectionConfigurationCatalog(
        [
            new ConfiguredApplicationIdentity("application", "Configured application identity", source, reference,
                (_, _, _) => Task.FromResult(application))
        ]);
        var backend = new StackConnectionBackend(
            telemetry.Object, _ => Task.FromResult(application), configurations: catalog);
        await using (backend.ConfigureAwait(false))
        {
            var service = new ConnectionService(telemetry.Object, null, backend, new ProfileCredentialProvider());
            await using (service.ConfigureAwait(false))
            {
                await service.GetConfigAsync().ConfigureAwait(false);
                var policy = new UserTokenPolicy(UserTokenType.Anonymous) { PolicyId = "anonymous" };
                EndpointDescription endpoint = Endpoint(policy);
                ConnectionProfile profile = ConnectionProfile.Create(
                    endpoint, policy, SubscriptionEngineKind.ChannelV2) with
                { ApplicationIdentityId = "application" };

                await Assert.ThatAsync(() => service.ConnectAsync(profile), Throws.InvalidOperationException)
                    .ConfigureAwait(false);

                certificates.VerifyNoOtherCalls();
                manager.As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Never);
                Assert.That(await service.GetConfigAsync().ConfigureAwait(false), Is.SameAs(application));
            }
            manager.As<IAsyncDisposable>().Verify(value => value.DisposeAsync(), Times.Once);
        }
    }

    [Test]
    public async Task AsyncDeviceCertificateIsPreparedBeforeTheStackIdentityConstructor()
    {
        using Certificate certificate = CertificateBuilder.Create("CN=Async device").SetRSAKeySize(2048).CreateForRSA();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loaded = new TaskCompletionSource<Certificate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var certificates = new Mock<ICertificateProvider>();
        certificates.Setup(value => value.GetPrivateKeyCertificateAsync(
            It.IsAny<CertificateIdentifier>(), It.IsAny<ICertificatePasswordProvider>(), null,
            It.IsAny<CancellationToken>())).Returns(() =>
            {
                entered.TrySetResult();
                return new ValueTask<Certificate?>(loaded.Task);
            });
        using var configuration = new ConnectionIdentityConfiguration([CertificateSource(certificates.Object)]);
        (EndpointDescription endpoint, ConnectionProfile profile) = CertificateProfile(certificate);
        Task<IUserIdentity> pending = configuration.Resolve(profile)
            .GetIdentityAsync(endpoint.UserIdentityTokens[0], Context(endpoint)).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Assert.That(pending.IsCompleted, Is.False);
        loaded.SetResult(certificate.AddRef());

        IUserIdentity identity = await pending.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        Assert.That(identity.TokenType, Is.EqualTo(UserTokenType.Certificate));
        certificates.Verify(value => value.GetPrivateKeyCertificateAsync(
            It.IsAny<CertificateIdentifier>(), It.IsAny<ICertificatePasswordProvider>(), null,
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.That(certificate.HasPrivateKey, Is.True);
        await ConnectionCredentials.ReleaseIdentityAsync(identity).ConfigureAwait(false);
    }

    [Test]
    public async Task CertificateReferenceRoundTripsWithoutStorePathsOrPrivateMaterialAndReacquiresFreshIdentities()
    {
        using Certificate certificate = CertificateBuilder.Create("CN=UaLens user").SetRSAKeySize(2048).CreateForRSA();
        Mock<ICertificateProvider> certificates = CertificateProvider(certificate);
        ConfiguredCertificateSource source = CertificateSource(certificates.Object);
        using var configuration = new ConnectionIdentityConfiguration([source]);
        (EndpointDescription endpoint, ConnectionProfile profile) = CertificateProfile(certificate);

        string json = JsonSerializer.Serialize(profile, ConnectionProfileJsonContext.Default.ConnectionProfile);
        ConnectionProfile restored = JsonSerializer.Deserialize(
            json, ConnectionProfileJsonContext.Default.ConnectionProfile)!;
        var resolver = new ProfileCredentialProvider(configuration: configuration);
        IClientIdentityProvider provider = await resolver.GetAsync(restored, CancellationToken.None)
            .ConfigureAwait(false);
        var pinned = new ProfileIdentityProvider(restored, provider);
        IUserIdentity first = await pinned.AcquireIdentityAsync(Context(endpoint)).ConfigureAwait(false);
        IUserIdentity second = await pinned.AcquireIdentityAsync(Context(endpoint)).ConfigureAwait(false);

        Assert.That(restored, Is.EqualTo(profile));
        Assert.That(json,
            Does.Contain(certificate.Thumbprint).And.Contain("configured-user").And.Contain("device-pin"));
        Assert.That(json,
            Does.Not.Contain(source.Store.StorePath!).And.Not.Contain("TokenData").And.Not.Contain("PrivateKey"));
        Assert.That(first.TokenType, Is.EqualTo(UserTokenType.Certificate));
        Assert.That(second.PolicyId, Is.EqualTo(profile.UserTokenPolicyId));
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(second.TokenHandler.Token, Is.TypeOf<X509IdentityToken>());
        Assert.That(((X509IdentityToken)second.TokenHandler.Token).CertificateData,
            Is.EqualTo(ByteString.From(certificate.RawData)));
        certificates.Verify(value => value.GetPrivateKeyCertificateAsync(
            It.Is<CertificateIdentifier>(id => id.StorePath == source.Store.StorePath),
            It.IsAny<ICertificatePasswordProvider>(), null, It.IsAny<CancellationToken>()), Times.AtLeast(2));
        await ConnectionCredentials.ReleaseIdentityAsync(first).ConfigureAwait(false);
        await ConnectionCredentials.ReleaseIdentityAsync(second).ConfigureAwait(false);
        Assert.That(certificate.HasPrivateKey, Is.True, "The registration's certificate remains provider-owned.");
    }

    [Test]
    public async Task RsaUserKeyRejectsAnEccTokenPolicyInsteadOfSelectingAnotherOfferedPolicy()
    {
        using Certificate certificate = CertificateBuilder.Create("CN=RSA operator").SetRSAKeySize(2048).CreateForRSA();
        using var configuration = new ConnectionIdentityConfiguration(
            [CertificateSource(CertificateProvider(certificate).Object)]);
        (EndpointDescription endpoint, ConnectionProfile profile) =
            CertificateProfile(certificate, SecurityPolicies.ECC_nistP256);
        IClientIdentityProvider provider = configuration.Resolve(profile);

        CanSatisfyResult result = await provider.CanSatisfyAsync(
            endpoint.UserIdentityTokens[0], Context(endpoint)).ConfigureAwait(false);

        Assert.That(result.CanSatisfy, Is.False);
        Assert.That(result.RejectionReason, Does.Contain("CertificateAlgorithmMismatch"));
    }

    [Test]
    public async Task PublicOnlyCertificateIsNotPrivateKeyEligibility()
    {
        using Certificate privateCertificate = CertificateBuilder.Create("CN=Public only")
            .SetRSAKeySize(2048).CreateForRSA();
        using var publicCertificate = new Certificate(privateCertificate.RawData);
        using var configuration = new ConnectionIdentityConfiguration(
            [CertificateSource(CertificateProvider(publicCertificate).Object)]);
        (EndpointDescription endpoint, ConnectionProfile profile) = CertificateProfile(publicCertificate);

        await Assert.ThatAsync(async () => await configuration.Resolve(profile)
            .CanSatisfyAsync(endpoint.UserIdentityTokens[0], Context(endpoint)).ConfigureAwait(false),
            Throws.TypeOf<ConnectionIdentityException>()
                .With.Property(nameof(ConnectionIdentityException.Failure))
                .EqualTo(ConnectionIdentityFailure.Incompatible))
            .ConfigureAwait(false);
        Assert.That(privateCertificate.HasPrivateKey, Is.True);
    }

    [Test]
    public async Task MissingCertificateOrDeviceIsReportedWithoutAnonymousFallback()
    {
        var certificates = new Mock<ICertificateProvider>();
        using var configuration = new ConnectionIdentityConfiguration([CertificateSource(certificates.Object)]);
        (EndpointDescription endpoint, ConnectionProfile profile) = CertificateProfile();

        await Assert.ThatAsync(async () => await configuration.Resolve(profile)
            .CanSatisfyAsync(endpoint.UserIdentityTokens[0], Context(endpoint)).ConfigureAwait(false),
            Throws.TypeOf<ConnectionIdentityException>()
                .With.Property(nameof(ConnectionIdentityException.Failure))
                .EqualTo(ConnectionIdentityFailure.Unavailable))
            .ConfigureAwait(false);
    }

    [Test]
    public async Task DeniedPinProviderDoesNotLeakItsExceptionText()
    {
        string sensitive = Guid.NewGuid().ToString("N");
        var certificates = new Mock<ICertificateProvider>();
        certificates.Setup(value => value.GetPrivateKeyCertificateAsync(
            It.IsAny<CertificateIdentifier>(), It.IsAny<ICertificatePasswordProvider>(), null,
            It.IsAny<CancellationToken>())).Throws(new UnauthorizedAccessException(sensitive));
        using var configuration = new ConnectionIdentityConfiguration([CertificateSource(certificates.Object)]);
        (EndpointDescription endpoint, ConnectionProfile profile) = CertificateProfile();
        ConnectionIdentityException? failure = null;
        try
        {
            await configuration.Resolve(profile).CanSatisfyAsync(
                endpoint.UserIdentityTokens[0], Context(endpoint)).ConfigureAwait(false);
        }
        catch (ConnectionIdentityException error)
        {
            failure = error;
        }

        Assert.That(failure, Is.Not.Null);
        Assert.That(failure!.Failure, Is.EqualTo(ConnectionIdentityFailure.Denied));
        Assert.That(failure.ToString(), Does.Not.Contain(sensitive));
        Assert.That(failure.InnerException, Is.Null);
    }

    [Test]
    public async Task CancelingAnAsyncDeviceLoadNeverBlocksTheStackIdentityConstructor()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<Certificate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var certificates = new Mock<ICertificateProvider>();
        certificates.Setup(value => value.GetPrivateKeyCertificateAsync(
            It.IsAny<CertificateIdentifier>(), It.IsAny<ICertificatePasswordProvider>(), null,
            It.IsAny<CancellationToken>())).Returns((CertificateIdentifier _, ICertificatePasswordProvider? _,
                string? _, CancellationToken ct) =>
            {
                entered.TrySetResult();
                return new ValueTask<Certificate?>(release.Task.WaitAsync(ct));
            });
        using var configuration = new ConnectionIdentityConfiguration([CertificateSource(certificates.Object)]);
        using var cancellation = new CancellationTokenSource();
        (EndpointDescription endpoint, ConnectionProfile profile) = CertificateProfile();
        Task<IUserIdentity> pending = configuration.Resolve(profile)
            .GetIdentityAsync(endpoint.UserIdentityTokens[0], Context(endpoint), cancellation.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        Assert.That(pending.IsCompleted, Is.False);

        await cancellation.CancelAsync().ConfigureAwait(false);

        await Assert.ThatAsync(() => pending, Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
        Assert.That(release.Task.IsCompleted, Is.False, "Cancellation did not require a device result to unblock.");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task HardwareRegistrationCannotFallBackToPlatformOrAnotherKeyPurpose(bool applicationKey)
    {
        CryptoPurpose purpose = applicationKey ? CryptoPurpose.ApplicationInstanceKey : CryptoPurpose.UserIdentityKey;
        var certificates = new Mock<ICertificateProvider>(MockBehavior.Strict);
        var crypto = new Mock<ICryptoProviderRegistry>();
        var platform = new Mock<ICryptoProvider>();
        platform.SetupGet(value => value.Name).Returns("Platform");
        crypto.Setup(value => value.Resolve(It.IsAny<CryptoPurpose>(), It.IsAny<string>(), It.IsAny<NodeId>()))
            .Returns(platform.Object);
        var source = new ConfiguredCertificateSource(
            "configured-user", "Hardware reference", new CertificateIdentifier { StoreType = "Configured" },
            certificates.Object, [PasswordSource()], purpose, crypto.Object, "ConfiguredDevice");
        using var configuration = new ConnectionIdentityConfiguration([source]);
        (EndpointDescription endpoint, ConnectionProfile profile) = CertificateProfile();

        await Assert.ThatAsync(async () => await configuration.Resolve(profile)
            .CanSatisfyAsync(endpoint.UserIdentityTokens[0], Context(endpoint)).ConfigureAwait(false),
            Throws.TypeOf<ConnectionIdentityException>()).ConfigureAwait(false);
        certificates.VerifyNoOtherCalls();
    }

    [Test]
    public void MissingConfiguredProviderIsNotAnAnonymousIdentity()
    {
        using var configuration = new ConnectionIdentityConfiguration();
        (_, ConnectionProfile profile) = CertificateProfile();

        Assert.That(() => configuration.Resolve(profile),
            Throws.TypeOf<ConnectionIdentityException>()
                .With.Property(nameof(ConnectionIdentityException.Failure))
                .EqualTo(ConnectionIdentityFailure.RequiresConfiguration));
    }

    [TestCase("..\\untrusted-module.dll")]
    [TestCase("https://provider.example.test")]
    [TestCase("provider/child")]
    public void WorkspaceCannotLoadAnArbitraryProviderPath(string sourceId)
    {
        (_, ConnectionProfile profile) = CertificateProfile();
        profile = profile with { CertificateIdentity = profile.CertificateIdentity! with { SourceId = sourceId } };
        Assert.That(profile.Validate, Throws.ArgumentException);
    }

    [Test]
    public async Task ConfiguredIssuedProviderPinsAuthorityReacquiresAndClearsAcquiredBuffers()
    {
        var clock = new FixedTimeProvider();
        var access = new Mock<IAccessTokenProvider>();
        access.SetupGet(value => value.AuthorityUri).Returns(k_authority);
        byte[] firstBytes = Guid.NewGuid().ToByteArray();
        byte[] secondBytes = Guid.NewGuid().ToByteArray();
        access.SetupSequence(value => value.AcquireAsync(
            It.IsAny<AuthorizationServerMetadata>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(Token(firstBytes, clock)))
            .Returns(() => ValueTask.FromResult(Token(secondBytes, clock)));
        var source = new ConfiguredAccessTokenSource("authority", "Configured authority", access.Object);
        using var configuration = new ConnectionIdentityConfiguration(
            accessTokenSources: [source], timeProvider: clock);
        (EndpointDescription endpoint, ConnectionProfile profile) = IssuedProfile();
        var provider = new ProfileIdentityProvider(profile, configuration.Resolve(profile));

        IUserIdentity first = await provider.AcquireIdentityAsync(Context(endpoint)).ConfigureAwait(false);
        IUserIdentity second = await provider.AcquireIdentityAsync(Context(endpoint)).ConfigureAwait(false);
        string json = JsonSerializer.Serialize(profile, ConnectionProfileJsonContext.Default.ConnectionProfile);

        Assert.That(first.TokenType, Is.EqualTo(UserTokenType.IssuedToken));
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(provider.ExpiresAt, Is.EqualTo(clock.GetUtcNow().UtcDateTime.AddMinutes(10)));
        Assert.That(firstBytes, Is.All.EqualTo(0));
        Assert.That(secondBytes, Is.All.EqualTo(0));
        Assert.That(json, Does.Contain(k_authority).And.Contain("authority"));
        Assert.That(json,
            Does.Not.Contain("TokenData").And.Not.Contain("IssuerEndpointUrl").And.Not.Contain("TokenHandler"));
        access.Verify(value => value.AcquireAsync(
            It.Is<AuthorizationServerMetadata>(metadata => metadata.AuthorityUri == k_authority &&
                metadata.ResourceUri == k_resource && metadata.TokenEndpoint == null),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        await ConnectionCredentials.ReleaseIdentityAsync(first).ConfigureAwait(false);
        await ConnectionCredentials.ReleaseIdentityAsync(second).ConfigureAwait(false);
        Assert.That(((IssuedIdentityTokenHandler)first.TokenHandler).DecryptedTokenData, Is.Null);
        Assert.That(((IssuedIdentityTokenHandler)second.TokenHandler).DecryptedTokenData, Is.Null);
    }

    [Test]
    public async Task ChangedAuthorityOrResourceIsRejectedBeforeCallingTheAuthority()
    {
        var access = new Mock<IAccessTokenProvider>(MockBehavior.Strict);
        access.SetupGet(value => value.AuthorityUri).Returns(k_authority);
        using var configuration = new ConnectionIdentityConfiguration(
            accessTokenSources: [new ConfiguredAccessTokenSource("authority", "Configured authority", access.Object)]);
        (EndpointDescription endpoint, ConnectionProfile profile) = IssuedProfile();
        endpoint.UserIdentityTokens[0].IssuerEndpointUrl =
            "{\"authorityUri\":\"https://other.example.test\",\"ua:resourceUri\":\"urn:other\"}";
        IClientIdentityProvider provider = new ProfileIdentityProvider(profile, configuration.Resolve(profile));

        await Assert.ThatAsync(async () => await provider
            .AcquireIdentityAsync(Context(endpoint)).ConfigureAwait(false),
            Throws.InstanceOf<ServiceResultException>()).ConfigureAwait(false);
        access.Verify(value => value.AcquireAsync(It.IsAny<AuthorizationServerMetadata>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ExpiredOrWrongProfileAuthorityTokensAreDisposed(bool expired)
    {
        var clock = new FixedTimeProvider();
        var access = new Mock<IAccessTokenProvider>();
        access.SetupGet(value => value.AuthorityUri).Returns(k_authority);
        byte[] bytes = Guid.NewGuid().ToByteArray();
        var token = new AccessToken(
            expired ? Profiles.JwtUserToken : "urn:unsupported-token",
            bytes,
            expired ? clock.GetUtcNow().UtcDateTime.AddMinutes(-1) : clock.GetUtcNow().UtcDateTime.AddMinutes(10),
            string.Empty);
        access.Setup(value => value.AcquireAsync(
            It.IsAny<AuthorizationServerMetadata>(), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(token));
        var provider = new GuardedAccessTokenProvider(
            new ConfiguredAccessTokenSource("authority", "Configured authority", access.Object), clock);

        await Assert.ThatAsync(async () => await provider.AcquireAsync(new AuthorizationServerMetadata
        {
            AuthorityUri = k_authority,
            ResourceUri = k_resource
        }).ConfigureAwait(false), Throws.TypeOf<ConnectionIdentityException>()).ConfigureAwait(false);
        Assert.That(bytes, Is.All.EqualTo(0));
    }

    [Test]
    public async Task ATokenReturnedAfterCancellationIsDisposedWithoutBeingActivated()
    {
        var clock = new FixedTimeProvider();
        using var cancellation = new CancellationTokenSource();
        var access = new Mock<IAccessTokenProvider>();
        access.SetupGet(value => value.AuthorityUri).Returns(k_authority);
        byte[] bytes = Guid.NewGuid().ToByteArray();
        access.Setup(value => value.AcquireAsync(
            It.IsAny<AuthorizationServerMetadata>(), It.IsAny<CancellationToken>()))
            .Returns(async (AuthorizationServerMetadata _, CancellationToken _) =>
            {
                await cancellation.CancelAsync().ConfigureAwait(false);
                return Token(bytes, clock);
            });
        var provider = new GuardedAccessTokenProvider(
            new ConfiguredAccessTokenSource("authority", "Configured authority", access.Object), clock);

        await Assert.ThatAsync(async () => await provider.AcquireAsync(new AuthorizationServerMetadata
        {
            AuthorityUri = k_authority,
            ResourceUri = k_resource
        }, cancellation.Token).ConfigureAwait(false), Throws.InstanceOf<OperationCanceledException>())
            .ConfigureAwait(false);
        Assert.That(bytes, Is.All.EqualTo(0));
    }

    [Test]
    public async Task AuthorityFailureExposesNoRawProviderException()
    {
        string sensitive = Guid.NewGuid().ToString("N");
        var access = new Mock<IAccessTokenProvider>();
        access.SetupGet(value => value.AuthorityUri).Returns(k_authority);
        access.Setup(value => value.AcquireAsync(
            It.IsAny<AuthorizationServerMetadata>(), It.IsAny<CancellationToken>()))
            .Throws(new ServiceResultException(StatusCodes.BadUserAccessDenied, sensitive));
        var provider = new GuardedAccessTokenProvider(
            new ConfiguredAccessTokenSource("authority", "Configured authority", access.Object),
            new FixedTimeProvider());
        ConnectionIdentityException? failure = null;
        try
        {
            await provider.AcquireAsync(new AuthorizationServerMetadata
            {
                AuthorityUri = k_authority,
                ResourceUri = k_resource
            }).ConfigureAwait(false);
        }
        catch (ConnectionIdentityException error)
        {
            failure = error;
        }
        Assert.That(failure, Is.Not.Null);
        Assert.That(failure!.Failure, Is.EqualTo(ConnectionIdentityFailure.Denied));
        Assert.That(failure.ToString(), Does.Not.Contain(sensitive));
        Assert.That(failure.InnerException, Is.Null);
    }

    private static ConfiguredCertificatePasswordSource PasswordSource()
    {
        return new ConfiguredCertificatePasswordSource(
            "device-pin", "Configured PIN source", new Mock<ICertificatePasswordProvider>().Object);
    }

    private static ConfiguredCertificateSource CertificateSource(ICertificateProvider certificates)
    {
        return new ConfiguredCertificateSource(
            "configured-user", "User certificates",
            new CertificateIdentifier
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = Path.Combine("host", "user-keys")
            },
            certificates, [PasswordSource()]);
    }

    private static Mock<ICertificateProvider> CertificateProvider(Certificate certificate)
    {
        var provider = new Mock<ICertificateProvider>();
        provider.Setup(value => value.GetPrivateKeyCertificateAsync(
            It.IsAny<CertificateIdentifier>(), It.IsAny<ICertificatePasswordProvider>(), null,
            It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<Certificate?>(certificate.AddRef()));
        return provider;
    }

    private static (EndpointDescription Endpoint, ConnectionProfile Profile) CertificateProfile(
        Certificate? certificate = null,
        string? tokenPolicy = null)
    {
        var policy = new UserTokenPolicy(UserTokenType.Certificate)
        {
            PolicyId = "certificate",
            SecurityPolicyUri = tokenPolicy ?? SecurityPolicies.Basic256Sha256
        };
        EndpointDescription endpoint = Endpoint(policy);
        var reference = new CertificateIdentityReference
        {
            SourceId = "configured-user",
            PasswordSourceId = "device-pin",
            Thumbprint = certificate?.Thumbprint,
            SubjectName = certificate?.Subject ?? "CN=Configured operator"
        };
        return (endpoint, ConnectionProfile.Create(
            endpoint, policy, SubscriptionEngineKind.ChannelV2, certificateIdentity: reference));
    }

    private static (EndpointDescription Endpoint, ConnectionProfile Profile) IssuedProfile()
    {
        var policy = new UserTokenPolicy(UserTokenType.IssuedToken)
        {
            PolicyId = "jwt",
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            IssuedTokenType = Profiles.JwtUserToken,
            IssuerEndpointUrl = "{\"authorityUri\":\"" + k_authority + "\",\"ua:resourceUri\":\"" +
                k_resource + "\",\"tokenEndpoint\":\"https://ignored.example.test/token\"}"
        };
        EndpointDescription endpoint = Endpoint(policy);
        return (endpoint, ConnectionProfile.Create(endpoint, policy, SubscriptionEngineKind.ChannelV2,
            issuedIdentity: new IssuedIdentityReference { ProviderId = "authority", AuthorityUri = k_authority }));
    }

    private static EndpointDescription Endpoint(UserTokenPolicy policy)
    {
        return new EndpointDescription("opc.tcp://localhost:4840/identity")
        {
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            TransportProfileUri = Profiles.UaTcpTransport,
            Server = new ApplicationDescription { ApplicationUri = k_resource },
            UserIdentityTokens = [policy]
        };
    }

    private static IdentitySelectionContext Context(EndpointDescription endpoint)
    {
        var telemetry = new Mock<ITelemetryContext>();
        telemetry.SetupGet(value => value.LoggerFactory).Returns(NullLoggerFactory.Instance);
        return new IdentitySelectionContext(endpoint, endpoint.UserIdentityTokens,
            ServiceMessageContext.Create(telemetry.Object),
            new[] { SecurityPolicies.Basic256Sha256, SecurityPolicies.ECC_nistP256 });
    }

    private static AccessToken Token(byte[] bytes, TimeProvider clock)
    {
        return new AccessToken(Profiles.JwtUserToken, bytes,
            clock.GetUtcNow().UtcDateTime.AddMinutes(10), string.Empty);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }
    }

    private const string k_authority = "https://authority.example.test";
    private const string k_resource = "urn:ualens:configured-server";
}
