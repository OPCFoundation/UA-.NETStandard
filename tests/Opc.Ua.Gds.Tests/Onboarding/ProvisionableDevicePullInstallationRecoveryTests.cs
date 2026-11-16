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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Verifies the serialized security owner's bound pull installation and recovery transitions.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class ProvisionableDevicePullInstallationRecoveryTests
    {
        /// <summary>
        /// Creates the retained backend, authenticated peer, and independently mocked platform operations.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_peer = CreateCertificate(kRegistrarUri);
            m_device = CreateCertificate(kApplicationUri);
            m_secrets = new InMemorySecretStore();
            m_certificates = new Mock<ICertificateManager>(MockBehavior.Strict);
            m_certificates.Setup(manager => manager.ValidateAsync(
                    It.IsAny<CertificateCollection>(), TrustListIdentifier.Peers,
                    It.IsAny<ValidationOptions>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(CertificateValidationResult.Success);
            m_installer = new Mock<IProvisionableDeviceSecurityInstaller>(MockBehavior.Strict);
            m_pullInstaller = m_installer.As<IProvisionableDevicePullSecurityInstaller>();
            m_applied = m_installer.As<IProvisionableDeviceAppliedSecurity>();
            m_journal = new Mock<IProvisionableDevicePullInstallationJournal>(MockBehavior.Strict);
            m_pullInstaller.Setup(value => value.RequirePullKeyAsync(
                    It.IsAny<Guid>(), It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            m_pullInstaller.Setup(value => value.VerifyInstalledPullKeyAsync(
                    It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            m_journal.Setup(value => value.RequireReceivedAsync(
                    It.IsAny<Guid>(), It.IsAny<ByteString>(), It.IsAny<ProvisionableDeviceCredentials>(),
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            m_journal.Setup(value => value.RecordInstalledAsync(
                    It.IsAny<Guid>(), It.IsAny<ByteString>(), It.IsAny<ProvisionableDeviceCredentials>(),
                    It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
        }

        /// <summary>
        /// Releases the fixture-owned certificates and telemetry.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            m_peer.Dispose();
            m_device.Dispose();
            (m_telemetry as IDisposable)?.Dispose();
        }

        /// <summary>
        /// Matching stores cannot bypass endpoint activation after the first installation was interrupted.
        /// </summary>
        [Test]
        public async Task RecoveryReplaysInterruptedActivationEvenWhenAppliedStoresMatch()
        {
            ProvisionableDeviceCredentials credentials = Credentials();
            Guid attemptId = Guid.NewGuid();
            int installations = 0;
            var failure = new IOException("Endpoint activation interrupted.");
            m_pullInstaller.Setup(value => value.InstallForPullAsync(
                    attemptId, It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()))
                .Returns(() => Interlocked.Increment(ref installations) == 1
                    ? new ValueTask(Task.FromException(failure))
                    : default);
            m_applied.Setup(value => value.CaptureAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(credentials);
            await using SessionRequestContextTestContext session = PullSession();
            using (var original = Create())
            {
                IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
                    await original.InstallFromPullAsync(
                        session.ManagedSession, kRegistrarUri, credentials, attemptId).ConfigureAwait(false));
                Assert.That(error, Is.SameAs(failure));
                Assert.That(await original.GetStageAsync().ConfigureAwait(false),
                    Is.EqualTo(ProvisionableDeviceSecurityStage.Installing));
                m_applied.Verify(value => value.CaptureAsync(It.IsAny<CancellationToken>()), Times.Never);
            }

            using var restarted = Create();
            await restarted.RecoverAsync().ConfigureAwait(false);
            Assert.That(installations, Is.EqualTo(2));
            Assert.That(await restarted.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
            m_applied.Verify(value => value.CaptureAsync(It.IsAny<CancellationToken>()), Times.Once);
            m_journal.Verify(value => value.RecordInstalledAsync(
                attemptId, Encoded(m_peer), It.IsAny<ProvisionableDeviceCredentials>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        /// <summary>
        /// Missing attempt key evidence rejects installation before an Installing checkpoint is written.
        /// </summary>
        [Test]
        public async Task MissingPullKeyIsRejectedBeforeInstallingCheckpoint()
        {
            Guid attemptId = Guid.NewGuid();
            m_pullInstaller.Setup(value => value.RequirePullKeyAsync(
                    attemptId, It.IsAny<ByteString>(), It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadNotSupported));
            await using SessionRequestContextTestContext session = PullSession();
            using var security = Create();

            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.InstallFromPullAsync(
                    session.ManagedSession, kRegistrarUri, Credentials(), attemptId).ConfigureAwait(false));

            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
            m_pullInstaller.Verify(value => value.InstallForPullAsync(
                It.IsAny<Guid>(), It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()),
                Times.Never);
            m_journal.Verify(value => value.RecordInstalledAsync(
                It.IsAny<Guid>(), It.IsAny<ByteString>(), It.IsAny<ProvisionableDeviceCredentials>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// An uncertain journal promotion is reported and must be reconciled before another installation.
        /// </summary>
        [Test]
        public async Task FailedPromotionBlocksNewInstallationUntilRecoveryPublishesIt()
        {
            ProvisionableDeviceCredentials credentials = Credentials();
            Guid attemptId = Guid.NewGuid();
            int publications = 0;
            var failure = new IOException("Journal promotion failed.");
            m_pullInstaller.Setup(value => value.InstallForPullAsync(
                    attemptId, It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            m_applied.Setup(value => value.CaptureAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(credentials);
            m_journal.Setup(value => value.RecordInstalledAsync(
                    attemptId, It.IsAny<ByteString>(), It.IsAny<ProvisionableDeviceCredentials>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => Interlocked.Increment(ref publications) == 1
                    ? new ValueTask(Task.FromException(failure))
                    : default);
            await using SessionRequestContextTestContext session = PullSession();
            using var security = Create();

            IOException? error = await Assert.ThrowsAsync<IOException>(async () =>
                await security.InstallFromPullAsync(
                    session.ManagedSession, kRegistrarUri, credentials, attemptId).ConfigureAwait(false));
            Assert.That(error, Is.SameAs(failure));
            ServiceResultException? blocked = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.InstallFromPullAsync(
                    session.ManagedSession, kRegistrarUri, credentials, attemptId).ConfigureAwait(false));
            Assert.That(blocked!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));

            await security.RecoverAsync().ConfigureAwait(false);
            Assert.That(publications, Is.EqualTo(2));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
            m_pullInstaller.Verify(value => value.InstallForPullAsync(
                attemptId, It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()),
                Times.Once);
            m_applied.Verify(value => value.CaptureAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        /// <summary>
        /// Installed recovery cannot publish matching registry data when the persisted key is missing.
        /// </summary>
        [Test]
        public async Task RecoveryDoesNotPromoteWhenPersistedIssuedKeyIsMissing()
        {
            ProvisionableDeviceCredentials credentials = Credentials();
            Guid attemptId = Guid.NewGuid();
            int publications = 0;
            m_pullInstaller.Setup(value => value.InstallForPullAsync(
                    attemptId, It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            m_applied.Setup(value => value.CaptureAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(credentials);
            m_journal.Setup(value => value.RecordInstalledAsync(
                    attemptId, It.IsAny<ByteString>(), It.IsAny<ProvisionableDeviceCredentials>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => Interlocked.Increment(ref publications) == 1
                    ? new ValueTask(Task.FromException(new IOException("Journal promotion failed.")))
                    : default);
            await using SessionRequestContextTestContext session = PullSession();
            using var security = Create();
            await Assert.ThrowsAsync<IOException>(async () =>
                await security.InstallFromPullAsync(
                    session.ManagedSession, kRegistrarUri, credentials, attemptId).ConfigureAwait(false));
            m_pullInstaller.Setup(value => value.VerifyInstalledPullKeyAsync(
                    credentials.Certificate, It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadSecurityChecksFailed));

            ServiceResultException? missing = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await security.RecoverAsync().ConfigureAwait(false));
            Assert.That(missing!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(publications, Is.EqualTo(1));

            m_pullInstaller.Setup(value => value.VerifyInstalledPullKeyAsync(
                    credentials.Certificate, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            await security.RecoverAsync().ConfigureAwait(false);
            Assert.That(publications, Is.EqualTo(2));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Installed));
        }

        /// <summary>
        /// Reset cannot invalidate a request until installation verification and journal promotion finish.
        /// </summary>
        [Test]
        public async Task ResetWaitsForInstallationVerificationAndPromotion()
        {
            ProvisionableDeviceCredentials credentials = Credentials();
            Guid attemptId = Guid.NewGuid();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var events = new List<string>();
            m_pullInstaller.Setup(value => value.InstallForPullAsync(
                    attemptId, It.IsAny<ProvisionableDeviceCredentials>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    events.Add("install");
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                });
            m_applied.Setup(value => value.CaptureAsync(It.IsAny<CancellationToken>()))
                .Callback(() => events.Add("capture"))
                .ReturnsAsync(credentials);
            m_journal.Setup(value => value.RecordInstalledAsync(
                    attemptId, It.IsAny<ByteString>(), It.IsAny<ProvisionableDeviceCredentials>(),
                    It.IsAny<CancellationToken>()))
                .Callback(() => events.Add("promote"))
                .Returns(default(ValueTask));
            m_journal.Setup(value => value.BeginResetAsync(It.IsAny<CancellationToken>()))
                .Callback(() => events.Add("begin-reset"))
                .Returns(default(ValueTask));
            m_installer.Setup(value => value.ResetToServerDefaultsAsync(It.IsAny<CancellationToken>()))
                .Callback(() => events.Add("reset"))
                .Returns(default(ValueTask));
            m_journal.Setup(value => value.CompleteResetAsync(It.IsAny<CancellationToken>()))
                .Callback(() => events.Add("complete-reset"))
                .Returns(default(ValueTask));
            await using SessionRequestContextTestContext session = PullSession();
            using var security = Create();
            Task installing = security.InstallFromPullAsync(
                session.ManagedSession, kRegistrarUri, credentials, attemptId).AsTask();
            Task? resetting = null;
            try
            {
                await entered.Task.WaitAsync(s_timeout).ConfigureAwait(false);
                resetting = security.ResetToServerDefaultsAsync().AsTask();
                Assert.That(resetting.IsCompleted, Is.False);
                m_journal.Verify(value => value.BeginResetAsync(It.IsAny<CancellationToken>()), Times.Never);
            }
            finally
            {
                release.TrySetResult(true);
                await ObserveCompletionAsync(installing).ConfigureAwait(false);
                if (resetting != null)
                {
                    await ObserveCompletionAsync(resetting).ConfigureAwait(false);
                }
            }
            await installing.WaitAsync(s_timeout).ConfigureAwait(false);
            await resetting!.WaitAsync(s_timeout).ConfigureAwait(false);
            Assert.That(events, Is.EqualTo(s_expectedInstallationRecoveryEvents));
            Assert.That(await security.GetStageAsync().ConfigureAwait(false),
                Is.EqualTo(ProvisionableDeviceSecurityStage.Unconfigured));
        }

        /// <summary>
        /// Observes an interrupted operation before its fixture-owned collaborators are disposed.
        /// </summary>
        private static async Task ObserveCompletionAsync(Task operation)
        {
            try
            {
                await operation.WaitAsync(s_timeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                throw;
            }
            catch (Exception)
            {
                // The test asserts the operation's outcome after cleanup.
            }
        }

        /// <summary>
        /// Reopens one security owner over the same retained backend and test collaborators.
        /// </summary>
        private ProvisionableDeviceSecurity Create()
        {
            return new ProvisionableDeviceSecurity(
                m_secrets, "device", kApplicationUri, m_certificates.Object,
                m_installer.Object, m_telemetry, m_journal.Object);
        }

        /// <summary>
        /// Creates a confidential session with the registrar's actual certificate.
        /// </summary>
        private SessionRequestContextTestContext PullSession()
        {
            var session = new SessionRequestContextTestContext(
                telemetry: m_telemetry, securityMode: MessageSecurityMode.SignAndEncrypt);
            session.Channel.Object.EndpointDescription.Server.ApplicationUri = kRegistrarUri;
            session.Channel.SetupGet(value => value.ServerChannelCertificate).Returns(m_peer.RawData);
            return session;
        }

        /// <summary>
        /// Returns complete credentials, including every trust-list field.
        /// </summary>
        private ProvisionableDeviceCredentials Credentials()
        {
            return new ProvisionableDeviceCredentials(
                Encoded(m_device), [],
                new TrustListDataType
                {
                    SpecifiedLists = (uint)TrustListMasks.All,
                    TrustedCertificates = [Encoded(m_peer), Encoded(m_device)],
                    TrustedCrls = [],
                    IssuerCertificates = [],
                    IssuerCrls = []
                });
        }

        /// <summary>
        /// Creates a keyed application certificate valid for the named URI.
        /// </summary>
        private static Certificate CreateCertificate(string applicationUri)
        {
            return CertificateBuilder.Create("CN=Pull Recovery")
                .SetNotBefore(DateTime.UtcNow.AddDays(-1))
                .SetLifeTime(TimeSpan.FromDays(2))
                .AddExtension(new X509SubjectAltNameExtension(applicationUri, ["localhost"]))
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        /// <summary>
        /// Copies the public certificate encoding.
        /// </summary>
        private static ByteString Encoded(Certificate certificate)
        {
            return ByteString.From(certificate.RawData.AsSpan());
        }

        /// <summary>The DCA application URI.</summary>
        private const string kApplicationUri = "urn:device:pull-recovery";

        /// <summary>The authenticated registrar application URI.</summary>
        private const string kRegistrarUri = "urn:registrar:pull-recovery";

        /// <summary>The maximum synchronization wait.</summary>
        private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(30);

        /// <summary>The installation and reset events expected after recovery.</summary>
        private static readonly string[] s_expectedInstallationRecoveryEvents =
        [
            "install", "capture", "promote", "begin-reset", "reset", "complete-reset"
        ];

        /// <summary>The telemetry context.</summary>
        private ITelemetryContext m_telemetry = null!;

        /// <summary>The authenticated registrar.</summary>
        private Certificate m_peer = null!;

        /// <summary>The received DCA certificate.</summary>
        private Certificate m_device = null!;

        /// <summary>The state backend shared by recreated owners.</summary>
        private InMemorySecretStore m_secrets = null!;

        /// <summary>The registrar and credential validator.</summary>
        private Mock<ICertificateManager> m_certificates = null!;

        /// <summary>The platform installation operations.</summary>
        private Mock<IProvisionableDeviceSecurityInstaller> m_installer = null!;

        /// <summary>The attempt-bound installation capability.</summary>
        private Mock<IProvisionableDevicePullSecurityInstaller> m_pullInstaller = null!;

        /// <summary>The applied-material verification capability.</summary>
        private Mock<IProvisionableDeviceAppliedSecurity> m_applied = null!;

        /// <summary>The retained pull installation journal.</summary>
        private Mock<IProvisionableDevicePullInstallationJournal> m_journal = null!;
    }
}
