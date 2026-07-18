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
 *
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

// CA2000: test code; disposables are released by test cleanup paths.
#pragma warning disable CA2000

namespace Opc.Ua.Core.Tests.Stack.Client
{
    /// <summary>
    /// Tests certificate-rotation integration for <see cref="ClientChannelManager"/>.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public sealed class ClientChannelManagerCertRotationTests
    {
        private static readonly ICertificateFactory s_factory = DefaultCertificateFactory.Instance;

        [Test]
        public async Task CertificateRotationTriggersReconnectAllAsync()
        {
            using Certificate oldCertificate = s_factory.CreateCertificate("CN=old-client").CreateForRSA();
            using Certificate newCertificate = s_factory.CreateCertificate("CN=new-client").CreateForRSA();
            using Certificate serverCertificate = s_factory.CreateCertificate("CN=server").CreateForRSA();

            TestCertificateChangeSource changes = new();
            ConcurrentQueue<TransportChannelSettings> openSettings = new();
            ClientChannelManager sut = CreateSut(oldCertificate, changes, openSettings);
            ConfiguredEndpoint endpoint = GetTestEndpoint(serverCertificate);
            TestParticipant firstParticipant = new("first", endpoint);
            TestParticipant secondParticipant = new("second", endpoint);
            IManagedTransportChannel? firstChannel = null;
            IManagedTransportChannel? secondChannel = null;

            try
            {
                sut.UpdateClientCertificate(oldCertificate, null);
                firstChannel = await sut.GetAsync(firstParticipant).ConfigureAwait(false);
                secondChannel = await sut.GetAsync(secondParticipant).ConfigureAwait(false);

                changes.Raise(new CertificateChangeEvent(
                    CertificateChangeKind.ApplicationCertificateUpdated,
                    TrustListIdentifier.Peers,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    oldCertificate,
                    newCertificate,
                    null));

                await WaitUntilAsync(() =>
                    firstParticipant.NotificationCount == 1 &&
                    secondParticipant.NotificationCount == 1 &&
                    openSettings.Count >= 2).ConfigureAwait(false);

                TransportChannelSettings[] settings = [.. openSettings];
                Assert.That(settings[^1].ClientCertificate?.Thumbprint, Is.EqualTo(newCertificate.Thumbprint));
                Assert.That(firstParticipant.NotificationCount, Is.EqualTo(1));
                Assert.That(secondParticipant.NotificationCount, Is.EqualTo(1));
            }
            finally
            {
                if (firstChannel != null)
                {
                    await firstChannel.CloseAsync().ConfigureAwait(false);
                }

                if (secondChannel != null)
                {
                    await secondChannel.CloseAsync().ConfigureAwait(false);
                }

                await sut.DisposeAsync().ConfigureAwait(false);

                // The production channel factory parses description.ServerCertificate
                // into a Certificate that the real channel would own and dispose; the
                // mock channel does not, so dispose the captured copies here.
                while (openSettings.TryDequeue(out TransportChannelSettings? opened))
                {
                    opened.ServerCertificate?.Dispose();
                }
            }
        }

        [Test]
        public async Task CertificateRotationIgnoresUnrelatedCerts()
        {
            using Certificate oldCertificate = s_factory.CreateCertificate("CN=old-client").CreateForRSA();
            using Certificate unrelatedOldCertificate = s_factory.CreateCertificate("CN=old-other").CreateForRSA();
            using Certificate unrelatedNewCertificate = s_factory.CreateCertificate("CN=new-other").CreateForRSA();
            using Certificate serverCertificate = s_factory.CreateCertificate("CN=server").CreateForRSA();

            TestCertificateChangeSource changes = new();
            ConcurrentQueue<TransportChannelSettings> openSettings = new();
            ClientChannelManager sut = CreateSut(oldCertificate, changes, openSettings);
            ConfiguredEndpoint endpoint = GetTestEndpoint(serverCertificate);
            TestParticipant participant = new("participant", endpoint);
            IManagedTransportChannel? channel = null;

            try
            {
                sut.UpdateClientCertificate(oldCertificate, null);
                channel = await sut.GetAsync(participant).ConfigureAwait(false);

                changes.Raise(new CertificateChangeEvent(
                    CertificateChangeKind.ApplicationCertificateUpdated,
                    TrustListIdentifier.Peers,
                    ObjectTypeIds.RsaSha256ApplicationCertificateType,
                    unrelatedOldCertificate,
                    unrelatedNewCertificate,
                    null));

                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);

                Assert.That(participant.NotificationCount, Is.Zero);
                Assert.That(openSettings, Has.Count.EqualTo(1));
            }
            finally
            {
                if (channel != null)
                {
                    await channel.CloseAsync().ConfigureAwait(false);
                }

                await sut.DisposeAsync().ConfigureAwait(false);

                // The production channel factory parses description.ServerCertificate
                // into a Certificate that the real channel would own and dispose; the
                // mock channel does not, so dispose the captured copies here.
                while (openSettings.TryDequeue(out TransportChannelSettings? opened))
                {
                    opened.ServerCertificate?.Dispose();
                }
            }
        }

        [Test]
        public async Task ReconnectWaitsForRequiredParticipantRecreationAsync()
        {
            using Certificate clientCertificate = s_factory
                .CreateCertificate("CN=recreate-client")
                .CreateForRSA();
            using Certificate serverCertificate = s_factory
                .CreateCertificate("CN=recreate-server")
                .CreateForRSA();

            TestCertificateChangeSource changes = new();
            ConcurrentQueue<TransportChannelSettings> openSettings = new();
            ClientChannelManager sut = CreateSut(
                clientCertificate,
                changes,
                openSettings);
            var participant = new BlockingRecreateParticipant(
                "recreate",
                GetTestEndpoint(serverCertificate));
            IManagedTransportChannel? channel = null;

            try
            {
                sut.UpdateClientCertificate(clientCertificate, null);
                channel = await sut.GetAsync(participant).ConfigureAwait(false);

                Task reconnect = sut.ReconnectAsync(channel).AsTask();
                await participant.RecreateStarted.Task
                    .WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);

                Assert.That(reconnect.IsCompleted, Is.False);
                Assert.That(channel.State, Is.Not.EqualTo(ChannelState.Ready));

                participant.ReleaseRecreate();
                await reconnect.ConfigureAwait(false);

                Assert.That(participant.RecreateCount, Is.EqualTo(1));
                Assert.That(channel.State, Is.EqualTo(ChannelState.Ready));
            }
            finally
            {
                participant.ReleaseRecreate();
                if (channel != null)
                {
                    await channel.CloseAsync().ConfigureAwait(false);
                }
                await sut.DisposeAsync().ConfigureAwait(false);
                while (openSettings.TryDequeue(out TransportChannelSettings? opened))
                {
                    opened.ServerCertificate?.Dispose();
                }
            }
        }

        [Test]
        public async Task DisposeUnsubscribesFromCertEvent()
        {
            using Certificate oldCertificate = s_factory.CreateCertificate("CN=old-client").CreateForRSA();
            using Certificate newCertificate = s_factory.CreateCertificate("CN=new-client").CreateForRSA();

            TestCertificateChangeSource changes = new();
            ConcurrentQueue<TransportChannelSettings> openSettings = new();
            ClientChannelManager sut = CreateSut(oldCertificate, changes, openSettings);

            Assert.That(changes.ObserverCount, Is.EqualTo(1));

            await sut.DisposeAsync().ConfigureAwait(false);
            changes.Raise(new CertificateChangeEvent(
                CertificateChangeKind.ApplicationCertificateUpdated,
                TrustListIdentifier.Peers,
                ObjectTypeIds.RsaSha256ApplicationCertificateType,
                oldCertificate,
                newCertificate,
                null));

            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);

            Assert.That(changes.ObserverCount, Is.Zero);
            Assert.That(openSettings, Is.Empty);
        }

        private static ClientChannelManager CreateSut(
            Certificate applicationCertificate,
            TestCertificateChangeSource changes,
            ConcurrentQueue<TransportChannelSettings> openSettings)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var certificateManager = new Mock<ICertificateManager>();
            certificateManager.SetupGet(m => m.CertificateChanges).Returns(changes);

            var channel = new Mock<IChannel>();
            channel.Setup(c => c.OpenAsync(
                    It.IsAny<Uri>(),
                    It.IsAny<TransportChannelSettings>(),
                    It.IsAny<CancellationToken>()))
                .Callback<Uri, TransportChannelSettings, CancellationToken>((_, settings, _) =>
                    openSettings.Enqueue(settings))
                .Returns(new ValueTask());
            channel.Setup(c => c.OpenAsync(
                    It.IsAny<ITransportWaitingConnection>(),
                    It.IsAny<TransportChannelSettings>(),
                    It.IsAny<CancellationToken>()))
                .Callback<ITransportWaitingConnection, TransportChannelSettings, CancellationToken>((_, settings, _) =>
                    openSettings.Enqueue(settings))
                .Returns(new ValueTask());
            channel.Setup(c => c.CloseAsync(It.IsAny<CancellationToken>())).Returns(new ValueTask());
            channel.Setup(c => c.SupportedFeatures).Returns(TransportChannelFeatures.None);

            var bindings = new Mock<ITransportChannelBindings>();
            bindings.Setup(b => b.Create(It.IsAny<string>(), It.IsAny<ITelemetryContext>()))
                .Returns(channel.Object);

            var configuration = new ApplicationConfiguration(telemetry)
            {
                CertificateManager = certificateManager.Object
            };
            configuration.SecurityConfiguration.ApplicationCertificate = new CertificateIdentifier
            {
                CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType,
                Thumbprint = applicationCertificate.Thumbprint
            };
            configuration.SecurityConfiguration.SendCertificateChain = false;

            return new ClientChannelManager(
                configuration,
                telemetry,
                bindings.Object,
                new ImmediateReconnectPolicy());
        }

        private static ConfiguredEndpoint GetTestEndpoint(Certificate serverCertificate)
        {
            var endpoint = new ConfiguredEndpoint
            {
                Configuration = new EndpointConfiguration
                {
                    OperationTimeout = 6000
                }
            };
            endpoint.Description.EndpointUrl = "opc.tcp://localhost:4840";
            endpoint.Description.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            endpoint.Description.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            endpoint.Description.ServerCertificate = serverCertificate.RawData.ToByteString();
            return endpoint;
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition())
            {
                if (DateTime.UtcNow >= deadline)
                {
                    Assert.Fail("The certificate rotation reconnect did not complete in time.");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(20)).ConfigureAwait(false);
            }
        }

        public interface IChannel : ITransportChannel, ISecureChannel;

        private sealed class ImmediateReconnectPolicy : IChannelReconnectPolicy
        {
            public TimeSpan GetDelay(int attempt)
            {
                return attempt == 0 ? TimeSpan.Zero : Timeout.InfiniteTimeSpan;
            }
        }

        private sealed class TestCertificateChangeSource : IObservable<CertificateChangeEvent>
        {
            public int ObserverCount
            {
                get
                {
                    lock (m_lock)
                    {
                        return m_observers.Count;
                    }
                }
            }

            public IDisposable Subscribe(IObserver<CertificateChangeEvent> observer)
            {
                if (observer == null)
                {
                    throw new ArgumentNullException(nameof(observer));
                }

                lock (m_lock)
                {
                    m_observers.Add(observer);
                }

                return new Subscription(this, observer);
            }

            public void Raise(CertificateChangeEvent evt)
            {
                IObserver<CertificateChangeEvent>[] snapshot;
                lock (m_lock)
                {
                    snapshot = [.. m_observers];
                }

                for (int i = 0; i < snapshot.Length; i++)
                {
                    snapshot[i].OnNext(evt);
                }
            }

            private void Unsubscribe(IObserver<CertificateChangeEvent> observer)
            {
                lock (m_lock)
                {
                    m_observers.Remove(observer);
                }
            }

            private sealed class Subscription : IDisposable
            {
                public Subscription(
                    TestCertificateChangeSource owner,
                    IObserver<CertificateChangeEvent> observer)
                {
                    m_owner = owner;
                    m_observer = observer;
                }

                public void Dispose()
                {
                    if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                    {
                        return;
                    }

                    m_owner.Unsubscribe(m_observer);
                }

                private readonly TestCertificateChangeSource m_owner;
                private readonly IObserver<CertificateChangeEvent> m_observer;
                private int m_disposed;
            }

            private readonly Lock m_lock = new();
            private readonly List<IObserver<CertificateChangeEvent>> m_observers = [];
        }

        private sealed class TestParticipant : IReconnectParticipant
        {
            private int m_notificationCount;

            public TestParticipant(string id, ConfiguredEndpoint endpoint)
            {
                Id = id;
                Endpoint = endpoint;
            }

            public string Id { get; }

            public ConfiguredEndpoint Endpoint { get; }

            public int NotificationCount => Volatile.Read(ref m_notificationCount);

            public ValueTask<ParticipantReconnectResult> OnReconnectAsync(
                IManagedTransportChannel channel,
                int reconnectAttempt,
                CancellationToken ct)
            {
                Interlocked.Increment(ref m_notificationCount);
                return new ValueTask<ParticipantReconnectResult>(ParticipantReconnectResult.Reactivated);
            }
        }

        private sealed class BlockingRecreateParticipant :
            IRecreateAwareReconnectParticipant
        {
            public BlockingRecreateParticipant(
                string id,
                ConfiguredEndpoint endpoint)
            {
                Id = id;
                Endpoint = endpoint;
            }

            public string Id { get; }

            public ConfiguredEndpoint Endpoint { get; }

            public TaskCompletionSource<bool> RecreateStarted { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int RecreateCount => Volatile.Read(ref m_recreateCount);

            public ValueTask<ParticipantReconnectResult> OnReconnectAsync(
                IManagedTransportChannel channel,
                int reconnectAttempt,
                CancellationToken ct)
            {
                return new ValueTask<ParticipantReconnectResult>(
                    reconnectAttempt < 0
                        ? ParticipantReconnectResult.Reactivated
                        : ParticipantReconnectResult.RequiresSessionRecreate);
            }

            public async ValueTask RecreateAsync(CancellationToken ct = default)
            {
                Interlocked.Increment(ref m_recreateCount);
                RecreateStarted.TrySetResult(true);
                await m_release.Task.WaitAsync(ct).ConfigureAwait(false);
            }

            public void ReleaseRecreate()
            {
                m_release.TrySetResult(true);
            }

            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_recreateCount;
        }
    }
}
