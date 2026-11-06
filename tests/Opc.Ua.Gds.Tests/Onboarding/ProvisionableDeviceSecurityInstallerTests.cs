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
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Verifies installation, immutable identity retention, endpoint activation, and security-only reset.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class ProvisionableDeviceSecurityInstallerTests
    {
        /// <summary>
        /// Creates one application identity for the installer and isolates all store operations.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_identity = CreateCertificate(kApplicationUri);
            m_activeCertificate = m_identity.AddRef();
            m_events = [];
            m_storeCertificates = [];
            m_secrets = new InMemorySecretStore();
            m_certificates = new Mock<ICertificateManager>(MockBehavior.Strict);
            m_stores = new Mock<ICertificateStoreResolver>(MockBehavior.Strict);
            m_pendingKeys = new Mock<IPeekablePendingCertificateKeyStore>(MockBehavior.Strict);
            m_activator = new Mock<IServerApplicationCertificateActivator>(MockBehavior.Strict);
            m_applicationStore = CreateStore("application");
            m_identityStore = CreateStore("identity", enumeratePublicCertificates: true);

            m_stores.Setup(s => s.OpenCertificateStore(
                    kApplicationStorePath, kStoreType, false))
                .Returns(m_applicationStore.Object);
            m_stores.Setup(s => s.OpenCertificateStore(
                    kIdentityStorePath, kStoreType, false))
                .Returns(m_identityStore.Object);
            m_certificates.Setup(c => c.AcquireApplicationCertificateByType(
                    CertificateIdentifier.GetCertificateType(m_identity)))
                .Returns(() => new CertificateEntry(
                    m_activeCertificate, new CertificateCollection(),
                    CertificateIdentifier.GetCertificateType(m_identity)));
            m_certificates.Setup(c => c.ReadTrustListAsync(
                    TrustListIdentifier.Peers, TrustListMasks.All, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new TrustListData());
            m_certificates.Setup(c => c.WriteTrustListAsync(
                    TrustListIdentifier.Peers, It.IsAny<TrustListData>(), TrustListMasks.All,
                    It.IsAny<CancellationToken>()))
                .Callback<TrustListIdentifier, TrustListData, TrustListMasks, CancellationToken>(
                    (_, _, _, _) => m_events.Add("trust"))
                .Returns(Task.CompletedTask);
            m_certificates.Setup(c => c.UpdateApplicationCertificateAsync(
                    CertificateIdentifier.GetCertificateType(m_identity),
                    It.IsAny<Certificate>(),
                    It.IsAny<CertificateCollection?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<NodeId, Certificate, CertificateCollection?, CancellationToken>(
                    (_, certificate, _, _) =>
                    {
                        Assert.That(certificate.HasPrivateKey, Is.True);
                        Certificate previous = m_activeCertificate;
                        m_activeCertificate = certificate.AddRef();
                        previous.Dispose();
                        m_events.Add("registry");
                    })
                .Returns(Task.CompletedTask);
            m_pendingKeys.Setup(s => s.TryPeekMatchingAsync(
                    It.IsAny<PendingCertificateKeyContext>(),
                    It.IsAny<Certificate>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<Certificate?>((Certificate?)null));
            m_pendingKeys.Setup(s => s.RemoveAsync(
                    It.IsAny<PendingCertificateKeyContext>(),
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            m_activator.Setup(a => a.ActivateAsync(
                    CertificateIdentifier.GetCertificateType(m_identity),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Callback<NodeId, string, CancellationToken>((_, thumbprint, _) =>
                {
                    Assert.That(thumbprint, Is.EqualTo(m_activeCertificate.Thumbprint));
                    m_events.Add("activate");
                })
                .Returns(default(ValueTask));
        }

        /// <summary>
        /// Releases the generated private key and mock-owned stores.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            m_identity.Dispose();
            m_activeCertificate.Dispose();
            if (m_rotatedCertificate != null)
            {
                m_rotatedCertificate.Dispose();
            }
            foreach (List<Certificate> certificates in m_storeCertificates.Values)
            {
                foreach (Certificate certificate in certificates)
                {
                    certificate.Dispose();
                }
            }
        }

        /// <summary>
        /// Retains identity and private key separately before changing trust or activating the application slot.
        /// </summary>
        [Test]
        public async Task InstallRetainsIdentityBeforeTrustAndEndpointActivation()
        {
            using var installer = CreateInstaller();

            await installer.InstallAsync(Credentials(m_identity)).ConfigureAwait(false);

            Assert.That(m_events, Is.EqualTo(s_expectedInstalledEvents));
            m_identityStore.Verify(s => s.AddAsync(
                It.Is<Certificate>(certificate =>
                    certificate.Thumbprint == m_identity.Thumbprint && certificate.HasPrivateKey),
                It.IsAny<char[]?>(),
                It.IsAny<CancellationToken>()), Times.Once);
            m_applicationStore.Verify(s => s.AddAsync(
                It.Is<Certificate>(certificate => certificate.HasPrivateKey),
                It.IsAny<char[]?>(),
                It.IsAny<CancellationToken>()), Times.Once);
            m_pendingKeys.Verify(s => s.TryPeekMatchingAsync(
                It.IsAny<PendingCertificateKeyContext>(),
                It.IsAny<Certificate>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Software approval is required before changing keys, trust or endpoints, and a later approval permits retry.
        /// </summary>
        [Test]
        public async Task IncompleteSoftwarePreventsInstallationUntilPolicyApproves()
        {
            bool ready = false;
            using var deadline = new CancellationTokenSource();
            var policy = new Mock<IProvisionableDeviceSoftwareUpdatePolicy>(MockBehavior.Strict);
            policy.Setup(value => value.RequireReadyAsync(kApplicationUri, deadline.Token))
                .Returns(() => ready ? default : new ValueTask(Task.FromException(
                    new ServiceResultException(StatusCodes.BadInvalidState, "Software installation is incomplete."))));
            using var installer = CreateInstaller(softwareUpdates: policy.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await installer.InstallAsync(Credentials(m_identity), deadline.Token)
                    .ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(m_events, Is.Empty);
            Assert.That(m_activeCertificate.Thumbprint, Is.EqualTo(m_identity.Thumbprint));
            m_pendingKeys.Verify(value => value.TryPeekMatchingAsync(
                It.IsAny<PendingCertificateKeyContext>(), It.IsAny<Certificate>(),
                It.IsAny<CancellationToken>()), Times.Never);
            ready = true;

            await installer.InstallAsync(Credentials(m_identity), deadline.Token).ConfigureAwait(false);

            policy.Verify(value => value.RequireReadyAsync(kApplicationUri, deadline.Token), Times.Exactly(2));
            m_activator.Verify(value => value.ActivateAsync(
                It.IsAny<NodeId>(), m_identity.Thumbprint, deadline.Token), Times.Once);
        }

        /// <summary>
        /// Standard push completion and a fresh installer's recovery cannot accept a DCA certificate without approval.
        /// </summary>
        [Test]
        public async Task AppliedCredentialsRequireSoftwareApprovalAfterInstallerRestart()
        {
            bool ready = false;
            var policy = new Mock<IProvisionableDeviceSoftwareUpdatePolicy>(MockBehavior.Strict);
            policy.Setup(value => value.RequireReadyAsync(kApplicationUri, It.IsAny<CancellationToken>()))
                .Returns(() => ready ? default : new ValueTask(Task.FromException(
                    new ServiceResultException(StatusCodes.BadInvalidState, "Software installation is incomplete."))));
            using (var original = CreateInstaller(softwareUpdates: policy.Object))
            {
                await original.PreserveDefaultsAsync().ConfigureAwait(false);
            }
            using Certificate replacement = CreateCertificate(kApplicationUri);
            Certificate previous = m_activeCertificate;
            m_activeCertificate = replacement.AddRef();
            previous.Dispose();
            m_events.Clear();
            using var restarted = CreateInstaller(softwareUpdates: policy.Object);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await restarted.CaptureAsync().ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(m_events, Is.EqualTo(s_expectedResetEvents));
            m_events.Clear();
            ready = true;

            ProvisionableDeviceCredentials? captured = await restarted.CaptureAsync().ConfigureAwait(false);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.Certificate.Span.ToArray(), Is.EqualTo(replacement.RawData));
            Assert.That(m_events, Is.EqualTo(s_expectedResetEvents));
            policy.Verify(value => value.RequireReadyAsync(kApplicationUri, It.IsAny<CancellationToken>()),
                Times.Exactly(2));
            m_activator.Verify(value => value.ActivateAsync(
                It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Bootstrap defaults, factory identity capture and security reset do not require or alter software approval.
        /// </summary>
        [Test]
        public async Task FactorySecurityRemainsAvailableWhileSoftwareIsIncomplete()
        {
            var policy = new Mock<IProvisionableDeviceSoftwareUpdatePolicy>(MockBehavior.Strict);
            using var installer = CreateInstaller(softwareUpdates: policy.Object);

            await installer.PreserveDefaultsAsync().ConfigureAwait(false);
            ProvisionableDeviceCredentials? captured = await installer.CaptureAsync().ConfigureAwait(false);
            await installer.ResetToServerDefaultsAsync().ConfigureAwait(false);

            Assert.That(captured, Is.Null);
            Assert.That(m_activeCertificate.Thumbprint, Is.EqualTo(m_identity.Thumbprint));
            policy.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Fluent policy registration reaches the container-owned installer and cannot be silently replaced.
        /// </summary>
        [Test]
        public async Task FluentSoftwarePolicyAppliesToContainerOwnedInstaller()
        {
            var policy = new Mock<IProvisionableDeviceSoftwareUpdatePolicy>(MockBehavior.Strict);
            policy.Setup(value => value.RequireReadyAsync(kApplicationUri, It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadInvalidState, "Software approval is missing."));
            var services = new ServiceCollection();
            services.AddSingleton<ISecretStore>(m_secrets);
            services.AddSingleton(m_certificates.Object);
            services.AddSingleton(m_stores.Object);
            services.AddSingleton(m_pendingKeys.Object);
            services.AddSingleton<IPendingCertificateKeyStore>(m_pendingKeys.Object);
            services.AddSingleton(m_activator.Object);
            services.AddSingleton(NUnitTelemetryContext.Create());
            Assert.That(services.AddProvisionableDeviceSoftwareUpdatePolicy(_ => policy.Object), Is.SameAs(services));
            int count = services.Count;
            Assert.That(() => services.AddProvisionableDeviceSoftwareUpdatePolicy(_ => policy.Object),
                Throws.InvalidOperationException);
            Assert.That(services, Has.Count.EqualTo(count));
            services.AddProvisionableDeviceSecurity("device", kApplicationUri, CreateOptions());
            using ServiceProvider provider = services.BuildServiceProvider();
            IProvisionableDeviceSecurityInstaller installer = provider
                .GetRequiredService<IProvisionableDeviceSecurityInstaller>();

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(
                async () => await installer.InstallAsync(Credentials(m_identity)).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(provider.GetRequiredService<IProvisionableDeviceSoftwareUpdatePolicy>(),
                Is.SameAs(policy.Object));
            Assert.That(m_events, Is.Empty);
            policy.Verify(value => value.RequireReadyAsync(kApplicationUri, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Retained startup defaults survive a standard configuration change outside the installer.
        /// </summary>
        [Test]
        public async Task StartupPreservesIdentityBeforeExternalApplicationCertificateReplacement()
        {
            using var installer = CreateInstaller();
            await installer.PreserveDefaultsAsync().ConfigureAwait(false);
            Certificate previous = m_activeCertificate;
            using Certificate replacement = CreateCertificate(kApplicationUri);
            m_activeCertificate = replacement.AddRef();
            previous.Dispose();
            m_events.Clear();

            await installer.PreserveDefaultsAsync().ConfigureAwait(false);
            await installer.ResetToServerDefaultsAsync().ConfigureAwait(false);

            Assert.That(m_activeCertificate.Thumbprint, Is.EqualTo(m_identity.Thumbprint));
            Assert.That(m_storeCertificates["identity"].Single().Thumbprint, Is.EqualTo(m_identity.Thumbprint));
            m_identityStore.Verify(value => value.AddAsync(
                It.IsAny<Certificate>(), It.IsAny<char[]?>(), It.IsAny<CancellationToken>()), Times.Once);
            m_certificates.Verify(value => value.ReadTrustListAsync(
                TrustListIdentifier.Peers, TrustListMasks.All, It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Verification reads retained defaults without creating or replacing them, even after trust has changed.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DefaultVerificationNeverRecapturesMissingOriginalTrust(bool removeDefaults)
        {
            using var installer = CreateInstaller();
            await installer.PreserveDefaultsAsync().ConfigureAwait(false);
            var id = new SecretIdentifier(
                "ProvisionableDeviceDefaults-" +
                    Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("device"))),
                m_secrets.StoreType);
            if (removeDefaults)
            {
                Assert.That(await m_secrets.RemoveAsync(id).ConfigureAwait(false), Is.True);
            }
            m_events.Clear();
            if (removeDefaults)
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await installer.VerifyDefaultsAsync().ConfigureAwait(false));
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                using ISecret? missing = await m_secrets.GetAsync(id).ConfigureAwait(false);
                Assert.That(missing, Is.Null);
            }
            else
            {
                await installer.VerifyDefaultsAsync().ConfigureAwait(false);
            }
            Assert.That(m_events, Has.Count.EqualTo(1));
            Assert.That(m_events[0], Is.EqualTo("load-identity"));
            m_identityStore.Verify(value => value.AddAsync(
                It.IsAny<Certificate>(), It.IsAny<char[]?>(), It.IsAny<CancellationToken>()), Times.Once);
            m_certificates.Verify(value => value.ReadTrustListAsync(
                TrustListIdentifier.Peers, TrustListMasks.All, It.IsAny<CancellationToken>()), Times.Once);
            m_certificates.Verify(value => value.WriteTrustListAsync(
                It.IsAny<TrustListIdentifier>(), It.IsAny<TrustListData>(), It.IsAny<TrustListMasks>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Losing original defaults cannot turn operational trust into a factory reset baseline.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task MissingOriginalDefaultsRejectPreservationResetAndInstallationAfterIdentityRotation(
            int operation)
        {
            using var installer = CreateInstaller();
            await installer.PreserveDefaultsAsync().ConfigureAwait(false);
            var id = new SecretIdentifier(
                "ProvisionableDeviceDefaults-" +
                    Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("device"))),
                m_secrets.StoreType);
            Assert.That(await m_secrets.RemoveAsync(id).ConfigureAwait(false), Is.True);
            using Certificate replacement = CreateCertificate(kApplicationUri);
            Certificate previous = m_activeCertificate;
            m_activeCertificate = replacement.AddRef();
            previous.Dispose();
            m_events.Clear();

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                switch (operation)
                {
                    case 0:
                        await installer.PreserveDefaultsAsync().ConfigureAwait(false);
                        break;
                    case 1:
                        await installer.ResetToServerDefaultsAsync().ConfigureAwait(false);
                        break;
                    default:
                        await installer.InstallAsync(Credentials(replacement)).ConfigureAwait(false);
                        break;
                }
            });

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(m_activeCertificate.RawData, Is.EqualTo(replacement.RawData));
            Assert.That(m_events, Has.Count.EqualTo(1));
            Assert.That(m_events[0], Is.EqualTo("load-identity"));
            using ISecret? missing = await m_secrets.GetAsync(id).ConfigureAwait(false);
            Assert.That(missing, Is.Null);
            m_certificates.Verify(value => value.ReadTrustListAsync(
                TrustListIdentifier.Peers, TrustListMasks.All, It.IsAny<CancellationToken>()), Times.Once);
            m_certificates.Verify(value => value.WriteTrustListAsync(
                It.IsAny<TrustListIdentifier>(), It.IsAny<TrustListData>(), It.IsAny<TrustListMasks>(),
                It.IsAny<CancellationToken>()), Times.Never);
            m_activator.Verify(value => value.ActivateAsync(
                It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Observing standard push reads retained defaults and the actual keyed slot without another installation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CaptureReadsAppliedSlotWithoutMutatingStores(bool replaceIdentity)
        {
            using var installer = CreateInstaller();
            await installer.PreserveDefaultsAsync().ConfigureAwait(false);
            using Certificate replacement = CreateCertificate(kApplicationUri);
            if (replaceIdentity)
            {
                Certificate previous = m_activeCertificate;
                m_activeCertificate = replacement.AddRef();
                previous.Dispose();
            }
            m_events.Clear();

            ProvisionableDeviceCredentials? captured = await installer.CaptureAsync().ConfigureAwait(false);

            if (replaceIdentity)
            {
                Assert.That(captured, Is.Not.Null);
                Assert.That(captured!.Certificate.Span.ToArray(), Is.EqualTo(replacement.RawData));
                Assert.That(captured.TrustList.SpecifiedLists, Is.EqualTo((uint)TrustListMasks.All));
            }
            else
            {
                Assert.That(captured, Is.Null);
            }
            Assert.That(m_events, Is.EqualTo(s_expectedResetEvents));
            m_certificates.Verify(value => value.WriteTrustListAsync(
                It.IsAny<TrustListIdentifier>(), It.IsAny<TrustListData>(), It.IsAny<TrustListMasks>(),
                It.IsAny<CancellationToken>()), Times.Never);
            m_activator.Verify(value => value.ActivateAsync(
                It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Capture cannot treat a post-installation certificate as a newly discovered factory default.
        /// </summary>
        [Test]
        public async Task CaptureRequiresPreviouslyRetainedIdentity()
        {
            using var installer = CreateInstaller();
            await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await installer.CaptureAsync().ConfigureAwait(false));

            Assert.That(m_events, Is.Empty);
            m_identityStore.Verify(value => value.AddAsync(
                It.IsAny<Certificate>(), It.IsAny<char[]?>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Public certificate presence does not establish possession of the selected DCA private key.
        /// </summary>
        [Test]
        public async Task CaptureRejectsSlotWithoutPrivateKey()
        {
            using var installer = CreateInstaller();
            await installer.PreserveDefaultsAsync().ConfigureAwait(false);
            Certificate previous = m_activeCertificate;
            using Certificate replacement = CreateCertificate(kApplicationUri);
            m_activeCertificate = Certificate.FromRawData(replacement.RawData);
            previous.Dispose();
            m_events.Clear();

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await installer.CaptureAsync().ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(m_events, Is.EqualTo(s_expectedResetEvents));
        }

        /// <summary>
        /// A retained trust checkpoint cannot be applied after moving the installer to another certificate group.
        /// </summary>
        [Test]
        public async Task ResetRejectsTrustDefaultsFromAnotherConfiguredScope()
        {
            using (var original = CreateInstaller())
            {
                await original.InstallAsync(Credentials(m_identity)).ConfigureAwait(false);
            }
            m_events.Clear();
            ProvisionableDeviceSecurityInstallerOptions options = CreateOptions();
            options.CertificateGroupId = new NodeId("other-group", 0);
            using var replacement = CreateInstaller(options: options);

            await Assert.ThrowsAsync<System.IO.InvalidDataException>(async () =>
                await replacement.ResetToServerDefaultsAsync().ConfigureAwait(false));

            Assert.That(m_events, Is.EqualTo(s_expectedResetEvents));
            Assert.That(m_activeCertificate.Thumbprint, Is.EqualTo(m_identity.Thumbprint));
        }

        /// <summary>
        /// Rejects certificates whose OPC UA certificate type is not the selected application slot.
        /// </summary>
        [Test]
        public async Task InstallRejectsCertificateTypeMismatchBeforeStoreMutation()
        {
            using var installer = CreateInstaller(Opc.Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType);

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await installer.InstallAsync(Credentials(m_identity)).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUseNotAllowed));
            Assert.That(m_events, Is.Empty);
            m_stores.Verify(s => s.OpenCertificateStore(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
            m_certificates.Verify(c => c.WriteTrustListAsync(
                It.IsAny<TrustListIdentifier>(), It.IsAny<TrustListData>(), It.IsAny<TrustListMasks>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Rejects certificates whose application URI is not the configured device URI.
        /// </summary>
        [Test]
        public async Task InstallRejectsApplicationUriMismatchBeforeStoreMutation()
        {
            using Certificate invalidCertificate = CreateCertificate("urn:device:other");
            using var installer = CreateInstaller();

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await installer.InstallAsync(Credentials(invalidCertificate)).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadCertificateUriInvalid));
            Assert.That(m_events, Is.Empty);
            m_stores.Verify(s => s.OpenCertificateStore(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
            m_certificates.Verify(c => c.WriteTrustListAsync(
                It.IsAny<TrustListIdentifier>(), It.IsAny<TrustListData>(), It.IsAny<TrustListMasks>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Restores a claimed signing key when endpoint activation fails after certificate registration.
        /// </summary>
        [Test]
        public async Task InstallRestoresClaimedPendingKeyWhenEndpointActivationFails()
        {
            Certificate rotatedCertificate = CreateCertificate(kApplicationUri);
            m_rotatedCertificate = rotatedCertificate;
            m_pendingKeys.Setup(s => s.TryPeekMatchingAsync(
                    It.IsAny<PendingCertificateKeyContext>(),
                    It.IsAny<Certificate>(),
                    It.IsAny<CancellationToken>()))
                .Returns((PendingCertificateKeyContext _, Certificate _, CancellationToken _) =>
                    new ValueTask<Certificate?>(rotatedCertificate.AddRef()));
            m_pendingKeys.Setup(s => s.TryTakeMatchingAsync(
                    It.IsAny<PendingCertificateKeyContext>(),
                    It.IsAny<Certificate>(),
                    It.IsAny<CancellationToken>()))
                .Returns((PendingCertificateKeyContext _, Certificate _, CancellationToken _) =>
                {
                    m_events.Add("claim");
                    return new ValueTask<Certificate?>(rotatedCertificate.AddRef());
                });
            m_pendingKeys.Setup(s => s.TryRestoreAsync(
                    It.IsAny<PendingCertificateKeyContext>(),
                    It.IsAny<Certificate>(),
                    It.IsAny<CancellationToken>()))
                .Returns((PendingCertificateKeyContext _, Certificate _, CancellationToken _) =>
                {
                    m_events.Add("restore");
                    return new ValueTask<bool>(true);
                });
            m_activator.Setup(a => a.ActivateAsync(
                    CertificateIdentifier.GetCertificateType(m_identity),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns((NodeId _, string _, CancellationToken _) =>
                    throw new InvalidOperationException("Endpoint activation failed."));
            using var installer = CreateInstaller();

            InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await installer.InstallAsync(Credentials(rotatedCertificate)).ConfigureAwait(false));

            Assert.That(error!.Message, Is.EqualTo("Endpoint activation failed."));
            Assert.That(
                m_events,
                Is.EqualTo(s_expectedRestoreEvents));
            m_pendingKeys.Verify(s => s.TryRestoreAsync(
                It.IsAny<PendingCertificateKeyContext>(),
                It.IsAny<Certificate>(),
                CancellationToken.None), Times.Once);
        }

        /// <summary>
        /// Restores the separately retained device identity and original trust state without changing software.
        /// </summary>
        [Test]
        public async Task ResetRestoresIdentityTrustAndActiveEndpointCertificate()
        {
            Certificate rotatedCertificate = CreateCertificate(kApplicationUri);
            m_rotatedCertificate = rotatedCertificate;
            m_pendingKeys.Setup(s => s.TryPeekMatchingAsync(
                    It.IsAny<PendingCertificateKeyContext>(),
                    It.IsAny<Certificate>(),
                    It.IsAny<CancellationToken>()))
                .Returns((PendingCertificateKeyContext _, Certificate _, CancellationToken _) =>
                    new ValueTask<Certificate?>(rotatedCertificate.AddRef()));
            m_pendingKeys.Setup(s => s.TryTakeMatchingAsync(
                    It.IsAny<PendingCertificateKeyContext>(),
                    It.IsAny<Certificate>(),
                    It.IsAny<CancellationToken>()))
                .Returns((PendingCertificateKeyContext _, Certificate _, CancellationToken _) =>
                {
                    m_events.Add("claim");
                    return new ValueTask<Certificate?>(rotatedCertificate.AddRef());
                });
            using var installer = CreateInstaller();

            await installer.InstallAsync(Credentials(rotatedCertificate)).ConfigureAwait(false);
            Assert.That(
                m_events,
                Is.EqualTo(s_expectedRotationEvents));
            Assert.That(m_activeCertificate.Thumbprint, Is.EqualTo(rotatedCertificate.Thumbprint));
            Assert.That(m_storeCertificates["identity"].Single().Thumbprint, Is.EqualTo(m_identity.Thumbprint));
            m_events.Clear();
            await installer.ResetToServerDefaultsAsync().ConfigureAwait(false);

            Assert.That(
                m_events,
                Is.EqualTo(s_expectedActivationEvents));
            Assert.That(m_activeCertificate.Thumbprint, Is.EqualTo(m_identity.Thumbprint));
            Assert.That(m_storeCertificates["identity"].Single().Thumbprint, Is.EqualTo(m_identity.Thumbprint));
            Assert.That(m_storeCertificates["application"].Select(c => c.Thumbprint),
                Does.Contain(m_identity.Thumbprint).And.Not.Contain(rotatedCertificate.Thumbprint));
            m_pendingKeys.Verify(s => s.RemoveAsync(
                It.IsAny<PendingCertificateKeyContext>(), It.IsAny<CancellationToken>()), Times.Once);
            m_pendingKeys.Verify(s => s.TryTakeMatchingAsync(
                It.IsAny<PendingCertificateKeyContext>(),
                It.IsAny<Certificate>(),
                It.IsAny<CancellationToken>()), Times.Once);
            m_identityStore.Verify(s => s.LoadPrivateKeyAsync(
                m_identity.Thumbprint,
                null,
                kApplicationUri,
                CertificateIdentifier.GetCertificateType(m_identity),
                It.IsAny<char[]?>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Rejects a claimed private key that differs from the key observed by the non-consuming peek.
        /// </summary>
        [Test]
        public async Task InstallRejectsAndRestoresClaimedKeyThatDoesNotMatchCertificate()
        {
            using Certificate mismatchedClaim = CreateCertificate(kApplicationUri);
            m_pendingKeys.Setup(s => s.TryPeekMatchingAsync(
                    It.IsAny<PendingCertificateKeyContext>(),
                    It.IsAny<Certificate>(),
                    It.IsAny<CancellationToken>()))
                .Returns((PendingCertificateKeyContext _, Certificate _, CancellationToken _) =>
                    new ValueTask<Certificate?>(m_identity.AddRef()));
            m_pendingKeys.Setup(s => s.TryTakeMatchingAsync(
                    It.IsAny<PendingCertificateKeyContext>(),
                    It.IsAny<Certificate>(),
                    It.IsAny<CancellationToken>()))
                .Returns((PendingCertificateKeyContext _, Certificate _, CancellationToken _) =>
                {
                    m_events.Add("claim");
                    return new ValueTask<Certificate?>(mismatchedClaim.AddRef());
                });
            m_pendingKeys.Setup(s => s.TryRestoreAsync(
                    It.IsAny<PendingCertificateKeyContext>(),
                    It.IsAny<Certificate>(),
                    It.IsAny<CancellationToken>()))
                .Returns((PendingCertificateKeyContext _, Certificate _, CancellationToken _) =>
                {
                    m_events.Add("restore");
                    return new ValueTask<bool>(true);
                });
            using var installer = CreateInstaller();

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await installer.InstallAsync(Credentials(m_identity)).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(m_events, Is.EqualTo(s_expectedFailedRestoreEvents));
            m_pendingKeys.Verify(s => s.TryRestoreAsync(
                It.IsAny<PendingCertificateKeyContext>(),
                It.Is<Certificate>(certificate =>
                    certificate.Thumbprint == mismatchedClaim.Thumbprint),
                CancellationToken.None), Times.Once);
        }

        /// <summary>
        /// Reports when a claimed signing key cannot be restored after endpoint activation fails.
        /// </summary>
        [Test]
        public async Task InstallReportsWhenClaimedPendingKeyCannotBeRestored()
        {
            Certificate rotatedCertificate = CreateCertificate(kApplicationUri);
            m_rotatedCertificate = rotatedCertificate;
            m_pendingKeys.Setup(s => s.TryPeekMatchingAsync(
                    It.IsAny<PendingCertificateKeyContext>(),
                    It.IsAny<Certificate>(),
                    It.IsAny<CancellationToken>()))
                .Returns((PendingCertificateKeyContext _, Certificate _, CancellationToken _) =>
                    new ValueTask<Certificate?>(rotatedCertificate.AddRef()));
            m_pendingKeys.Setup(s => s.TryTakeMatchingAsync(
                    It.IsAny<PendingCertificateKeyContext>(),
                    It.IsAny<Certificate>(),
                    It.IsAny<CancellationToken>()))
                .Returns((PendingCertificateKeyContext _, Certificate _, CancellationToken _) =>
                    new ValueTask<Certificate?>(rotatedCertificate.AddRef()));
            m_pendingKeys.Setup(s => s.TryRestoreAsync(
                    It.IsAny<PendingCertificateKeyContext>(),
                    It.IsAny<Certificate>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<bool>(false));
            m_activator.Setup(a => a.ActivateAsync(
                    CertificateIdentifier.GetCertificateType(m_identity),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns((NodeId _, string _, CancellationToken _) =>
                    throw new InvalidOperationException("Endpoint activation failed."));
            using var installer = CreateInstaller();

            AggregateException? error = await Assert.ThrowsAsync<AggregateException>(async () =>
                await installer.InstallAsync(Credentials(rotatedCertificate)).ConfigureAwait(false));

            Assert.That(error!.Message, Does.Contain("pending signing key could not be restored"));
            Assert.That(error.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(error.InnerExceptions[0].Message, Is.EqualTo("Endpoint activation failed."));
            Assert.That(error.InnerExceptions[1].Message, Does.Contain("scope is no longer empty"));
        }

        /// <summary>
        /// Keeps the configured store, type, and certificate-group values after caller mutation of options.
        /// </summary>
        [Test]
        public async Task InstallerSnapshotsCertificateStoreOptionsAtConstruction()
        {
            var observedPasswords = new List<(string? StorePath, string? StoreType)>();
            var passwordProvider = new Mock<ICertificatePasswordProvider>(MockBehavior.Strict);
            passwordProvider.Setup(provider => provider.GetPassword(It.IsAny<CertificateIdentifier>()))
                .Callback<CertificateIdentifier>(identifier =>
                    observedPasswords.Add((identifier.StorePath, identifier.StoreType)))
                .Returns([]);
            ProvisionableDeviceSecurityInstallerOptions options = CreateOptions();
            options.CertificatePasswordProvider = passwordProvider.Object;
            using var installer = CreateInstaller(options: options);
            Certificate rotatedCertificate = CreateCertificate(kApplicationUri);
            m_rotatedCertificate = rotatedCertificate;
            m_pendingKeys.Setup(s => s.TryPeekMatchingAsync(
                    It.IsAny<PendingCertificateKeyContext>(),
                    It.IsAny<Certificate>(),
                    It.IsAny<CancellationToken>()))
                .Returns((PendingCertificateKeyContext _, Certificate _, CancellationToken _) =>
                    new ValueTask<Certificate?>(rotatedCertificate.AddRef()));
            m_pendingKeys.Setup(s => s.TryTakeMatchingAsync(
                    It.IsAny<PendingCertificateKeyContext>(),
                    It.IsAny<Certificate>(),
                    It.IsAny<CancellationToken>()))
                .Returns((PendingCertificateKeyContext _, Certificate _, CancellationToken _) =>
                    new ValueTask<Certificate?>(rotatedCertificate.AddRef()));

            options.ApplicationCertificate!.StorePath = "mutated-application-store";
            options.ApplicationCertificate.StoreType = "mutated-application-type";
            options.ApplicationCertificate.CertificateType =
                Opc.Ua.ObjectTypeIds.EccNistP256ApplicationCertificateType;
            options.DeviceIdentityStorePath = "mutated-identity-store";
            options.DeviceIdentityStoreType = "mutated-identity-type";
            options.CertificateGroupId = new NodeId(99999u);

            await installer.InstallAsync(Credentials(rotatedCertificate)).ConfigureAwait(false);
            await installer.ResetToServerDefaultsAsync().ConfigureAwait(false);

            Assert.That(m_activeCertificate.Thumbprint, Is.EqualTo(m_identity.Thumbprint));
            Assert.That(m_storeCertificates["identity"].Single().Thumbprint, Is.EqualTo(m_identity.Thumbprint));
            Assert.That(observedPasswords, Does.Contain((kIdentityStorePath, kStoreType)));
            Assert.That(observedPasswords, Does.Contain((kApplicationStorePath, kStoreType)));
            m_pendingKeys.Verify(s => s.TryPeekMatchingAsync(
                It.Is<PendingCertificateKeyContext>(context =>
                    context.BaseStore.StorePath == kApplicationStorePath &&
                    context.CertificateGroupId ==
                        Opc.Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup),
                It.IsAny<Certificate>(),
                It.IsAny<CancellationToken>()), Times.Once);
            m_stores.Verify(s => s.OpenCertificateStore(
                kApplicationStorePath, kStoreType, false), Times.Exactly(2));
            m_stores.Verify(s => s.OpenCertificateStore(
                kIdentityStorePath, kStoreType, false), Times.Exactly(2));
            passwordProvider.Verify(
                provider => provider.GetPassword(It.IsAny<CertificateIdentifier>()), Times.AtLeastOnce);
        }

        /// <summary>
        /// Creates a store mock that records writes and returns independent snapshots.
        /// </summary>
        private Mock<ICertificateStore> CreateStore(string name, bool enumeratePublicCertificates = false)
        {
            var storedCertificates = new List<Certificate>();
            m_storeCertificates.Add(name, storedCertificates);
            var store = new Mock<ICertificateStore>(MockBehavior.Strict);
            store.Setup(s => s.Dispose());
            store.SetupGet(s => s.SupportsLoadPrivateKey).Returns(enumeratePublicCertificates);
            store.Setup(s => s.EnumerateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    var snapshot = new CertificateCollection();
                    foreach (Certificate certificate in storedCertificates)
                    {
                        if (enumeratePublicCertificates)
                        {
                            using Certificate publicCertificate = Certificate.FromRawData(certificate.RawData);
                            snapshot.Add(publicCertificate);
                        }
                        else
                        {
                            snapshot.Add(certificate);
                        }
                    }
                    return snapshot;
                });
            store.Setup(s => s.LoadPrivateKeyAsync(
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<NodeId>(),
                    It.IsAny<char[]?>(),
                    It.IsAny<CancellationToken>()))
                .Returns((string thumbprint, string? _, string? _, NodeId _, char[]? _, CancellationToken _) =>
                {
                    if (enumeratePublicCertificates)
                    {
                        m_events.Add("load-identity");
                    }
                    Certificate? certificate = storedCertificates.FirstOrDefault(c =>
                        string.Equals(c.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase) &&
                        c.HasPrivateKey);
                    return Task.FromResult<Certificate?>(certificate?.AddRef());
                });
            store.Setup(s => s.FindByThumbprintAsync(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string thumbprint, CancellationToken _) =>
                {
                    var matches = new CertificateCollection();
                    foreach (Certificate certificate in storedCertificates.Where(c =>
                        string.Equals(c.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase)))
                    {
                        matches.Add(certificate);
                    }
                    return matches;
                });
            store.Setup(s => s.AddAsync(
                    It.IsAny<Certificate>(), It.IsAny<char[]?>(), It.IsAny<CancellationToken>()))
                .Callback<Certificate, char[]?, CancellationToken>((certificate, _, _) =>
                {
                    if (!storedCertificates.Any(existing =>
                        string.Equals(existing.Thumbprint, certificate.Thumbprint, StringComparison.OrdinalIgnoreCase)))
                    {
                        storedCertificates.Add(certificate.AddRef());
                    }
                    m_events.Add(name);
                })
                .Returns(Task.CompletedTask);
            store.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string thumbprint, CancellationToken _) =>
                {
                    Certificate? certificate = storedCertificates.FirstOrDefault(c =>
                        string.Equals(c.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase));
                    if (certificate == null)
                    {
                        return false;
                    }
                    storedCertificates.Remove(certificate);
                    certificate.Dispose();
                    return true;
                });
            return store;
        }

        /// <summary>
        /// Creates a concrete installer with a distinct identity store.
        /// </summary>
        private ProvisionableDeviceSecurityInstaller CreateInstaller(
            NodeId certificateType = default,
            ProvisionableDeviceSecurityInstallerOptions? options = null,
            IProvisionableDeviceSoftwareUpdatePolicy? softwareUpdates = null)
        {
            options ??= CreateOptions(certificateType);
            return new ProvisionableDeviceSecurityInstaller(
                m_secrets,
                "device",
                kApplicationUri,
                m_certificates.Object,
                m_stores.Object,
                m_pendingKeys.Object,
                m_activator.Object,
                options,
                NUnitTelemetryContext.Create(),
                softwareUpdates);
        }

        /// <summary>
        /// Creates the mutable installer options used to verify constructor-time configuration snapshots.
        /// </summary>
        private ProvisionableDeviceSecurityInstallerOptions CreateOptions(NodeId certificateType = default)
        {
            return new ProvisionableDeviceSecurityInstallerOptions
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StorePath = kApplicationStorePath,
                    StoreType = kStoreType,
                    CertificateType = certificateType.IsNull
                        ? CertificateIdentifier.GetCertificateType(m_identity)
                        : certificateType
                },
                DeviceIdentityStorePath = kIdentityStorePath,
                DeviceIdentityStoreType = kStoreType,
                CertificateGroupId = Opc.Ua.ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup
            };
        }

        /// <summary>
        /// Builds one complete device installation request from the supplied application certificate.
        /// </summary>
        private static ProvisionableDeviceCredentials Credentials(Certificate certificate)
        {
            var trustList = new TrustListDataType
            {
                SpecifiedLists = (uint)TrustListMasks.All
            };
            return new(
                ByteString.From(certificate.RawData.AsSpan()),
                [],
                trustList);
        }

        /// <summary>
        /// Creates an application certificate with a private key and matching application URI.
        /// </summary>
        private static Certificate CreateCertificate(string applicationUri)
        {
            return CertificateBuilder.Create("CN=Provisionable Device Installer Test")
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(TimeSpan.FromDays(2))
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, ["localhost"]))
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        /// <summary>
        /// The installer event order for a successful initial installation.
        /// </summary>
        private static readonly string[] s_expectedInstalledEvents =
            ["identity", "trust", "application", "registry", "activate"];
        /// <summary>
        /// The event order for resetting server-managed defaults.
        /// </summary>
        private static readonly string[] s_expectedResetEvents = ["load-identity"];
        /// <summary>
        /// The event order for restoring a pending certificate.
        /// </summary>
        private static readonly string[] s_expectedRestoreEvents =
            ["identity", "claim", "trust", "application", "registry", "restore"];
        /// <summary>
        /// The event order for rotating the application certificate.
        /// </summary>
        private static readonly string[] s_expectedRotationEvents =
            ["identity", "claim", "trust", "application", "registry", "activate"];
        /// <summary>
        /// The event order for activating an existing application certificate.
        /// </summary>
        private static readonly string[] s_expectedActivationEvents =
            ["load-identity", "trust", "application", "registry", "activate"];
        /// <summary>
        /// The event order retained when pending-key restoration fails.
        /// </summary>
        private static readonly string[] s_expectedFailedRestoreEvents = ["identity", "claim", "restore"];

        /// <summary>The application URI authorized by these tests.</summary>
        private const string kApplicationUri = "urn:device:installer";

        /// <summary>The rotating application certificate store.</summary>
        private const string kApplicationStorePath = "application-store";

        /// <summary>The independent immutable device identity store.</summary>
        private const string kIdentityStorePath = "device-identity-store";

        /// <summary>The certificate store implementation used by the mocks.</summary>
        private const string kStoreType = CertificateStoreType.Directory;

        /// <summary>The setup-time device identity certificate.</summary>
        private Certificate m_identity = null!;

        /// <summary>The current certificate registered in the mocked certificate manager.</summary>
        private Certificate m_activeCertificate = null!;

        /// <summary>The application certificate used to exercise a private-key rotation.</summary>
        private Certificate? m_rotatedCertificate;

        /// <summary>The test secret provider for reset defaults.</summary>
        private InMemorySecretStore m_secrets = null!;

        /// <summary>The certificate manager and trust-list manager.</summary>
        private Mock<ICertificateManager> m_certificates = null!;

        /// <summary>The store resolver for the active and immutable stores.</summary>
        private Mock<ICertificateStoreResolver> m_stores = null!;

        /// <summary>The pending-key store supporting non-consuming matching.</summary>
        private Mock<IPeekablePendingCertificateKeyStore> m_pendingKeys = null!;

        /// <summary>The explicit server endpoint activation seam.</summary>
        private Mock<IServerApplicationCertificateActivator> m_activator = null!;

        /// <summary>The active application certificate store mock.</summary>
        private Mock<ICertificateStore> m_applicationStore = null!;

        /// <summary>The separate device identity store mock.</summary>
        private Mock<ICertificateStore> m_identityStore = null!;

        /// <summary>The ordered certificate, trust, and activation effects.</summary>
        private List<string> m_events = null!;

        /// <summary>Certificate handles retained by the two stateful certificate-store mocks.</summary>
        private Dictionary<string, List<Certificate>> m_storeCertificates = null!;
    }
}
