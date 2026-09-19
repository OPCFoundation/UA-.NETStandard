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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Verifies accepted-socket cleanup, admission reservations, and reconnect lookup progress under contention.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class TcpAdmissionLifetimeRegressionTests
    {
        /// <summary>
        /// Verifies blocked peers are closed without consuming capacity and an allowed peer can complete Hello
        /// afterward.
        /// </summary>
        [Test]
        public async Task BlockedAcceptedSocketsCloseAndTheNextAllowedClientCompletesHelloAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var clock = new FakeTimeProvider();
            using var tracker = new ActiveClientTracker(telemetry, clock);
            for (int i = 0; i < 4; i++)
            {
                tracker.AddClientAction(IPAddress.Loopback);
            }
            await using var harness = new AcceptHarness(telemetry);
            SetField(harness.Listener, "m_activeClientTracker", tracker);
            (Socket firstClient, Socket firstAccepted) = await harness.CreateFirstConnectionAsync().ConfigureAwait(false);
            using (firstClient)
            using (firstAccepted)
            {
                harness.Admit(firstAccepted);
                using var stream = new NetworkStream(firstClient, ownsSocket: false);
                int read = await stream.ReadAsync(new byte[1], 0, 1)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(read, Is.Zero);
                Assert.That(harness.Channels, Is.Empty);
                for (int i = 0; i < 2; i++)
                {
                    using Socket rejected = await harness.ConnectAsync().ConfigureAwait(false);
                    using var rejectedStream = new NetworkStream(rejected, ownsSocket: false);
                    Assert.That(await rejectedStream.ReadAsync(new byte[1], 0, 1)
                        .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.Zero);
                }
            }

            clock.Advance(TimeSpan.FromMinutes(1));
            Assert.That(tracker.IsBlocked(IPAddress.Loopback), Is.False);
            using Socket allowed = await harness.ConnectAsync().ConfigureAwait(false);
            using var allowedStream = new NetworkStream(allowed, ownsSocket: false);
            byte[] hello = harness.CreateHello();
#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
            await allowedStream.WriteAsync(hello.AsMemory()).ConfigureAwait(false);
#else
            await allowedStream.WriteAsync(hello, 0, hello.Length).ConfigureAwait(false);
#endif
            byte[] header = new byte[8];
            int offset = 0;
            while (offset < header.Length)
            {
                int read = await allowedStream.ReadAsync(header, offset, header.Length - offset)
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(read, Is.GreaterThan(0));
                offset += read;
            }
            Assert.That(BitConverter.ToUInt32(header, 0), Is.EqualTo(TcpMessageType.Acknowledge));
            Assert.That(harness.Channels, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Verifies idle-channel cleanup does not retain the listener lookup lock needed by reconnect requests.
        /// </summary>
        [Test]
        public async Task IdleAdmissionCleanupDoesNotHoldTheReconnectLookupLockAsync()
        {
            var cleanupScheduled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var logger = new CallbackLogger(
                id =>
                {
                    if (id.Id == CoreEventIds.TcpTransportListener + 14)
                    {
                        cleanupScheduled.TrySetResult(true);
                    }
                });
            var factory = new Mock<ILoggerFactory>();
            factory.Setup(value => value.CreateLogger(It.IsAny<string>())).Returns(logger);
            var telemetry = new Mock<ITelemetryContext>();
            telemetry.SetupGet(value => value.LoggerFactory).Returns(factory.Object);
            await using var harness = new AcceptHarness(telemetry.Object, maxChannels: 1);
            using var idle = new IdleChannel(harness.Listener, harness.Buffers, harness.Quotas, telemetry.Object);
            harness.Channels[1] = idle;
            (Socket client, Socket accepted) = await harness.CreateFirstConnectionAsync().ConfigureAwait(false);
            using (client)
            using (accepted)
            {
                ChannelGate.Releaser gate = idle.Gate.Enter();
                var admission = Task.Run(() => harness.Admit(accepted));
                Task? lookup = null;
                try
                {
                    await cleanupScheduled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    lookup = Task.Run(() =>
                    {
                        ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                            harness.Listener.ReconnectToExistingChannel(
                                idle, Mock.Of<IUaSCByteTransport>(), 1, 1, 99, null!, null!,
                                new OpenSecureChannelRequest { RequestType = SecurityTokenRequestType.Renew }))!;
                        Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadTcpSecureChannelUnknown));
                    });
                    await lookup.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    Assert.That(admission.IsCompleted, Is.False);
                }
                finally
                {
                    gate.Dispose();
                    await admission.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    if (lookup != null)
                    {
                        await lookup.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                }
                Assert.That(harness.Channels.Values, Does.Not.Contain(idle));
                Assert.That(harness.Channels, Has.Count.EqualTo(1));
            }
        }

        /// <summary>
        /// Verifies channel removal cannot leave empty entries in the listener's disposal snapshot.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task DisposalUsesStableChannelSnapshotWhenConnectionsCloseAsync(int closedDuringCopy)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var harness = new AcceptHarness(telemetry);
            using var first = new IdleChannel(harness.Listener, harness.Buffers, harness.Quotas, telemetry);
            using var second = new IdleChannel(
                harness.Listener, harness.Buffers, harness.Quotas, telemetry, channelId: 2);
            var channels = new ClosingOnCopyDictionary(() =>
            {
                for (uint channelId = 1; channelId <= closedDuringCopy; channelId++)
                {
                    harness.Listener.ChannelClosed(channelId);
                }
            })
            {
                [1] = first,
                [2] = second
            };
            SetField(harness.Listener, "m_channels", channels);

            await harness.Listener.DisposeAsync().ConfigureAwait(false);

            Assert.That(channels, Is.Empty);
            Assert.That(first.ResourcesDisposed, Is.True);
            Assert.That(second.ResourcesDisposed, Is.True);
        }

        /// <summary>
        /// Verifies an admitted-but-unpublished channel reserves capacity and releases it on failure or shutdown.
        /// </summary>
        [Test]
        public async Task InFlightAdmissionReservesCapacityAndUnwindsAfterFailureOrShutdownAsync(
            [Values("success", "failure", "shutdown")] string outcome)
        {
            await using var harness = new AcceptHarness(NUnitTelemetryContext.Create(), maxChannels: 1);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            SetField(harness.Listener, "m_onAcceptedChannel", (Action<TcpListenerChannel>)(_ =>
            {
                entered.TrySetResult(true);
                if (!release.Wait(TimeSpan.FromSeconds(15)))
                {
                    throw new TimeoutException("Admission barrier was not released.");
                }
                if (outcome == "failure")
                {
                    throw new InvalidOperationException("Injected capture callback failure.");
                }
            }));
            (Socket client, Socket accepted) = await harness.CreateFirstConnectionAsync().ConfigureAwait(false);
            using (client)
            using (accepted)
            {
                var first = Task.Run(() => harness.Admit(accepted));
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    (Socket rejectedClient, Socket rejectedAccepted) =
                        await harness.CreateFirstConnectionAsync().ConfigureAwait(false);
                    using (rejectedClient)
                    using (rejectedAccepted)
                    {
                        await Task.Run(() => harness.Admit(rejectedAccepted))
                            .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        using var stream = new NetworkStream(rejectedClient, ownsSocket: false);
                        Assert.That(await stream.ReadAsync(new byte[1], 0, 1)
                            .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.Zero);
                    }
                    Assert.That(harness.Channels, Is.Empty);
                    if (outcome == "shutdown")
                    {
                        await harness.Listener.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    release.Set();
                    await first.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                Assert.That(harness.Channels, Has.Count.EqualTo(outcome == "success" ? 1 : 0));
                if (outcome != "success")
                {
                    using var stream = new NetworkStream(client, ownsSocket: false);
                    Assert.That(await stream.ReadAsync(new byte[1], 0, 1)
                        .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.Zero);
                }
            }
        }

        /// <summary>
        /// Sets an existing private listener seam needed to control admission deterministically.
        /// </summary>
        /// <typeparam name="T">The value type of the listener field.</typeparam>
        /// <exception cref="InvalidOperationException"></exception>
        private static void SetField<T>(TcpTransportListener listener, string name, T value)
        {
            FieldInfo field = typeof(TcpTransportListener).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Missing listener field {name}.");
            field.SetValue(listener, value);
        }

        /// <summary>
        /// Provides an open idle channel whose cleanup gate can be held while admission proceeds.
        /// </summary>
        private sealed class IdleChannel : TcpListenerChannel
        {
            /// <summary>
            /// Creates the idle channel occupying the listener's only capacity slot.
            /// </summary>
            public IdleChannel(
                ITcpChannelListener listener,
                BufferManager buffers,
                ChannelQuotas quotas,
                ITelemetryContext telemetry,
                uint channelId = 1)
                : base("idle", listener, buffers, quotas, null!, [], telemetry, new FakeTimeProvider())
            {
                ChannelId = channelId;
                State = TcpChannelState.Open;
            }

            /// <summary>
            /// Gets whether the channel's owned resources have been disposed.
            /// </summary>
            public bool ResourcesDisposed { get; private set; }

            /// <inheritdoc/>
            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing)
                {
                    ResourcesDisposed = true;
                }
            }
        }

        /// <summary>
        /// Models connection closure between a collection copy's count and copy operations.
        /// </summary>
        private sealed class ClosingOnCopyDictionary(Action closeChannels)
            : ConcurrentDictionary<uint, TcpListenerChannel>, ICollection<KeyValuePair<uint, TcpListenerChannel>>
        {
            /// <inheritdoc/>
            void ICollection<KeyValuePair<uint, TcpListenerChannel>>.CopyTo(
                KeyValuePair<uint, TcpListenerChannel>[] array,
                int arrayIndex)
            {
                closeChannels();
                ToArray().CopyTo(array, arrayIndex);
            }
        }

        /// <summary>
        /// Uses emitted listener event identifiers as barriers without introducing timing-based polling.
        /// </summary>
        private sealed class CallbackLogger(Action<EventId> onLog) : ILogger
        {
            /// <inheritdoc/>
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            /// <inheritdoc/>
            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            /// <inheritdoc/>
            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                onLog(eventId);
            }
        }

        /// <summary>
        /// Hosts a loopback listener with controlled socket acceptance and an observable channel registry.
        /// </summary>
        private sealed class AcceptHarness : IAsyncDisposable
        {
            /// <summary>
            /// Creates isolated listener state with an optional channel-capacity limit.
            /// </summary>
            public AcceptHarness(ITelemetryContext telemetry, int maxChannels = 0)
            {
                Listener = new TcpTransportListener(telemetry, new FakeTimeProvider());
                Context = ServiceMessageContext.Create(telemetry);
                Quotas = new ChannelQuotas(Context);
                Buffers = new BufferManager("admission-regression", 65536, telemetry);
                m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                m_socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                m_socket.Listen(8);
                Endpoint = (IPEndPoint)m_socket.LocalEndPoint!;
                SetField(Listener, "m_channels", Channels);
                SetField(Listener, "m_bufferManager", Buffers);
                SetField(Listener, "m_quotas", Quotas);
                SetField(Listener, "m_serverCertificates", Mock.Of<ICertificateRegistry>());
                SetField(Listener, "m_listeningSocket", m_socket);
                SetField(Listener, "m_descriptions", new List<EndpointDescription>
                {
                    new()
                    {
                        EndpointUrl = $"opc.tcp://127.0.0.1:{Endpoint.Port}",
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None,
                        TransportProfileUri = Profiles.UaTcpTransport
                    }
                });
                typeof(TcpTransportListener).GetProperty(nameof(TcpTransportListener.MaxChannelCount))!
                    .SetValue(Listener, maxChannels);
                typeof(TcpTransportListener).GetProperty(nameof(TcpTransportListener.EndpointUrl))!
                    .SetValue(Listener, new Uri($"opc.tcp://127.0.0.1:{Endpoint.Port}"));
                MethodInfo onAccept = typeof(TcpTransportListener)
                    .GetMethod("OnAccept", BindingFlags.Instance | BindingFlags.NonPublic)!;
#if NET5_0_OR_GREATER
                m_onAccept = onAccept.CreateDelegate<Action<object?, SocketAsyncEventArgs>>(Listener);
#else
                m_onAccept = (Action<object?, SocketAsyncEventArgs>)onAccept.CreateDelegate(
                    typeof(Action<object?, SocketAsyncEventArgs>), Listener);
#endif
            }

            /// <summary>
            /// Gets the listener whose private accept callback is exercised.
            /// </summary>
            public TcpTransportListener Listener { get; }

            /// <summary>
            /// Gets the channel registry populated only after successful admission.
            /// </summary>
            public ConcurrentDictionary<uint, TcpListenerChannel> Channels { get; } = new();

            /// <summary>
            /// Gets the encoding context used by the listener and generated Hello message.
            /// </summary>
            public ServiceMessageContext Context { get; }

            /// <summary>
            /// Gets the transport quotas supplied to accepted channels.
            /// </summary>
            public ChannelQuotas Quotas { get; }

            /// <summary>
            /// Gets the shared buffers used by accepted channels.
            /// </summary>
            public BufferManager Buffers { get; }

            /// <summary>
            /// Gets the dynamically allocated loopback endpoint.
            /// </summary>
            public IPEndPoint Endpoint { get; }

            /// <summary>
            /// Connects a client and returns both socket ends before invoking listener admission.
            /// </summary>
            public async Task<(Socket Client, Socket Accepted)> CreateFirstConnectionAsync()
            {
                Socket client = await ConnectAsync().ConfigureAwait(false);
                try
                {
                    Socket accepted = await m_socket.AcceptAsync()
                        .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    return (client, accepted);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            }

            /// <summary>
            /// Connects a new client socket with bounded test setup and failure cleanup.
            /// </summary>
            public async Task<Socket> ConnectAsync()
            {
                var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await client.ConnectAsync(Endpoint).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    return client;
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            }

            /// <summary>
            /// Passes an accepted socket through the listener's real admission callback.
            /// </summary>
            public void Admit(Socket accepted)
            {
                using var args = new SocketAsyncEventArgs { AcceptSocket = accepted, UserToken = m_socket };
                m_onAccept(null, args);
            }

            /// <summary>
            /// Encodes a valid Hello for this listener's endpoint and channel quotas.
            /// </summary>
            public byte[] CreateHello()
            {
                byte[] buffer = new byte[256];
                using var encoder = new BinaryEncoder(buffer, 0, buffer.Length, Context);
                encoder.WriteUInt32(null, TcpMessageType.Hello);
                encoder.WriteUInt32(null, 0);
                encoder.WriteUInt32(null, 0);
                encoder.WriteUInt32(null, 8192);
                encoder.WriteUInt32(null, 8192);
                encoder.WriteUInt32(null, 0);
                encoder.WriteUInt32(null, 0);
                encoder.WriteString(null, $"opc.tcp://127.0.0.1:{Endpoint.Port}");
                int count = encoder.Close();
                BitConverter.GetBytes(count).CopyTo(buffer, 4);
                return buffer.AsSpan(0, count).ToArray();
            }

            /// <summary>
            /// Drains listener disposal before releasing the listening socket.
            /// </summary>
            public async ValueTask DisposeAsync()
            {
                await Listener.DisposeAsync().ConfigureAwait(false);
                m_socket.Dispose();
            }

            /// <summary>
            /// Owns the loopback listening socket used to create accepted clients.
            /// </summary>
            private readonly Socket m_socket;

            /// <summary>
            /// Invokes the existing listener accept callback without a separate adapter implementation.
            /// </summary>
            private readonly Action<object?, SocketAsyncEventArgs> m_onAccept;
        }
    }
}
