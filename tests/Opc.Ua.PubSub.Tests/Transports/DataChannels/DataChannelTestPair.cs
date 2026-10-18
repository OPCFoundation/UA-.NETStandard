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
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Tests.Transports.DataChannels
{
    /// <summary>
    /// Runs both real Core contexts over bounded in-process record paths.
    /// This fixture proves the runtime boundary, not network authentication or a native carrier implementation.
    /// </summary>
    internal sealed class DataChannelTestPair : IAsyncDisposable
    {
        private DataChannelTestPair(DataChannelCarrierKind carrierKind, DataChannelOptions? options)
        {
            LeftCarrier = new InProcessCarrier(carrierKind, true);
            RightCarrier = new InProcessCarrier(carrierKind, false);
            LeftAuthorizer = new ResourceAuthorizer();
            RightAuthorizer = new ResourceAuthorizer();
            LeftBudget = new DataChannelApplicationBudget();
            RightBudget = new DataChannelApplicationBudget();
            Left = new DataChannelContext(LeftCarrier, LeftAuthorizer, options ?? new DataChannelOptions(),
                LeftBudget, TimeProvider.System, NUnitTelemetryContext.Create());
            Right = new DataChannelContext(RightCarrier, RightAuthorizer, options ?? new DataChannelOptions(),
                RightBudget, TimeProvider.System, NUnitTelemetryContext.Create());
            LeftCarrier.Attach(Right);
            RightCarrier.Attach(Left);
        }

        /// <summary>
        /// Gets the transport initiator's real context.
        /// </summary>
        public DataChannelContext Left { get; }

        /// <summary>
        /// Gets the transport acceptor's real context.
        /// </summary>
        public DataChannelContext Right { get; }

        /// <summary>
        /// Gets the initiator's bounded record path and independent payload capture.
        /// </summary>
        public InProcessCarrier LeftCarrier { get; }

        /// <summary>
        /// Gets the acceptor's bounded record path and independent payload capture.
        /// </summary>
        public InProcessCarrier RightCarrier { get; }

        /// <summary>
        /// Gets the initiator's explicit configured resource policy.
        /// </summary>
        public ResourceAuthorizer LeftAuthorizer { get; }

        /// <summary>
        /// Gets the acceptor's explicit configured resource policy.
        /// </summary>
        public ResourceAuthorizer RightAuthorizer { get; }

        /// <summary>
        /// Gets accounting for the initiator, independently of the other application.
        /// </summary>
        public DataChannelApplicationBudget LeftBudget { get; }

        /// <summary>
        /// Gets accounting for the acceptor, including application-owned receive leases.
        /// </summary>
        public DataChannelApplicationBudget RightBudget { get; }

        /// <summary>
        /// Completes actual HELLO exchange before returning either context.
        /// </summary>
        public static async Task<DataChannelTestPair> CreateAsync(
            DataChannelCarrierKind carrierKind = DataChannelCarrierKind.SharedUasc,
            DataChannelOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var pair = new DataChannelTestPair(carrierKind, options);
            try
            {
                await Task.WhenAll(
                    pair.Left.StartAsync(cancellationToken).AsTask(),
                    pair.Right.StartAsync(cancellationToken).AsTask()).ConfigureAwait(false);
                return pair;
            }
            catch
            {
                await pair.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await Left.DisposeAsync().ConfigureAwait(false);
            await Right.DisposeAsync().ConfigureAwait(false);
            await LeftCarrier.DisposeAsync().ConfigureAwait(false);
            await RightCarrier.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Authorizes only the fixture's explicit resource prefix and supported configured protocols.
        /// </summary>
        internal sealed class ResourceAuthorizer : IDataChannelAuthorizer
        {
            /// <summary>
            /// Gets or sets explicit refusal used by failed-open tests.
            /// </summary>
            public bool Deny { get; set; }

            /// <summary>
            /// Gets or sets the explicitly admitted grant identifier for adapter-forwarding tests only.
            /// Server grant redemption and real Session authorization have separate control-layer tests.
            /// </summary>
            public Guid? SessionGrant { get; set; }

            /// <summary>
            /// Gets recorded authorization inputs, proving configured paths have no Session route.
            /// </summary>
            public ConcurrentQueue<DataChannelAuthorizationRequest> Requests { get; } = new();

            /// <inheritdoc/>
            public ValueTask<DataChannelAuthorization> AuthorizeAsync(
                DataChannelAuthorizationRequest request,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Requests.Enqueue(request);
                DataChannelParametersDataType parameters = request.Parameters;
                bool permitted = !Deny &&
                    (request.Mode == DataChannelAuthorizationMode.Configured ||
                        (request.Mode == DataChannelAuthorizationMode.Session &&
                            SessionGrant.HasValue &&
                            request.GrantId == SessionGrant)) &&
                    parameters.ResourceUri?.StartsWith("urn:test:pubsub:", StringComparison.Ordinal) == true &&
                    parameters.Protocol is DataChannelProfiles.UadpPayload or DataChannelProfiles.JsonPayload;
                return new ValueTask<DataChannelAuthorization>(permitted
                    ? new DataChannelAuthorization(DataChannelResult.Good, parameters,
                        sessionId: request.Mode == DataChannelAuthorizationMode.Session ? new NodeId(99, 1) : default)
                    : new DataChannelAuthorization(DataChannelResult.NotAuthorized));
            }
        }

        /// <summary>
        /// An explicit test carrier: every committed write traverses a bounded queue into the peer's real parser.
        /// </summary>
        internal sealed class InProcessCarrier : IDataChannelCarrier, IAsyncDisposable
        {
            /// <summary>
            /// Creates reciprocal synthetic verified identities for the in-process runtime fixture only.
            /// </summary>
            public InProcessCarrier(DataChannelCarrierKind kind, bool initiator)
            {
                Kind = kind;
                Identity = new DataChannelPeerIdentity(
                    initiator ? "urn:test:pubsub:left" : "urn:test:pubsub:right",
                    initiator ? "urn:test:pubsub:right" : "urn:test:pubsub:left",
                    ByteString.From(new byte[32]),
                    ByteString.From(new byte[32]),
                    DataChannelProtectionMode.Confidential,
                    initiator);
                m_streamId = initiator ? 0 : -3;
                m_records = Channel.CreateBounded<Packet>(new BoundedChannelOptions(32)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false
                });
            }

            /// <inheritdoc/>
            public DataChannelCarrierKind Kind { get; }

            /// <inheritdoc/>
            public DataChannelPeerIdentity Identity { get; }

            /// <inheritdoc/>
            public int MaxRecordSize => 1024 * 1024;

            /// <inheritdoc/>
            public int MaxDatagramSize => Kind is DataChannelCarrierKind.Quic or DataChannelCarrierKind.WebTransport
                ? 65536 : 0;

            /// <inheritdoc/>
            public ulong ManagementStreamId => 0;

            /// <summary>
            /// Gets DATA bodies recovered independently from the complete committed carrier records.
            /// </summary>
            public ConcurrentQueue<ByteString> Payloads { get; } = new();

            /// <summary>
            /// Gets unexpected receive outcomes for assertions.
            /// </summary>
            public ConcurrentQueue<DataChannelResult> Failures { get; } = new();

            /// <summary>
            /// Gets or sets an explicit post-commit observation for owned-buffer lifetime tests.
            /// </summary>
            public TaskCompletionSource<bool>? DataCommitted { get; set; }

            /// <summary>
            /// Gets or sets a post-commit carrier hold. Caller cancellation cannot retract this write.
            /// </summary>
            public Task? ReleaseCommittedData { get; set; }

            /// <summary>
            /// Gets currently bound native streams for cleanup assertions.
            /// </summary>
            public int BoundStreams
            {
                get
                {
                    lock (m_gate)
                    {
                        return m_streams.Count;
                    }
                }
            }

            /// <summary>
            /// Starts the sole serialized inbound dispatcher after both contexts exist.
            /// </summary>
            public void Attach(DataChannelContext peer)
            {
                m_peer = peer;
                m_pump = Task.Run(PumpAsync);
            }

            /// <inheritdoc/>
            public ValueTask<ulong> OpenStreamAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<ulong>((ulong)Interlocked.Add(ref m_streamId, 4));
            }

            /// <inheritdoc/>
            public ValueTask<bool> BindStreamAsync(
                ulong streamId,
                uint channelId,
                bool locallyOpened,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool localStream = (streamId & 1) == (Identity.IsTransportInitiator ? 0ul : 1ul);
                lock (m_gate)
                {
                    return new ValueTask<bool>(streamId != ManagementStreamId &&
                        localStream == locallyOpened &&
                        m_streams.Add(streamId));
                }
            }

            /// <inheritdoc/>
            public async ValueTask<DataChannelResult> SendAsync(
                DataChannelWrite write,
                CancellationToken cancellationToken = default)
            {
                if (!await m_records.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false))
                {
                    return DataChannelResult.Closed;
                }
                if (!write.TryCommit())
                {
                    return write.Result;
                }
                if (write.IsData)
                {
                    DataCommitted?.TrySetResult(true);
                    if (ReleaseCommittedData is { } release)
                    {
                        await release.ConfigureAwait(false);
                    }
                }
                ByteString payload = write.Payload.Copy();
                if (write.IsData)
                {
                    int prefix = Kind is DataChannelCarrierKind.DedicatedUasc or DataChannelCarrierKind.SharedUasc ||
                        write.Path == DataChannelPath.Datagram
                        ? 4 : 0;
                    uint length = BinaryPrimitives.ReadUInt32LittleEndian(payload.Span[prefix..]);
                    if (length + 4 != payload.Length - prefix || payload[prefix + 4] != 0)
                    {
                        throw new InvalidOperationException("The Core DATA record has an invalid boundary.");
                    }
                    Payloads.Enqueue(payload[(prefix + 13)..].Copy());
                }
                await m_records.Writer.WriteAsync(
                    new Packet(write.Path, write.StreamId, write.RequestId, payload),
                    CancellationToken.None).ConfigureAwait(false);
                return DataChannelResult.Good;
            }

            /// <inheritdoc/>
            public ValueTask CloseFlowAsync(
                ulong streamId,
                DataChannelResult result,
                CancellationToken cancellationToken = default)
            {
                lock (m_gate)
                {
                    m_streams.Remove(streamId);
                }
                return default;
            }

            /// <inheritdoc/>
            public ValueTask CloseAsync(
                DataChannelResult result,
                CancellationToken cancellationToken = default)
            {
                return default;
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                m_records.Writer.TryComplete();
                await m_stop.CancelAsync().ConfigureAwait(false);
                if (m_pump is not null)
                {
                    await m_pump.ConfigureAwait(false);
                }
                m_stop.Dispose();
            }

            /// <summary>
            /// Delivers distinct UASC, ordered-native and datagram paths to the real common runtime.
            /// </summary>
            private async Task PumpAsync()
            {
                try
                {
                    await foreach (Packet packet in m_records.Reader.ReadAllAsync(m_stop.Token).ConfigureAwait(false))
                    {
                        DataChannelResult result;
                        if (Kind is DataChannelCarrierKind.DedicatedUasc or DataChannelCarrierKind.SharedUasc)
                        {
                            result = await m_peer!.ReceiveUascAsync(
                                (byte)'F', packet.RequestId, packet.Payload, m_stop.Token).ConfigureAwait(false);
                        }
                        else if (packet.Path == DataChannelPath.Datagram)
                        {
                            result = await m_peer!.ReceiveDatagramAsync(packet.Payload, m_stop.Token)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            result = await m_peer!.ReceiveStreamAsync(
                                packet.StreamId, packet.Payload, cancellationToken: m_stop.Token).ConfigureAwait(false);
                        }
                        if (result is not (DataChannelResult.Good or DataChannelResult.Closed))
                        {
                            Failures.Enqueue(result);
                        }
                    }
                }
                catch (OperationCanceledException) when (m_stop.IsCancellationRequested)
                {
                }
            }

            /// <summary>
            /// Owns one committed record until the peer has finished processing it.
            /// </summary>
            private sealed record Packet(DataChannelPath Path, ulong StreamId, uint RequestId, ByteString Payload);

            private readonly Channel<Packet> m_records;
            private readonly CancellationTokenSource m_stop = new();
            private readonly Lock m_gate = new();
            private readonly HashSet<ulong> m_streams = [];
            private DataChannelContext? m_peer;
            private Task? m_pump;
            private long m_streamId;
        }
    }
}
