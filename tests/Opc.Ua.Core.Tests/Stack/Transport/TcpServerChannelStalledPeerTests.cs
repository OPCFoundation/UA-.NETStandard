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

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Transport
{
    /// <summary>
    /// A peer that sends requests but stops reading its socket keeps the server's
    /// sends waiting. The server channel must bound the responses it queues by their
    /// bytes, not only by their count, and must close a channel whose send waits
    /// longer than the channel lifetime, also while a Session keeps it active.
    /// </summary>
    /// <remarks>
    /// The peer is a raw socket with a small receive buffer that performs the
    /// HEL / OPN (SecurityPolicy None) handshake and then pipelines Read requests
    /// without reading. Every request is answered with a large response.
    /// </remarks>
    [TestFixture]
    [Category("TransportChannel")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class TcpServerChannelStalledPeerTests
    {
        private const int kMaxMessageSize = 1024 * 1024;
        private const int kResponseBytes = 512 * 1024;
        private const int kRequestCount = 150;

        [Test]
        [CancelAfter(60000)]
        public async Task QueuedResponsesOfPeerThatStopsReadingAreBoundedByBytesAsync(
            CancellationToken ct)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Uri endpointUrl = new($"opc.tcp://127.0.0.1:{GetFreeTcpPort()}");
            var callback = new LargeResponseCallback(kResponseBytes);

            await using var listener = new TcpTransportListener(telemetry);
            await listener.OpenAsync(
                endpointUrl,
                CreateListenerSettings(endpointUrl, CreateConfiguration(channelLifetime: 600000)),
                callback,
                ct).ConfigureAwait(false);

            using var peer = new RawPeer(endpointUrl, kMaxMessageSize);
            await peer.ConnectAndOpenAsync(ct).ConfigureAwait(false);
            await peer.SendReadRequestsAsync(kRequestCount, ct).ConfigureAwait(false);

            TcpListenerChannel channel = await WaitForChannelAsync(listener, ct).ConfigureAwait(false);
            await WaitForQueueToSettleAsync(channel, ct).ConfigureAwait(false);

            long limit = channel.WriteQueueByteLimit;
            long queuedBytes = channel.QueuedWriteBytes;
            int queuedWrites = ReadActiveWriteRequests(channel);
            TestContext.Out.WriteLine(
                $"limit={limit} queuedBytes={queuedBytes} queuedWrites={queuedWrites} " +
                $"requests={callback.RequestCount}");

            // Before the byte bound the receive loop paused only at 100 queued responses
            // (50 MiB here); now it pauses once the queued buffers exceed the limit, and
            // only the response of the request read last can overshoot it.
            Assert.That(limit, Is.EqualTo(4L * kMaxMessageSize));
            Assert.That(queuedWrites, Is.LessThan(30));
            Assert.That(queuedBytes, Is.GreaterThan(0));
            Assert.That(queuedBytes, Is.LessThanOrEqualTo(limit + kMaxMessageSize));
            Assert.That(callback.RequestCount, Is.LessThan(kRequestCount));
            Assert.That(ChannelIsRegistered(listener, channel), Is.True, "the paused channel was closed");

            // Pausing must not lose anything: once the peer reads, the queue drains below
            // the low-water mark, the receive loop resumes and every request is answered.
            int responses = await peer.ReadResponsesAsync(kRequestCount, ct).ConfigureAwait(false);
            Assert.That(responses, Is.EqualTo(kRequestCount));
            Assert.That(callback.RequestCount, Is.EqualTo(kRequestCount));

            await listener.CloseAsync(ct).ConfigureAwait(false);
        }

        [Test]
        [CancelAfter(60000)]
        public async Task ChannelWhoseSendStallsIsClosedWhileSessionKeepsItActiveAsync(
            CancellationToken ct)
        {
            const int channelLifetime = 1500;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Uri endpointUrl = new($"opc.tcp://127.0.0.1:{GetFreeTcpPort()}");
            var callback = new LargeResponseCallback(kResponseBytes);

            await using var listener = new TcpTransportListener(telemetry);
            await listener.OpenAsync(
                endpointUrl,
                CreateListenerSettings(endpointUrl, CreateConfiguration(channelLifetime)),
                callback,
                ct).ConfigureAwait(false);

            using var peer = new RawPeer(endpointUrl, kMaxMessageSize);
            await peer.ConnectAndOpenAsync(ct).ConfigureAwait(false);
            await peer.SendReadRequestsAsync(kRequestCount, ct).ConfigureAwait(false);

            TcpListenerChannel channel = await WaitForChannelAsync(listener, ct).ConfigureAwait(false);
            string globalChannelId = channel.GlobalChannelId;

            // The Session monitor refreshes the activity of a channel that serves a Session
            // (every MinSessionTimeout), so the idle check alone never closes it.
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            bool closed = false;
            while (DateTime.UtcNow < deadline)
            {
                listener.UpdateChannelLastActiveTime(globalChannelId);
                if (!ChannelIsRegistered(listener, channel))
                {
                    closed = true;
                    break;
                }
                await Task.Delay(100, ct).ConfigureAwait(false);
            }

            TestContext.Out.WriteLine(
                $"closed={closed} stall={channel.GetWriteStallTime().TotalMilliseconds} ms " +
                $"queuedBytes={channel.QueuedWriteBytes}");
            Assert.That(closed, Is.True, "the channel with a stalled send stayed open");

            // the queued responses were released when the channel closed
            bool drained = await WaitForAsync(() => channel.QueuedWriteBytes == 0, 10000, ct)
                .ConfigureAwait(false);
            Assert.That(drained, Is.True, $"{channel.QueuedWriteBytes} queued bytes were kept");

            await listener.CloseAsync(ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Pausing the receive loop does not stop the handlers of requests read before the
        /// pause from queuing their responses. A peer that pipelines requests whose handlers
        /// complete only after it stopped reading must not grow the queue without bound: the
        /// channel is faulted once the queued responses exceed four times the byte limit.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task ResponsesCompletedAfterThePeerStoppedReadingFaultTheChannelAtTheHardLimitAsync(
            CancellationToken ct)
        {
            const int requests = 60;
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Uri endpointUrl = new($"opc.tcp://127.0.0.1:{GetFreeTcpPort()}");
            var callback = new LargeResponseCallback(kResponseBytes, holdResponses: true);

            await using var listener = new TcpTransportListener(telemetry);
            await listener.OpenAsync(
                endpointUrl,
                CreateListenerSettings(endpointUrl, CreateConfiguration(channelLifetime: 600000)),
                callback,
                ct).ConfigureAwait(false);

            using var peer = new RawPeer(endpointUrl, kMaxMessageSize);
            await peer.ConnectAndOpenAsync(ct).ConfigureAwait(false);
            await peer.SendReadRequestsAsync(requests, ct).ConfigureAwait(false);

            // nothing is queued while the handlers wait, so the server reads every request
            TcpListenerChannel channel = await WaitForChannelAsync(listener, ct).ConfigureAwait(false);
            Assert.That(await WaitForAsync(() => callback.RequestCount == requests, 10000, ct).ConfigureAwait(false),
                Is.True, $"only {callback.RequestCount} requests were read");
            long hardLimit = 4 * channel.WriteQueueByteLimit;
            Assert.That(hardLimit, Is.LessThan((long)requests * kResponseBytes));

            callback.ReleaseResponses();

            bool closed = await WaitForAsync(() => !ChannelIsRegistered(listener, channel), 20000, ct)
                .ConfigureAwait(false);
            TestContext.Out.WriteLine(
                $"closed={closed} queuedBytes={channel.QueuedWriteBytes} hardLimit={hardLimit}");
            Assert.That(closed, Is.True, "the channel kept growing its queue for a peer that does not read");
            bool drained = await WaitForAsync(() => channel.QueuedWriteBytes == 0, 10000, ct)
                .ConfigureAwait(false);
            Assert.That(drained, Is.True, $"{channel.QueuedWriteBytes} queued bytes were kept");

            await listener.CloseAsync(ct).ConfigureAwait(false);
        }

        private static async Task WaitForQueueToSettleAsync(TcpListenerChannel channel, CancellationToken ct)
        {
            long last = -1;
            int stable = 0;
            for (int ii = 0; ii < 300; ii++)
            {
                long current = channel.QueuedWriteBytes;
                if (current == last && current > 0 && channel.GetWriteStallTime() > TimeSpan.Zero)
                {
                    if (++stable >= 5)
                    {
                        return;
                    }
                }
                else
                {
                    stable = 0;
                    last = current;
                }
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            Assert.Fail("the write queue of the server channel did not settle");
        }

        private static async Task<TcpListenerChannel> WaitForChannelAsync(
            TcpTransportListener listener,
            CancellationToken ct)
        {
            for (int ii = 0; ii < 200; ii++)
            {
                foreach (TcpListenerChannel channel in GetChannels(listener).Values)
                {
                    return channel;
                }
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            Assert.Fail("no server channel was registered");
            return null!;
        }

        private static ConcurrentDictionary<uint, TcpListenerChannel> GetChannels(TcpTransportListener listener)
        {
            FieldInfo field = typeof(TcpTransportListener).GetField(
                "m_channels", BindingFlags.NonPublic | BindingFlags.Instance)!;
            return (ConcurrentDictionary<uint, TcpListenerChannel>?)field.GetValue(listener) ?? [];
        }

        private static bool ChannelIsRegistered(TcpTransportListener listener, TcpListenerChannel channel)
        {
            foreach (TcpListenerChannel registered in GetChannels(listener).Values)
            {
                if (ReferenceEquals(registered, channel))
                {
                    return true;
                }
            }
            return false;
        }

        private static int ReadActiveWriteRequests(TcpListenerChannel channel)
        {
            FieldInfo field = typeof(UaSCUaBinaryChannel).GetField(
                "m_activeWriteRequests", BindingFlags.NonPublic | BindingFlags.Instance)!;
            return (int)field.GetValue(channel)!;
        }

        private static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return true;
                }
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            return condition();
        }

        private static EndpointConfiguration CreateConfiguration(int channelLifetime)
        {
            EndpointConfiguration configuration = EndpointConfiguration.Create();
            configuration.OperationTimeout = 60000;
            configuration.MaxMessageSize = kMaxMessageSize;
            configuration.MaxBufferSize = 65535;
            configuration.ChannelLifetime = channelLifetime;
            configuration.SecurityTokenLifetime = 3600000;
            return configuration;
        }

        private static TransportListenerSettings CreateListenerSettings(
            Uri endpointUrl,
            EndpointConfiguration configuration)
        {
            var certificateRegistry = new Mock<ICertificateRegistry>();
            certificateRegistry
                .Setup(r => r.AcquireApplicationCertificateBySecurityPolicy(It.IsAny<string>()))
                .Returns((CertificateEntry)null!);

            return new TransportListenerSettings
            {
                Descriptions =
                [
                    new EndpointDescription
                    {
                        EndpointUrl = endpointUrl.ToString(),
                        SecurityMode = MessageSecurityMode.None,
                        SecurityPolicyUri = SecurityPolicies.None,
                        TransportProfileUri = Profiles.UaTcpTransport
                    }
                ],
                Configuration = configuration,
                ServerCertificates = certificateRegistry.Object,
                NamespaceUris = new NamespaceTable(),
                Factory = EncodeableFactory.Create(),
                MaxChannelCount = 10,
                ChunkReassemblyBudget = new ChunkReassemblyBudget(64 * 1024 * 1024)
            };
        }

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        /// <summary>
        /// Answers every request with a Read response that carries a large ByteString.
        /// </summary>
        private sealed class LargeResponseCallback : ITransportListenerCallback
        {
            /// <param name="responseBytes">The size of the ByteString in each response.</param>
            /// <param name="holdResponses">Whether responses wait for <see cref="ReleaseResponses"/>,
            /// as the handlers of held Publish requests or slow reads complete after their
            /// requests were read.</param>
            public LargeResponseCallback(int responseBytes, bool holdResponses = false)
            {
                m_payload = ByteString.From(new byte[responseBytes]);
                m_holdResponses = holdResponses;
            }

            public int RequestCount => Volatile.Read(ref m_requestCount);

            public ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext secureChannelContext,
                IServiceRequest request,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_requestCount);
                IServiceResponse response = new ReadResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good },
                    Results = [new DataValue(new Variant(m_payload))],
                    DiagnosticInfos = []
                };
                if (!m_holdResponses)
                {
                    return new ValueTask<IServiceResponse>(response);
                }
                return new ValueTask<IServiceResponse>(m_release.Task.ContinueWith(
                    _ => response,
                    CancellationToken.None,
                    TaskContinuationOptions.RunContinuationsAsynchronously,
                    TaskScheduler.Default));
            }

            /// <summary>
            /// Completes every held response.
            /// </summary>
            public void ReleaseResponses()
            {
                m_release.TrySetResult(true);
            }

            public bool TryGetSecureChannelIdForAuthenticationToken(NodeId authenticationToken, out uint channelId)
            {
                channelId = 0;
                return false;
            }

            public void ReportAuditOpenSecureChannelEvent(
                string globalChannelId,
                EndpointDescription endpointDescription,
                OpenSecureChannelRequest request,
                Certificate clientCertificate,
                Exception exception)
            {
            }

            public void ReportAuditCloseSecureChannelEvent(string globalChannelId, Exception exception)
            {
            }

            public void ReportAuditCertificateEvent(Certificate clientCertificate, Exception exception)
            {
            }

            private readonly ByteString m_payload;
            private readonly bool m_holdResponses;
            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int m_requestCount;
        }

        /// <summary>
        /// A raw UA-TCP peer with a small receive buffer: it opens a channel with
        /// SecurityPolicy None, pipelines requests, and reads responses only when told to.
        /// </summary>
        private sealed class RawPeer : IDisposable
        {
            public RawPeer(Uri endpointUrl, int maxMessageSize)
            {
                m_endpointUrl = endpointUrl;
                m_maxMessageSize = maxMessageSize;
                m_context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
                m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                {
                    ReceiveBufferSize = 4096,
                    NoDelay = true
                };
            }

            public async Task ConnectAndOpenAsync(CancellationToken ct)
            {
                await m_socket.ConnectAsync(IPAddress.Loopback, m_endpointUrl.Port).ConfigureAwait(false);

                await SendFrameAsync(BuildHello(), ct).ConfigureAwait(false);
                byte[] ack = await ReadFrameAsync(ct).ConfigureAwait(false);
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(ack), Is.EqualTo(TcpMessageType.Acknowledge));

                await SendFrameAsync(BuildOpenSecureChannel(), ct).ConfigureAwait(false);
                byte[] open = await ReadFrameAsync(ct).ConfigureAwait(false);
                Assert.That(
                    BinaryPrimitives.ReadUInt32LittleEndian(open) & TcpMessageType.MessageTypeMask,
                    Is.EqualTo(TcpMessageType.Open));
                DecodeOpenResponse(open);
            }

            public async Task SendReadRequestsAsync(int count, CancellationToken ct)
            {
                for (int ii = 0; ii < count; ii++)
                {
                    await SendFrameAsync(BuildReadRequest(), ct).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Reads until <paramref name="count"/> final message chunks arrived.
            /// </summary>
            public async Task<int> ReadResponsesAsync(int count, CancellationToken ct)
            {
                int responses = 0;
                while (responses < count)
                {
                    byte[] frame = await ReadFrameAsync(ct).ConfigureAwait(false);
                    uint type = BinaryPrimitives.ReadUInt32LittleEndian(frame);
                    Assert.That(type & TcpMessageType.MessageTypeMask, Is.EqualTo(TcpMessageType.Message));
                    if ((type & TcpMessageType.ChunkTypeMask) == TcpMessageType.Final)
                    {
                        responses++;
                    }
                }
                return responses;
            }

            private byte[] BuildHello()
            {
                return BuildChunk(TcpMessageType.Hello | TcpMessageType.Final, encoder =>
                {
                    encoder.WriteUInt32(null, 0);
                    encoder.WriteUInt32(null, 65535);
                    encoder.WriteUInt32(null, 65535);
                    encoder.WriteUInt32(null, (uint)m_maxMessageSize);
                    encoder.WriteUInt32(null, 0);
                    encoder.WriteString(null, m_endpointUrl.ToString());
                });
            }

            private byte[] BuildOpenSecureChannel()
            {
                var request = new OpenSecureChannelRequest
                {
                    RequestHeader = new RequestHeader { Timestamp = DateTime.UtcNow, TimeoutHint = 60000 },
                    ClientProtocolVersion = 0,
                    RequestType = SecurityTokenRequestType.Issue,
                    SecurityMode = MessageSecurityMode.None,
                    RequestedLifetime = 3600000
                };
                byte[] body = BinaryEncoder.EncodeMessage(request, m_context);
                return BuildChunk(TcpMessageType.Open | TcpMessageType.Final, encoder =>
                {
                    encoder.WriteUInt32(null, 0);
                    encoder.WriteString(null, SecurityPolicies.None);
                    encoder.WriteByteString(null, default(ByteString));
                    encoder.WriteByteString(null, default(ByteString));
                    encoder.WriteUInt32(null, ++m_sequenceNumber);
                    encoder.WriteUInt32(null, ++m_requestId);
                    encoder.WriteRawBytes(body, 0, body.Length);
                });
            }

            private byte[] BuildReadRequest()
            {
                var request = new ReadRequest
                {
                    RequestHeader = new RequestHeader { Timestamp = DateTime.UtcNow, TimeoutHint = 60000 },
                    TimestampsToReturn = TimestampsToReturn.Neither,
                    NodesToRead = [new ReadValueId { NodeId = new NodeId(2258), AttributeId = Attributes.Value }]
                };
                byte[] body = BinaryEncoder.EncodeMessage(request, m_context);
                return BuildChunk(TcpMessageType.Message | TcpMessageType.Final, encoder =>
                {
                    encoder.WriteUInt32(null, m_secureChannelId);
                    encoder.WriteUInt32(null, m_tokenId);
                    encoder.WriteUInt32(null, ++m_sequenceNumber);
                    encoder.WriteUInt32(null, ++m_requestId);
                    encoder.WriteRawBytes(body, 0, body.Length);
                });
            }

            private void DecodeOpenResponse(byte[] frame)
            {
                using var decoder = new BinaryDecoder(
                    new ArraySegment<byte>(frame, 8, frame.Length - 8), m_context);
                _ = decoder.ReadUInt32(null);
                _ = decoder.ReadString(null);
                _ = decoder.ReadByteString(null);
                _ = decoder.ReadByteString(null);
                _ = decoder.ReadUInt32(null);
                _ = decoder.ReadUInt32(null);
                OpenSecureChannelResponse response = decoder.DecodeMessage<OpenSecureChannelResponse>();
                m_secureChannelId = response.SecurityToken.ChannelId;
                m_tokenId = response.SecurityToken.TokenId;
            }

            private byte[] BuildChunk(uint messageType, Action<BinaryEncoder> writeBody)
            {
                using var stream = new MemoryStream();
                int size;
                using (var encoder = new BinaryEncoder(stream, m_context, true))
                {
                    encoder.WriteUInt32(null, messageType);
                    encoder.WriteUInt32(null, 0);
                    writeBody(encoder);
                    size = encoder.Close();
                }
                byte[] chunk = stream.ToArray();
                BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)size);
                return chunk;
            }

            private async Task SendFrameAsync(byte[] frame, CancellationToken ct)
            {
                // a pending socket operation ignores the token: closing the socket ends it
                using CancellationTokenRegistration registration = AbortOnCancel(ct);
                int offset = 0;
                while (offset < frame.Length)
                {
                    int sent;
                    try
                    {
                        sent = await m_socket.SendAsync(
                            new ArraySegment<byte>(frame, offset, frame.Length - offset),
                            SocketFlags.None).ConfigureAwait(false);
                    }
                    catch (Exception) when (ct.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(ct);
                    }
                    if (sent <= 0)
                    {
                        throw new IOException("The socket closed while sending.");
                    }
                    offset += sent;
                    ct.ThrowIfCancellationRequested();
                }
            }

            private CancellationTokenRegistration AbortOnCancel(CancellationToken ct)
            {
                return ct.Register(static state => ((Socket)state!).Dispose(), m_socket);
            }

            private async Task<byte[]> ReadFrameAsync(CancellationToken ct)
            {
                byte[] header = await ReadExactlyAsync(8, ct).ConfigureAwait(false);
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
                byte[] body = await ReadExactlyAsync((int)size - 8, ct).ConfigureAwait(false);
                byte[] frame = new byte[size];
                Array.Copy(header, frame, 8);
                Array.Copy(body, 0, frame, 8, body.Length);
                return frame;
            }

            private async Task<byte[]> ReadExactlyAsync(int count, CancellationToken ct)
            {
                using CancellationTokenRegistration registration = AbortOnCancel(ct);
                byte[] buffer = new byte[count];
                int offset = 0;
                while (offset < count)
                {
                    int read;
                    try
                    {
                        read = await m_socket.ReceiveAsync(
                            new ArraySegment<byte>(buffer, offset, count - offset),
                            SocketFlags.None).ConfigureAwait(false);
                    }
                    catch (Exception) when (ct.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(ct);
                    }
                    if (read <= 0)
                    {
                        throw new IOException("The socket closed while reading.");
                    }
                    offset += read;
                    ct.ThrowIfCancellationRequested();
                }
                return buffer;
            }

            public void Dispose()
            {
                m_socket.Dispose();
            }

            private readonly Uri m_endpointUrl;
            private readonly int m_maxMessageSize;
            private readonly Socket m_socket;
            private readonly ServiceMessageContext m_context;
            private uint m_secureChannelId;
            private uint m_tokenId;
            private uint m_sequenceNumber;
            private uint m_requestId;
        }
    }
}
