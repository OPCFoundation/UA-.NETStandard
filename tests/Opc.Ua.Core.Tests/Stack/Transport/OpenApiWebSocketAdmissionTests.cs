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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// Exercises the listener's real OpenAPI receive, dispatch and send paths without a network host.
    /// </summary>
    [TestFixture]
    [Parallelizable]
    public sealed class OpenApiWebSocketAdmissionTests
    {
        [TestCase(ResourceIsolationStage.RequestQueue)]
        [TestCase(ResourceIsolationStage.RequestQueueBytes)]
        public async Task SaturatedPendingFramesAreRejectedBeforeDispatchAsync(ResourceIsolationStage saturatedStage)
        {
            var provider = new AdmissionProvider();
            await using var connection = new Connection(provider);
            byte[] first = connection.Encode(new PublishRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 1 }
            });
            provider.RequestLimit = saturatedStage == ResourceIsolationStage.RequestQueue ? 1 : 8;
            provider.ByteLimit = saturatedStage == ResourceIsolationStage.RequestQueueBytes ? first.Length : 65536;
            var release = NewSignal();
            connection.Process = async (request, ct) =>
            {
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                return CreateResponse(request);
            };
            connection.Enqueue(first);
            connection.Start();
            await connection.WaitForDispatchAsync().ConfigureAwait(false);
            Assert.That(provider.Active(ResourceIsolationStage.RequestQueue), Is.EqualTo(1));
            Assert.That(provider.Active(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(first.Length));

            connection.Enqueue(connection.Encode(new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 2, AuthenticationToken = new NodeId(123u) }
            }));
            await connection.Finished.WaitAsync(kTimeout).ConfigureAwait(false);

            Assert.That(connection.DispatchCount, Is.EqualTo(1));
            Assert.That(connection.SendCount, Is.Zero);
            Assert.That(connection.OutputCloseStatus, Is.EqualTo((WebSocketCloseStatus)1013));
            Assert.That(provider.Grants(ResourceIsolationStage.RequestQueue),
                Is.EqualTo(saturatedStage == ResourceIsolationStage.RequestQueue ? 1 : 2));
            Assert.That(provider.Grants(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(1));
            Assert.That(provider.LastEndpoint, Is.EqualTo(Connection.RemoteEndpoint));
            AssertReleased(provider);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EmptyFramesWaitingToSendRespectConnectionBoundAsync(bool sharedOnlyProvider)
        {
            var provider = sharedOnlyProvider ? new AdmissionProvider { UseFairScheduling = false } : null;
            await using var connection = new Connection(provider, outstandingLimit: 2);
            var sending = NewSignal();
            var releaseSend = NewSignal();
            connection.Send = async ct =>
            {
                sending.TrySetResult(true);
                await releaseSend.Task.WaitAsync(ct).ConfigureAwait(false);
            };
            connection.Enqueue([]);
            connection.Start();
            await sending.Task.WaitAsync(kTimeout).ConfigureAwait(false);
            connection.Enqueue([]);
            connection.Enqueue([]);
            await connection.Finished.WaitAsync(kTimeout).ConfigureAwait(false);

            Assert.That(connection.OutputCloseStatus, Is.EqualTo((WebSocketCloseStatus)1013));
            Assert.That(connection.DispatchCount, Is.Zero, "Empty frames must not enter the service callback.");
            Assert.That(connection.SendCount, Is.EqualTo(1), "The second response waits behind the active send.");
            if (provider != null)
            {
                Assert.That(provider.Grants(ResourceIsolationStage.RequestQueue), Is.EqualTo(2));
                Assert.That(provider.Grants(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(2));
                Assert.That(provider.LastByteAmount, Is.EqualTo(1));
                AssertReleased(provider);
            }
        }

        [Test]
        public async Task SlowPublishDoesNotBlockReadAndCompletedCapacityIsReusableAsync()
        {
            var provider = new AdmissionProvider { RequestLimit = 2 };
            await using var connection = new Connection(provider);
            var releasePublish = NewSignal();
            connection.Process = async (request, ct) =>
            {
                if (request is PublishRequest)
                {
                    await releasePublish.Task.WaitAsync(ct).ConfigureAwait(false);
                }
                return CreateResponse(request);
            };
            connection.Enqueue(connection.Encode(new PublishRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 1 }
            }));
            connection.Start();
            await connection.WaitForDispatchAsync().ConfigureAwait(false);
            connection.Enqueue(connection.Encode(new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 2 }
            }));
            IServiceResponse read = await connection.ReceiveResponseAsync().ConfigureAwait(false);
            Assert.That(read, Is.InstanceOf<ReadResponse>());
            Assert.That(read.ResponseHeader.RequestHandle, Is.EqualTo(2));
            Assert.That(releasePublish.Task.IsCompleted, Is.False);
            await provider.WaitForRequestCountAsync(1).ConfigureAwait(false);

            connection.Enqueue(connection.Encode(new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 3 }
            }));
            IServiceResponse nextRead = await connection.ReceiveResponseAsync().ConfigureAwait(false);
            Assert.That(nextRead.ResponseHeader.RequestHandle, Is.EqualTo(3));
            releasePublish.TrySetResult(true);
            IServiceResponse publish = await connection.ReceiveResponseAsync().ConfigureAwait(false);
            Assert.That(publish, Is.InstanceOf<PublishResponse>());
            Assert.That(publish.ResponseHeader.RequestHandle, Is.EqualTo(1));
            connection.Close();
            await connection.Finished.WaitAsync(kTimeout).ConfigureAwait(false);

            Assert.That(connection.OutputCloseStatus, Is.EqualTo(WebSocketCloseStatus.NormalClosure));
            Assert.That(connection.DispatchCount, Is.EqualTo(3));
            Assert.That(provider.Grants(ResourceIsolationStage.RequestQueue), Is.EqualTo(3));
            AssertReleased(provider);
        }

        [Test]
        public async Task CloseReleasesConnectionBodyAndPendingMessageLeasesAsync()
        {
            var provider = new AdmissionProvider();
            await using var connection = new Connection(provider);
            var release = NewSignal();
            connection.Process = async (request, ct) =>
            {
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
                return CreateResponse(request);
            };
            connection.Enqueue(connection.Encode(new PublishRequest()));
            connection.Start(upgrade: true);
            await connection.WaitForDispatchAsync().ConfigureAwait(false);

            Assert.That(provider.Active(ResourceIsolationStage.Connection), Is.EqualTo(1));
            Assert.That(provider.Active(ResourceIsolationStage.Handshake), Is.Zero);
            Assert.That(provider.Active(ResourceIsolationStage.ReassemblyBytes),
                Is.EqualTo((3L * connection.Quotas.MessageContext.MaxMessageSize) + Connection.FrameSize));
            Assert.That(provider.Active(ResourceIsolationStage.RequestQueue), Is.EqualTo(1));
            connection.Close();
            await connection.Finished.WaitAsync(kTimeout).ConfigureAwait(false);

            Assert.That(connection.DispatchCount, Is.EqualTo(1));
            Assert.That(connection.SendCount, Is.Zero);
            Assert.That(provider.Grants(ResourceIsolationStage.ReassemblyBytes), Is.EqualTo(1));
            AssertReleased(provider);
        }

        [Test]
        public async Task CancellationDuringAdmissionReleasesWithoutDispatchAsync()
        {
            var provider = new AdmissionProvider();
            await using var connection = new Connection(provider);
            provider.AfterByteGrant = connection.Cancel;
            connection.Enqueue(connection.Encode(new ReadRequest()));
            connection.Start();
            await connection.Finished.WaitAsync(kTimeout).ConfigureAwait(false);

            Assert.That(connection.DispatchCount, Is.Zero);
            Assert.That(connection.SendCount, Is.Zero);
            Assert.That(provider.Grants(ResourceIsolationStage.RequestQueue), Is.EqualTo(1));
            Assert.That(provider.Grants(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(1));
            AssertReleased(provider);
        }

        [Test]
        public async Task CancelledBeforeWorkerEntryReleasesBothLeasesExactlyOnceAsync()
        {
            var provider = new AdmissionProvider();
            await using var connection = new Connection(provider);
            using var shutdown = new CancellationTokenSource();
            using var sendGate = new SemaphoreSlim(1, 1);
            shutdown.Cancel();
            await StartAdmittedRequest(connection, provider, sendGate, shutdown.Token).ConfigureAwait(false);

            Assert.That(connection.DispatchCount, Is.Zero);
            Assert.That(connection.SendCount, Is.Zero);
            AssertReleased(provider);
        }

        [Test]
        public async Task RequestsWaitingForSendGateRetainBothLeasesAsync()
        {
            var provider = new AdmissionProvider();
            var connection = new Connection(provider);
            var shutdown = new CancellationTokenSource();
            var sendGate = new SemaphoreSlim(0, 1);
            Task processing = ProcessAsync();
            try
            {
                await connection.WaitForDispatchAsync().ConfigureAwait(false);
                Assert.That(processing.IsCompleted, Is.False);
                Assert.That(connection.SendCount, Is.Zero);
                Assert.That(provider.Active(ResourceIsolationStage.RequestQueue), Is.EqualTo(1));
                Assert.That(provider.Active(ResourceIsolationStage.RequestQueueBytes), Is.GreaterThan(0));
                sendGate.Release();
                await processing.WaitAsync(kTimeout).ConfigureAwait(false);
                Assert.That(connection.SendCount, Is.EqualTo(1));
                AssertReleased(provider);
            }
            finally
            {
                shutdown.Cancel();
                try
                {
                    await processing.ConfigureAwait(false);
                }
                finally
                {
                    sendGate.Dispose();
                    shutdown.Dispose();
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
            }

            async Task ProcessAsync()
            {
                await StartAdmittedRequest(connection, provider, sendGate, shutdown.Token).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ShutdownTimeoutKeepsActiveAccountingUntilWorkerExitsAsync()
        {
            var provider = new AdmissionProvider();
            var connection = new Connection(provider);
            var release = NewSignal();
            connection.Process = async (request, _) =>
            {
                await release.Task.ConfigureAwait(false);
                return CreateResponse(request);
            };
            var shutdown = new CancellationTokenSource();
            var sendGate = new SemaphoreSlim(1, 1);
            Task processing = ProcessAsync();
            Task? stopping = null;
            try
            {
                await connection.WaitForDispatchAsync().ConfigureAwait(false);
                stopping = InvokeTask(connection.Listener, "StopOpenApiWebSocketRequestsAsync",
                    new List<Task> { processing }, sendGate, shutdown, connection.Socket.Object, TimeSpan.Zero);
                await stopping.WaitAsync(kTimeout).ConfigureAwait(false);

                Assert.That(connection.AbortCount, Is.EqualTo(1));
                Assert.That(processing.IsCompleted, Is.False);
                Assert.That(provider.Active(ResourceIsolationStage.RequestQueue), Is.EqualTo(1));
                Assert.That(provider.Active(ResourceIsolationStage.RequestQueueBytes), Is.GreaterThan(0));
                Assert.That(shutdown.Token.IsCancellationRequested, Is.True,
                    "Cancellation must remain alive until the uncooperative callback exits.");
                await sendGate.WaitAsync().ConfigureAwait(false);
                sendGate.Release();
            }
            finally
            {
                release.TrySetResult(true);
                try
                {
                    await processing.ConfigureAwait(false);
                    if (stopping != null)
                    {
                        await stopping.ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (stopping == null)
                    {
                        sendGate.Dispose();
                        shutdown.Dispose();
                    }
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
            }
            Assert.That(connection.SendCount, Is.Zero);
            AssertReleased(provider);

            async Task ProcessAsync()
            {
                await StartAdmittedRequest(connection, provider, sendGate, shutdown.Token).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task SendFailureReleasesBothLeasesExactlyOnceAsync()
        {
            var provider = new AdmissionProvider();
            await using var connection = new Connection(provider);
            connection.Send = _ => throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
            connection.Enqueue(connection.Encode(new ReadRequest()));
            connection.Start();
            await connection.Finished.WaitAsync(kTimeout).ConfigureAwait(false);

            Assert.That(connection.DispatchCount, Is.EqualTo(1));
            Assert.That(connection.SendCount, Is.EqualTo(1));
            Assert.That(connection.AbortCount, Is.EqualTo(1));
            AssertReleased(provider);
        }

        private static async Task StartAdmittedRequest(
            Connection connection,
            AdmissionProvider provider,
            SemaphoreSlim sendGate,
            CancellationToken ct)
        {
            byte[] message = connection.Encode(new PublishRequest());
            using var admission = new HttpsTransportListener.OpenApiWebSocketAdmission();
            Assert.That(admission.TryAcquire(provider, Connection.RemoteEndpoint, message.Length), Is.True);
            await admission.RunAsync(async () =>
            {
                await InvokeTask(connection.Listener, "ProcessAdmittedOpenApiWebSocketRequestAsync",
                    connection.Socket.Object, sendGate, connection.ChannelContext, message, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        private static Task InvokeTask(HttpsTransportListener listener, string name, params object?[] arguments)
        {
            MethodInfo method = typeof(HttpsTransportListener).GetMethod(
                name, BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (Task)method.Invoke(listener, arguments)!;
        }

        private static void AssertReleased(AdmissionProvider provider)
        {
            for (int stage = 0; stage <= (int)ResourceIsolationStage.ParkedRequest; stage++)
            {
                var resourceStage = (ResourceIsolationStage)stage;
                Assert.That(provider.Active(resourceStage), Is.Zero, resourceStage.ToString());
            }
            Assert.That(provider.AllLeasesDisposedOnce, Is.True);
            Assert.That(provider.SessionClassificationCalls, Is.Zero,
                "Only observed ingress may classify an undecoded frame, never its claimed Session token.");
        }

        private static IServiceResponse CreateResponse(IServiceRequest request)
        {
            var header = new ResponseHeader { RequestHandle = request.RequestHeader?.RequestHandle ?? 0 };
            if (request is PublishRequest)
            {
                return new PublishResponse { ResponseHeader = header };
            }
            return new ReadResponse { ResponseHeader = header };
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Owns a scripted WebSocket and the actual listener message loop.
        /// </summary>
        private sealed class Connection : IAsyncDisposable
        {
            public Connection(AdmissionProvider? provider, int outstandingLimit = 4)
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                var messageContext = ServiceMessageContext.Create(telemetry);
                messageContext.MaxMessageSize = FrameSize * outstandingLimit;
                Quotas = new ChannelQuotas(messageContext)
                {
                    MaxBufferSize = FrameSize,
                    MaxMessageSize = messageContext.MaxMessageSize,
                    ResourceIsolationProvider = provider
                };
                Listener = new HttpsTransportListener(Utils.UriSchemeWss, telemetry);
                SetField("m_quotas", Quotas);
                SetField("m_bufferManager", new BufferManager(DefaultBufferManagerFactory.Instance
                    .Create("openapi-admission", FrameSize, telemetry)));
                SetField("m_admission", new UaScConnectionAdmission(1, null, provider));
                SetField("m_descriptions", new List<EndpointDescription> { ChannelContext.EndpointDescription! });
                SetField("m_serverCertProvider", Mock.Of<ICertificateRegistry>());
                var callback = new Mock<ITransportListenerCallback>();
                callback.Setup(value => value.ProcessRequestAsync(
                    It.IsAny<SecureChannelContext>(), It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()))
                    .Returns((SecureChannelContext _, IServiceRequest request, CancellationToken ct) =>
                    {
                        Interlocked.Increment(ref m_dispatchCount);
                        m_dispatched.Release();
                        return Process(request, ct);
                    });
                SetField("m_callback", callback.Object);
                Socket.SetupGet(value => value.State).Returns(() => (WebSocketState)Volatile.Read(ref m_state));
                Socket.Setup(value => value.ReceiveAsync(
                    It.IsAny<ArraySegment<byte>>(), It.IsAny<CancellationToken>())).Returns(ReceiveAsync);
                Socket.Setup(value => value.SendAsync(
                    It.IsAny<ArraySegment<byte>>(), It.IsAny<WebSocketMessageType>(),
                    It.IsAny<bool>(), It.IsAny<CancellationToken>())).Returns(SendAsync);
                Socket.Setup(value => value.CloseOutputAsync(
                    It.IsAny<WebSocketCloseStatus>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((WebSocketCloseStatus status, string _, CancellationToken _) =>
                    {
                        OutputCloseStatus = status;
                        Volatile.Write(ref m_state, (int)WebSocketState.CloseSent);
                        return Task.CompletedTask;
                    });
                Socket.Setup(value => value.Abort()).Callback(() =>
                {
                    Interlocked.Increment(ref m_abortCount);
                    Volatile.Write(ref m_state, (int)WebSocketState.Aborted);
                    m_socketAborted.Cancel();
                });
            }

            public HttpsTransportListener Listener { get; }
            public ChannelQuotas Quotas { get; }
            public Mock<WebSocket> Socket { get; } = new();
            public SecureChannelContext ChannelContext { get; } = new(
                "openapi-test-channel",
                new EndpointDescription
                {
                    SecurityMode = MessageSecurityMode.None,
                    SecurityPolicyUri = SecurityPolicies.None,
                    TransportProfileUri = Profiles.WssOpenApiTransport
                },
                RequestEncoding.Json,
                peerAddress: IPAddress.Loopback);
            public Func<IServiceRequest, CancellationToken, ValueTask<IServiceResponse>> Process { get; set; } =
                static (request, _) => new ValueTask<IServiceResponse>(CreateResponse(request));
            public Func<CancellationToken, Task> Send { get; set; } = static _ => Task.CompletedTask;
            public Task Finished => m_handler ?? throw new InvalidOperationException("Connection has not started.");
            public WebSocketCloseStatus? OutputCloseStatus { get; private set; }
            public int DispatchCount => Volatile.Read(ref m_dispatchCount);
            public int SendCount => Volatile.Read(ref m_sendCount);
            public int AbortCount => Volatile.Read(ref m_abortCount);
            public static IPEndPoint RemoteEndpoint { get; } = new(IPAddress.Loopback, 12345);

            public void Start(bool upgrade = false)
            {
                if (upgrade)
                {
                    var context = new DefaultHttpContext { RequestAborted = m_cancel.Token };
                    context.Request.Headers["Sec-WebSocket-Protocol"] = Profiles.OpcUaWsSubProtocolOpenApi;
                    context.Connection.RemoteIpAddress = RemoteEndpoint.Address;
                    context.Connection.RemotePort = RemoteEndpoint.Port;
                    var feature = new Mock<IHttpWebSocketFeature>();
                    feature.SetupGet(value => value.IsWebSocketRequest).Returns(true);
                    feature.Setup(value => value.AcceptAsync(It.IsAny<WebSocketAcceptContext>()))
                        .ReturnsAsync(Socket.Object);
                    context.Features.Set(feature.Object);
                    m_handler = Listener.AcceptWebSocketAsync(context);
                }
                else
                {
                    m_handler = Listener.ReceiveOpenApiWebSocketMessagesAsync(
                        Socket.Object, ChannelContext, RemoteEndpoint, m_cancel.Token);
                }
            }

            public void Enqueue(ReadOnlySpan<byte> message)
            {
                m_frames.Enqueue((message.ToArray(), WebSocketMessageType.Text));
                m_framesReady.Release();
            }

            public void Close()
            {
                m_frames.Enqueue(([], WebSocketMessageType.Close));
                m_framesReady.Release();
            }

            public void Cancel()
            {
                m_cancel.Cancel();
            }

            public async Task WaitForDispatchAsync()
            {
                await m_dispatched.WaitAsync().WaitAsync(kTimeout).ConfigureAwait(false);
            }

            public async Task<IServiceResponse> ReceiveResponseAsync()
            {
                await m_responsesReady.WaitAsync().WaitAsync(kTimeout).ConfigureAwait(false);
                Assert.That(m_responses.TryDequeue(out IServiceResponse? response), Is.True);
                return response!;
            }

            public byte[] Encode(IServiceRequest request)
            {
                using var stream = new MemoryStream();
                using (var encoder = new JsonEncoder(stream, Quotas.MessageContext, JsonEncoderOptions.Compact))
                {
                    encoder.EncodeMessage(request, request.TypeId);
                }
                return stream.ToArray();
            }

            public async ValueTask DisposeAsync()
            {
                m_cancel.Cancel();
                if (m_handler != null)
                {
                    await m_handler.WaitAsync(kTimeout).ConfigureAwait(false);
                }
                await Listener.DisposeAsync().ConfigureAwait(false);
                m_cancel.Dispose();
                m_socketAborted.Dispose();
                m_dispatched.Dispose();
                m_framesReady.Dispose();
                m_responsesReady.Dispose();
                Socket.Object.Dispose();
            }

            private void SetField<T>(string name, T value)
            {
                typeof(HttpsTransportListener).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(Listener, value);
            }

            private async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken ct)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, m_socketAborted.Token);
                await m_framesReady.WaitAsync(linked.Token).ConfigureAwait(false);
                Assert.That(m_frames.TryDequeue(out (byte[] Bytes, WebSocketMessageType Type) frame), Is.True);
                frame.Bytes.AsSpan().CopyTo(buffer.AsSpan());
                if (frame.Type == WebSocketMessageType.Close)
                {
                    Volatile.Write(ref m_state, (int)WebSocketState.CloseReceived);
                }
                return new WebSocketReceiveResult(frame.Bytes.Length, frame.Type, true);
            }

            private async Task SendAsync(
                ArraySegment<byte> bytes, WebSocketMessageType messageType, bool endOfMessage, CancellationToken ct)
            {
                Assert.That(messageType, Is.EqualTo(WebSocketMessageType.Text));
                Assert.That(endOfMessage, Is.True);
                Interlocked.Increment(ref m_sendCount);
                await Send(ct).ConfigureAwait(false);
                m_responses.Enqueue(JsonDecoder.DecodeMessage<IServiceResponse>(
                    bytes.AsSpan().ToArray(), Quotas.MessageContext));
                m_responsesReady.Release();
            }

            public const int FrameSize = 4096;
            private readonly CancellationTokenSource m_cancel = new();
            private readonly CancellationTokenSource m_socketAborted = new();
            private readonly ConcurrentQueue<(byte[] Bytes, WebSocketMessageType Type)> m_frames = new();
            private readonly ConcurrentQueue<IServiceResponse> m_responses = new();
            private readonly SemaphoreSlim m_framesReady = new(0);
            private readonly SemaphoreSlim m_responsesReady = new(0);
            private readonly SemaphoreSlim m_dispatched = new(0);
            private Task? m_handler;
            private int m_state = (int)WebSocketState.Open;
            private int m_dispatchCount;
            private int m_sendCount;
            private int m_abortCount;
        }

        /// <summary>
        /// Supplies small deterministic capacities and detects every duplicate lease disposal.
        /// </summary>
        private sealed class AdmissionProvider : IServerResourceIsolationProvider
        {
            public bool UseFairScheduling { get; set; } = true;
            public long RequestLimit { get; set; } = 8;
            public long ByteLimit { get; set; } = 65536;
            public long LastByteAmount { get; private set; }
            public Action? AfterByteGrant { get; set; }
            public IPEndPoint? LastEndpoint { get; private set; }
            public int SessionClassificationCalls { get; private set; }
            public event Action<ResourceIsolationStage>? CapacityAvailable;

            public bool AllLeasesDisposedOnce
            {
                get
                {
                    lock (m_gate)
                    {
                        return m_leases.TrueForAll(static lease => lease.DisposeCount == 1);
                    }
                }
            }

            public ResourceIsolationOwner ClassifyConnection(IPEndPoint? remoteEndpoint)
            {
                LastEndpoint = remoteEndpoint;
                return new ResourceIsolationOwner(
                    "observed-source", ResourceIsolationClass.Established, 1,
                    new long[] { 8, 8, 1048576, 8, RequestLimit, 8, ByteLimit, 8 });
            }

            public ResourceIsolationOwner Classify(
                SecureChannelContext channelContext,
                NodeId authenticationToken = default,
                bool sessionEstablishment = false,
                bool controlRequest = false)
            {
                SessionClassificationCalls++;
                throw new InvalidOperationException("An undecoded frame cannot supply a verified session.");
            }

            public bool IsCurrent(
                ResourceIsolationOwner owner,
                SecureChannelContext channelContext,
                NodeId authenticationToken = default,
                bool sessionEstablishment = false,
                bool controlRequest = false)
            {
                throw new InvalidOperationException("The core request scheduler owns session validation.");
            }

            public bool TryAcquire(
                ResourceIsolationStage stage,
                ResourceIsolationOwner owner,
                long amount,
                [NotNullWhen(true)] out IDisposable? lease,
                out ResourceIsolationFailure failure)
            {
                lock (m_gate)
                {
                    if (amount > owner.GetHardLimit(stage) - m_used[(int)stage])
                    {
                        lease = null;
                        failure = new ResourceIsolationFailure(ResourceIsolationFailureReason.Capacity, TimeSpan.Zero);
                        return false;
                    }
                    m_used[(int)stage] += amount;
                    m_grants[(int)stage]++;
                    var tracked = new TrackedLease(() => Release(stage, amount));
                    m_leases.Add(tracked);
                    lease = tracked;
                    failure = default;
                }
                if (stage == ResourceIsolationStage.RequestQueueBytes)
                {
                    LastByteAmount = amount;
                    AfterByteGrant?.Invoke();
                }
                return true;
            }

            public long Active(ResourceIsolationStage stage)
            {
                lock (m_gate)
                {
                    return m_used[(int)stage];
                }
            }

            public int Grants(ResourceIsolationStage stage)
            {
                lock (m_gate)
                {
                    return m_grants[(int)stage];
                }
            }

            public async Task WaitForRequestCountAsync(long count)
            {
                var reached = NewSignal();
                void CheckCount(ResourceIsolationStage stage)
                {
                    if (stage == ResourceIsolationStage.RequestQueue && Active(stage) == count)
                    {
                        reached.TrySetResult(true);
                    }
                }
                CapacityAvailable += CheckCount;
                try
                {
                    CheckCount(ResourceIsolationStage.RequestQueue);
                    await reached.Task.WaitAsync(kTimeout).ConfigureAwait(false);
                }
                finally
                {
                    CapacityAvailable -= CheckCount;
                }
            }

            private void Release(ResourceIsolationStage stage, long amount)
            {
                lock (m_gate)
                {
                    m_used[(int)stage] -= amount;
                }
                CapacityAvailable?.Invoke(stage);
            }

            private readonly Lock m_gate = new();
            private readonly long[] m_used = new long[(int)ResourceIsolationStage.ParkedRequest + 1];
            private readonly int[] m_grants = new int[(int)ResourceIsolationStage.ParkedRequest + 1];
            private readonly List<TrackedLease> m_leases = [];
        }

        /// <summary>
        /// Records attempted disposal as well as returning capacity exactly once.
        /// </summary>
        private sealed class TrackedLease(Action release) : IDisposable
        {
            public int DisposeCount => Volatile.Read(ref m_disposeCount);

            public void Dispose()
            {
                if (Interlocked.Increment(ref m_disposeCount) == 1)
                {
                    release();
                }
            }

            private int m_disposeCount;
        }

        private static readonly TimeSpan kTimeout = TimeSpan.FromSeconds(5);
    }
}
