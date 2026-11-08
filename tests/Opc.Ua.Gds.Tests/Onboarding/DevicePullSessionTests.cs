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

#if NET10_0_OR_GREATER
using System;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using OnboardingDevice;
using Opc.Ua.Client;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Verifies ownership of the one-shot discovery session used by device pull.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class DevicePullSessionTests
    {
        /// <summary>
        /// The workflow and security installer borrow the same attempt-owned session, even when installation fails.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task WorkflowAndInstallerBorrowTheSelectedAttemptSession(bool failInstallation)
        {
            await using var context = new SessionRequestContextTestContext();
            int created = 0;
            var sessions = new DevicePullSession(_ =>
            {
                created++;
                return Task.FromResult(context.ManagedSession);
            });
            ByteString identity = ByteString.From(new byte[] { 1 });
            ByteString csr = ByteString.From(new byte[] { 2 });
            ByteString certificate = ByteString.From(new byte[] { 3 });
            var applicationId = new NodeId("device", 1);
            var requestId = new NodeId("request", 1);
            var profile = new PullDcaCertificateProfile(new NodeId("group", 1),
                global::Opc.Ua.ObjectTypeIds.RsaSha256ApplicationCertificateType);
            var trust = new TrustListDataType();
            var localIdentity = new Mock<IPullDcaIdentityProvider>(MockBehavior.Strict);
            localIdentity.Setup(value => value.ActivateAsync(identity, It.IsAny<CancellationToken>()))
                .Returns(default(ValueTask));
            var signing = new Mock<IPullDcaCredentialProvider>(MockBehavior.Strict);
            signing.Setup(value => value.CreateSigningRequestAsync(
                    applicationId, profile, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ByteString>(csr));
            var security = new Mock<IProvisionableDeviceSecurity>(MockBehavior.Strict);
            var installation = security.Setup(value => value.InstallFromPullAsync(context.ManagedSession, "urn:registrar",
                    It.Is<ProvisionableDeviceCredentials>(credentials =>
                        credentials.Certificate == certificate && credentials.TrustList == trust),
                    It.IsAny<CancellationToken>()));
            var expectedFailure = new InvalidOperationException("Device installation failed.");
            if (failInstallation)
            {
                installation.Throws(expectedFailure);
            }
            else
            {
                installation.Returns(default(ValueTask));
            }
            var credentials = new DevicePullCredentials(
                localIdentity.Object, signing.Object, security.Object, sessions.GetCurrentAsync, "urn:registrar");
            var connection = new PullConnection(identity, applicationId, requestId, profile, csr, certificate, trust);
            ManagedSession? workflowSession = null;
            var workflow = new PullDcaWorkflow(new PullDcaOptions(),
                async (_, ct) =>
                {
                    workflowSession = await sessions.GetCurrentAsync(ct).ConfigureAwait(false);
                    return connection;
                }, credentials, credentials);
            var registrar = new PullDcaRegistrar(new Uri("opc.tcp://localhost:4840"),
                new NodeId("registrar", 1), new NodeId("directory", 1));
            var request = new PullDcaRequest(new PullDcaIdentityOffer([identity], [], []), profile, []);
            await using (DevicePullSession.Attempt attempt = await sessions.BeginAsync(CancellationToken.None)
                .ConfigureAwait(false))
            {
                if (failInstallation)
                {
                    InvalidOperationException? error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                        await workflow.RunAsync(registrar, request).ConfigureAwait(false)).ConfigureAwait(false);
                    Assert.That(error, Is.SameAs(expectedFailure));
                }
                else
                {
                    PullDcaResult result = await workflow.RunAsync(registrar, request).ConfigureAwait(false);
                    Assert.That(result.DcaCredentials.Certificate, Is.EqualTo(certificate));
                }
                Assert.That(workflowSession, Is.SameAs(attempt.Session));
                Assert.That(attempt.Session, Is.SameAs(context.ManagedSession));
                Assert.That(created, Is.EqualTo(1));
                Assert.That(context.ManagedSession.Disposed, Is.False);
            }
            Assert.That(context.ManagedSession.Disposed, Is.True);
            Assert.That(context.Requests.FindAll(static request => request is CloseSessionRequest),
                Has.Count.EqualTo(1));
            Assert.That(connection.Starts, Is.EqualTo(1));
            Assert.That(connection.Finishes, Is.EqualTo(1));
            Assert.That(connection.TrustReads, Is.EqualTo(1));
            Assert.That(connection.Reconnects, Is.EqualTo(failInstallation ? 1 : 2));
            Assert.That(connection.ManagerReads, Is.EqualTo(failInstallation ? 0 : 1));
            localIdentity.VerifyAll();
            signing.VerifyAll();
            security.VerifyAll();
        }

        /// <summary>
        /// A rejected overlapping attempt cannot create or retire the first attempt's session.
        /// </summary>
        [Test]
        public async Task ConcurrentAttemptIsRejectedWhileConnectionIsInProgress()
        {
            await using var context = new SessionRequestContextTestContext();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int created = 0;
            var sessions = new DevicePullSession(async ct =>
            {
                Interlocked.Increment(ref created);
                entered.SetResult(true);
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                return context.ManagedSession;
            });
            Task<DevicePullSession.Attempt> first = sessions.BeginAsync(CancellationToken.None).AsTask();
            await entered.Task.ConfigureAwait(false);
            Task<DevicePullSession.Attempt> second = sessions.BeginAsync(CancellationToken.None).AsTask();
            release.SetResult(true);
            await using (DevicePullSession.Attempt attempt = await first.ConfigureAwait(false))
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await second.ConfigureAwait(false)).ConfigureAwait(false);
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(created, Is.EqualTo(1));
                Assert.That(attempt.Session, Is.SameAs(context.ManagedSession));
                Assert.That(context.ManagedSession.Disposed, Is.False);
                Assert.That(context.Requests.FindAll(static request => request is CloseSessionRequest), Is.Empty);
            }
            Assert.That(context.ManagedSession.Disposed, Is.True);
            Assert.That(context.Requests.FindAll(static request => request is CloseSessionRequest),
                Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Retiring one attempt never disposes the next attempt's session.
        /// </summary>
        [Test]
        public async Task SequentialAttemptsRetireOnlyTheirOwnSessions()
        {
            await using var first = new SessionRequestContextTestContext();
            await using var second = new SessionRequestContextTestContext();
            int created = 0;
            var sessions = new DevicePullSession(_ =>
                Task.FromResult(++created == 1 ? first.ManagedSession : second.ManagedSession));
            DevicePullSession.Attempt firstAttempt = await sessions.BeginAsync(CancellationToken.None)
                .ConfigureAwait(false);
            await firstAttempt.DisposeAsync().ConfigureAwait(false);
            Assert.That(first.ManagedSession.Disposed, Is.True);
            Assert.That(second.ManagedSession.Disposed, Is.False);
            Assert.That(first.Requests.FindAll(static request => request is CloseSessionRequest), Has.Count.EqualTo(1));
            await using (DevicePullSession.Attempt secondAttempt = await sessions.BeginAsync(CancellationToken.None)
                .ConfigureAwait(false))
            {
                await firstAttempt.DisposeAsync().ConfigureAwait(false);
                Assert.That(await sessions.GetCurrentAsync(CancellationToken.None).ConfigureAwait(false),
                    Is.SameAs(secondAttempt.Session));
                Assert.That(second.ManagedSession.Disposed, Is.False);
                Assert.That(second.Requests.FindAll(static request => request is CloseSessionRequest), Is.Empty);
                Assert.That(first.Requests.FindAll(static request => request is CloseSessionRequest),
                    Has.Count.EqualTo(1));
                Assert.That(created, Is.EqualTo(2));
            }
            Assert.That(second.ManagedSession.Disposed, Is.True);
            Assert.That(second.Requests.FindAll(static request => request is CloseSessionRequest),
                Has.Count.EqualTo(1));
        }

        /// <summary>
        /// A failed one-shot connection releases the attempt reservation.
        /// </summary>
        [Test]
        public async Task ConnectionFailurePermitsAnotherAttempt()
        {
            await using var context = new SessionRequestContextTestContext();
            int calls = 0;
            var sessions = new DevicePullSession(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw new ServiceResultException(StatusCodes.BadTimeout);
                }
                return Task.FromResult(context.ManagedSession);
            });
            ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await sessions.BeginAsync(CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
            await using DevicePullSession.Attempt attempt = await sessions.BeginAsync(CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(attempt.Session, Is.SameAs(context.ManagedSession));
            Assert.That(calls, Is.EqualTo(2));
        }

        /// <summary>
        /// A failed transport-resource disposal prevents another attempt from opening a session.
        /// </summary>
        [Test]
        public async Task RetirementFailurePreventsAnotherAttempt()
        {
            var context = new SessionRequestContextTestContext();
            var resources = new Mock<IDisposable>(MockBehavior.Strict);
            var expectedFailure = new InvalidOperationException("Transport resources could not be released.");
            resources.Setup(value => value.Dispose()).Throws(expectedFailure);
            context.ManagedSession.OwnTransportResources(null, resources.Object);
            int created = 0;
            var sessions = new DevicePullSession(_ =>
            {
                created++;
                return Task.FromResult(context.ManagedSession);
            });
            try
            {
                DevicePullSession.Attempt attempt = await sessions.BeginAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => attempt.DisposeAsync().AsTask()).ConfigureAwait(false);
                Assert.That(failure, Is.SameAs(expectedFailure));
                ServiceResultException? rejected = await Assert.ThrowsAsync<ServiceResultException>(
                    () => sessions.BeginAsync(CancellationToken.None).AsTask()).ConfigureAwait(false);
                Assert.That(rejected!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(created, Is.EqualTo(1));
                Assert.That(context.ManagedSession.Disposed, Is.True);
                Assert.That(context.Requests.FindAll(static request => request is CloseSessionRequest),
                    Has.Count.EqualTo(1));
                resources.Verify(value => value.Dispose(), Times.Once);
                await attempt.DisposeAsync().ConfigureAwait(false);
                resources.Verify(value => value.Dispose(), Times.Once);
            }
            finally
            {
                InvalidOperationException? failure = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => context.DisposeAsync().AsTask()).ConfigureAwait(false);
                Assert.That(failure, Is.SameAs(expectedFailure));
            }
        }

        /// <summary>
        /// Failure or cancellation after connection retires the attempt's session.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedOrCanceledAttemptDisposesItsSession(bool cancel)
        {
            await using var context = new SessionRequestContextTestContext();
            var sessions = new DevicePullSession(_ => Task.FromResult(context.ManagedSession));
            using var cancellation = new CancellationTokenSource();
            async Task RunAttemptAsync()
            {
                await using DevicePullSession.Attempt attempt = await sessions.BeginAsync(cancellation.Token)
                    .ConfigureAwait(false);
                if (cancel)
                {
                    cancellation.Cancel();
                    cancellation.Token.ThrowIfCancellationRequested();
                }
                throw new ServiceResultException(StatusCodes.BadSecurityChecksFailed);
            }
            if (cancel)
            {
                await Assert.CatchAsync<OperationCanceledException>(RunAttemptAsync).ConfigureAwait(false);
            }
            else
            {
                ServiceResultException? error = await Assert.ThrowsAsync<ServiceResultException>(RunAttemptAsync)
                    .ConfigureAwait(false);
                Assert.That(error!.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
            }
            Assert.That(context.ManagedSession.Disposed, Is.True);
            Assert.That(context.Requests.FindAll(static request => request is CloseSessionRequest),
                Has.Count.EqualTo(1));
            ServiceResultException? unavailable = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await sessions.GetCurrentAsync(CancellationToken.None).ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(unavailable!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        /// <summary>
        /// Supplies a deterministic typed transport without opening another session.
        /// </summary>
        private sealed class PullConnection : IPullDcaConnection
        {
            /// <summary>
            /// Captures the expected signing request and issued material.
            /// </summary>
            public PullConnection(
                ByteString identity,
                NodeId applicationId,
                NodeId requestId,
                PullDcaCertificateProfile profile,
                ByteString csr,
                ByteString certificate,
                TrustListDataType trust)
            {
                m_identity = identity;
                m_applicationId = applicationId;
                m_requestId = requestId;
                m_profile = profile;
                m_csr = csr;
                m_certificate = certificate;
                m_trust = trust;
            }

            /// <summary>
            /// The number of signing requests.
            /// </summary>
            public int Starts { get; private set; }

            /// <summary>
            /// The number of finish requests.
            /// </summary>
            public int Finishes { get; private set; }

            /// <summary>
            /// The number of trust-list reads.
            /// </summary>
            public int TrustReads { get; private set; }

            /// <summary>
            /// The number of reconnects.
            /// </summary>
            public int Reconnects { get; private set; }

            /// <summary>
            /// The number of manager reads.
            /// </summary>
            public int ManagerReads { get; private set; }

            /// <inheritdoc/>
            public ValueTask<PullDcaIdentitySelection> ProvideIdentitiesAsync(
                PullDcaIdentityOffer offer, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                Assert.That(offer.Identities.Contains(m_identity), Is.True);
                return new ValueTask<PullDcaIdentitySelection>(
                    new PullDcaIdentitySelection(m_identity, new DeviceIdentityTicketType(), m_applicationId, null));
            }

            /// <inheritdoc/>
            public ValueTask ReloadAndReconnectAsync(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                Reconnects++;
                return default;
            }

            /// <inheritdoc/>
            public ValueTask<ArrayOf<ManagerDescription>> GetManagersAsync(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                ManagerReads++;
                return new ValueTask<ArrayOf<ManagerDescription>>(ArrayOf<ManagerDescription>.Empty);
            }

            /// <inheritdoc/>
            public ValueTask<NodeId> RegisterAsync(PullDcaManagedApplication application, CancellationToken ct)
            {
                throw new AssertionException("Managed application registration was not requested.");
            }

            /// <inheritdoc/>
            public ValueTask<NodeId> StartAsync(
                NodeId applicationId, PullDcaCertificateProfile profile, ByteString csr, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                Assert.That(applicationId, Is.EqualTo(m_applicationId));
                Assert.That(profile, Is.EqualTo(m_profile));
                Assert.That(csr, Is.EqualTo(m_csr));
                Starts++;
                return new ValueTask<NodeId>(m_requestId);
            }

            /// <inheritdoc/>
            public ValueTask<(ByteString Certificate, ArrayOf<ByteString> Issuers)> FinishAsync(
                NodeId applicationId, NodeId requestId, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                Assert.That(applicationId, Is.EqualTo(m_applicationId));
                Assert.That(requestId, Is.EqualTo(m_requestId));
                Finishes++;
                return new ValueTask<(ByteString, ArrayOf<ByteString>)>((m_certificate, []));
            }

            /// <inheritdoc/>
            public ValueTask<TrustListDataType> ReadTrustListAsync(
                NodeId applicationId, NodeId certificateGroupId, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                Assert.That(applicationId, Is.EqualTo(m_applicationId));
                Assert.That(certificateGroupId, Is.EqualTo(m_profile.CertificateGroupId));
                TrustReads++;
                return new ValueTask<TrustListDataType>(m_trust);
            }

            /// <inheritdoc/>
            public ValueTask<(ArrayOf<NodeId> Types, ArrayOf<ByteString> Certificates)> GetCertificatesAsync(
                NodeId applicationId, NodeId certificateGroupId, CancellationToken ct)
            {
                throw new AssertionException("Signing recovery was not requested.");
            }

            /// <summary>The selected identity.</summary>
            private readonly ByteString m_identity;

            /// <summary>The expected application identifier.</summary>
            private readonly NodeId m_applicationId;

            /// <summary>The expected request identifier.</summary>
            private readonly NodeId m_requestId;

            /// <summary>The requested certificate profile.</summary>
            private readonly PullDcaCertificateProfile m_profile;

            /// <summary>The expected signing request.</summary>
            private readonly ByteString m_csr;

            /// <summary>The issued certificate.</summary>
            private readonly ByteString m_certificate;

            /// <summary>The downloaded trust list.</summary>
            private readonly TrustListDataType m_trust;
        }
    }
}
#endif
