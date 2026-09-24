/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Client
{
    /// <summary>
    /// Verifies independent certificate ownership across managed channel open, reuse, rotation, reconnect, and
    /// shutdown.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class ManagedCertificateLifetimeRegressionTests
    {
        /// <summary>
        /// Verifies channel closure leaves manager-owned certificates and issuer chains usable for later connections.
        /// </summary>
        [Test]
        public async Task ClosingManagedChannelDoesNotDisposeManagerCertificatesAsync(
            [Values(false, true)] bool chain,
            [Values(false, true)] bool transportOwnsSettings,
            [Values(false, true)] bool reverse)
        {
            long created = Certificate.InstancesCreated;
            long disposed = Certificate.InstancesDisposed;
            await using (var harness = new ChannelHarness(chain) { TransportOwnsSettings = transportOwnsSettings })
            {
                IManagedTransportChannel first = await harness.GetAsync("first", reverse).ConfigureAwait(false);
                await first.CloseAsync().ConfigureAwait(false);
                AssertPrivateKeyWorks(harness.ClientCertificate);
                if (chain)
                {
                    Assert.That(harness.ClientChain, Has.Count.EqualTo(2));
                    using RSA key = harness.ClientChain[1].GetRSAPublicKey()!;
                    Assert.That(key.KeySize, Is.GreaterThanOrEqualTo(2048));
                }
                IManagedTransportChannel second = await harness.GetAsync("second", reverse).ConfigureAwait(false);
                Assert.That(second.State, Is.EqualTo(ChannelState.Ready));
                Assert.That(harness.OpenSettings, Has.Count.EqualTo(2));
                await second.CloseAsync().ConfigureAwait(false);
                AssertPrivateKeyWorks(harness.ClientCertificate);
            }
            AssertBalanced(created, disposed);
        }

        /// <summary>
        /// Verifies failed opens release only their own certificate references and allow a subsequent retry.
        /// </summary>
        [Test]
        public async Task FailedOpenReleasesOnlyItsOwnCertificateReferencesAsync(
            [Values(false, true)] bool transportOwnsSettings)
        {
            long created = Certificate.InstancesCreated;
            long disposed = Certificate.InstancesDisposed;
            await using (var harness = new ChannelHarness(true) { TransportOwnsSettings = transportOwnsSettings })
            {
                harness.BeforeOpen = (_, _) => throw new ServiceResultException(StatusCodes.BadCommunicationError);
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                    () => harness.Manager.GetAsync(Participant("failed")).AsTask())!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadCommunicationError));
                AssertPrivateKeyWorks(harness.ClientCertificate);
                harness.BeforeOpen = null;
                IManagedTransportChannel channel = await harness.Manager.GetAsync(Participant("retry")).ConfigureAwait(false);
                Assert.That(channel.State, Is.EqualTo(ChannelState.Ready));
                Assert.That(harness.OpenSettings, Has.Count.EqualTo(2));
                await channel.CloseAsync().ConfigureAwait(false);
            }
            AssertBalanced(created, disposed);
        }

        /// <summary>
        /// Verifies rotation cannot invalidate a key borrowed by an opening channel, including canceled opens.
        /// </summary>
        [Test]
        public async Task RotationCannotRetireAnOpeningChannelsKeyAsync([Values(false, true)] bool cancel)
        {
            long created = Certificate.InstancesCreated;
            long disposed = Certificate.InstancesDisposed;
            using (Certificate replacement = NewCertificate("replacement"))
            using (var cancellation = new CancellationTokenSource())
            {
                await using var harness = new ChannelHarness(true);
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                harness.BeforeOpen = async (_, ct) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                };
                Task<IManagedTransportChannel> opening = harness.Manager.GetAsync(
                    Participant("opening"), cancellation.Token).AsTask();
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    harness.Manager.UpdateClientCertificate(replacement, null);
                    AssertPrivateKeyWorks(harness.OpenSettings[0].ClientCertificate!);
                    Assert.That(harness.OpenSettings[0].ClientCertificateChain, Has.Count.EqualTo(2));
                    if (cancel)
                    {
                        cancellation.Cancel();
                        Assert.That(() => opening, Throws.InstanceOf<OperationCanceledException>());
                    }
                    else
                    {
                        release.TrySetResult(true);
                        IManagedTransportChannel old = await opening.ConfigureAwait(false);
                        Assert.That(old.State, Is.EqualTo(ChannelState.Ready));
                        await old.CloseAsync().ConfigureAwait(false);
                    }
                    harness.BeforeOpen = null;
                    IManagedTransportChannel current = await harness.Manager.GetAsync(Participant("current")).ConfigureAwait(false);
                    Assert.That(harness.OpenSettings[^1].ClientCertificate!.Thumbprint, Is.EqualTo(replacement.Thumbprint));
                    AssertPrivateKeyWorks(harness.OpenSettings[^1].ClientCertificate!);
                    await current.CloseAsync().ConfigureAwait(false);
                }
                finally
                {
                    release.TrySetResult(true);
                    try
                    {
                        IManagedTransportChannel channel = await opening.ConfigureAwait(false);
                        await channel.CloseAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                    }
                }
            }
            AssertBalanced(created, disposed);
        }

        /// <summary>
        /// Verifies channel sharing and both reconnect modes release unused snapshots without invalidating manager
        /// keys.
        /// </summary>
        [Test]
        public async Task ReuseAndReconnectReleaseUnusedCertificateSnapshotsAsync(
            [Values(false, true)] bool inPlace)
        {
            long created = Certificate.InstancesCreated;
            long disposed = Certificate.InstancesDisposed;
            await using (var harness = new ChannelHarness(true) { SupportsReconnect = inPlace })
            {
                IManagedTransportChannel first = await harness.Manager.GetAsync(Participant("first")).ConfigureAwait(false);
                IManagedTransportChannel second = await harness.Manager.GetAsync(Participant("second")).ConfigureAwait(false);
                Assert.That(harness.OpenSettings, Has.Count.EqualTo(1));
                await harness.Manager.ReconnectAsync(first).ConfigureAwait(false);
                Assert.That(harness.Reconnects, Is.EqualTo(inPlace ? 1 : 0));
                Assert.That(harness.OpenSettings, Has.Count.EqualTo(inPlace ? 1 : 2));
                Assert.That(first.State, Is.EqualTo(ChannelState.Ready));
                Assert.That(second.State, Is.EqualTo(ChannelState.Ready));
                AssertPrivateKeyWorks(harness.ClientCertificate);
                await first.CloseAsync().ConfigureAwait(false);
                await second.CloseAsync().ConfigureAwait(false);
            }
            AssertBalanced(created, disposed);
        }

        /// <summary>
        /// Verifies capacity rejection releases its certificate snapshot without opening another transport.
        /// </summary>
        [Test]
        public async Task MaxChannelRejectionReturnsItsCertificateSnapshotAsync()
        {
            long created = Certificate.InstancesCreated;
            long disposed = Certificate.InstancesDisposed;
            await using (var harness = new ChannelHarness(true, new ChannelManagerOptions { MaxChannels = 1 }))
            {
                IManagedTransportChannel first = await harness.Manager.GetAsync(Participant("first")).ConfigureAwait(false);
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                    () => harness.Manager.GetAsync(Participant("rejected", "opc.tcp://localhost:4841")).AsTask())!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
                Assert.That(harness.OpenSettings, Has.Count.EqualTo(1));
                AssertPrivateKeyWorks(harness.ClientCertificate);
                await first.CloseAsync().ConfigureAwait(false);
            }
            AssertBalanced(created, disposed);
        }

        /// <summary>
        /// Verifies recovery of a faulted lease retains the replacement transport's usable private key.
        /// </summary>
        [Test]
        public async Task FaultedLeaseSwapKeepsItsReplacementKeyAliveAsync()
        {
            long created = Certificate.InstancesCreated;
            long disposed = Certificate.InstancesDisposed;
            await using (var harness = new ChannelHarness(true))
            {
                IManagedTransportChannel channel = await harness.Manager.GetAsync(Participant("first")).ConfigureAwait(false);
                ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                    () => harness.Manager.ReconnectAsync(channel, new RetryBudget(TimeSpan.Zero)).AsTask())!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelClosed));
                Assert.That(channel.State, Is.EqualTo(ChannelState.Faulted));
                await harness.Manager.ReconnectAsync(channel).ConfigureAwait(false);
                Assert.That(channel.State, Is.EqualTo(ChannelState.Ready));
                Assert.That(harness.OpenSettings, Has.Count.GreaterThan(1));
                AssertPrivateKeyWorks(harness.OpenSettings[^1].ClientCertificate!);
                AssertPrivateKeyWorks(harness.ClientCertificate);
                await channel.CloseAsync().ConfigureAwait(false);
            }
            AssertBalanced(created, disposed);
        }

        /// <summary>
        /// Verifies manager disposal cannot release the certificate still borrowed by an incomplete open operation.
        /// </summary>
        [Test]
        public async Task ManagerDisposalRetainsAnInFlightOpenBorrowUntilItUnwindsAsync()
        {
            long created = Certificate.InstancesCreated;
            long disposed = Certificate.InstancesDisposed;
            await using (var harness = new ChannelHarness(true))
            {
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                harness.BeforeOpen = async (_, _) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                };
                Task<IManagedTransportChannel> opening = harness.Manager.GetAsync(Participant("opening")).AsTask();
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await harness.Manager.DisposeAsync().ConfigureAwait(false);
                    AssertPrivateKeyWorks(harness.OpenSettings[0].ClientCertificate!);
                }
                finally
                {
                    release.TrySetResult(true);
                    ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(() => opening)!;
                    Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelClosed));
                }
            }
            AssertBalanced(created, disposed);
        }

        /// <summary>
        /// Verifies a stopped manager rejects certificate publication without consuming the caller's certificate.
        /// </summary>
        [Test]
        public async Task StoppedManagerRejectsCertificatePublicationWithoutTakingOwnershipAsync()
        {
            long created = Certificate.InstancesCreated;
            long disposed = Certificate.InstancesDisposed;
            using (Certificate replacement = NewCertificate("replacement"))
            {
                await using var harness = new ChannelHarness(true);
                await harness.Manager.DisposeAsync().ConfigureAwait(false);
                Assert.Throws<ObjectDisposedException>(() => harness.Manager.UpdateClientCertificate(replacement, null));
                AssertPrivateKeyWorks(replacement);
            }
            AssertBalanced(created, disposed);
        }

        /// <summary>
        /// Verifies an acquired transport snapshot retains its installed key after publication changes and shutdown.
        /// </summary>
        [Test]
        public async Task InstalledTransportSnapshotKeepsItsKeyAfterNewPublicationAndShutdownAsync()
        {
            long created = Certificate.InstancesCreated;
            long disposed = Certificate.InstancesDisposed;
            ClientChannelCertificateSnapshot? snapshot = null;
            try
            {
                using (Certificate replacement = NewCertificate("next-publication"))
                {
                    await using var harness = new ChannelHarness(true);
                    var lease = (ManagedTransportChannelLease)await harness.Manager.GetAsync(
                        Participant("snapshot")).ConfigureAwait(false);
                    string installedThumbprint = harness.ClientCertificate.Thumbprint;
                    harness.Manager.UpdateClientCertificate(replacement.AddRef(), null);

                    snapshot = lease.Entry.SnapshotClientCertificate();

                    Assert.Multiple(() =>
                    {
                        Assert.That(snapshot.Certificate!.Thumbprint, Is.EqualTo(installedThumbprint));
                        Assert.That(snapshot.Certificate.Thumbprint, Is.Not.EqualTo(replacement.Thumbprint));
                        Assert.That(snapshot.Chain, Has.Count.EqualTo(2));
                    });
                    await lease.CloseAsync().ConfigureAwait(false);
                    ServiceResultException error = Assert.Throws<ServiceResultException>(
                        () => lease.Entry.SnapshotClientCertificate())!;
                    Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelClosed));
                }
                AssertPrivateKeyWorks(snapshot.Certificate!);
            }
            finally
            {
                snapshot?.Dispose();
            }
            AssertBalanced(created, disposed);
        }

        /// <summary>
        /// Verifies reconnect adopts a certificate published during transport open or in-place reconnect.
        /// </summary>
        [Test]
        public async Task ReconnectObservesCertificatePublishedDuringTransportOperationAsync(
            [Values(false, true)] bool inPlace,
            [Values(1, 2)] int maxAttempts)
        {
            long created = Certificate.InstancesCreated;
            long disposed = Certificate.InstancesDisposed;
            using (Certificate replacement = NewCertificate("during-reconnect"))
            {
                await using var harness = new ChannelHarness(false, maxAttempts: maxAttempts)
                {
                    SupportsReconnect = inPlace
                };
                var lease = (ManagedTransportChannelLease)await harness.Manager.GetAsync(
                    Participant("reconnecting")).ConfigureAwait(false);
                bool published = false;
                void PublishReplacement()
                {
                    if (!published)
                    {
                        published = true;
                        harness.Manager.UpdateClientCertificate(replacement.AddRef(), null);
                    }
                }
                harness.BeforeOpen = (_, _) =>
                {
                    PublishReplacement();
                    return Task.CompletedTask;
                };
                harness.BeforeReconnect = () =>
                {
                    PublishReplacement();
                    return default;
                };

                await harness.Manager.ReconnectAsync(lease).ConfigureAwait(false);

                using ClientChannelCertificateSnapshot snapshot = lease.Entry.SnapshotClientCertificate();
                Assert.Multiple(() =>
                {
                    Assert.That(published, Is.True);
                    Assert.That(lease.State, Is.EqualTo(ChannelState.Ready));
                    Assert.That(snapshot.Certificate!.Thumbprint, Is.EqualTo(replacement.Thumbprint));
                    Assert.That(harness.OpenSettings[^1].ClientCertificate!.Thumbprint,
                        Is.EqualTo(replacement.Thumbprint));
                });
                AssertPrivateKeyWorks(snapshot.Certificate!);
                await lease.CloseAsync().ConfigureAwait(false);
            }
            AssertBalanced(created, disposed);
        }

        /// <summary>
        /// Verifies republishing identical certificate material does not unnecessarily replace the installed transport.
        /// </summary>
        [Test]
        public async Task RepeatedCertificatePublicationKeepsTheInstalledTransportAsync(
            [Values(false, true)] bool includeLeafOnlyChain)
        {
            long created = Certificate.InstancesCreated;
            long disposed = Certificate.InstancesDisposed;
            await using (var harness = new ChannelHarness(false) { SupportsReconnect = true })
            {
                IManagedTransportChannel lease = await harness.Manager.GetAsync(Participant("same-key"))
                    .ConfigureAwait(false);
                harness.Manager.UpdateClientCertificate(
                    harness.ClientCertificate.AddRef(),
                    includeLeafOnlyChain ? new CertificateCollection { harness.ClientCertificate } : null);

                await harness.Manager.ReconnectAsync(lease).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(lease.State, Is.EqualTo(ChannelState.Ready));
                    Assert.That(harness.OpenSettings, Has.Count.EqualTo(1));
                    Assert.That(harness.Reconnects, Is.EqualTo(1));
                });
                await lease.CloseAsync().ConfigureAwait(false);
            }
            AssertBalanced(created, disposed);
        }

        /// <summary>
        /// Verifies a certificate change during participant reactivation is observed before reconnect reports
        /// readiness.
        /// </summary>
        [Test]
        public async Task ReconnectObservesCertificatePublishedDuringParticipantReactivationAsync(
            [Values(1, 2)] int maxAttempts)
        {
            long created = Certificate.InstancesCreated;
            long disposed = Certificate.InstancesDisposed;
            using (Certificate replacement = NewCertificate("during-reactivation"))
            {
                await using var harness = new ChannelHarness(false, maxAttempts: maxAttempts)
                {
                    SupportsReconnect = true
                };
                Mock<IReconnectParticipant> participant = Mock.Get(Participant("rotating-participant"));
                int reactivations = 0;
                participant.Setup(value => value.OnReconnectAsync(
                        It.IsAny<IManagedTransportChannel>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        if (++reactivations == 1)
                        {
                            harness.Manager.UpdateClientCertificate(replacement.AddRef(), null);
                        }
                        return new ValueTask<ParticipantReconnectResult>(ParticipantReconnectResult.Reactivated);
                    });
                var lease = (ManagedTransportChannelLease)await harness.Manager.GetAsync(participant.Object)
                    .ConfigureAwait(false);

                await harness.Manager.ReconnectAsync(lease).ConfigureAwait(false);

                using ClientChannelCertificateSnapshot snapshot = lease.Entry.SnapshotClientCertificate();
                Assert.Multiple(() =>
                {
                    Assert.That(reactivations, Is.EqualTo(2));
                    Assert.That(lease.State, Is.EqualTo(ChannelState.Ready));
                    Assert.That(snapshot.Certificate!.Thumbprint, Is.EqualTo(replacement.Thumbprint));
                });
                await lease.CloseAsync().ConfigureAwait(false);
            }
            AssertBalanced(created, disposed);
        }

        /// <summary>
        /// Verifies a changed issuer chain replaces the transport even when the leaf certificate is unchanged.
        /// </summary>
        [Test]
        public async Task ChangedIssuerChainStillReplacesTheInstalledTransportAsync()
        {
            long created = Certificate.InstancesCreated;
            long disposed = Certificate.InstancesDisposed;
            using (Certificate issuer = NewCertificate("new-issuer"))
            {
                await using var harness = new ChannelHarness(false) { SupportsReconnect = true };
                IManagedTransportChannel lease = await harness.Manager.GetAsync(Participant("new-chain"))
                    .ConfigureAwait(false);
                harness.Manager.UpdateClientCertificate(
                    harness.ClientCertificate.AddRef(),
                    new CertificateCollection { harness.ClientCertificate, issuer });

                await harness.Manager.ReconnectAsync(lease).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(lease.State, Is.EqualTo(ChannelState.Ready));
                    Assert.That(harness.OpenSettings, Has.Count.EqualTo(2));
                    Assert.That(harness.OpenSettings[^1].ClientCertificateChain, Has.Count.EqualTo(2));
                    Assert.That(harness.OpenSettings[^1].ClientCertificateChain![1].Thumbprint,
                        Is.EqualTo(issuer.Thumbprint));
                });
                await lease.CloseAsync().ConfigureAwait(false);
            }
            AssertBalanced(created, disposed);
        }

        /// <summary>
        /// Requires every certificate handle created since the test baseline to have been disposed.
        /// </summary>
        private static void AssertBalanced(long created, long disposed)
        {
            Assert.That(Certificate.InstancesDisposed - disposed, Is.EqualTo(Certificate.InstancesCreated - created));
        }

        /// <summary>
        /// Signs and verifies a SHA-256 digest to prove the retained RSA private key remains usable.
        /// </summary>
        private static void AssertPrivateKeyWorks(Certificate certificate)
        {
            using RSA key = certificate.GetRSAPrivateKey() ?? throw new InvalidOperationException("No private key.");
            using RSA verifier = certificate.GetRSAPublicKey() ?? throw new InvalidOperationException("No public key.");
            byte[] hash = new byte[32];
            byte[] signature = key.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            Assert.That(verifier.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), Is.True);
        }

        /// <summary>
        /// Creates an independently owned RSA certificate with a scenario-specific subject.
        /// </summary>
        private static Certificate NewCertificate(string name)
        {
            return DefaultCertificateFactory.Instance.CreateCertificate($"CN=managed-{name}").CreateForRSA();
        }

        /// <summary>
        /// Creates a participant that reports successful reactivation against the selected endpoint.
        /// </summary>
        private static IReconnectParticipant Participant(string id, string url = "opc.tcp://localhost:4840")
        {
            var participant = new Mock<IReconnectParticipant>();
            participant.SetupGet(value => value.Id).Returns(id);
            participant.SetupGet(value => value.Endpoint).Returns(new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = url,
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport
            }, new EndpointConfiguration())
            { UpdateBeforeConnect = false });
            participant.Setup(value => value.OnReconnectAsync(
                    It.IsAny<IManagedTransportChannel>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ParticipantReconnectResult>(ParticipantReconnectResult.Reactivated));
            return participant.Object;
        }

        /// <summary>
        /// Owns a managed channel manager and fake transports with controlled certificate adoption and reconnect
        /// timing.
        /// </summary>
        private sealed class ChannelHarness : IAsyncDisposable
        {
            /// <summary>
            /// Creates the initial certificate material and a bounded, zero-delay reconnect policy.
            /// </summary>
            public ChannelHarness(bool chain, ChannelManagerOptions? options = null, int maxAttempts = 2)
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                ClientCertificate = NewCertificate("client");
                if (chain)
                {
                    using Certificate issuer = NewCertificate("issuer");
                    ClientChain = [ClientCertificate, issuer];
                }
                var bindings = new Mock<ITransportChannelBindings>();
                bindings.Setup(value => value.Create(It.IsAny<string>(), It.IsAny<ITelemetryContext>()))
                    .Returns(CreateChannel);
                Manager = new ClientChannelManager(
                    new ApplicationConfiguration(telemetry), telemetry, bindings.Object,
                    new ExponentialBackoffChannelReconnectPolicy
                    {
                        MinDelay = TimeSpan.Zero,
                        MaxDelay = TimeSpan.Zero,
                        MaxAttempts = maxAttempts
                    },
                    options: options);
                Manager.UpdateClientCertificate(ClientCertificate, ClientChain);
            }

            /// <summary>
            /// Gets the manager coordinating certificate publication and channel leases.
            /// </summary>
            public ClientChannelManager Manager { get; }

            /// <summary>
            /// Gets the original application certificate retained by the harness.
            /// </summary>
            public Certificate ClientCertificate { get; }

            /// <summary>
            /// Gets the optional original leaf-and-issuer chain.
            /// </summary>
            public CertificateCollection? ClientChain { get; }

            /// <summary>
            /// Gets settings observed by every physical transport open attempt.
            /// </summary>
            public List<TransportChannelSettings> OpenSettings { get; } = [];

            /// <summary>
            /// Gets or sets a callback that delays or fails transport opening after settings are received.
            /// </summary>
            public Func<TransportChannelSettings, CancellationToken, Task>? BeforeOpen { get; set; }

            /// <summary>
            /// Gets or sets work performed during an in-place reconnect.
            /// </summary>
            public Func<ValueTask>? BeforeReconnect { get; set; }

            /// <summary>
            /// Gets or sets whether a transport adopts and disposes the certificate handles in its settings.
            /// </summary>
            public bool TransportOwnsSettings { get; set; } = true;

            /// <summary>
            /// Gets or sets whether the fake transport advertises in-place reconnect.
            /// </summary>
            public bool SupportsReconnect { get; set; }

            /// <summary>
            /// Gets the number of in-place reconnect calls.
            /// </summary>
            public int Reconnects { get; private set; }

            /// <summary>
            /// Acquires a managed channel through forward or reverse connection establishment.
            /// </summary>
            public ValueTask<IManagedTransportChannel> GetAsync(string id, bool reverse)
            {
                IReconnectParticipant participant = Participant(id);
                return reverse
                    ? Manager.GetAsync(
                        participant.Endpoint, _ => participant, new Mock<ITransportWaitingConnection>().Object)
                    : Manager.GetAsync(participant);
            }

            /// <summary>
            /// Disposes the manager before releasing the harness's own certificate and chain references.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                await Manager.DisposeAsync().ConfigureAwait(false);
                ClientCertificate.Dispose();
                ClientChain?.Dispose();
            }

            /// <summary>
            /// Creates a fake transport that records opens and independently disposes any settings it adopts.
            /// </summary>
            private ITransportChannel CreateChannel()
            {
                TransportChannelSettings? adopted = null;
                var channel = new Mock<ITransportChannel>();
                Mock<ISecureChannel> secure = channel.As<ISecureChannel>();
                secure.Setup(value => value.OpenAsync(
                        It.IsAny<Uri>(), It.IsAny<TransportChannelSettings>(), It.IsAny<CancellationToken>()))
                    .Returns((Uri _, TransportChannelSettings settings, CancellationToken ct) =>
                        new ValueTask(OpenAsync(settings, ct)));
                secure.Setup(value => value.OpenAsync(
                        It.IsAny<ITransportWaitingConnection>(),
                        It.IsAny<TransportChannelSettings>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((ITransportWaitingConnection _, TransportChannelSettings settings, CancellationToken ct) =>
                        new ValueTask(OpenAsync(settings, ct)));
                channel.SetupGet(value => value.SupportedFeatures)
                    .Returns(() => SupportsReconnect ? TransportChannelFeatures.Reconnect : TransportChannelFeatures.None);
                channel.Setup(value => value.ReconnectAsync(
                        It.IsAny<ITransportWaitingConnection>(), It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        Reconnects++;
                        return BeforeReconnect?.Invoke() ?? default;
                    });
                channel.Setup(value => value.CloseAsync(It.IsAny<CancellationToken>())).Returns(default(ValueTask));
                channel.Setup(value => value.Dispose()).Callback(() =>
                {
                    TransportChannelSettings? owned = Interlocked.Exchange(ref adopted, null);
                    owned?.ServerCertificate?.Dispose();
                    owned?.ClientCertificate?.Dispose();
                    owned?.ClientCertificateChain?.Dispose();
                });
                return channel.Object;

                async Task OpenAsync(TransportChannelSettings settings, CancellationToken ct)
                {
                    OpenSettings.Add(settings);
                    if (TransportOwnsSettings)
                    {
                        adopted = settings;
                    }
                    if (BeforeOpen != null)
                    {
                        await BeforeOpen(settings, ct).ConfigureAwait(false);
                    }
                    AssertPrivateKeyWorks(settings.ClientCertificate!);
                }
            }
        }
    }
}
