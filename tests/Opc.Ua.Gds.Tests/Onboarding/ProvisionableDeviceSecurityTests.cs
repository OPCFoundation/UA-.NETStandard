/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;
using ClientSession = Opc.Ua.Client.ISession;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Verifies actual-peer ownership, installed trust, interrupted installation, and security-only reset.
    /// Platform installation is mocked; these are not endpoint-switching or durable-backend integration tests.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class ProvisionableDeviceSecurityTests
    {
        /// <summary>
        /// Creates real certificates without relying on fixed secrets or physical certificate stores.
        /// </summary>
        [OneTimeSetUp]
        public void CreateCertificates()
        {
            m_firstRegistrar = CreateCertificate("urn:first:registrar");
            m_otherRegistrar = CreateCertificate("urn:other:registrar");
            m_dca = CreateCertificate(kApplicationUri);
        }

        /// <summary>
        /// Creates isolated state and explicit validation/installation collaborators for each case.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_secrets = new InMemorySecretStore();
            m_certificates = new Mock<ICertificateManager>(MockBehavior.Strict);
            m_certificates.Setup(c => c.ValidateAsync(
                    It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                    It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CertificateValidationResult.Success);
            m_installer = new Mock<IProvisionableDeviceSecurityInstaller>(MockBehavior.Strict);
            m_installer.Setup(i => i.InstallAsync(
                    It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            m_installer.Setup(i => i.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
        }

        /// <summary>
        /// Releases the independently owned certificate handles.
        /// </summary>
        [OneTimeTearDown]
        public void DisposeCertificates()
        {
            m_firstRegistrar.Dispose();
            m_otherRegistrar.Dispose();
            m_dca.Dispose();
        }

        /// <summary>
        /// Unconfigured startup retains device defaults before the host may expose configuration listeners.
        /// </summary>
        [Test]
        public async Task UnconfiguredRecoveryPreservesFactoryDefaultsBeforeAcceptingConfiguration()
        {
            Mock<IProvisionableDeviceSecurityDefaults> defaults =
                m_installer.As<IProvisionableDeviceSecurityDefaults>();
            defaults.Setup(value => value.PreserveDefaultsAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            using var security = Create();

            await security.RecoverAsync().ConfigureAwait(false);

            defaults.Verify(value => value.PreserveDefaultsAsync(It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Pull installation uses the authenticated transport rather than its advertised certificate,
        /// preserves caller ownership and detaches credentials before asynchronous validation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PullInstallationUsesActualRegistrarAndRetainsBorrowedSession(bool managed)
        {
            await using SessionRequestContextTestContext session = PullSession();
            using var security = Create();
            ProvisionableDeviceCredentials credentials = Credentials();
            m_certificates.Setup(value => value.ValidateAsync(
                    It.Is<CertificateCollection>(chain => chain[0].Thumbprint == m_firstRegistrar.Thumbprint),
                    TrustListIdentifier.Peers,
                    It.Is<ValidationOptions>(options => options.TrustScope == null &&
                        options.AutoAcceptUntrustedCertificates == false &&
                        options.RejectUnknownRevocationStatus == true),
                    It.IsAny<CancellationToken>()))
                .Callback(() => credentials.TrustList.SpecifiedLists = 0)
                .ReturnsAsync(CertificateValidationResult.Success);
            ClientSession borrowed = managed ? session.ManagedSession : session.InnerSession;

            await security.InstallFromPullAsync(borrowed, "urn:first:registrar", credentials).ConfigureAwait(false);

            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
            m_installer.Verify(value => value.InstallAsync(
                It.Is<ProvisionableDeviceCredentials>(installed =>
                    installed.Certificate == credentials.Certificate &&
                    installed.TrustList.SpecifiedLists == (uint)TrustListMasks.All),
                It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(session.InnerSession.SessionId, Is.EqualTo(new NodeId("original-session", 0)));
            Assert.That(session.Requests.Exists(static request => request is CloseSessionRequest), Is.False);
        }

        /// <summary>
        /// An insecure channel, absent transport certificate or mismatched registrar identity cannot claim state.
        /// </summary>
        [TestCase("unencrypted")]
        [TestCase("none-policy")]
        [TestCase("missing-certificate")]
        [TestCase("endpoint-uri")]
        [TestCase("certificate-uri")]
        public async Task PullInstallationRejectsInvalidRegistrarEvidenceBeforeChangingState(string scenario)
        {
            await using SessionRequestContextTestContext session = PullSession();
            EndpointDescription endpoint = session.Channel.Object.EndpointDescription;
            string expectedUri = "urn:first:registrar";
            StatusCode expected;
            switch (scenario)
            {
                case "unencrypted":
                    endpoint.SecurityMode = MessageSecurityMode.Sign;
                    expected = StatusCodes.BadSecurityModeInsufficient;
                    break;
                case "none-policy":
                    endpoint.SecurityPolicyUri = SecurityPolicies.None;
                    expected = StatusCodes.BadSecurityModeInsufficient;
                    break;
                case "missing-certificate":
                    session.Channel.SetupGet(value => value.ServerChannelCertificate).Returns(Array.Empty<byte>());
                    expected = StatusCodes.BadCertificateInvalid;
                    break;
                case "endpoint-uri":
                    endpoint.Server.ApplicationUri = "urn:other:registrar";
                    expected = StatusCodes.BadCertificateUriInvalid;
                    break;
                case "certificate-uri":
                    expectedUri = "urn:other:registrar";
                    endpoint.Server.ApplicationUri = expectedUri;
                    expected = StatusCodes.BadCertificateUriInvalid;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario));
            }
            using var security = Create();

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.InstallFromPullAsync(session.ManagedSession, expectedUri, Credentials())
                    .ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(failure!.StatusCode, Is.EqualTo(expected));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            m_certificates.VerifyNoOtherCalls();
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Session recreation or replacement of the actual peer during trust validation rejects installation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PullInstallationRejectsChangedAuthenticatedGeneration(bool replacePeer)
        {
            await using SessionRequestContextTestContext session = PullSession();
            m_certificates.Setup(value => value.ValidateAsync(
                    It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                    It.Is<ValidationOptions>(options => options.TrustScope == null),
                    It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    if (replacePeer)
                    {
                        session.Channel.SetupGet(value => value.ServerChannelCertificate)
                            .Returns(m_otherRegistrar.RawData);
                    }
                    else
                    {
                        session.Recreate();
                    }
                })
                .ReturnsAsync(CertificateValidationResult.Success);
            using var security = Create();

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.InstallFromPullAsync(session.ManagedSession, "urn:first:registrar", Credentials())
                    .ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelIdInvalid));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A registrar rejected by current peer trust cannot become the device's first installation owner.
        /// </summary>
        [Test]
        public async Task PullInstallationPreservesRegistrarTrustFailureWithoutClaimingState()
        {
            await using SessionRequestContextTestContext session = PullSession();
            var expected = new ServiceResultException(StatusCodes.BadCertificateUntrusted);
            m_certificates.Setup(value => value.ValidateAsync(
                    It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                    It.Is<ValidationOptions>(options => options.TrustScope == null),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(expected);
            using var security = Create();

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.InstallFromPullAsync(session.ManagedSession, "urn:first:registrar", Credentials())
                    .ConfigureAwait(false)).ConfigureAwait(false);

            Assert.That(failure, Is.SameAs(expected));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Interrupted pull installation keeps its actual registrar owner and recovers through the same checkpoint.
        /// </summary>
        [Test]
        public async Task InterruptedPullInstallationRetainsOwnerAcrossProviderRecreation()
        {
            await using SessionRequestContextTestContext session = PullSession();
            var expected = new IOException("The device installer was interrupted.");
            m_installer.Setup(value => value.InstallAsync(
                    It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()))
                .Throws(expected);
            using (var security = Create())
            {
                IOException? failure = await Assert.ThrowsAsync<IOException>(async () =>
                    await security.InstallFromPullAsync(session.ManagedSession, "urn:first:registrar", Credentials())
                        .ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(failure, Is.SameAs(expected));
                Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                    Is.EqualTo(ProvisionableDeviceSecurityStage.Installing));
                ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await security.RequireRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false))
                    .ConfigureAwait(false);
                Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
            m_installer.Setup(value => value.InstallAsync(
                    It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            using var restarted = Create();

            await restarted.RecoverAsync().ConfigureAwait(false);

            Assert.That(await restarted.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        /// <summary>
        /// An engineering client can publish a different registrar without becoming the pending push owner.
        /// </summary>
        [Test]
        public async Task RegistrarPublicationPreservesUnconfiguredStateAcrossRestartAndAllowsPull()
        {
            Mock<ISecretStore> storage = ForwardStorage();
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            ArrayOf<ApplicationDescription> published = default;
            store.Setup(value => value.ReplaceAsync(
                    It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<ApplicationDescription>, CancellationToken>((values, _) => published = values)
                .Returns(default(ValueTask));
            store.Setup(value => value.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<ArrayOf<ApplicationDescription>>(published));
            ApplicationDescription candidate = Registrar();
            ApplicationDescription secondCandidate = Registrar("urn:second:registrar");
            secondCandidate.ApplicationType = ApplicationType.ClientAndServer;

            using (var initial = Create(storage.Object))
            {
                var runtime = CreateRuntime(store.Object, initial);
                await runtime.SetRegistrarEndpointsAsync(
                    Context(m_otherRegistrar), [candidate, secondCandidate]).ConfigureAwait(false);
                Assert.That(await initial.GetStageAsync().ConfigureAwait(false),
                    Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
                storage.Verify(value => value.SetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(),
                    It.IsAny<CancellationToken>()), Times.Never);
            }
            candidate.ApplicationUri = "urn:mutated:registrar";
            candidate.DiscoveryUrls = ["opc.tcp://mutated:4840"];
            using var restarted = Create();
            await restarted.RecoverAsync().ConfigureAwait(false);
            ArrayOf<ApplicationDescription> retained =
                await store.Object.ReadAsync().ConfigureAwait(false);

            Assert.That(m_otherRegistrar.Thumbprint, Is.Not.EqualTo(m_firstRegistrar.Thumbprint));
            Assert.That(await restarted.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            Assert.That(retained.Count, Is.EqualTo(2));
            Assert.That(retained[0], Is.Not.SameAs(candidate));
            Assert.That(retained[0].ApplicationUri, Is.EqualTo("urn:first:registrar"));
            Assert.That(retained[0].ApplicationType, Is.EqualTo(ApplicationType.Server));
            Assert.That(retained[0].DiscoveryUrls.ToArray(),
                Is.EqualTo(s_expectedRegistrarDiscoveryUrls));
            Assert.That(retained[1], Is.Not.SameAs(secondCandidate));
            Assert.That(retained[1].ApplicationUri, Is.EqualTo("urn:second:registrar"));
            Assert.That(retained[1].ApplicationType, Is.EqualTo(ApplicationType.ClientAndServer));
            m_certificates.VerifyNoOtherCalls();

            await using SessionRequestContextTestContext session = PullSession();
            await restarted.InstallFromPullAsync(
                session.ManagedSession, retained[0].ApplicationUri!, Credentials()).ConfigureAwait(false);

            Assert.That(await restarted.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
            m_certificates.Verify(value => value.ValidateAsync(
                It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                It.Is<ValidationOptions>(options => options.TrustScope == null &&
                    options.AutoAcceptUntrustedCertificates == false &&
                    options.RejectUnknownRevocationStatus == true),
                It.IsAny<CancellationToken>()), Times.Once);
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Once);
            store.Verify(value => value.ReplaceAsync(
                It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()), Times.Once);
            store.Verify(value => value.ReadAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// A failed atomic store replacement preserves both the preceding candidates and the security stage.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RegistrarPublicationFailurePreservesCandidatesAndSecurityStage(bool installed)
        {
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            ArrayOf<ApplicationDescription> published = default;
            bool failPublication = false;
            var expected = new IOException("Registrar publication failed before replacement.");
            store.Setup(value => value.ReplaceAsync(
                    It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((ArrayOf<ApplicationDescription> values, CancellationToken _) =>
                {
                    if (failPublication)
                    {
                        throw expected;
                    }
                    published = values;
                    return default;
                });
            store.Setup(value => value.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<ArrayOf<ApplicationDescription>>(published));
            ProvisionableDeviceSecurityStage expectedStage = installed
                ? ProvisionableDeviceSecurityStage.Installed
                : ProvisionableDeviceSecurityStage.Unconfigured;

            using (var initial = Create())
            {
                if (installed)
                {
                    await initial.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
                }
                var runtime = CreateRuntime(store.Object, initial);
                await runtime.SetRegistrarEndpointsAsync(
                    Context(m_firstRegistrar), [Registrar("urn:previous:registrar")]).ConfigureAwait(false);
                failPublication = true;

                IOException? failure = await Assert.ThrowsAsync<IOException>(async () =>
                    await runtime.SetRegistrarEndpointsAsync(
                        Context(m_firstRegistrar), [Registrar("urn:replacement:registrar")]).ConfigureAwait(false));

                Assert.That(failure, Is.SameAs(expected));
                Assert.That(await initial.GetStageAsync().ConfigureAwait(false), Is.EqualTo(expectedStage));
            }
            using var restarted = Create();
            ArrayOf<ApplicationDescription> retained =
                await store.Object.ReadAsync().ConfigureAwait(false);

            Assert.That(await restarted.GetStageAsync().ConfigureAwait(false), Is.EqualTo(expectedStage));
            Assert.That(retained.Count, Is.EqualTo(1));
            Assert.That(retained[0].ApplicationUri, Is.EqualTo("urn:previous:registrar"));
            Assert.That(retained[0].DiscoveryUrls.ToArray(),
                Is.EqualTo(s_expectedRegistrarDiscoveryUrls));
            store.Verify(value => value.ReplaceAsync(
                It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            store.Verify(value => value.ReadAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Neither the active push owner nor another peer may replace candidates during a pending transition.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task PendingPushOwnershipRejectsPublicationWithoutChangingOwner(bool installing)
        {
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            ProvisionableDeviceSecurityStage expectedStage = installing
                ? ProvisionableDeviceSecurityStage.Installing
                : ProvisionableDeviceSecurityStage.Configuring;
            using (var initial = Create())
            {
                if (installing)
                {
                    m_installer.Setup(value => value.InstallAsync(
                            It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()))
                        .Throws(new IOException("Installation remains pending."));
                    await Assert.ThrowsAsync<IOException>(async () =>
                        await initial.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false));
                }
                else
                {
                    await initial.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);
                }

                ServiceResultException? ownerFailure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await initial.ReplaceRegistrarEndpointsAsync(
                        Context(m_firstRegistrar), store.Object, [Registrar()]).ConfigureAwait(false));
                ServiceResultException? otherFailure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await initial.ReplaceRegistrarEndpointsAsync(
                        Context(m_otherRegistrar), store.Object, [Registrar()]).ConfigureAwait(false));

                Assert.That(ownerFailure!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(otherFailure!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(await initial.GetStageAsync().ConfigureAwait(false), Is.EqualTo(expectedStage));
            }
            using var restarted = Create();
            await restarted.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await restarted.RequireRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false));

            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(await restarted.GetStageAsync().ConfigureAwait(false), Is.EqualTo(expectedStage));
            store.VerifyNoOtherCalls();
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()),
                Times.Exactly(installing ? 1 : 0));
        }

        /// <summary>
        /// An interrupted reset rejects every administrative peer without publishing candidates.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ResettingRejectsRegistrarPublicationForEveryPeer(bool otherPeer)
        {
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            m_installer.Setup(value => value.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()))
                .Throws(new IOException("Reset remains pending."));
            using var security = Create();
            await Assert.ThrowsAsync<IOException>(async () =>
                await security.ResetToServerDefaultsAsync().ConfigureAwait(false));
            Certificate peer = otherPeer ? m_otherRegistrar : m_firstRegistrar;

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.ReplaceRegistrarEndpointsAsync(
                    Context(peer), store.Object, [Registrar()]).ConfigureAwait(false));

            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Resetting));
            store.VerifyNoOtherCalls();
            m_installer.Verify(value => value.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()), Times.Once);
            m_certificates.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Calling the optional interface directly still requires actual session, role, peer and channel evidence.
        /// </summary>
        [TestCase("role")]
        [TestCase("session")]
        [TestCase("identity")]
        [TestCase("peer")]
        [TestCase("encryption")]
        [TestCase("policy")]
        public async Task RegistrarConfigurationRejectsInvalidEvidenceBeforePublication(string scenario)
        {
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            using var security = Create();
            ProvisionableDeviceSecurity configuration = security;

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await configuration.ReplaceRegistrarEndpointsAsync(
                    Context(m_firstRegistrar, scenario), store.Object, [Registrar()]).ConfigureAwait(false));

            StatusCode expected = scenario switch
            {
                "encryption" => StatusCodes.BadSecurityModeInsufficient,
                "peer" => StatusCodes.BadSecurityChecksFailed,
                _ => StatusCodes.BadUserAccessDenied
            };
            Assert.That(failure!.StatusCode, Is.EqualTo(expected));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            store.VerifyNoOtherCalls();
            m_installer.VerifyNoOtherCalls();
            m_certificates.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Advertising a candidate does not make its actual pull-session certificate trusted.
        /// </summary>
        [Test]
        public async Task PublishedRegistrarMetadataDoesNotBypassPullCertificateTrust()
        {
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            ArrayOf<ApplicationDescription> published = default;
            store.Setup(value => value.ReplaceAsync(
                    It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<ApplicationDescription>, CancellationToken>((values, _) => published = values)
                .Returns(default(ValueTask));
            using var security = Create();
            var runtime = CreateRuntime(store.Object, security);
            await runtime.SetRegistrarEndpointsAsync(
                Context(m_otherRegistrar), [Registrar()]).ConfigureAwait(false);
            m_certificates.Setup(value => value.ValidateAsync(
                    It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                    It.Is<ValidationOptions>(options => options.TrustScope == null),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CertificateValidationResult(false, StatusCodes.BadCertificateUntrusted, [], false));
            await using SessionRequestContextTestContext session = PullSession();

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.InstallFromPullAsync(
                    session.ManagedSession, "urn:first:registrar", Credentials()).ConfigureAwait(false));

            Assert.That(published.Count, Is.EqualTo(1));
            Assert.That(published[0].ApplicationUri, Is.EqualTo("urn:first:registrar"));
            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            m_certificates.Verify(value => value.ValidateAsync(
                It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                It.Is<ValidationOptions>(options => options.TrustScope == null &&
                    options.AutoAcceptUntrustedCertificates == false),
                It.IsAny<CancellationToken>()), Times.Once);
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
            store.Verify(value => value.ReplaceAsync(
                It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// An accepted SecurityAdmin session can publish candidates independently of outbound registrar trust.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InstalledAdminCandidatePublicationDoesNotRequireRegistrarTrust(bool trusted)
        {
            using var security = Create();
            await security.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
            m_certificates.Invocations.Clear();
            ValidationOptions? observedOptions = null;
            string? observedThumbprint = null;
            m_certificates.Setup(value => value.ValidateAsync(
                    It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                    It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()))
                .Returns((CertificateCollection chain, TrustListIdentifier? _, ValidationOptions? options,
                    CancellationToken _) =>
                {
                    observedThumbprint = chain[0].Thumbprint;
                    observedOptions = options;
                    return Task.FromResult(trusted ? CertificateValidationResult.Success :
                        new CertificateValidationResult(false, StatusCodes.BadCertificateUntrusted, [], false));
                });
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            ArrayOf<ApplicationDescription> published = default;
            store.Setup(value => value.ReplaceAsync(
                    It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<ApplicationDescription>, CancellationToken>((values, _) => published = values)
                .Returns(default(ValueTask));
            Certificate caller = trusted ? m_firstRegistrar : m_otherRegistrar;

            await security.ReplaceRegistrarEndpointsAsync(
                Context(caller), store.Object, [Registrar("urn:other:registrar")]).ConfigureAwait(false);

            Assert.That(published.Count, Is.EqualTo(1));
            Assert.That(published[0].ApplicationUri, Is.EqualTo("urn:other:registrar"));
            m_certificates.Verify(value => value.ValidateAsync(
                It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()), Times.Never);
            if (trusted)
            {
                await security.RequireInstalledRegistrarAsync(Context(caller)).ConfigureAwait(false);
            }
            else
            {
                ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await security.RequireInstalledRegistrarAsync(Context(caller)).ConfigureAwait(false));
                Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
            }

            ValidationOptions validation = observedOptions ??
                throw new AssertionException("The authenticated caller was not validated.");
            var trust = validation.TrustScope ??
                throw new AssertionException("Installed trust was not supplied to the validator.");
            Assert.That(observedThumbprint, Is.EqualTo(caller.Thumbprint));
            Assert.That(trust.TrustedCertificates,
                Is.EqualTo(new[] { Encoded(m_firstRegistrar), Encoded(m_dca) }.ToArrayOf()));
            Assert.That(validation.AutoAcceptUntrustedCertificates, Is.False);
            Assert.That(validation.RecordRejectedCertificates, Is.False);
            Assert.That(validation.AcceptError!(
                m_otherRegistrar, new ServiceResult(StatusCodes.BadCertificateUntrusted)), Is.False);
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
            m_certificates.Verify(value => value.ValidateAsync(
                It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()), Times.Once);
            store.Verify(value => value.ReplaceAsync(
                It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()),
                Times.Once);
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Administrative publication cannot bypass an unresolved recovery failure merely because state is Installed.
        /// </summary>
        [Test]
        public async Task InstalledAdminCandidatePublicationRequiresCompletedRecovery()
        {
            using var security = Create();
            await security.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
            m_certificates.Setup(value => value.ValidateAsync(
                    It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                    It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("Installed security recovery could not verify credentials."));
            await Assert.ThrowsAsync<IOException>(
                () => security.RecoverAsync().AsTask()).ConfigureAwait(false);
            m_certificates.Invocations.Clear();
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);

            ServiceResultException? rejected = await Assert.ThrowsAsync<ServiceResultException>(() =>
                security.ReplaceRegistrarEndpointsAsync(
                    Context(m_otherRegistrar), store.Object, [Registrar()]).AsTask()).ConfigureAwait(false);

            Assert.That(rejected!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
            store.VerifyNoOtherCalls();
            m_certificates.Verify(value => value.ValidateAsync(
                It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Direct callers cannot bypass discovery-candidate validation performed by the runtime.
        /// </summary>
        [TestCase("uri")]
        [TestCase("missing")]
        [TestCase("client")]
        [TestCase("empty")]
        [TestCase("endpoint")]
        [TestCase("later")]
        public async Task RegistrarConfigurationRejectsInvalidCandidateBeforePublication(string scenario)
        {
            ApplicationDescription candidate = Registrar();
            switch (scenario)
            {
                case "uri":
                    candidate.ApplicationUri = "relative";
                    break;
                case "missing":
                    candidate = null!;
                    break;
                case "client":
                    candidate.ApplicationType = ApplicationType.Client;
                    break;
                case "empty":
                    candidate.DiscoveryUrls = [];
                    break;
                case "endpoint":
                case "later":
                    candidate.DiscoveryUrls = ["relative"];
                    break;
            }
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            using var security = Create();
            ArrayOf<ApplicationDescription> candidates = scenario == "later"
                ? [Registrar(), candidate]
                : [candidate];

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.ReplaceRegistrarEndpointsAsync(
                    Context(m_firstRegistrar), store.Object, candidates).ConfigureAwait(false));

            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            store.VerifyNoOtherCalls();
            m_installer.VerifyNoOtherCalls();
            m_certificates.VerifyNoOtherCalls();
        }

        /// <summary>
        /// An explicit empty replacement clears candidates without claiming the device.
        /// </summary>
        [Test]
        public async Task RegistrarConfigurationAllowsExplicitClearWithoutClaimingDevice()
        {
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            ArrayOf<ApplicationDescription> published = [Registrar()];
            store.Setup(value => value.ReplaceAsync(
                    It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<ApplicationDescription>, CancellationToken>((values, _) => published = values)
                .Returns(default(ValueTask));
            using var security = Create();

            await security.ReplaceRegistrarEndpointsAsync(
                Context(m_firstRegistrar), store.Object, ArrayOf<ApplicationDescription>.Empty).ConfigureAwait(false);

            Assert.That(published.Count, Is.Zero);
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            store.Verify(value => value.ReplaceAsync(
                It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()), Times.Once);
            m_installer.VerifyNoOtherCalls();
            m_certificates.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A pre-cancelled publication never reads the checkpoint or invokes the registrar store.
        /// </summary>
        [Test]
        public async Task CancelledRegistrarPublicationDoesNotReadOrPublish()
        {
            Mock<ISecretStore> storage = ForwardStorage();
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            using var security = Create(storage.Object);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);

            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await security.ReplaceRegistrarEndpointsAsync(
                    Context(m_firstRegistrar), store.Object, [Registrar()], cancellation.Token).ConfigureAwait(false));

            storage.Verify(value => value.GetAsync(
                It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()), Times.Never);
            storage.Verify(value => value.SetAsync(
                It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()), Times.Never);
            store.VerifyNoOtherCalls();
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
        }

        /// <summary>
        /// A push claim cannot take ownership until the asynchronous candidate replacement has finished.
        /// </summary>
        [Test]
        public async Task PushClaimWaitsForRegistrarPublicationAndBecomesOnlyOwner()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool storeEntered = false;
            bool storeCompleted = false;
            bool checkpointReadDuringStore = false;
            bool checkpointWrittenBeforeStoreCompleted = false;
            Mock<ISecretStore> storage = ForwardStorage();
            storage.Setup(value => value.GetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .Returns((SecretIdentifier id, CancellationToken token) =>
                {
                    if (storeEntered && !storeCompleted)
                    {
                        checkpointReadDuringStore = true;
                    }
                    return m_secrets.GetAsync(id, token);
                });
            storage.Setup(value => value.SetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns((SecretIdentifier id, ReadOnlyMemory<byte> bytes, CancellationToken token) =>
                {
                    if (!storeCompleted)
                    {
                        checkpointWrittenBeforeStoreCompleted = true;
                    }
                    return m_secrets.SetAsync(id, bytes, token);
                });
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            ArrayOf<ApplicationDescription> published = default;
            store.Setup(value => value.ReplaceAsync(
                    It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()))
                .Returns(async (ArrayOf<ApplicationDescription> values, CancellationToken token) =>
                {
                    published = values;
                    storeEntered = true;
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    storeCompleted = true;
                });
            using var security = Create(storage.Object);
            var runtime = CreateRuntime(store.Object, security);
            Task publication = runtime.SetRegistrarEndpointsAsync(
                Context(m_otherRegistrar), [Registrar()]).AsTask();
            await WaitForStoreEntryAsync(entered.Task, publication).ConfigureAwait(false);
            Task claim;
            bool claimWaited;
            bool publicationWaited;
            try
            {
                claim = security.RequireRegistrarAsync(Context(m_firstRegistrar)).AsTask();
                claimWaited = !claim.IsCompleted;
                publicationWaited = !publication.IsCompleted;
            }
            finally
            {
                release.TrySetResult(true);
            }
            await publication.ConfigureAwait(false);
            await claim.ConfigureAwait(false);

            Assert.That(publicationWaited, Is.True);
            Assert.That(claimWaited, Is.True);
            Assert.That(checkpointReadDuringStore, Is.False);
            Assert.That(checkpointWrittenBeforeStoreCompleted, Is.False);
            Assert.That(published.Count, Is.EqualTo(1));
            Assert.That(published[0].ApplicationUri, Is.EqualTo("urn:first:registrar"));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Configuring));
            await security.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.RequireRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false));
            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            storage.Verify(value => value.SetAsync(
                It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            store.Verify(value => value.ReplaceAsync(
                It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()), Times.Once);
            m_installer.VerifyNoOtherCalls();
        }

        /// <summary>
        /// An asynchronously failed store operation releases the gate without reserving the device.
        /// </summary>
        [Test]
        public async Task FailedInFlightRegistrarPublicationReleasesGateWithoutClaimingDevice()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var expected = new IOException("Asynchronous registrar publication failed.");
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            ArrayOf<ApplicationDescription> previous = [Registrar("urn:previous:registrar")];
            store.Setup(value => value.ReplaceAsync(
                    It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()))
                .Returns(async (ArrayOf<ApplicationDescription> _, CancellationToken token) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    throw expected;
                });
            store.Setup(value => value.ReadAsync(It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<ArrayOf<ApplicationDescription>>(previous));
            using var security = Create();
            var runtime = CreateRuntime(store.Object, security);
            Task publication = runtime.SetRegistrarEndpointsAsync(
                Context(m_otherRegistrar), [Registrar()]).AsTask();
            await WaitForStoreEntryAsync(entered.Task, publication).ConfigureAwait(false);
            bool publicationWaited = !publication.IsCompleted;
            release.TrySetResult(true);

            IOException? failure = await Assert.ThrowsAsync<IOException>(async () =>
                await publication.ConfigureAwait(false));
            ArrayOf<ApplicationDescription> retained =
                await store.Object.ReadAsync().ConfigureAwait(false);

            Assert.That(publicationWaited, Is.True);
            Assert.That(failure, Is.SameAs(expected));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            Assert.That(retained.Count, Is.EqualTo(1));
            Assert.That(retained[0].ApplicationUri, Is.EqualTo("urn:previous:registrar"));
            await security.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Configuring));
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.RequireRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false));
            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            store.Verify(value => value.ReplaceAsync(
                It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()), Times.Once);
            store.Verify(value => value.ReadAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Reset cannot run its platform effect while a candidate replacement still owns the security gate.
        /// </summary>
        [Test]
        public async Task ResetWaitsForRegistrarPublicationToComplete()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool storeEntered = false;
            bool storeCompleted = false;
            bool checkpointReadDuringStore = false;
            bool resetObservedCompletedStore = false;
            Mock<ISecretStore> storage = ForwardStorage();
            storage.Setup(value => value.GetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .Returns((SecretIdentifier id, CancellationToken token) =>
                {
                    if (storeEntered && !storeCompleted)
                    {
                        checkpointReadDuringStore = true;
                    }
                    return m_secrets.GetAsync(id, token);
                });
            storage.Setup(value => value.SetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns((SecretIdentifier id, ReadOnlyMemory<byte> bytes, CancellationToken token) =>
                    m_secrets.SetAsync(id, bytes, token));
            var store = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            store.Setup(value => value.ReplaceAsync(
                    It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()))
                .Returns(async (ArrayOf<ApplicationDescription> _, CancellationToken token) =>
                {
                    storeEntered = true;
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    storeCompleted = true;
                });
            m_installer.Setup(value => value.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()))
                .Callback(() => resetObservedCompletedStore = storeCompleted)
                .Returns(default(ValueTask));
            using var security = Create(storage.Object);
            Task publication = security.ReplaceRegistrarEndpointsAsync(
                Context(m_otherRegistrar), store.Object, [Registrar()]).AsTask();
            await WaitForStoreEntryAsync(entered.Task, publication).ConfigureAwait(false);
            Task reset;
            bool resetWaited;
            try
            {
                reset = security.ResetToServerDefaultsAsync().AsTask();
                resetWaited = !reset.IsCompleted;
            }
            finally
            {
                release.TrySetResult(true);
            }
            await publication.ConfigureAwait(false);
            await reset.ConfigureAwait(false);

            Assert.That(resetWaited, Is.True);
            Assert.That(checkpointReadDuringStore, Is.False);
            Assert.That(resetObservedCompletedStore, Is.True);
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            store.Verify(value => value.ReplaceAsync(
                It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()), Times.Once);
            m_installer.Verify(value => value.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// The direct configuration interface snapshots model objects before waiting for another operation.
        /// </summary>
        [Test]
        public async Task RegistrarConfigurationSnapshotsCandidatesBeforeWaitingForGate()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var holdingStore = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            holdingStore.Setup(value => value.ReplaceAsync(
                    It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()))
                .Returns(async (ArrayOf<ApplicationDescription> _, CancellationToken token) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                });
            var receivingStore = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            ArrayOf<ApplicationDescription> published = default;
            receivingStore.Setup(value => value.ReplaceAsync(
                    It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()))
                .Callback<ArrayOf<ApplicationDescription>, CancellationToken>((values, _) => published = values)
                .Returns(default(ValueTask));
            using var security = Create();
            Task first = security.ReplaceRegistrarEndpointsAsync(
                Context(m_firstRegistrar), holdingStore.Object, [Registrar("urn:holding:registrar")]).AsTask();
            await WaitForStoreEntryAsync(entered.Task, first).ConfigureAwait(false);
            ApplicationDescription candidate = Registrar();
            string[] discoveryUrls = ["opc.tcp://registrar:4840"];
            candidate.DiscoveryUrls = discoveryUrls;
            ApplicationDescription[] inputs = [candidate];
            Task second;
            bool secondWaited;
            try
            {
                second = security.ReplaceRegistrarEndpointsAsync(
                    Context(m_otherRegistrar), receivingStore.Object, inputs).AsTask();
                secondWaited = !second.IsCompleted;
                candidate.ApplicationUri = "urn:mutated:registrar";
                candidate.ApplicationName = new LocalizedText("Changed registrar");
                candidate.ProductUri = "urn:mutated:product";
                candidate.GatewayServerUri = "urn:mutated:gateway";
                candidate.DiscoveryProfileUri = "urn:mutated:profile";
                candidate.ApplicationType = ApplicationType.Client;
                discoveryUrls[0] = "opc.tcp://mutated:4840";
                inputs[0] = Registrar("urn:replacement:registrar");
            }
            finally
            {
                release.TrySetResult(true);
            }
            await first.ConfigureAwait(false);
            await second.ConfigureAwait(false);

            Assert.That(secondWaited, Is.True);
            Assert.That(published.Count, Is.EqualTo(1));
            Assert.That(published[0], Is.Not.SameAs(candidate));
            Assert.That(published[0], Is.Not.SameAs(inputs[0]));
            Assert.That(published[0].ApplicationUri, Is.EqualTo("urn:first:registrar"));
            Assert.That(published[0].ApplicationName, Is.EqualTo(new LocalizedText("Registrar One")));
            Assert.That(published[0].ProductUri, Is.EqualTo("urn:product:registrar"));
            Assert.That(published[0].GatewayServerUri, Is.EqualTo("urn:gateway:registrar"));
            Assert.That(published[0].DiscoveryProfileUri, Is.EqualTo("urn:profile:discovery"));
            Assert.That(published[0].ApplicationType, Is.EqualTo(ApplicationType.Server));
            Assert.That(published[0].DiscoveryUrls.ToArray(),
                Is.EqualTo(s_expectedRegistrarDiscoveryUrls));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            holdingStore.Verify(value => value.ReplaceAsync(
                It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()), Times.Once);
            receivingStore.Verify(value => value.ReplaceAsync(
                It.IsAny<ArrayOf<ApplicationDescription>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Application authorization cannot claim an unconfigured device or advance a pending configuration.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InstalledRegistrarCheckNeverClaimsAnUnconfiguredDevice(bool configuring)
        {
            using var security = Create();
            if (configuring)
            {
                await security.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);
            }

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.RequireInstalledRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false));

            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(configuring
                    ? ProvisionableDeviceSecurityStage.Configuring
                    : ProvisionableDeviceSecurityStage.Unconfigured));
            m_certificates.VerifyNoOtherCalls();
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Reset waits for a started application operation, including its asynchronous store or runtime effects.
        /// </summary>
        [Test]
        public async Task ResetWaitsForApplicationOperationToComplete()
        {
            using var security = Create();
            await security.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
            IApplicationConfigurationPushOperations operations = security.CreateApplicationOperations();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool operationCompleted = false;
            bool resetObservedCompletedOperation = false;
            m_installer.Setup(i => i.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()))
                .Callback(() => resetObservedCompletedOperation = operationCompleted)
                .Returns(default(ValueTask));
            Task<int> operation = operations.ExecuteAsync(async () =>
            {
                entered.SetResult(true);
                await release.Task.ConfigureAwait(false);
                operationCompleted = true;
                return 42;
            }).AsTask();
            await entered.Task.ConfigureAwait(false);

            Task reset = security.ResetToServerDefaultsAsync().AsTask();
            bool resetWaited = !reset.IsCompleted;
            release.SetResult(true);
            int result = await operation.ConfigureAwait(false);
            await reset.ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(42));
            Assert.That(resetWaited, Is.True);
            Assert.That(resetObservedCompletedOperation, Is.True);
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
        }

        /// <summary>
        /// A deferred effect from the previous deployment cannot run after reset, even after reinstallation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ResetRevokesApplicationOperationsAcrossReinstallation(bool reinstall)
        {
            using var security = Create();
            await security.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
            IApplicationConfigurationPushOperations previous = security.CreateApplicationOperations();
            await security.ResetToServerDefaultsAsync().ConfigureAwait(false);
            if (reinstall)
            {
                await security.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
            }
            bool invoked = false;

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await previous.ExecuteAsync(() =>
                {
                    invoked = true;
                    return default;
                }).ConfigureAwait(false));

            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(invoked, Is.False);
            if (reinstall)
            {
                await security.CreateApplicationOperations().ExecuteAsync(() =>
                {
                    invoked = true;
                    return default;
                }).ConfigureAwait(false);
                Assert.That(invoked, Is.True);
            }
        }

        /// <summary>
        /// An application failure propagates unchanged and does not prevent later device reset.
        /// </summary>
        [Test]
        public async Task FailedApplicationOperationPreservesFailureAndAllowsReset()
        {
            using var security = Create();
            await security.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
            IApplicationConfigurationPushOperations operations = security.CreateApplicationOperations();
            var expected = new InvalidOperationException("Application activation failed.");

            InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await operations.ExecuteAsync(() => new ValueTask(Task.FromException(expected)))
                    .ConfigureAwait(false));
            await security.ResetToServerDefaultsAsync().ConfigureAwait(false);

            Assert.That(failure, Is.SameAs(expected));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
        }

        /// <summary>
        /// A reset between observing installed state and authorizing an application does not claim a new owner.
        /// </summary>
        [Test]
        public async Task InstalledRegistrarCheckRejectsResetWithoutRecapturingOwnership()
        {
            using var security = Create();
            await security.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
            await security.ResetToServerDefaultsAsync().ConfigureAwait(false);

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.RequireInstalledRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false));

            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            await security.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);
            ServiceResultException? otherOwner = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.RequireRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false));
            Assert.That(otherOwner!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
        }

        /// <summary>
        /// Installed application access validates each authenticated peer against the installed DCA trust.
        /// </summary>
        [Test]
        public async Task InstalledRegistrarCheckRevalidatesPeerWithoutInstallingCredentials()
        {
            using var security = Create();
            await security.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
            m_certificates.Invocations.Clear();

            await security.RequireInstalledRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false);

            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
            m_certificates.Verify(value => value.ValidateAsync(
                It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()), Times.Once);
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// An interrupted installation or reset cannot authorize application access using pending credentials.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InstalledRegistrarCheckRejectsInterruptedSecurityTransition(bool reset)
        {
            using var security = Create();
            var transitionFailure = new IOException("The platform security transition was interrupted.");
            if (reset)
            {
                m_installer.Setup(value => value.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()))
                    .Throws(transitionFailure);
            }
            else
            {
                m_installer.Setup(value => value.InstallAsync(
                    It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()))
                    .Throws(transitionFailure);
            }
            IOException? interrupted = await Assert.ThrowsAsync<IOException>(async () =>
            {
                if (reset)
                {
                    await security.ResetToServerDefaultsAsync().ConfigureAwait(false);
                }
                else
                {
                    await security.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
                }
            });
            m_certificates.Invocations.Clear();

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.RequireInstalledRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false));

            Assert.That(interrupted, Is.SameAs(transitionFailure));
            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(reset
                    ? ProvisionableDeviceSecurityStage.Resetting
                    : ProvisionableDeviceSecurityStage.Installing));
            m_certificates.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Installed status does not hide a registrar validation failure or alter installed credentials.
        /// </summary>
        [Test]
        public async Task InstalledRegistrarCheckPreservesTrustFailure()
        {
            using var security = Create();
            await security.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
            var rejection = new ServiceResultException(StatusCodes.BadCertificateUntrusted);
            m_certificates.Setup(value => value.ValidateAsync(
                    It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                    It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()))
                .Throws(rejection);

            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.RequireInstalledRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false));

            Assert.That(failure, Is.SameAs(rejection));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Failure to retain factory defaults aborts startup without publishing a configured device.
        /// </summary>
        [Test]
        public async Task DefaultPreservationFailureRejectsUnconfiguredStartup()
        {
            var failure = new IOException("Device identity store is unavailable.");
            Mock<IProvisionableDeviceSecurityDefaults> defaults =
                m_installer.As<IProvisionableDeviceSecurityDefaults>();
            defaults.Setup(value => value.PreserveDefaultsAsync(It.IsAny<CancellationToken>())).Throws(failure);
            using var security = Create();

            IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
                await security.RecoverAsync().ConfigureAwait(false));

            Assert.That(error, Is.SameAs(failure));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A direct caller cannot publish configuration ownership or installation before retaining factory defaults.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DefaultPreservationFailureDoesNotClaimDirectConfiguration(bool install)
        {
            var failure = new IOException("Original device security cannot be retained.");
            Mock<IProvisionableDeviceSecurityDefaults> defaults =
                m_installer.As<IProvisionableDeviceSecurityDefaults>();
            defaults.Setup(value => value.PreserveDefaultsAsync(It.IsAny<CancellationToken>())).Throws(failure);
            using var security = Create();

            IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
            {
                if (install)
                {
                    await security.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
                }
                else
                {
                    await security.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);
                }
            });

            Assert.That(error, Is.SameAs(failure));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
            defaults.Setup(value => value.PreserveDefaultsAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            await security.RequireRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false);
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Configuring));
            defaults.Verify(value => value.PreserveDefaultsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        /// <summary>
        /// Recovery cannot replace missing original defaults with the current registrar's operational trust.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RecoveryVerifiesOriginalDefaultsWithoutRecapturingTrust(bool installed)
        {
            var failure = new IOException("The original trust checkpoint is missing.");
            Mock<IProvisionableDeviceSecurityDefaults> defaults =
                m_installer.As<IProvisionableDeviceSecurityDefaults>();
            defaults.Setup(value => value.PreserveDefaultsAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            defaults.Setup(value => value.VerifyDefaultsAsync(It.IsAny<CancellationToken>())).Throws(failure);
            using (var first = Create())
            {
                if (installed)
                {
                    await first.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
                }
                else
                {
                    await first.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);
                }
            }
            using var restarted = Create();

            IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
                await restarted.RecoverAsync().ConfigureAwait(false));

            Assert.That(error, Is.SameAs(failure));
            Assert.That(await restarted.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(installed
                    ? ProvisionableDeviceSecurityStage.Installed
                    : ProvisionableDeviceSecurityStage.Configuring));
            defaults.Verify(value => value.VerifyDefaultsAsync(It.IsAny<CancellationToken>()), Times.Once);
            defaults.Verify(value => value.PreserveDefaultsAsync(It.IsAny<CancellationToken>()), Times.Once);
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()),
                installed ? Times.Once() : Times.Never());
        }

        /// <summary>
        /// The first configuration owner survives restart before any credentials or installer effects exist.
        /// </summary>
        [Test]
        public async Task FirstConfigurationPinsRegistrarBeforeInstallationAcrossRestart()
        {
            Mock<IProvisionableDeviceSecurityDefaults> defaults =
                m_installer.As<IProvisionableDeviceSecurityDefaults>();
            defaults.Setup(value => value.PreserveDefaultsAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            defaults.Setup(value => value.VerifyDefaultsAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            using (var first = Create())
            {
                await first.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);
                Assert.That(await first.GetStageAsync().ConfigureAwait(false),
                    Is.EqualTo(ProvisionableDeviceSecurityStage.Configuring));
            }
            using var restarted = Create();
            await restarted.RecoverAsync().ConfigureAwait(false);
            await restarted.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await restarted.RequireRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false));
            ServiceResultException? installation = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await restarted.InstallAsync(Context(m_otherRegistrar), Credentials()).ConfigureAwait(false));

            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(installation!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(await restarted.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Configuring));
            defaults.Verify(value => value.PreserveDefaultsAsync(It.IsAny<CancellationToken>()), Times.Once);
            defaults.Verify(value => value.VerifyDefaultsAsync(It.IsAny<CancellationToken>()), Times.Once);
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
            m_certificates.VerifyNoOtherCalls();
        }

        /// <summary>
        /// The configuring owner can install, and both supported checkpoint versions retain installation evidence.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConfigurationOwnerInstallsAndRecoversCompatibleCheckpoint(bool legacyVersion)
        {
            Mock<ISecretStore> storage = ForwardStorage();
            SecretIdentifier? captured = null;
            byte[] checkpoint = [];
            storage.Setup(value => value.SetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns((SecretIdentifier id, ReadOnlyMemory<byte> bytes, CancellationToken ct) =>
                {
                    captured = id;
                    checkpoint = bytes.ToArray();
                    return m_secrets.SetAsync(id, bytes, ct);
                });
            using (var first = Create(storage.Object))
            {
                await first.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);
                await first.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
                Assert.That(await first.GetStageAsync().ConfigureAwait(false),
                    Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
            }
            if (legacyVersion)
            {
                checkpoint = checkpoint.AsSpan(0, checkpoint.Length - 1).ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(checkpoint, 1);
                await m_secrets.SetAsync(
                    captured ?? throw new AssertionException("Installation did not publish its checkpoint."),
                    checkpoint).ConfigureAwait(false);
            }
            using var restarted = Create();

            await restarted.RecoverAsync().ConfigureAwait(false);

            Assert.That(await restarted.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        /// <summary>
        /// Standard push records actual stores and verifies them after restart without reinstalling credentials.
        /// </summary>
        [Test]
        public async Task StandardPushCompletesWithoutReinstallAcrossRestart()
        {
            Mock<IProvisionableDeviceAppliedSecurity> applied = m_installer.As<IProvisionableDeviceAppliedSecurity>();
            applied.Setup(value => value.CaptureAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Credentials());
            using (var security = Create())
            {
                await security.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);
                await ((IPushConfigurationCompletion)security).CompleteAsync(Context(m_firstRegistrar))
                    .ConfigureAwait(false);
                Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                    Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
                await security.RequireRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false);
            }
            using var restarted = Create();
            await restarted.RecoverAsync().ConfigureAwait(false);

            Assert.That(await restarted.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
            applied.Verify(value => value.CaptureAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A trust-only commit does not pretend the retained device identity is an installed DCA certificate.
        /// </summary>
        [Test]
        public async Task StandardPushBootstrapTrustOnlyRetainsOwner()
        {
            Mock<IProvisionableDeviceAppliedSecurity> applied = m_installer.As<IProvisionableDeviceAppliedSecurity>();
            applied.Setup(value => value.CaptureAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync((ProvisionableDeviceCredentials?)null);
            using var security = Create();
            await security.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);
            await ((IPushConfigurationCompletion)security).CompleteAsync(Context(m_firstRegistrar))
                .ConfigureAwait(false);
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.RequireRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false));

            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Configuring));
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Recovery observes a committed certificate if the process stopped before recording standard completion.
        /// </summary>
        [Test]
        public async Task StandardPushRecoveryReconcilesUnacknowledgedCommit()
        {
            Mock<IProvisionableDeviceAppliedSecurity> applied = m_installer.As<IProvisionableDeviceAppliedSecurity>();
            applied.Setup(value => value.CaptureAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Credentials());
            using (var security = Create())
            {
                await security.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);
            }
            using var restarted = Create();
            await restarted.RecoverAsync().ConfigureAwait(false);

            Assert.That(await restarted.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Reopening a trust store may enumerate the same authoritative certificates in a different order.
        /// </summary>
        [Test]
        public async Task StandardPushRecoveryAcceptsReorderedTrustStore()
        {
            ProvisionableDeviceCredentials current = Credentials();
            Mock<IProvisionableDeviceAppliedSecurity> applied = m_installer.As<IProvisionableDeviceAppliedSecurity>();
            applied.Setup(value => value.CaptureAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => current);
            using (var security = Create())
            {
                await security.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);
                await ((IPushConfigurationCompletion)security).CompleteAsync(Context(m_firstRegistrar))
                    .ConfigureAwait(false);
            }
            current = Credentials();
            current.TrustList.TrustedCertificates = [Encoded(m_dca), Encoded(m_firstRegistrar)];
            using var restarted = Create();
            await restarted.RecoverAsync().ConfigureAwait(false);

            Assert.That(await restarted.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Recovery cannot replay an applied checkpoint over rolled-back, missing, or independently changed stores.
        /// </summary>
        [TestCase("missing")]
        [TestCase("certificate")]
        [TestCase("trust")]
        [TestCase("issuers")]
        public async Task StandardPushRecoveryRejectsChangedStore(string change)
        {
            ProvisionableDeviceCredentials original = Credentials();
            ProvisionableDeviceCredentials? current = original;
            Mock<IProvisionableDeviceAppliedSecurity> applied = m_installer.As<IProvisionableDeviceAppliedSecurity>();
            applied.Setup(value => value.CaptureAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => current);
            using (var security = Create())
            {
                await security.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);
                await ((IPushConfigurationCompletion)security).CompleteAsync(Context(m_firstRegistrar))
                    .ConfigureAwait(false);
            }
            current = change switch
            {
                "missing" => null,
                "certificate" => original with { Certificate = Encoded(m_otherRegistrar) },
                "trust" => original with
                {
                    TrustList = new TrustListDataType { SpecifiedLists = (uint)TrustListMasks.All }
                },
                "issuers" => original with { IssuerCertificates = [Encoded(m_otherRegistrar)] },
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            };
            using var restarted = Create();
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await restarted.RecoverAsync().ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A store that published before losing its response cannot authorize against rolled-back trust.
        /// </summary>
        [Test]
        public async Task StandardPushUncertainPublicationBlocksAccessAndDetectsRollback()
        {
            ProvisionableDeviceCredentials? current = Credentials();
            Mock<IProvisionableDeviceAppliedSecurity> applied = m_installer.As<IProvisionableDeviceAppliedSecurity>();
            applied.Setup(value => value.CaptureAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => current);
            Mock<ISecretStore> storage = ForwardStorage();
            bool loseResponse = false;
            storage.Setup(value => value.SetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(async (SecretIdentifier id, ReadOnlyMemory<byte> bytes, CancellationToken ct) =>
                {
                    await m_secrets.SetAsync(id, bytes, ct).ConfigureAwait(false);
                    if (loseResponse)
                    {
                        throw new IOException("The device checkpoint response was lost.");
                    }
                });
            using var security = Create(storage.Object);
            await security.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);
            loseResponse = true;
            await Assert.ThrowsAsync<IOException>(async () =>
                await ((IPushConfigurationCompletion)security).CompleteAsync(Context(m_firstRegistrar))
                    .ConfigureAwait(false));
            current = null;
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false));
            ServiceResultException? recovery = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.RecoverAsync().ConfigureAwait(false));

            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(recovery!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Reset releases a pre-installation registrar claim without requiring installed credentials.
        /// </summary>
        [Test]
        public async Task ResetReleasesConfigurationOwnerBeforeCredentialInstallation()
        {
            using var security = Create();
            await security.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);

            await security.ResetToServerDefaultsAsync().ConfigureAwait(false);

            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            await security.RequireRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false);
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false));
            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            m_installer.Verify(value => value.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()), Times.Once);
            m_installer.Verify(value => value.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// An authenticated peer without SecurityAdmin cannot reserve the unconfigured device.
        /// </summary>
        [Test]
        public async Task MissingSecurityAdminCannotClaimConfigurationOwnership()
        {
            using var security = Create();

            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.RequireRegistrarAsync(Context(m_firstRegistrar, "role")).ConfigureAwait(false));

            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            await security.RequireRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false);
            m_installer.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Failed ownership publication cannot permit configuration or reserve the device through volatile state.
        /// </summary>
        [Test]
        public async Task ConfigurationOwnershipWriteFailureDoesNotClaimOrRunPlatformEffects()
        {
            Mock<ISecretStore> storage = ForwardStorage();
            var failure = new IOException("Registrar ownership publication failed.");
            storage.Setup(value => value.SetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Throws(failure);
            using (var first = Create(storage.Object))
            {
                IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
                    await first.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false));
                Assert.That(error, Is.SameAs(failure));
                Assert.That(await first.GetStageAsync().ConfigureAwait(false),
                    Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            }
            using var restarted = Create();
            await restarted.RequireRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false);

            Assert.That(await restarted.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Configuring));
            m_installer.VerifyNoOtherCalls();
            m_certificates.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Installation intent survives a failed installer and is bound to the peer that authenticated the channel.
        /// </summary>
        [Test]
        public async Task FailedInstallationPinsActualPeerAcrossAdapterRestart()
        {
            var failure = new IOException("Device install interrupted.");
            m_installer.Setup(i => i.InstallAsync(
                    It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()))
                .Throws(failure);
            using (var first = Create())
            {
                IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
                    await first.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false));
                Assert.That(error, Is.SameAs(failure));
                Assert.That(await first.GetStageAsync().ConfigureAwait(false),
                    Is.EqualTo(ProvisionableDeviceSecurityStage.Installing));
            }
            using var restarted = Create();
            await restarted.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);
            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await restarted.RequireRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false));
            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            m_installer.Setup(i => i.InstallAsync(
                    It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));

            await restarted.RecoverAsync().ConfigureAwait(false);

            Assert.That(await restarted.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
            m_installer.Verify(i => i.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        /// <summary>
        /// Even the original registrar cannot change material while its previous installation remains unresolved.
        /// </summary>
        [Test]
        public async Task PendingInstallationRejectsDifferentMaterialFromOriginalPeer()
        {
            m_installer.Setup(i => i.InstallAsync(
                    It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()))
                .Throws(new IOException("Pending install."));
            using var security = Create();
            await Assert.ThrowsAsync<IOException>(async () =>
                await security.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false));
            ProvisionableDeviceCredentials changed = Credentials();
            changed.TrustList.TrustedCertificates = [Encoded(m_otherRegistrar)];

            ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.InstallAsync(Context(m_firstRegistrar), changed).ConfigureAwait(false));

            Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installing));
            m_installer.Verify(i => i.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// A later registrar must be validated against installed trust, never the replacement trust it supplies.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SubsequentRegistrarUsesInstalledTrustRatherThanPermanentFirstLeafPin(bool trusted)
        {
            using var security = Create();
            await security.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
            int peerChecks = 0;
            m_certificates.Setup(c => c.ValidateAsync(
                    It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                    It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()))
                .Returns((CertificateCollection chain, TrustListIdentifier? _, ValidationOptions? options,
                    CancellationToken _) =>
                {
                    if (chain[0].RawData.AsSpan().SequenceEqual(m_otherRegistrar.RawData))
                    {
                        peerChecks++;
                        Assert.That(options!.TrustScope!.TrustedCertificates,
                            Is.EqualTo(new[] { Encoded(m_firstRegistrar), Encoded(m_dca) }.ToArrayOf()));
                        Assert.That(options.AutoAcceptUntrustedCertificates, Is.False);
                        Assert.That(options.RecordRejectedCertificates, Is.False);
                        Assert.That(options.AcceptError!(m_otherRegistrar,
                            new ServiceResult(StatusCodes.BadCertificateUntrusted)), Is.False);
                        return Task.FromResult(trusted ? CertificateValidationResult.Success :
                            new CertificateValidationResult(false, StatusCodes.BadCertificateUntrusted, [], false));
                    }
                    return Task.FromResult(CertificateValidationResult.Success);
                });
            ProvisionableDeviceCredentials proposed = Credentials();
            proposed.TrustList.TrustedCertificates = [Encoded(m_otherRegistrar), Encoded(m_dca)];

            if (trusted)
            {
                await security.InstallAsync(Context(m_otherRegistrar), proposed).ConfigureAwait(false);
            }
            else
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await security.InstallAsync(Context(m_otherRegistrar), proposed).ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUntrusted));
            }

            Assert.That(peerChecks, Is.EqualTo(1));
            m_installer.Verify(i => i.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()),
                Times.Exactly(trusted ? 2 : 1));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
        }

        /// <summary>
        /// Publication must precede platform effects; a failure after effects leaves a recoverable pending record.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        public async Task CheckpointPublicationFailurePreservesRecoverableOrdering(int failWrite)
        {
            Mock<ISecretStore> storage = ForwardStorage();
            int writes = 0;
            var failure = new IOException("Checkpoint publication unavailable.");
            storage.Setup(s => s.SetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns((SecretIdentifier id, ReadOnlyMemory<byte> bytes, CancellationToken ct) =>
                {
                    if (++writes == failWrite)
                    {
                        throw failure;
                    }
                    return m_secrets.SetAsync(id, bytes, ct);
                });
            using (var first = Create(storage.Object))
            {
                IOException? actual = await Assert.ThrowsAsync<IOException>(async () =>
                    await first.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false));
                Assert.That(actual, Is.SameAs(failure));
                Assert.That(await first.GetStageAsync().ConfigureAwait(false),
                    Is.EqualTo(failWrite == 1 ? ProvisionableDeviceSecurityStage.Unconfigured :
                        ProvisionableDeviceSecurityStage.Installing));
            }
            using var restarted = Create();
            await restarted.RecoverAsync().ConfigureAwait(false);

            Assert.That(await restarted.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(failWrite == 1 ? ProvisionableDeviceSecurityStage.Unconfigured :
                    ProvisionableDeviceSecurityStage.Installed));
            m_installer.Verify(i => i.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()),
                Times.Exactly(failWrite == 1 ? 0 : 2));
        }

        /// <summary>
        /// Failed security reset blocks onboarding until startup recovery completes the same reset.
        /// </summary>
        [Test]
        public async Task InterruptedResetIsRecoveredBeforeAnotherRegistrarCanInstall()
        {
            using (var first = Create())
            {
                await first.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
                m_installer.Setup(i => i.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()))
                    .Throws(new IOException("Security reset interrupted."));
                await Assert.ThrowsAsync<IOException>(async () =>
                    await first.ResetToServerDefaultsAsync().ConfigureAwait(false));
                Assert.That(await first.GetStageAsync().ConfigureAwait(false),
                    Is.EqualTo(ProvisionableDeviceSecurityStage.Resetting));
                ServiceResultException? blocked = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await first.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false));
                Assert.That(blocked!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await first.InstallAsync(Context(m_otherRegistrar), Credentials()).ConfigureAwait(false));
            }
            m_installer.Setup(i => i.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            using var restarted = Create();

            await restarted.RecoverAsync().ConfigureAwait(false);

            Assert.That(await restarted.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            await restarted.InstallAsync(Context(m_otherRegistrar), Credentials()).ConfigureAwait(false);
            Assert.That(await restarted.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
            m_installer.Verify(i => i.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        /// <summary>
        /// No reset side effect occurs unless its reset intent was published.
        /// </summary>
        [Test]
        public async Task ResetIntentPublicationFailureDoesNotResetPlatformSecurity()
        {
            Mock<ISecretStore> storage = ForwardStorage();
            storage.Setup(s => s.SetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Throws(new IOException("Reset checkpoint unavailable."));
            using var security = Create(storage.Object);

            await Assert.ThrowsAsync<IOException>(async () =>
                await security.ResetToServerDefaultsAsync().ConfigureAwait(false));

            m_installer.Verify(i => i.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()), Times.Never);
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
        }

        /// <summary>
        /// Recovery repeats a completed platform reset only when its completion checkpoint did not commit.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ResetCompletionPublicationFailurePreservesRecoverableState(bool committed)
        {
            Mock<ISecretStore> storage = ForwardStorage();
            int writes = 0;
            var failure = new IOException("Reset completion response lost.");
            storage.Setup(s => s.SetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns(async (SecretIdentifier id, ReadOnlyMemory<byte> bytes, CancellationToken ct) =>
                {
                    if (++writes == 2)
                    {
                        if (committed)
                        {
                            await m_secrets.SetAsync(id, bytes, ct).ConfigureAwait(false);
                        }
                        throw failure;
                    }
                    await m_secrets.SetAsync(id, bytes, ct).ConfigureAwait(false);
                });
            using (var first = Create(storage.Object))
            {
                IOException? actual = await Assert.ThrowsAsync<IOException>(async () =>
                    await first.ResetToServerDefaultsAsync().ConfigureAwait(false));
                Assert.That(actual, Is.SameAs(failure));
                Assert.That(await first.GetStageAsync().ConfigureAwait(false),
                    Is.EqualTo(committed ? ProvisionableDeviceSecurityStage.Unconfigured :
                        ProvisionableDeviceSecurityStage.Resetting));
                if (!committed)
                {
                    ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                        await first.RequireRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false));
                    Assert.That(denied!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                }
            }
            using var restarted = Create();

            await restarted.RecoverAsync().ConfigureAwait(false);

            Assert.That(await restarted.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            await restarted.RequireRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false);
            m_installer.Verify(i => i.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()),
                Times.Exactly(committed ? 1 : 2));
            m_installer.Verify(i => i.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// An installer cannot mutate the public credential snapshot retained for recovery.
        /// </summary>
        [Test]
        public async Task InstallerMutationCannotChangePersistedTrust()
        {
            m_installer.Setup(i => i.InstallAsync(
                    It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()))
                .Callback<ProvisionableDeviceCredentials, CancellationToken>((credentials, _) =>
                    credentials.TrustList.TrustedCertificates = [])
                .Returns(default(ValueTask));
            using var security = Create();
            await security.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
            ArrayOf<ByteString> observed = default;
            m_certificates.Setup(c => c.ValidateAsync(
                    It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                    It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()))
                .Callback<CertificateCollection, TrustListIdentifier?, ValidationOptions?, CancellationToken>(
                    (_, _, options, _) => observed = options!.TrustScope!.TrustedCertificates)
                .ReturnsAsync(CertificateValidationResult.Success);

            await security.RequireRegistrarAsync(Context(m_firstRegistrar)).ConfigureAwait(false);

            Assert.That(observed, Is.EqualTo(Credentials().TrustList.TrustedCertificates));
        }

        /// <summary>
        /// Incomplete trust or a certificate for a different application cannot claim the device.
        /// </summary>
        [TestCase("trust")]
        [TestCase("uri")]
        [TestCase("chain")]
        public async Task InvalidCredentialsDoNotClaimOrInstall(string failure)
        {
            using var security = Create();
            ProvisionableDeviceCredentials credentials = failure == "uri"
                ? Credentials() with { Certificate = Encoded(m_otherRegistrar) }
                : Credentials();
            if (failure == "trust")
            {
                credentials.TrustList.SpecifiedLists = (uint)TrustListMasks.TrustedCertificates;
            }
            if (failure == "chain")
            {
                m_certificates.Setup(c => c.ValidateAsync(
                        It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                        It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new CertificateValidationResult(
                        false, StatusCodes.BadCertificateUntrusted, [], false));
            }

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.InstallAsync(Context(m_firstRegistrar), credentials).ConfigureAwait(false));

            StatusCode expected = failure switch
            {
                "uri" => StatusCodes.BadCertificateUriInvalid,
                "chain" => StatusCodes.BadCertificateUntrusted,
                _ => StatusCodes.BadSecurityChecksFailed
            };
            Assert.That(error!.StatusCode, Is.EqualTo(expected));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            m_installer.Verify(i => i.InstallAsync(
                It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Cancellation before publication does not call either platform operation or change the device state.
        /// </summary>
        [Test]
        public async Task PreCancelledOperationsDoNotChangeDeviceSecurity()
        {
            using var security = Create();
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await security.InstallAsync(Context(m_firstRegistrar), Credentials(), cancellation.Token)
                    .ConfigureAwait(false));
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await security.RecoverAsync(cancellation.Token).ConfigureAwait(false));
            await Assert.CatchAsync<OperationCanceledException>(async () =>
                await security.ResetToServerDefaultsAsync(cancellation.Token).ConfigureAwait(false));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            m_installer.VerifyNoOtherCalls();
        }

        /// <summary>
        /// An install cannot race a local physical reset; the reset runs after the installer has drained.
        /// </summary>
        [Test]
        public async Task PhysicalResetSerializesWithInFlightInstallation()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_installer.Setup(i => i.InstallAsync(
                    It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()))
                .Returns(async (ProvisionableDeviceCredentials _, CancellationToken ct) =>
                {
                    entered.SetResult(true);
                    await release.Task.ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                });
            using var security = Create();
            Task install = security.InstallAsync(Context(m_firstRegistrar), Credentials()).AsTask();
            await entered.Task.ConfigureAwait(false);
            Task reset = security.ResetToServerDefaultsAsync().AsTask();
            bool resetWasPending = !reset.IsCompleted;
            release.SetResult(true);
            await install.ConfigureAwait(false);
            await reset.ConfigureAwait(false);

            Assert.That(resetWasPending, Is.True);
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            m_installer.Verify(i => i.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Persisted corruption cannot enable another registrar, erase a previous installation, or run a reset.
        /// </summary>
        [TestCase("truncated")]
        [TestCase("version")]
        [TestCase("application")]
        [TestCase("stage")]
        [TestCase("owner")]
        [TestCase("legacyOwner")]
        [TestCase("trailing")]
        public async Task CorruptCheckpointRejectsRecoveryRegistrarAccessAndReset(string corruption)
        {
            Mock<ISecretStore> storage = ForwardStorage();
            SecretIdentifier? captured = null;
            byte[] original = [];
            storage.Setup(s => s.SetAsync(
                    It.IsAny<SecretIdentifier>(), It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
                .Returns((SecretIdentifier id, ReadOnlyMemory<byte> bytes, CancellationToken ct) =>
                {
                    captured = id;
                    original = bytes.ToArray();
                    return m_secrets.SetAsync(id, bytes, ct);
                });
            using (var first = Create(storage.Object))
            {
                await first.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
            }
            byte[] damaged;
            if (corruption is "version" or "application" or "stage" or "owner" or "legacyOwner")
            {
                using var buffer = new MemoryStream();
                using (var encoder = new BinaryEncoder(
                    buffer, ServiceMessageContext.Create(NUnitTelemetryContext.Create()), true))
                {
                    uint version = corruption switch
                    {
                        "version" => 9,
                        "owner" => 2,
                        _ => 1
                    };
                    int stage = corruption switch
                    {
                        "stage" => 99,
                        "owner" or "legacyOwner" => (int)ProvisionableDeviceSecurityStage.Configuring,
                        _ => 0
                    };
                    encoder.WriteUInt32(null, version);
                    encoder.WriteString(null, corruption == "application" ? "urn:other:dca" : kApplicationUri);
                    encoder.WriteInt32(null, stage);
                }
                damaged = buffer.ToArray();
            }
            else
            {
                damaged = corruption == "truncated" ? original.Take(5).ToArray() :
                    original.Concat(new byte[] { 1 }).ToArray();
            }
            SecretIdentifier identifier = captured ??
                throw new AssertionException("Installation did not publish its checkpoint.");
            await m_secrets.SetAsync(identifier, damaged).ConfigureAwait(false);
            m_installer.Invocations.Clear();
            using var restarted = Create();

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await restarted.RecoverAsync().ConfigureAwait(false));
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await restarted.RequireRegistrarAsync(Context(m_otherRegistrar)).ConfigureAwait(false));
            var registrars = new Mock<IProvisionableDeviceRegistrarStore>(MockBehavior.Strict);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await restarted.ReplaceRegistrarEndpointsAsync(
                    Context(m_otherRegistrar), registrars.Object, [Registrar()]).ConfigureAwait(false));
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await restarted.ResetToServerDefaultsAsync().ConfigureAwait(false));

            using ISecret? retained = await m_secrets.GetAsync(identifier).ConfigureAwait(false);
            Assert.That(retained!.Bytes.ToArray(), Is.EqualTo(damaged));
            registrars.VerifyNoOtherCalls();
            m_installer.VerifyNoOtherCalls();
        }

        /// <summary>
        /// A role label, unencrypted channel, missing peer, or missing session cannot start installation.
        /// </summary>
        [TestCase("role")]
        [TestCase("encryption")]
        [TestCase("peer")]
        [TestCase("session")]
        public async Task InstallationRequiresAuthenticatedEncryptedSessionAndSecurityAdmin(string failure)
        {
            using var security = Create();
            SessionSystemContext context = Context(m_firstRegistrar, failure);
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.InstallAsync(context, Credentials()).ConfigureAwait(false));

            StatusCode expected = failure switch
            {
                "encryption" => StatusCodes.BadSecurityModeInsufficient,
                "peer" => StatusCodes.BadSecurityChecksFailed,
                _ => StatusCodes.BadUserAccessDenied
            };
            Assert.That(error!.StatusCode, Is.EqualTo(expected));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            m_installer.VerifyNoOtherCalls();
            m_certificates.VerifyNoOtherCalls();
        }

        /// <summary>
        /// The standard server reset path and the application-controlled physical reset resolve the same state owner.
        /// </summary>
        [Test]
        public void FluentRegistrationSharesStandardResetProviderAndRejectsConflicts()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ISecretStore>(m_secrets);
            services.AddSingleton(m_certificates.Object);
            services.AddSingleton(m_installer.Object);
            services.AddSingleton(NUnitTelemetryContext.Create());
            Assert.That(services.AddProvisionableDeviceSecurity("device", kApplicationUri), Is.SameAs(services));
            ServiceDescriptor[] before = services.ToArray();
            Assert.That(() => services.AddProvisionableDeviceSecurity("device", kApplicationUri),
                Throws.InvalidOperationException);
            Assert.That(services.ToArray(), Is.EqualTo(before));
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.That(provider.GetRequiredService<IServerConfigurationResetProvider>(),
                Is.SameAs(provider.GetRequiredService<ProvisionableDeviceSecurity>()));
            Assert.That(provider.GetRequiredService<IProvisionableDeviceSecurity>(),
                Is.SameAs(provider.GetRequiredService<ProvisionableDeviceSecurity>()));
            Assert.That(provider.GetRequiredService<IProvisionableDeviceRegistrarConfiguration>(),
                Is.SameAs(provider.GetRequiredService<ProvisionableDeviceSecurity>()));
            Assert.That(provider.GetRequiredService<IPushConfigurationAuthorization>(),
                Is.SameAs(provider.GetRequiredService<ProvisionableDeviceSecurity>()));
        }

        /// <summary>
        /// A separately registered candidate publisher cannot be replaced by another device security owner.
        /// </summary>
        [Test]
        public void FluentRegistrationRejectsConflictingRegistrarConfigurationWithoutAddingServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ISecretStore>(m_secrets);
            services.AddSingleton(m_certificates.Object);
            services.AddSingleton(m_installer.Object);
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddSingleton(Mock.Of<IProvisionableDeviceRegistrarConfiguration>());
            ServiceDescriptor[] before = services.ToArray();

            Assert.That(() => services.AddProvisionableDeviceSecurity("device", kApplicationUri),
                Throws.InvalidOperationException);

            Assert.That(services.ToArray(), Is.EqualTo(before));
        }

        /// <summary>
        /// Fluent hosting restores installed credentials in the pre-listener phase and propagates recovery errors.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task HostedPreStartupTaskRecoversBeforeOpeningEndpoints(bool installerFails)
        {
            using (var first = Create())
            {
                await first.InstallAsync(Context(m_firstRegistrar), Credentials()).ConfigureAwait(false);
            }
            m_installer.Invocations.Clear();
            var failure = new IOException("Retained installation unavailable.");
            if (installerFails)
            {
                m_installer.Setup(i => i.InstallAsync(
                        It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()))
                    .Throws(failure);
            }
            var services = new ServiceCollection();
            services.AddSingleton<ISecretStore>(m_secrets);
            services.AddSingleton(m_certificates.Object);
            services.AddSingleton(m_installer.Object);
            services.AddSingleton(NUnitTelemetryContext.Create());
            services.AddProvisionableDeviceSecurity("device", kApplicationUri);
            using ServiceProvider provider = services.BuildServiceProvider();
            IServerPreStartupTask startup = provider.GetServices<IServerPreStartupTask>().Single();
            m_installer.VerifyNoOtherCalls();

            if (installerFails)
            {
                IOException? actual = await Assert.ThrowsAsync<IOException>(async () =>
                    await startup.OnServerStartingAsync(Mock.Of<IServerContext>()).ConfigureAwait(false));
                Assert.That(actual, Is.SameAs(failure));
            }
            else
            {
                await startup.OnServerStartingAsync(Mock.Of<IServerContext>()).ConfigureAwait(false);
            }

            m_installer.Verify(i => i.InstallAsync(
                It.Is<ProvisionableDeviceCredentials>(c => c.Certificate == Encoded(m_dca)),
                It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(await provider.GetRequiredService<IProvisionableDeviceSecurity>().GetStageAsync()
                .ConfigureAwait(false), Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
        }

        /// <summary>
        /// Creates an encrypted stack session whose advertised and authenticated peer certificates differ.
        /// </summary>
        private SessionRequestContextTestContext PullSession()
        {
            var session = new SessionRequestContextTestContext(securityMode: MessageSecurityMode.SignAndEncrypt);
            session.Channel.Object.EndpointDescription.Server.ApplicationUri = "urn:first:registrar";
            session.Channel.Object.EndpointDescription.ServerCertificate = Encoded(m_otherRegistrar);
            session.Channel.SetupGet(value => value.ServerChannelCertificate).Returns(m_firstRegistrar.RawData);
            return session;
        }

        /// <summary>
        /// Creates a state machine using the same retained backend on adapter recreation.
        /// </summary>
        private ProvisionableDeviceSecurity Create(ISecretStore? secrets = null)
        {
            return new(secrets ?? m_secrets, "device", kApplicationUri, m_certificates.Object,
                m_installer.Object, NUnitTelemetryContext.Create());
        }

        /// <summary>
        /// Creates the real runtime with unrelated ticket collaborators unused by registrar publication.
        /// </summary>
        private static ProvisionableDeviceRuntime CreateRuntime(
            IProvisionableDeviceRegistrarStore store,
            IProvisionableDeviceSecurity security)
        {
            return new(false, [], Mock.Of<ITicketStore>(), Mock.Of<IOnboardingTicketValidator>(), store, security);
        }

        /// <summary>
        /// Creates discovery metadata without supplying or approving a registrar certificate.
        /// </summary>
        private static ApplicationDescription Registrar(string applicationUri = "urn:first:registrar")
        {
            return new ApplicationDescription
            {
                ApplicationUri = applicationUri,
                ApplicationName = new LocalizedText("Registrar One"),
                ProductUri = "urn:product:registrar",
                GatewayServerUri = "urn:gateway:registrar",
                DiscoveryProfileUri = "urn:profile:discovery",
                ApplicationType = ApplicationType.Server,
                DiscoveryUrls = ["opc.tcp://registrar:4840"]
            };
        }

        /// <summary>
        /// Surfaces an early publication failure instead of waiting forever for a store that was never entered.
        /// </summary>
        private static async Task WaitForStoreEntryAsync(Task entered, Task publication)
        {
            Task completed = await Task.WhenAny(entered, publication)
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            if (completed == publication && !entered.IsCompleted)
            {
                await publication.ConfigureAwait(false);
                throw new AssertionException("Publication completed without reaching the registrar store.");
            }
            await entered.ConfigureAwait(false);
        }

        /// <summary>
        /// Returns complete independent public credentials and explicit registrar trust.
        /// </summary>
        private ProvisionableDeviceCredentials Credentials()
        {
            return new(Encoded(m_dca), [], new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.All,
                TrustedCertificates = [Encoded(m_firstRegistrar), Encoded(m_dca)],
                IssuerCertificates = [],
                TrustedCrls = [],
                IssuerCrls = []
            });
        }

        /// <summary>
        /// Forwards read operations to retained memory with publication controlled by each test.
        /// </summary>
        private Mock<ISecretStore> ForwardStorage()
        {
            var storage = new Mock<ISecretStore>(MockBehavior.Strict);
            storage.SetupGet(s => s.StoreType).Returns(m_secrets.StoreType);
            storage.Setup(s => s.GetAsync(It.IsAny<SecretIdentifier>(), It.IsAny<CancellationToken>()))
                .Returns((SecretIdentifier id, CancellationToken ct) => m_secrets.GetAsync(id, ct));
            return storage;
        }

        /// <summary>
        /// Uses real channel certificate bytes; there is no caller-supplied thumbprint argument.
        /// </summary>
        private static SessionSystemContext Context(Certificate peer, string failure = "")
        {
            var identity = new Mock<IUserIdentity>();
            identity.SetupGet(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.SetupGet(i => i.GrantedRoleIds)
                .Returns(failure == "role" ? [] :
                    new NodeId[] { Opc.Ua.ObjectIds.WellKnownRole_SecurityAdmin }.ToArrayOf());
            var session = new Mock<Opc.Ua.Server.ISession>();
            session.SetupGet(s => s.Id).Returns(failure == "session" ? NodeId.Null : new NodeId("device-session", 0));
            session.SetupGet(s => s.EffectiveIdentity)
                .Returns(failure == "identity" ? null! : identity.Object);
            var channel = new SecureChannelContext("device-channel", new EndpointDescription
            {
                SecurityMode = failure == "encryption" ? MessageSecurityMode.Sign : MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = failure == "policy" ? SecurityPolicies.None : SecurityPolicies.Basic256Sha256
            }, RequestEncoding.Binary, failure == "peer" ? [] : Encoded(peer).ToArray());
            return new SessionSystemContext(new OperationContext(
                new RequestHeader(), channel, RequestType.Call, RequestLifetime.None, session.Object),
                NUnitTelemetryContext.Create())
            {
                NamespaceUris = new NamespaceTable()
            };
        }

        /// <summary>
        /// Creates an exportable test key with the exact application URI under test.
        /// </summary>
        private static Certificate CreateCertificate(string applicationUri)
        {
            return CertificateBuilder.Create("CN=Provisionable Device Test")
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(TimeSpan.FromDays(2))
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, ["localhost"]))
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        /// <summary>
        /// Returns independently owned public certificate bytes.
        /// </summary>
        private static ByteString Encoded(Certificate certificate)
        {
            return ByteString.From(certificate.RawData.AsSpan());
        }

        /// <summary>The unchanged discovery URL retained across candidate publication.</summary>
        private static readonly string[] s_expectedRegistrarDiscoveryUrls = ["opc.tcp://registrar:4840"];

        /// <summary>The URI owned by this DCA.</summary>
        private const string kApplicationUri = "urn:device:dca";

        /// <summary>The first registrar's actual channel certificate.</summary>
        private Certificate m_firstRegistrar = null!;

        /// <summary>A different registrar's actual channel certificate.</summary>
        private Certificate m_otherRegistrar = null!;

        /// <summary>The device's issued DCA certificate.</summary>
        private Certificate m_dca = null!;

        /// <summary>The retained memory backend, not a durable-storage demonstration.</summary>
        private InMemorySecretStore m_secrets = null!;

        /// <summary>The injected scoped validator.</summary>
        private Mock<ICertificateManager> m_certificates = null!;

        /// <summary>The explicitly mocked platform operations.</summary>
        private Mock<IProvisionableDeviceSecurityInstaller> m_installer = null!;
    }
}
