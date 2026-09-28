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

#nullable enable

#if HAS_KESTREL_TCP_LISTENER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    [TestFixture]
    [NonParallelizable]
    public sealed class KestrelAdmissionTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task StartupCompletesRestHandshakeBeforeContributorsButPreservesUacpDeadlineAsync(
            bool sharedHost,
            bool uacp)
        {
            var provider = new CountingIsolationProvider();
            var clock = new FakeTimeProvider();
            var admission = new UaScConnectionAdmission(2, null, provider, timeProvider: clock);
            bool contributorCalled = false;
            var contributor = new Mock<IHttpsListenerStartupContributor>();
            contributor.Setup(value => value.Configure(
                It.IsAny<IApplicationBuilder>(), It.IsAny<HttpsTransportListener>()))
                .Callback<IApplicationBuilder, HttpsTransportListener>((branch, _) =>
                    branch.Run(context =>
                    {
                        contributorCalled = true;
                        Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.EqualTo(1));
                        Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.EqualTo(uacp ? 1 : 0));
                        context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                        return Task.CompletedTask;
                    }));
            await using var listener = new HttpsTransportListener(
                Utils.UriSchemeHttps, NUnitTelemetryContext.Create(), [contributor.Object]);
            using ServiceProvider services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
            var application = new ApplicationBuilder(services);
            if (sharedHost)
            {
                var shared = new SharedKestrelHost(new SharedHostKey("localhost", 48443), "test-certificate");
                shared.AddListener("/rest", listener);
                new SharedHostStartup().Configure(application, new SharedHostAccessor { Instance = shared });
            }
            else
            {
                new Startup().Configure(application, listener);
            }
            RequestDelegate pipeline = application.Build();
            await using var connection = new TestConnectionContext
            {
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 1234)
            };
            await HttpsTransportListener.RunHttpsConnectionAsync(connection, async physical =>
            {
                physical.Features.Set<IHttpRequestFeature>(new HttpRequestFeature());
                physical.Features.Set<IHttpResponseFeature>(new HttpResponseFeature());
                var http = new DefaultHttpContext(physical.Features);
                http.Request.Path = "/rest";
                http.Request.Method = "GET";
                var webSocket = new Mock<IHttpWebSocketFeature>();
                webSocket.SetupGet(value => value.IsWebSocketRequest).Returns(uacp);
                http.Features.Set(webSocket.Object);
                if (uacp)
                {
                    http.Request.Headers["Sec-WebSocket-Protocol"] = Profiles.OpcUaWsSubProtocolUacp;
                }
                await pipeline(http).ConfigureAwait(false);
                Assert.That(contributorCalled, Is.True);
                clock.Advance(TimeSpan.FromMinutes(3));
                Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.EqualTo(uacp ? 0 : 1));
                if (uacp)
                {
                    await AssertAbortedAsync(connection).ConfigureAwait(false);
                }
                else
                {
                    Assert.That(connection.ConnectionClosed.IsCancellationRequested, Is.False);
                }
            }, admission).ConfigureAwait(false);
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
        }

        [Test]
        public async Task HttpsPhysicalAdmissionPrecedesTlsAndDoesNotCountHttpRequestsAsConnectionsAsync()
        {
            var provider = new CountingIsolationProvider();
            var limiter = new UaScConnectionAdmissionTests.SwitchableLimiter();
            var admission = new UaScConnectionAdmission(2, limiter, provider);
            await using var connection = new TestConnectionContext
            {
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 1234)
            };
            await HttpsTransportListener.RunHttpsConnectionAsync(connection, physical =>
            {
                Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.EqualTo(1));
                Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.EqualTo(1));
                var http = new DefaultHttpContext(physical.Features);
                HttpsTransportListener.CompleteHttpHandshake(http);
                Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
                Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.EqualTo(1));
                HttpsTransportListener.CompleteHttpHandshake(http);
                Assert.That(limiter.Calls, Is.EqualTo(1));
                Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.EqualTo(1));
                return Task.CompletedTask;
            }, admission).ConfigureAwait(false);
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
        }

        [Test]
        public async Task HttpsRuntimeRejectionNeverEntersTlsOrConsumesLegacyTokensAsync()
        {
            var provider = new CountingIsolationProvider(rejectedStage: ResourceIsolationStage.Handshake);
            var limiter = new UaScConnectionAdmissionTests.SwitchableLimiter();
            var admission = new UaScConnectionAdmission(2, limiter, provider);
            await using var connection = new TestConnectionContext();
            int tlsCalls = 0;
            await HttpsTransportListener.RunHttpsConnectionAsync(connection, _ =>
            {
                tlsCalls++;
                return Task.CompletedTask;
            }, admission).ConfigureAwait(false);
            Assert.That(tlsCalls, Is.Zero);
            Assert.That(limiter.Calls, Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
            await AssertAbortedAsync(connection).ConfigureAwait(false);
        }

        [Test]
        public async Task RuntimeOwnerRejectionPrecedesLegacyLimiterAndChannelAllocationAsync()
        {
            var provider = new CountingIsolationProvider(connectionLimit: 2, ownerLimit: 1);
            var limiter = new UaScConnectionAdmissionTests.SwitchableLimiter();
            await using var harness = new HandlerHarness(limiter, provider: provider);
            await using var first = new TestConnectionContext
            {
                RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 1234)
            };
            Task running = harness.Handler.OnConnectedAsync(first);
            try
            {
                Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.EqualTo(1));
                Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.EqualTo(1));
                await using var rejected = new TestConnectionContext
                {
                    RemoteEndPoint = new IPEndPoint(IPAddress.Loopback, 5678)
                };
                await harness.Handler.OnConnectedAsync(rejected).ConfigureAwait(false);
                await AssertAbortedAsync(rejected).ConfigureAwait(false);
                Assert.That(limiter.Calls, Is.EqualTo(1));
                Assert.That(harness.Channels, Has.Count.EqualTo(1));
            }
            finally
            {
                first.Abort();
                await running.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
        }

        [Test]
        public async Task RejectAllLimiterPreventsChannelAllocationAsync()
        {
            var limiter = new UaScConnectionAdmissionTests.SwitchableLimiter { Allow = false };
            await using var harness = new HandlerHarness(limiter);
            await using var first = new TestConnectionContext();
            await using var second = new TestConnectionContext();
            await harness.Handler.OnConnectedAsync(first).ConfigureAwait(false);
            await harness.Handler.OnConnectedAsync(second).ConfigureAwait(false);
            Assert.That(limiter.Calls, Is.EqualTo(2));
            Assert.That(harness.Channels, Is.Empty);
            await AssertAbortedAsync(first).ConfigureAwait(false);
            await AssertAbortedAsync(second).ConfigureAwait(false);
        }

        [Test]
        public async Task ConcurrentHandlersReserveOneSlotAndCancellationRecoversItAsync()
        {
            await using var harness = new HandlerHarness();
            await using var first = new TestConnectionContext();
            Task firstHandler = harness.Handler.OnConnectedAsync(first);
            try
            {
                Assert.That(harness.Channels, Has.Count.EqualTo(1));
                await using var second = new TestConnectionContext();
                await harness.Handler.OnConnectedAsync(second).WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                await AssertAbortedAsync(second).ConfigureAwait(false);
                Assert.That(firstHandler.IsCompleted, Is.False);
                Assert.That(harness.Channels, Has.Count.EqualTo(1));
                first.Abort();
                await firstHandler.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await using var healthy = new TestConnectionContext();
                Task healthyHandler = harness.Handler.OnConnectedAsync(healthy);
                Assert.That(healthyHandler.IsCompleted, Is.False);
                Assert.That(harness.Channels, Has.Count.EqualTo(1));
                healthy.Abort();
                await healthyHandler.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            finally
            {
                first.Abort();
                await firstHandler.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ChannelConstructionFailureReturnsReservedCapacityAsync()
        {
            await using var listener = new KestrelTcpTransportListener(NUnitTelemetryContext.Create());
            var admission = new UaScConnectionAdmission(1, null);
            SetField(listener, "m_admission", admission);
            await using var connection = new TestConnectionContext();
            await new KestrelTcpConnectionHandler(listener).OnConnectedAsync(connection)
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(admission.TryAcquire(null, out UaScConnectionAdmission.Lease? recovered), Is.True);
            recovered!.Dispose();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReverseHandoffRetainsCapacityUntilTransportCloseOrStopAsync(bool stopListener)
        {
            var provider = new CountingIsolationProvider();
            var clock = new FakeTimeProvider();
            await using var harness = new HandlerHarness(reverse: true, provider: provider, clock: clock);
            await using var connection = new TestConnectionContext();
            var adoption = new TaskCompletionSource<IUaSCByteTransport>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            harness.Listener.ConnectionWaiting += (_, args) =>
            {
                adoption.TrySetResult(((TcpConnectionWaitingEventArgs)args).Transport);
                args.Accepted = true;
                return Task.CompletedTask;
            };
            Task handler = harness.Handler.OnConnectedAsync(connection);
            try
            {
                await connection.SendFrameAsync(WssAdmissionTests.CreateReverseHello()).ConfigureAwait(false);
                IUaSCByteTransport adopted = await adoption.Task.WaitAsync(TimeSpan.FromSeconds(5))
                    .ConfigureAwait(false);
                try
                {
                    await provider.WaitForHandshakeCompletionAsync().ConfigureAwait(false);
                    clock.Advance(TimeSpan.FromMinutes(3));
                    Assert.That(harness.Channels, Is.Empty);
                    Assert.That(handler.IsCompleted, Is.False);
                    Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
                    Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.EqualTo(1));
                    Assert.That(harness.Listener.TryAdmitConnection(null, out _), Is.False);
                    if (stopListener)
                    {
                        await harness.Listener.StopAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        adopted.Close();
                    }
                    await handler.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await AssertAbortedAsync(connection).ConfigureAwait(false);
                    Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
                    if (!stopListener)
                    {
                        Assert.That(harness.Listener.TryAdmitConnection(
                            null, out UaScConnectionAdmission.Lease? recovered), Is.True);
                        recovered!.Dispose();
                    }
                }
                finally
                {
                    adopted.Close();
                }
            }
            finally
            {
                connection.Abort();
                await handler.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ReverseCallbackFailureClosesDetachedTransportAndRecoversCapacityAsync()
        {
            await using var harness = new HandlerHarness(reverse: true);
            await using var connection = new TestConnectionContext();
            harness.Listener.ConnectionWaiting += (_, _) => throw new InvalidOperationException("handoff failed");
            Task handler = harness.Handler.OnConnectedAsync(connection);
            uint id = harness.Channels.Keys.Single();
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await harness.Listener.TransferListenerChannelAsync(
                    id, "urn:test:server", new Uri("opc.tcp://localhost:4840")).ConfigureAwait(false));
            await handler.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await AssertAbortedAsync(connection).ConfigureAwait(false);
            Assert.That(harness.Listener.TryAdmitConnection(
                null, out UaScConnectionAdmission.Lease? recovered), Is.True);
            recovered!.Dispose();
        }

        [Test]
        public async Task ConnectionDisposalWaitsForQueuedAbortCallbacksAsync()
        {
            await using var connection = new TestConnectionContext();
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = connection.ConnectionClosed.Register(() =>
            {
                entered.TrySetResult(true);
                Assert.That(release.Wait(TimeSpan.FromSeconds(5)), Is.True);
            });
            connection.Abort();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Task disposal = connection.DisposeAsync().AsTask();
                try
                {
                    Assert.That(disposal.IsCompleted, Is.False);
                    connection.Abort();
                    Assert.That(disposal.IsCompleted, Is.False);
                }
                finally
                {
                    release.Set();
                    await disposal.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
            finally
            {
                release.Set();
            }
        }

        [Test]
        public async Task LoopbackRejectsLimitedConnectionsThenAcceptsHealthyHelloAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var limiter = new UaScConnectionAdmissionTests.SwitchableLimiter { Allow = false };
            await using var listener = new KestrelTcpTransportListener(telemetry);
            var url = new Uri("opc.tcp://127.0.0.1:0");
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            await listener.OpenAsync(url, new TransportListenerSettings
            {
                Configuration = EndpointConfiguration.Create(),
                Factory = context.Factory,
                NamespaceUris = context.NamespaceUris,
                ServerCertificates = Mock.Of<ICertificateRegistry>(),
                Descriptions = [CreateEndpoint(url)],
                MaxChannelCount = 1,
                ConnectionRateLimiter = limiter
            }, Mock.Of<ITransportListenerCallback>()).ConfigureAwait(false);
            IHost host = GetField<IHost>(listener, "m_host");
            IServer server = host.Services.GetRequiredService<IServer>();
            int port = new Uri(server.Features.Get<IServerAddressesFeature>()!.Addresses.Single()).Port;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            for (int i = 0; i < 2; i++)
            {
                using var rejected = new TcpClient();
                await rejected.ConnectAsync(IPAddress.Loopback, port, deadline.Token).ConfigureAwait(false);
                await AssertClosedAsync(rejected.GetStream(), deadline.Token).ConfigureAwait(false);
            }
            Assert.That(limiter.Calls, Is.EqualTo(2));

            limiter.Allow = true;
            using var healthy = new TcpClient();
            await healthy.ConnectAsync(IPAddress.Loopback, port, deadline.Token).ConfigureAwait(false);
            byte[] hello = CreateHello(context, url);
            await healthy.GetStream().WriteAsync(hello, deadline.Token).ConfigureAwait(false);
            byte[] header = new byte[8];
            await healthy.GetStream().ReadExactlyAsync(header, deadline.Token).ConfigureAwait(false);
            Assert.That(BitConverter.ToUInt32(header), Is.EqualTo(TcpMessageType.Acknowledge));
            Assert.That(limiter.Calls, Is.EqualTo(3));
            using var excess = new TcpClient();
            await excess.ConnectAsync(IPAddress.Loopback, port, deadline.Token).ConfigureAwait(false);
            await AssertClosedAsync(excess.GetStream(), deadline.Token).ConfigureAwait(false);
            Assert.That(limiter.Calls, Is.EqualTo(4));
        }

        private static async Task AssertClosedAsync(NetworkStream stream, CancellationToken ct)
        {
            try
            {
                Assert.That(await stream.ReadAsync(new byte[1], ct).ConfigureAwait(false), Is.Zero);
            }
            catch (IOException ex) when (ex.InnerException is SocketException socket &&
                socket.SocketErrorCode is SocketError.ConnectionReset or SocketError.ConnectionAborted)
            {
                // Kestrel may reset rather than gracefully close a rejected TCP connection.
            }
        }

        private static async Task AssertAbortedAsync(ConnectionContext connection)
        {
            var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (connection.ConnectionClosed.Register(() => closed.TrySetResult(true)))
            {
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            Assert.That(connection.ConnectionClosed.IsCancellationRequested, Is.True);
        }

        private static EndpointDescription CreateEndpoint(Uri url)
        {
            return new EndpointDescription
            {
                EndpointUrl = url.ToString(),
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                TransportProfileUri = Profiles.UaTcpTransport
            };
        }

        private static byte[] CreateHello(IServiceMessageContext context, Uri url)
        {
            byte[] buffer = new byte[256];
            using var encoder = new BinaryEncoder(buffer, 0, buffer.Length, context);
            encoder.WriteUInt32(null, TcpMessageType.Hello);
            encoder.WriteUInt32(null, 0);
            encoder.WriteUInt32(null, 0);
            encoder.WriteUInt32(null, 8192);
            encoder.WriteUInt32(null, 8192);
            encoder.WriteUInt32(null, 0);
            encoder.WriteUInt32(null, 0);
            encoder.WriteString(null, url.ToString());
            int count = encoder.Close();
            BitConverter.GetBytes(count).CopyTo(buffer, 4);
            return buffer.AsSpan(0, count).ToArray();
        }

        private static void SetField<T>(KestrelTcpTransportListener listener, string name, T value)
        {
            typeof(KestrelTcpTransportListener).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(listener, value);
        }

        private static T GetField<T>(KestrelTcpTransportListener listener, string name)
        {
            return (T)typeof(KestrelTcpTransportListener)
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(listener)!;
        }

        private sealed class HandlerHarness : IAsyncDisposable
        {
            public HandlerHarness(
                IConnectionRateLimiter? limiter = null,
                bool reverse = false,
                IServerResourceIsolationProvider? provider = null,
                TimeProvider? clock = null)
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
                Listener = new KestrelTcpTransportListener(telemetry);
                SetField(Listener, "m_admission", new UaScConnectionAdmission(
                    1, limiter, provider, timeProvider: clock));
                SetField(Listener, "m_quotas", new ChannelQuotas(context)
                {
                    ResourceIsolationProvider = provider
                });
                SetField(Listener, "m_bufferManager", new BufferManager(DefaultBufferManagerFactory.Instance
                    .Create("kestrel-admission", 65536, telemetry)));
                SetField(Listener, "m_channels", Channels);
                SetField(Listener, "m_descriptions", new List<EndpointDescription>
                {
                    CreateEndpoint(new Uri("opc.tcp://localhost:4840"))
                });
                SetField(Listener, "m_serverCertificates", Mock.Of<ICertificateRegistry>());
                SetField(Listener, "m_reverseConnectListener", reverse);
                Handler = new KestrelTcpConnectionHandler(Listener);
            }

            public KestrelTcpTransportListener Listener { get; }
            public KestrelTcpConnectionHandler Handler { get; }
            public ConcurrentDictionary<uint, (TcpListenerChannel Channel, TaskCompletionSource<bool> Done)> Channels
                { get; } = new();

            public ValueTask DisposeAsync()
            {
                return Listener.DisposeAsync();
            }
        }

        private sealed class DuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
        {
            public PipeReader Input { get; } = input;
            public PipeWriter Output { get; } = output;
        }

        /// <summary>
        /// Owns cancellation completion so disposal cannot race queued abort callbacks.
        /// </summary>
        private sealed class TestConnectionContext : DefaultConnectionContext
        {
            public TestConnectionContext()
            {
                ConnectionClosed = m_closed.Token;
                Transport = new DuplexPipe(m_input.Reader, m_output.Writer);
            }

            public override void Abort(ConnectionAbortedException abortReason)
            {
                if (Interlocked.Exchange(ref m_aborting, 1) == 0)
                {
                    _ = CancelConnectionAsync();
                }
            }

            public async ValueTask SendFrameAsync(ReadOnlyMemory<byte> frame)
            {
                await m_input.Writer.WriteAsync(frame).ConfigureAwait(false);
            }

            public override async ValueTask DisposeAsync()
            {
                Abort();
                try
                {
                    await m_abortCompleted.Task.ConfigureAwait(false);
                }
                finally
                {
                    m_closed.Dispose();
                    await m_input.Writer.CompleteAsync().ConfigureAwait(false);
                    await m_input.Reader.CompleteAsync().ConfigureAwait(false);
                    await m_output.Writer.CompleteAsync().ConfigureAwait(false);
                    await m_output.Reader.CompleteAsync().ConfigureAwait(false);
                    await base.DisposeAsync().ConfigureAwait(false);
                }
            }

            private async Task CancelConnectionAsync()
            {
                try
                {
                    await m_closed.CancelAsync().ConfigureAwait(false);
                    m_abortCompleted.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    m_abortCompleted.TrySetException(ex);
                }
            }

            private readonly CancellationTokenSource m_closed = new();
            private readonly TaskCompletionSource<bool> m_abortCompleted =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly Pipe m_input = new();
            private readonly Pipe m_output = new();
            private int m_aborting;
        }
    }
}
#endif
