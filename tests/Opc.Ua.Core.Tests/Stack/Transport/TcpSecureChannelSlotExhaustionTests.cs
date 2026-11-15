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
using System.Diagnostics;
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
    /// Regression tests for an unauthenticated peer pinning opc.tcp channel slots
    /// with unfinished messages. After HEL + OPN (SecurityPolicy None, a discovery-only
    /// channel every server grants) the peer sends an intermediate message chunk, then
    /// keeps sending small intermediate chunks each with a new request id. A chunk with a
    /// different request id drops the partial message and used to start a fresh one with
    /// a new start timestamp, so the assembly deadline never fired; the chunk refreshes
    /// last-active, so the inactivity sweep never fires; and admission reclaim used to
    /// refuse a channel with a partial message. With MaxChannelCount such peers every new
    /// client was refused with Bad_TcpNotEnoughResources.
    /// </summary>
    /// <remarks>
    /// The server is a real <see cref="TcpTransportListener"/> opened on loopback with
    /// MaxChannelCount 8 and ChannelLifetime 4000 ms. ChannelLifetime drives three timers:
    /// the message-assembly deadline (= ChannelLifetime), the inactivity sweep (period
    /// ChannelLifetime / 2, closes channels idle longer than ChannelLifetime) and the idle
    /// reclaim comparison. The attacker trickles one chunk per channel every 1000 ms, below
    /// all of them.
    /// </remarks>
    [TestFixture]
    [Category("TransportChannel")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class TcpSecureChannelSlotExhaustionTests
    {
        private const int kMaxMessageSize = 1024 * 1024;
        private const int kMaxChannelCount = 8;
        private const int kChannelLifetime = 4000;
        private const int kAttackChunkIntervalMs = 1000;

        /// <summary>
        /// Control A: MaxChannelCount peers that complete HEL + OPN(None) and then stay idle
        /// are reclaimed by the server (inactivity sweep / admission reclaim), so a real
        /// client connects shortly after the slots fill.
        /// </summary>
        [Test]
        [CancelAfter(120000)]
        public async Task IdleSessionlessChannelsAreReclaimedSoALegitimateClientConnectsAsync(
            CancellationToken ct)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Uri endpointUrl = new($"opc.tcp://127.0.0.1:{GetFreeTcpPort()}");

            await using var listener = new TcpTransportListener(telemetry);
            await listener.OpenAsync(
                endpointUrl,
                CreateListenerSettings(endpointUrl, kMaxChannelCount, kChannelLifetime),
                new MinimalCallback(),
                ct).ConfigureAwait(false);

            var peers = new List<RawPeer>();
            try
            {
                for (int ii = 0; ii < kMaxChannelCount; ii++)
                {
                    var peer = new RawPeer(endpointUrl, kMaxMessageSize);
                    peers.Add(peer);
                    await peer.ConnectAndOpenAsync(ct).ConfigureAwait(false);
                }

                TestContext.Out.WriteLine(
                    $"filled {peers.Count} idle sessionless channels; MaxChannelCount={kMaxChannelCount}, " +
                    $"ChannelLifetime={kChannelLifetime} ms");

                var stopwatch = Stopwatch.StartNew();
                (bool connected, string status) = await ProbeUntilConnectedAsync(
                    endpointUrl, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                stopwatch.Stop();

                TestContext.Out.WriteLine(
                    $"Control A: legitimate client connected={connected} after " +
                    $"{stopwatch.ElapsedMilliseconds} ms (last status {status})");
                Assert.That(connected, Is.True,
                    "the server did not reclaim an idle sessionless channel for a real client");
            }
            finally
            {
                foreach (RawPeer peer in peers)
                {
                    peer.Dispose();
                }
                await listener.CloseAsync(ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Attack B: the same number of peers hold their slots with a self-renewing partial
        /// message (rotating request ids). A real client must still connect within 10 s
        /// while the attack runs: admission reclaim closes the oldest channel without a
        /// session even when it holds an unfinished message. Service must also recover
        /// once the attacker stops.
        /// </summary>
        [Test]
        [CancelAfter(120000)]
        public async Task PartialMessageChannelsWithRotatingRequestIdsDoNotLockOutLegitimateClientsAsync(
            CancellationToken ct)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            Uri endpointUrl = new($"opc.tcp://127.0.0.1:{GetFreeTcpPort()}");

            await using var listener = new TcpTransportListener(telemetry);
            await listener.OpenAsync(
                endpointUrl,
                CreateListenerSettings(endpointUrl, kMaxChannelCount, kChannelLifetime),
                new MinimalCallback(),
                ct).ConfigureAwait(false);

            var peers = new List<RawPeer>();
            using var attackCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task attackTask = Task.CompletedTask;
            try
            {
                for (int ii = 0; ii < kMaxChannelCount; ii++)
                {
                    var peer = new RawPeer(endpointUrl, kMaxMessageSize);
                    peers.Add(peer);
                    await peer.ConnectAndOpenAsync(ct).ConfigureAwait(false);
                    // the first intermediate chunk starts the partial message (request id 1)
                    await peer.SendPartialChunkAsync(ct).ConfigureAwait(false);
                }

                int channelsNow = GetChannels(listener).Count;
                TestContext.Out.WriteLine(
                    $"{peers.Count} attack channels opened (registered channels={channelsNow}); " +
                    $"MaxChannelCount={kMaxChannelCount}, ChannelLifetime={kChannelLifetime} ms, " +
                    $"attacker sends one new-requestId chunk per channel every {kAttackChunkIntervalMs} ms");

                // keep every partial message self-renewing: a chunk with a new request id
                // every kAttackChunkIntervalMs ms, below ChannelLifetime.
                attackTask = Task.Run(() => RunAttackAsync(peers, attackCts.Token), attackCts.Token);

                // Probe a legitimate full connect once per second for >= 3x ChannelLifetime.
                var sw = Stopwatch.StartNew();
                int attempts = 0;
                int successes = 0;
                long firstSuccessMs = -1;
                string lastStatus = "none";
                TimeSpan attackWindow = TimeSpan.FromMilliseconds(3 * kChannelLifetime);
                while (sw.Elapsed < attackWindow)
                {
                    (bool connected, string status) = await ProbeOnceAsync(endpointUrl, ct)
                        .ConfigureAwait(false);
                    attempts++;
                    lastStatus = status;
                    if (connected)
                    {
                        successes++;
                        if (firstSuccessMs < 0)
                        {
                            firstSuccessMs = sw.ElapsedMilliseconds;
                        }
                    }
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }

                TestContext.Out.WriteLine(
                    $"Attack B: during {attackWindow.TotalSeconds:F0} s of attack, {successes}/{attempts} " +
                    $"legitimate connects succeeded; firstSuccessMs={firstSuccessMs}; last status={lastStatus}");

                // Stop the attacker and confirm the service recovers.
                attackCts.Cancel();
                try
                {
                    await attackTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                foreach (RawPeer peer in peers)
                {
                    peer.Dispose();
                }
                peers.Clear();

                var recovery = Stopwatch.StartNew();
                (bool recovered, string recoveryStatus) = await ProbeUntilConnectedAsync(
                    endpointUrl, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                recovery.Stop();
                TestContext.Out.WriteLine(
                    $"recovery after attacker stopped: connected={recovered} in " +
                    $"{recovery.ElapsedMilliseconds} ms (status {recoveryStatus})");

                // A legitimate client keeps connecting while the attack runs.
                Assert.That(successes, Is.GreaterThan(0),
                    "no legitimate client could connect at any time during the pre-authentication " +
                    "channel-slot exhaustion attack");
                Assert.That(firstSuccessMs, Is.LessThanOrEqualTo(10000),
                    "a legitimate client could not connect within 10 s while the attack ran");
                Assert.That(recovered, Is.True, "the server did not recover after the attacker stopped");
            }
            finally
            {
                attackCts.Cancel();
                try
                {
                    await attackTask.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // the attack loop ends when its sockets close or the token cancels
                }
                foreach (RawPeer peer in peers)
                {
                    peer.Dispose();
                }
                await listener.CloseAsync(ct).ConfigureAwait(false);
            }
        }

        private static async Task RunAttackAsync(IReadOnlyList<RawPeer> peers, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                foreach (RawPeer peer in peers)
                {
                    try
                    {
                        await peer.SendPartialChunkAsync(ct).ConfigureAwait(false);
                    }
                    catch (Exception) when (!ct.IsCancellationRequested)
                    {
                        // a peer socket may have been closed; ignore and keep the rest alive
                    }
                }
                await Task.Delay(kAttackChunkIntervalMs, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Retries a legitimate full connect until it succeeds or the budget elapses.
        /// </summary>
        private static async Task<(bool Connected, string Status)> ProbeUntilConnectedAsync(
            Uri endpointUrl,
            TimeSpan budget,
            CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            string status = "none";
            while (sw.Elapsed < budget)
            {
                bool connected;
                (connected, status) = await ProbeOnceAsync(endpointUrl, ct).ConfigureAwait(false);
                if (connected)
                {
                    return (true, status);
                }
                await Task.Delay(500, ct).ConfigureAwait(false);
            }
            return (false, status);
        }

        /// <summary>
        /// One legitimate connect attempt: HEL then OPN(None). Returns whether a secure
        /// channel was granted and a short status string.
        /// </summary>
        private static async Task<(bool Connected, string Status)> ProbeOnceAsync(
            Uri endpointUrl,
            CancellationToken ct)
        {
            using var probe = new RawPeer(endpointUrl, kMaxMessageSize);
            try
            {
                string status = await probe.TryConnectAndOpenAsync(ct).ConfigureAwait(false);
                return (status == "ok", status);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return (false, ex.GetType().Name);
            }
        }

        private static ConcurrentDictionary<uint, TcpListenerChannel> GetChannels(TcpTransportListener listener)
        {
            FieldInfo field = typeof(TcpTransportListener).GetField(
                "m_channels", BindingFlags.NonPublic | BindingFlags.Instance)!;
            return (ConcurrentDictionary<uint, TcpListenerChannel>?)field.GetValue(listener) ?? [];
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
            int maxChannelCount,
            int channelLifetime)
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
                Configuration = CreateConfiguration(channelLifetime),
                ServerCertificates = certificateRegistry.Object,
                NamespaceUris = new NamespaceTable(),
                Factory = EncodeableFactory.Create(),
                MaxChannelCount = maxChannelCount,
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
        /// A minimal transport callback. The attack never completes a message, so no request
        /// is ever dispatched to it.
        /// </summary>
        private sealed class MinimalCallback : ITransportListenerCallback
        {
            public ValueTask<IServiceResponse> ProcessRequestAsync(
                SecureChannelContext secureChannelContext,
                IServiceRequest request,
                CancellationToken cancellationToken = default)
            {
                IServiceResponse response = new ServiceFault
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadServiceUnsupported }
                };
                return new ValueTask<IServiceResponse>(response);
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
        }

        /// <summary>
        /// A raw UA-TCP peer that opens a SecurityPolicy None channel by hand and can trickle
        /// self-renewing partial-message chunks (a new request id each time).
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
                    NoDelay = true
                };
            }

            public async Task ConnectAndOpenAsync(CancellationToken ct)
            {
                string status = await TryConnectAndOpenAsync(ct).ConfigureAwait(false);
                Assert.That(status, Is.EqualTo("ok"), $"peer handshake failed: {status}");
            }

            /// <summary>
            /// Attempts HEL + OPN(None). Returns "ok" on a granted secure channel, otherwise a
            /// short status describing the refusal.
            /// </summary>
            public async Task<string> TryConnectAndOpenAsync(CancellationToken ct)
            {
                await m_socket.ConnectAsync(IPAddress.Loopback, m_endpointUrl.Port).ConfigureAwait(false);

                await SendFrameAsync(BuildHello(), ct).ConfigureAwait(false);
                byte[] ack = await ReadFrameAsync(ct).ConfigureAwait(false);
                uint ackType = BinaryPrimitives.ReadUInt32LittleEndian(ack);
                if (ackType == TcpMessageType.Error)
                {
                    uint code = BinaryPrimitives.ReadUInt32LittleEndian(ack.AsSpan(8));
                    return code == StatusCodes.BadTcpNotEnoughResources.Code
                        ? "BadTcpNotEnoughResources"
                        : $"Error:0x{code:X8}";
                }
                if (ackType != TcpMessageType.Acknowledge)
                {
                    return $"unexpected:0x{ackType:X8}";
                }

                await SendFrameAsync(BuildOpenSecureChannel(), ct).ConfigureAwait(false);
                byte[] open = await ReadFrameAsync(ct).ConfigureAwait(false);
                uint openType = BinaryPrimitives.ReadUInt32LittleEndian(open);
                if (openType == TcpMessageType.Error)
                {
                    uint code = BinaryPrimitives.ReadUInt32LittleEndian(open.AsSpan(8));
                    return $"OpenError:0x{code:X8}";
                }
                if ((openType & TcpMessageType.MessageTypeMask) != TcpMessageType.Open)
                {
                    return $"unexpectedOpen:0x{openType:X8}";
                }
                DecodeOpenResponse(open);
                return "ok";
            }

            /// <summary>
            /// Sends one intermediate message chunk carrying a GetEndpointsRequest body under a
            /// NEW request id, which drops any partial message and restarts assembly.
            /// </summary>
            public Task SendPartialChunkAsync(CancellationToken ct)
            {
                uint requestId = ++m_requestId;
                byte[] body = BinaryEncoder.EncodeMessage(
                    new GetEndpointsRequest
                    {
                        RequestHeader = new RequestHeader { Timestamp = DateTime.UtcNow, TimeoutHint = 60000 },
                        EndpointUrl = m_endpointUrl.ToString()
                    },
                    m_context);
                byte[] chunk = BuildChunk(TcpMessageType.Message | TcpMessageType.Intermediate, encoder =>
                {
                    encoder.WriteUInt32(null, m_secureChannelId);
                    encoder.WriteUInt32(null, m_tokenId);
                    encoder.WriteUInt32(null, ++m_sequenceNumber);
                    encoder.WriteUInt32(null, requestId);
                    encoder.WriteRawBytes(body, 0, body.Length);
                });
                return SendFrameAsync(chunk, ct);
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
