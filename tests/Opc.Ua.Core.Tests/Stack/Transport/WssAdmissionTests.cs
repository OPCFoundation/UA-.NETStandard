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

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    [TestFixture]
    [NonParallelizable]
    public sealed class WssAdmissionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task AcceptedReverseHelloAllowsPausedHandoffWithoutReleasingConnectionAsync(bool stopListener)
        {
            var provider = new CountingIsolationProvider();
            var clock = new FakeTimeProvider();
            await using HttpsTransportListener listener = CreateListener(
                reverse: true, provider: provider, clock: clock);
            var adopted = new TaskCompletionSource<IUaSCByteTransport>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            listener.ConnectionWaiting += (_, args) =>
            {
                args.Accepted = true;
                adopted.TrySetResult(((TcpConnectionWaitingEventArgs)args).Transport);
                return Task.CompletedTask;
            };
            using var request = new UpgradeRequest(CreateReverseHello());
            Task handler = listener.AcceptWebSocketAsync(request.Context);
            IUaSCByteTransport transport = await adopted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            try
            {
                await provider.WaitForHandshakeCompletionAsync().ConfigureAwait(false);
                clock.Advance(TimeSpan.FromMinutes(3));
                Assert.That(handler.IsCompleted, Is.False);
                Assert.That(request.Context.RequestAborted.IsCancellationRequested, Is.False);
                Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.EqualTo(1));
                Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
                if (stopListener)
                {
                    await listener.StopAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                transport.Close();
                await handler.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
        }

        [Test]
        public async Task ReverseConnectionWithoutReverseHelloStillExpiresAsync()
        {
            var provider = new CountingIsolationProvider();
            var clock = new FakeTimeProvider();
            await using HttpsTransportListener listener = CreateListener(
                reverse: true, provider: provider, clock: clock);
            using var request = new UpgradeRequest();
            Task handler = listener.AcceptWebSocketAsync(request.Context);
            try
            {
                await request.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                clock.Advance(TimeSpan.FromSeconds(119));
                Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.EqualTo(1));
                Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.EqualTo(1));
                clock.Advance(TimeSpan.FromSeconds(1));
                await handler.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
                Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
            }
            finally
            {
                request.Context.Abort();
                await handler.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task RejectAllLimiterPreventsUacpUpgradeAsync()
        {
            var limiter = new UaScConnectionAdmissionTests.SwitchableLimiter { Allow = false };
            await using HttpsTransportListener listener = CreateListener(limiter);
            for (int i = 0; i < 2; i++)
            {
                using var request = new UpgradeRequest();
                await listener.AcceptWebSocketAsync(request.Context).ConfigureAwait(false);
                Assert.That(request.Context.Response.StatusCode, Is.EqualTo(503));
                Assert.That(request.UpgradeCalls, Is.Zero);
            }
            Assert.That(limiter.Calls, Is.EqualTo(2));
            Assert.That(limiter.LastEndpoint, Is.EqualTo(new IPEndPoint(IPAddress.Loopback, 12345)));
            Assert.That(limiter.Disposed, Is.False);
        }

        [Test]
        public async Task CapacityIncludesPendingUpgradesAndRecoversAfterUpgradeFailureAsync()
        {
            await using HttpsTransportListener listener = CreateListener();
            using var first = new UpgradeRequest
            {
                HoldUpgrade = true,
                UpgradeError = new IOException("upgrade failed")
            };
            Task firstHandler = listener.AcceptWebSocketAsync(first.Context);
            try
            {
                await first.UpgradeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                using var rejected = new UpgradeRequest();
                await listener.AcceptWebSocketAsync(rejected.Context).ConfigureAwait(false);
                Assert.That(rejected.UpgradeCalls, Is.Zero);
                Assert.That(rejected.Context.Response.StatusCode, Is.EqualTo(503));
                first.ReleaseUpgrade.TrySetResult(true);
                Assert.ThrowsAsync<IOException>(async () =>
                    await firstHandler.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
                await AssertHealthyUpgradeAsync(listener).ConfigureAwait(false);
            }
            finally
            {
                first.Context.Abort();
                first.ReleaseUpgrade.TrySetResult(true);
            }
        }

        [Test]
        public async Task RequestCancellationReturnsCapacityAsync()
        {
            await using HttpsTransportListener listener = CreateListener();
            await AssertHealthyUpgradeAsync(listener).ConfigureAwait(false);
            await AssertHealthyUpgradeAsync(listener).ConfigureAwait(false);
        }

        [Test]
        public async Task ListenerStopAbortsPendingUpgradeAsync()
        {
            await using HttpsTransportListener listener = CreateListener();
            using var request = new UpgradeRequest { HoldUpgrade = true };
            Task handler = listener.AcceptWebSocketAsync(request.Context);
            await request.UpgradeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await listener.StopAsync().ConfigureAwait(false);
            Assert.That(request.Context.RequestAborted.IsCancellationRequested, Is.True);
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await handler.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false));
            using var rejected = new UpgradeRequest();
            await listener.AcceptWebSocketAsync(rejected.Context).ConfigureAwait(false);
            Assert.That(rejected.UpgradeCalls, Is.Zero);
            Assert.That(rejected.Context.Response.StatusCode, Is.EqualTo(503));
        }

        [Test]
        public async Task ReverseHandoffRetainsCapacityUntilAdoptedTransportClosesAsync()
        {
            await using HttpsTransportListener listener = CreateListener(reverse: true);
            var adopted = new TaskCompletionSource<IUaSCByteTransport>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            listener.ConnectionWaiting += (_, args) =>
            {
                args.Accepted = true;
                adopted.TrySetResult(((TcpConnectionWaitingEventArgs)args).Transport);
                return Task.CompletedTask;
            };
            using var request = new UpgradeRequest(CreateReverseHello());
            Task handler = listener.AcceptWebSocketAsync(request.Context);
            IUaSCByteTransport transport = await adopted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            try
            {
                Assert.That(handler.IsCompleted, Is.False);
                Assert.That(request.Context.RequestAborted.IsCancellationRequested, Is.False);
                using var rejected = new UpgradeRequest();
                await listener.AcceptWebSocketAsync(rejected.Context).ConfigureAwait(false);
                Assert.That(rejected.UpgradeCalls, Is.Zero);
                Assert.That(rejected.Context.Response.StatusCode, Is.EqualTo(503));
            }
            finally
            {
                transport.Close();
                await handler.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            await AssertHealthyUpgradeAsync(listener).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RejectedOrFailedReverseHandoffReturnsCapacityAsync(bool throwCallback)
        {
            var provider = new CountingIsolationProvider();
            await using HttpsTransportListener listener = CreateListener(reverse: true, provider: provider);
            listener.ConnectionWaiting += (_, _) => throwCallback
                ? throw new InvalidOperationException("handoff failed")
                : Task.CompletedTask;
            using var request = new UpgradeRequest(CreateReverseHello());
            await listener.AcceptWebSocketAsync(request.Context).WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
            request.Socket.Verify(value => value.Abort(), Times.Once);
            Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
            await AssertHealthyUpgradeAsync(listener).ConfigureAwait(false);
        }

        [TestCase(Profiles.OpcUaWsSubProtocolUaJson)]
        [TestCase(Profiles.OpcUaWsSubProtocolOpenApi)]
        public async Task JsonUpgradeUsesPhysicalConnectionAdmissionBeforeUpgradeAsync(string subProtocol)
        {
            var limiter = new UaScConnectionAdmissionTests.SwitchableLimiter { Allow = false };
            await using HttpsTransportListener listener = CreateListener(limiter);
            using var request = new UpgradeRequest { UpgradeError = new IOException("upgrade reached") };
            request.Context.Request.Headers["Sec-WebSocket-Protocol"] = subProtocol;
            await listener.AcceptWebSocketAsync(request.Context).ConfigureAwait(false);
            Assert.That(request.UpgradeCalls, Is.Zero);
            Assert.That(request.Context.Response.StatusCode, Is.EqualTo(503));
            Assert.That(limiter.Calls, Is.EqualTo(1));
        }

        [TestCase(Profiles.OpcUaWsSubProtocolUacp)]
        [TestCase(Profiles.OpcUaWsSubProtocolUaJson)]
        [TestCase(Profiles.OpcUaWsSubProtocolOpenApi)]
        public async Task RuntimeRejectionPrecedesUpgradeAndLegacyLimiterForEveryProfileAsync(string subProtocol)
        {
            var provider = new CountingIsolationProvider(rejectedStage: ResourceIsolationStage.Handshake);
            var limiter = new UaScConnectionAdmissionTests.SwitchableLimiter();
            await using HttpsTransportListener listener = CreateListener(limiter, provider: provider);
            using var request = new UpgradeRequest();
            request.Context.Request.Headers["Sec-WebSocket-Protocol"] = subProtocol;
            await listener.AcceptWebSocketAsync(request.Context).ConfigureAwait(false);
            Assert.That(request.UpgradeCalls, Is.Zero);
            Assert.That(request.Context.Response.StatusCode, Is.EqualTo(503));
            Assert.That(limiter.Calls, Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
        }

        [Test]
        public async Task UpgradeFailureReleasesRuntimeStagesAsync()
        {
            var provider = new CountingIsolationProvider();
            await using HttpsTransportListener listener = CreateListener(provider: provider);
            using var request = new UpgradeRequest { UpgradeError = new IOException("upgrade failed") };
            Assert.ThrowsAsync<IOException>(async () =>
                await listener.AcceptWebSocketAsync(request.Context).ConfigureAwait(false));
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task HttpBodiesAreRejectedBeforeReadWithoutPretendingToBeConnectionsAsync(bool json)
        {
            var provider = new CountingIsolationProvider(rejectedStage: ResourceIsolationStage.ReassemblyBytes);
            await using HttpsTransportListener listener = CreateListener(provider: provider);
            var context = new DefaultHttpContext();
            using var response = new MemoryStream();
            context.Response.Body = response;
            var body = new Mock<Stream>();
            body.SetupGet(value => value.CanRead).Returns(true);
            context.Request.Body = body.Object;
            if (json)
            {
                await listener.SendJsonAsync(context).ConfigureAwait(false);
            }
            else
            {
                await listener.SendBinaryAsync(context).ConfigureAwait(false);
            }
            Assert.That(context.Response.StatusCode, Is.EqualTo(503));
            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
            body.VerifyNoOtherCalls();
        }

        private static async Task AssertHealthyUpgradeAsync(HttpsTransportListener listener)
        {
            using var request = new UpgradeRequest();
            Task handler = listener.AcceptWebSocketAsync(request.Context);
            try
            {
                await request.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(request.UpgradeCalls, Is.EqualTo(1));
                Assert.That(handler.IsCompleted, Is.False);
            }
            finally
            {
                request.Context.Abort();
                await handler.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            request.Socket.Verify(value => value.Abort(), Times.Once);
        }

        private static HttpsTransportListener CreateListener(
            IConnectionRateLimiter? limiter = null,
            bool reverse = false,
            IServerResourceIsolationProvider? provider = null,
            TimeProvider? clock = null)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var listener = new HttpsTransportListener(Utils.UriSchemeWss, telemetry);
            SetField(listener, "m_admission", new UaScConnectionAdmission(1, limiter, provider, timeProvider: clock));
            SetField(listener, "m_quotas", new ChannelQuotas(ServiceMessageContext.Create(telemetry))
            {
                ResourceIsolationProvider = provider
            });
            SetField(listener, "m_bufferManager", new BufferManager(DefaultBufferManagerFactory.Instance
                .Create("wss-admission", 65536, telemetry)));
            SetField(listener, "m_descriptions", new List<EndpointDescription>());
            SetField(listener, "m_callback", Mock.Of<ITransportListenerCallback>());
            SetField(listener, "m_serverCertProvider", Mock.Of<ICertificateRegistry>());
            SetField(listener, "m_reverseConnectListener", reverse);
            return listener;
        }

        private static void SetField<T>(HttpsTransportListener listener, string name, T value)
        {
            typeof(HttpsTransportListener).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(listener, value);
        }

        internal static byte[] CreateReverseHello()
        {
            IServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            byte[] buffer = new byte[256];
            using var encoder = new BinaryEncoder(buffer, 0, buffer.Length, context);
            encoder.WriteUInt32(null, TcpMessageType.ReverseHello);
            encoder.WriteUInt32(null, 0);
            encoder.WriteString(null, "urn:test:server");
            encoder.WriteString(null, "opc.tcp://localhost:4840");
            int length = encoder.Close();
            BitConverter.GetBytes(length).CopyTo(buffer, 4);
            return buffer.AsSpan(0, length).ToArray();
        }

        private sealed class UpgradeRequest : IDisposable
        {
            public UpgradeRequest(byte[]? frame = null)
            {
                m_frame = frame;
                Context = new DefaultHttpContext();
                Context.Response.Body = new MemoryStream();
                Context.Request.Headers["Sec-WebSocket-Protocol"] = Profiles.OpcUaWsSubProtocolUacp;
                Context.Connection.RemoteIpAddress = IPAddress.Loopback;
                Context.Connection.RemotePort = 12345;
                var lifetime = new Mock<IHttpRequestLifetimeFeature>();
                lifetime.SetupGet(value => value.RequestAborted).Returns(m_aborted.Token);
                lifetime.Setup(value => value.Abort()).Callback(m_aborted.Cancel);
                Context.Features.Set(lifetime.Object);
                var feature = new Mock<IHttpWebSocketFeature>();
                feature.SetupGet(value => value.IsWebSocketRequest).Returns(true);
                feature.Setup(value => value.AcceptAsync(It.IsAny<WebSocketAcceptContext>()))
                    .Returns(AcceptAsync);
                Context.Features.Set(feature.Object);
                Socket.SetupGet(value => value.State).Returns(WebSocketState.Open);
                Socket.Setup(value => value.ReceiveAsync(
                    It.IsAny<ArraySegment<byte>>(), It.IsAny<CancellationToken>())).Returns(ReceiveAsync);
                Socket.Setup(value => value.SendAsync(
                    It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(),
                    It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            }

            public DefaultHttpContext Context { get; }
            public Mock<WebSocket> Socket { get; } = new() { CallBase = true };
            public TaskCompletionSource<bool> UpgradeEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> ReleaseUpgrade { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> ReceiveEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int UpgradeCalls { get; private set; }
            public bool HoldUpgrade { get; set; }
            public Exception? UpgradeError { get; set; }

            public void Dispose()
            {
                m_aborted.Cancel();
                m_aborted.Dispose();
                Context.Response.Body.Dispose();
            }

            private async Task<WebSocket> AcceptAsync(WebSocketAcceptContext _)
            {
                UpgradeCalls++;
                UpgradeEntered.TrySetResult(true);
                if (HoldUpgrade)
                {
                    await ReleaseUpgrade.Task.WaitAsync(Context.RequestAborted).ConfigureAwait(false);
                }
                if (UpgradeError != null)
                {
                    throw UpgradeError;
                }
                return Socket.Object;
            }

            private async Task<WebSocketReceiveResult> ReceiveAsync(
                ArraySegment<byte> buffer, CancellationToken ct)
            {
                ReceiveEntered.TrySetResult(true);
                byte[]? frame = Interlocked.Exchange(ref m_frame, null);
                if (frame != null)
                {
                    frame.CopyTo(buffer.Array!, buffer.Offset);
                    return new WebSocketReceiveResult(frame.Length, WebSocketMessageType.Binary, true);
                }
                var pending = new TaskCompletionSource<WebSocketReceiveResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using (ct.Register(() => pending.TrySetCanceled()))
                {
                    return await pending.Task.ConfigureAwait(false);
                }
            }

            private readonly CancellationTokenSource m_aborted = new();
            private byte[]? m_frame;
        }
    }
}
