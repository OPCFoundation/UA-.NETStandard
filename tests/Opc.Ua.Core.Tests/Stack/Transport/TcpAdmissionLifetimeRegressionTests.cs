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
    [TestFixture]
    [NonParallelizable]
    public sealed class TcpAdmissionLifetimeRegressionTests
    {
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
                var gate = idle.Gate.Enter();
                Task admission = Task.Run(() => harness.Admit(accepted));
                Task? lookup = null;
                try
                {
                    await cleanupScheduled.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    lookup = Task.Run(() =>
                    {
                        ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                            harness.Listener.ReconnectToExistingChannel(
                                Mock.Of<IUaSCByteTransport>(), 1, 1, 99, null!, null!,
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
                Task first = Task.Run(() => harness.Admit(accepted));
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

        private static void SetField<T>(TcpTransportListener listener, string name, T value)
        {
            FieldInfo field = typeof(TcpTransportListener).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException($"Missing listener field {name}.");
            field.SetValue(listener, value);
        }

        private sealed class IdleChannel : TcpListenerChannel
        {
            public IdleChannel(
                ITcpChannelListener listener, BufferManager buffers, ChannelQuotas quotas, ITelemetryContext telemetry)
                : base("idle", listener, buffers, quotas, null!, [], telemetry, new FakeTimeProvider())
            {
                ChannelId = 1;
                State = TcpChannelState.Open;
            }
        }

        private sealed class CallbackLogger(Action<EventId> onLog) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                onLog(eventId);
            }
        }

        private sealed class AcceptHarness : IAsyncDisposable
        {
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

            public TcpTransportListener Listener { get; }
            public ConcurrentDictionary<uint, TcpListenerChannel> Channels { get; } = new();
            public ServiceMessageContext Context { get; }
            public ChannelQuotas Quotas { get; }
            public BufferManager Buffers { get; }
            public IPEndPoint Endpoint { get; }

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

            public void Admit(Socket accepted)
            {
                using var args = new SocketAsyncEventArgs { AcceptSocket = accepted, UserToken = m_socket };
                m_onAccept(null, args);
            }

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

            public async ValueTask DisposeAsync()
            {
                await Listener.DisposeAsync().ConfigureAwait(false);
                m_socket.Dispose();
            }

            private readonly Socket m_socket;
            private readonly Action<object?, SocketAsyncEventArgs> m_onAccept;
        }
    }
}
