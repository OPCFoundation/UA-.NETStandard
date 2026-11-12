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

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.Https.WebApi.Tests
{
    public sealed partial class RealHttpsListenerIntegrationTests
    {
        /// <summary>
        /// Silent WebSockets release physical capacity on admission pressure or after the channel lifetime, including
        /// binary channels whose secure-channel handshake has already completed.
        /// </summary>
        [TestCase(Profiles.OpcUaWsSubProtocolUacp, false)]
        [TestCase(Profiles.OpcUaWsSubProtocolUaJson, false)]
        [TestCase(Profiles.OpcUaWsSubProtocolOpenApi, false)]
        [TestCase(Profiles.OpcUaWsSubProtocolUacp, true)]
        [TestCase(Profiles.OpcUaWsSubProtocolUaJson, true)]
        [TestCase(Profiles.OpcUaWsSubProtocolOpenApi, true)]
        [CancelAfter(20000)]
        public async Task SilentWebSocketReturnsConnectionCapacityAsync(
            string subProtocol,
            bool reclaimOnAdmission,
            CancellationToken ct)
        {
            Certificate certificate = CreateServerCertificate();
            string serverThumbprint = certificate.Thumbprint;
            using var certificateRegistry = new InMemoryCertificateRegistry(certificate);
            certificate.Dispose();
            (HttpsTransportListener listener, int port) = await OpenListenerOnFreePortAsync(
                () => (HttpsTransportListener)new HttpsTransportListenerFactory().Create(m_telemetry!),
                p =>
                {
                    TransportListenerSettings settings = CreateListenerSettings(certificateRegistry, p);
                    settings.MaxChannelCount = 1;
                    settings.Configuration!.ChannelLifetime = reclaimOnAdmission ? 60000 : 1000;
                    return settings;
                },
                m_callback!).ConfigureAwait(false);
            try
            {
                var endpointUrl = new Uri($"wss://127.0.0.1:{port}/");
                using var webSocket = new ClientWebSocket();
                webSocket.Options.RemoteCertificateValidationCallback =
                    (_, remote, _, _) => remote?.GetCertHashString() == serverThumbprint;
                webSocket.Options.AddSubProtocol(subProtocol);
                await webSocket.ConnectAsync(endpointUrl, ct).ConfigureAwait(false);
                if (subProtocol == Profiles.OpcUaWsSubProtocolUacp)
                {
                    var peer = new UacpPeer(webSocket, endpointUrl, 65535, m_telemetry!);
                    await peer.OpenAsync(ct).ConfigureAwait(false);
                }

                var upgrades = (System.Collections.IDictionary)
                    typeof(HttpsTransportListener).GetField(
                        "m_activeUpgrades", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(listener)!;
                Assert.That(upgrades, Has.Count.EqualTo(1));
                using var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback =
                        (_, remote, _, _) => remote?.Thumbprint == serverThumbprint
                };
                using var client = new HttpClient(handler);
                var httpsUrl = new Uri($"https://127.0.0.1:{port}/");
                if (reclaimOnAdmission)
                {
                    using HttpResponseMessage admitted = await client.GetAsync(httpsUrl, ct).ConfigureAwait(false);
                    Assert.That(admitted.StatusCode, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
                }
                DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                while (upgrades.Count != 0 && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(25, ct).ConfigureAwait(false);
                }
                Assert.That(upgrades, Is.Empty, "the silent WebSocket retained the only connection slot");

                using HttpResponseMessage response = await client.GetAsync(httpsUrl, ct).ConfigureAwait(false);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.MethodNotAllowed));
            }
            finally
            {
                await listener.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                await listener.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A binary WebSocket peer that pipelines requests and stops reading keeps a send
        /// of the server waiting, and every queued response with it. Like the TCP
        /// listeners, the HTTPS listener closes such a channel once its send waited longer
        /// than the channel lifetime.
        /// </summary>
        [Test]
        [CancelAfter(60000)]
        public async Task BinaryWebSocketChannelWhoseSendStallsIsClosedAsync(CancellationToken ct)
        {
            const int channelLifetime = 1500;
            const int maxMessageSize = 1024 * 1024;
            const int requestCount = 150;

            Certificate certificate = CreateServerCertificate();
            using var certificateRegistry = new InMemoryCertificateRegistry(certificate);
            certificate.Dispose();
            var callback = new LargeResponseCallback(16 * 1024);

            (HttpsTransportListener listener, int port) = await OpenListenerOnFreePortAsync(
                () => (HttpsTransportListener)new HttpsTransportListenerFactory().Create(m_telemetry!),
                p =>
                {
                    TransportListenerSettings settings = CreateListenerSettings(certificateRegistry, p);
                    EndpointConfiguration configuration = settings.Configuration!;
                    configuration.MaxMessageSize = maxMessageSize;
                    configuration.MaxBufferSize = 65535;
                    configuration.OperationTimeout = 60000;
                    configuration.ChannelLifetime = channelLifetime;
                    return settings;
                },
                callback).ConfigureAwait(false);
            try
            {
                await WaitForListenerReadyAsync(port).ConfigureAwait(false);
                var endpointUrl = new Uri($"wss://127.0.0.1:{port}/");

                using var handler = new SocketsHttpHandler
                {
                    // a small receive buffer, so the server's sends wait soon after the peer stops reading
                    ConnectCallback = async (_, token) =>
                    {
                        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                        {
                            ReceiveBufferSize = 4096,
                            NoDelay = true
                        };
                        try
                        {
                            await socket.ConnectAsync(IPAddress.Loopback, port, token).ConfigureAwait(false);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch
                        {
                            socket.Dispose();
                            throw;
                        }
                    },
#pragma warning disable CA5359 // the loopback test server uses a self-signed certificate
                    SslOptions = new SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = static (_, _, _, _) => true
                    }
#pragma warning restore CA5359
                };
                using var invoker = new HttpMessageInvoker(handler);
                using var webSocket = new ClientWebSocket();
                webSocket.Options.AddSubProtocol(Profiles.OpcUaWsSubProtocolUacp);
                await webSocket.ConnectAsync(endpointUrl, invoker, ct).ConfigureAwait(false);

                var peer = new UacpPeer(webSocket, endpointUrl, maxMessageSize, m_telemetry!);
                await peer.OpenAsync(ct).ConfigureAwait(false);
                ConcurrentDictionary<uint, TcpListenerChannel> channels = GetWssChannels(listener);
                Assert.That(channels, Has.Count.EqualTo(1));

                // a response arrives while the peer reads
                await peer.SendReadRequestsAsync(1, ct).ConfigureAwait(false);
                byte[] first = await peer.ReceiveAsync(ct).ConfigureAwait(false);
                Assert.That(
                    BinaryPrimitives.ReadUInt32LittleEndian(first),
                    Is.EqualTo(TcpMessageType.Message | TcpMessageType.Final));

                // pipeline requests whose responses the peer never reads
                await peer.SendReadRequestsAsync(requestCount, ct).ConfigureAwait(false);

                DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                while (!channels.IsEmpty && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(100, ct).ConfigureAwait(false);
                }
                TestContext.Out.WriteLine($"requests={callback.RequestCount} channels={channels.Count}");
                Assert.That(callback.RequestCount, Is.GreaterThan(0));
                Assert.That(channels, Is.Empty, "the WebSocket channel with a stalled send stayed open");
            }
            finally
            {
                await listener.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                await listener.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static ConcurrentDictionary<uint, TcpListenerChannel> GetWssChannels(HttpsTransportListener listener)
        {
            FieldInfo field = typeof(HttpsTransportListener).GetField(
                "m_wssChannels", BindingFlags.NonPublic | BindingFlags.Instance)!;
            return (ConcurrentDictionary<uint, TcpListenerChannel>)field.GetValue(listener)!;
        }

        /// <summary>
        /// Answers every request with a Read response that carries a large ByteString.
        /// </summary>
        private sealed class LargeResponseCallback : ITransportListenerCallback
        {
            public LargeResponseCallback(int responseBytes)
            {
                m_payload = ByteString.From(new byte[responseBytes]);
            }

            public int RequestCount => Volatile.Read(ref m_requestCount);

            public ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext secureChannelContext,
                IServiceRequest request,
                CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_requestCount);
                return new ValueTask<IServiceResponse>(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good },
                    Results = [new DataValue(new Variant(m_payload))],
                    DiagnosticInfos = []
                });
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
            private int m_requestCount;
        }

        /// <summary>
        /// A UA-TCP peer over an <c>opcua+uacp</c> WebSocket: it opens a channel with
        /// SecurityPolicy None and pipelines requests, one chunk per WebSocket message.
        /// </summary>
        private sealed class UacpPeer
        {
            public UacpPeer(WebSocket webSocket, Uri endpointUrl, int maxMessageSize, ITelemetryContext telemetry)
            {
                m_webSocket = webSocket;
                m_endpointUrl = endpointUrl;
                m_maxMessageSize = maxMessageSize;
                m_context = ServiceMessageContext.Create(telemetry);
            }

            public async Task OpenAsync(CancellationToken ct)
            {
                await SendAsync(BuildHello(), ct).ConfigureAwait(false);
                byte[] ack = await ReceiveAsync(ct).ConfigureAwait(false);
                Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(ack), Is.EqualTo(TcpMessageType.Acknowledge));

                await SendAsync(BuildOpenSecureChannel(), ct).ConfigureAwait(false);
                byte[] open = await ReceiveAsync(ct).ConfigureAwait(false);
                Assert.That(
                    BinaryPrimitives.ReadUInt32LittleEndian(open) & TcpMessageType.MessageTypeMask,
                    Is.EqualTo(TcpMessageType.Open));
                using var decoder = new BinaryDecoder(
                    new ArraySegment<byte>(open, 8, open.Length - 8), m_context);
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

            public async Task SendReadRequestsAsync(int count, CancellationToken ct)
            {
                for (int ii = 0; ii < count; ii++)
                {
                    await SendAsync(BuildReadRequest(), ct).ConfigureAwait(false);
                }
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

            private Task SendAsync(byte[] chunk, CancellationToken ct)
            {
                return m_webSocket.SendAsync(
                    new ArraySegment<byte>(chunk), WebSocketMessageType.Binary, endOfMessage: true, ct);
            }

            /// <summary>
            /// Reads one chunk, which may span several WebSocket messages.
            /// </summary>
            public async Task<byte[]> ReceiveAsync(CancellationToken ct)
            {
                byte[] header = await ReceiveExactlyAsync(8, ct).ConfigureAwait(false);
                int size = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
                byte[] chunk = new byte[size];
                Array.Copy(header, chunk, 8);
                byte[] body = await ReceiveExactlyAsync(size - 8, ct).ConfigureAwait(false);
                Array.Copy(body, 0, chunk, 8, body.Length);
                return chunk;
            }

            private async Task<byte[]> ReceiveExactlyAsync(int count, CancellationToken ct)
            {
                byte[] buffer = new byte[count];
                int offset = 0;
                while (offset < count)
                {
                    int take = Math.Min(count - offset, m_pendingCount);
                    if (take > 0)
                    {
                        Array.Copy(m_pending, m_pendingOffset, buffer, offset, take);
                        m_pendingOffset += take;
                        m_pendingCount -= take;
                        offset += take;
                        continue;
                    }
                    WebSocketReceiveResult result = await m_webSocket
                        .ReceiveAsync(new ArraySegment<byte>(m_pending), ct)
                        .ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        throw new IOException("The WebSocket closed while reading.");
                    }
                    m_pendingOffset = 0;
                    m_pendingCount = result.Count;
                }
                return buffer;
            }

            private readonly WebSocket m_webSocket;
            private readonly Uri m_endpointUrl;
            private readonly int m_maxMessageSize;
            private readonly ServiceMessageContext m_context;
            private readonly byte[] m_pending = new byte[65536];
            private int m_pendingOffset;
            private int m_pendingCount;
            private uint m_secureChannelId;
            private uint m_tokenId;
            private uint m_sequenceNumber;
            private uint m_requestId;
        }
    }
}
