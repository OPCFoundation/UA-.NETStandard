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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Opc.Ua.DataChannels;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.Core.DataChannels.Tests.Runtime
{
    /// <summary>
    /// Deterministic in-process carrier fixture for common runtime obligations, not live carrier evidence.
    /// All traffic crosses public ingress and the actual common serializer.
    /// </summary>
    internal sealed class FlowLoopback : IAsyncDisposable
    {
        /// <summary>
        /// Creates isolated endpoints with optional direct providers, telemetry and deterministic time.
        /// </summary>
        internal FlowLoopback(
            DataChannelCarrierKind kind = DataChannelCarrierKind.SharedUasc,
            DataChannelOptions? options = null,
            DataChannelApplicationBudget? initiatorBudget = null,
            FakeTimeProvider? clock = null,
            bool enableDirectBinding = false,
            bool enableDirectStreams = false,
            bool enableDirectDatagrams = false,
            Action<ILoggingBuilder>? configureLogging = null)
        {
            Clock = clock ?? new FakeTimeProvider(new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero));
            Options = options ??
                new DataChannelOptions
                {
                    MaxMessageSize = 1024,
                    MaxReceiveBytes = 4096,
                    MaxContextBytes = 32768,
                    MaxQueueMessages = 16,
                    MaxContextMessages = 128
                };
            m_telemetry = DefaultTelemetry.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.None);
                configureLogging?.Invoke(builder);
            });
            InitiatorBudget = initiatorBudget ?? new DataChannelApplicationBudget();
            AcceptorBudget = new DataChannelApplicationBudget();
            InitiatorPolicy = new TestFlowAuthorizer();
            AcceptorPolicy = new TestFlowAuthorizer();
            var association = new TestFlowAssociation();
            InitiatorCarrier = new TestFlowCarrier(kind, true, association, enableDirectBinding);
            AcceptorCarrier = new TestFlowCarrier(kind, false, association, enableDirectBinding);
            if (enableDirectStreams)
            {
                var direct = new TestDirectStreamNetwork();
                InitiatorCarrier.DirectStreams = direct;
                AcceptorCarrier.DirectStreams = direct;
            }
            Initiator = new DataChannelContext(
                InitiatorCarrier, InitiatorPolicy, Options, InitiatorBudget, Clock, m_telemetry);
            Acceptor = new DataChannelContext(
                AcceptorCarrier, AcceptorPolicy, Options, AcceptorBudget, Clock, m_telemetry);
            InitiatorCarrier.Peer = Acceptor;
            AcceptorCarrier.Peer = Initiator;
            InitiatorCarrier.PeerCarrier = AcceptorCarrier;
            AcceptorCarrier.PeerCarrier = InitiatorCarrier;
            if (enableDirectDatagrams)
            {
                InitiatorCarrier.MaxDirectDatagramSize = 2048;
                AcceptorCarrier.MaxDirectDatagramSize = 2048;
            }
        }

        internal FakeTimeProvider Clock { get; }
        internal DataChannelOptions Options { get; }
        internal DataChannelContext Initiator { get; }
        internal DataChannelContext Acceptor { get; }
        internal TestFlowCarrier InitiatorCarrier { get; }
        internal TestFlowCarrier AcceptorCarrier { get; }
        internal TestFlowAuthorizer InitiatorPolicy { get; }
        internal TestFlowAuthorizer AcceptorPolicy { get; }
        internal DataChannelApplicationBudget InitiatorBudget { get; }
        internal DataChannelApplicationBudget AcceptorBudget { get; }

        internal async Task StartAsync()
        {
            await Task.WhenAll(
                Initiator.StartAsync().AsTask(),
                Acceptor.StartAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }

        internal async Task<FlowPair> OpenAsync(
            DataChannelParametersDataType? parameters = null,
            bool fromInitiator = true,
            DataChannelAuthorizationMode authorization = DataChannelAuthorizationMode.Configured,
            Guid? grant = null)
        {
            DataChannelContext opener = fromInitiator ? Initiator : Acceptor;
            DataChannelContext acceptor = fromInitiator ? Acceptor : Initiator;
            Task<DataChannel> opened = opener.OpenAsync(
                parameters ?? Parameters(), authorization, grant).AsTask();
            DataChannel local = await opened.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            DataChannel? remote = await acceptor.AcceptAsync().AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            return new FlowPair(local, remote ?? throw new InvalidOperationException("A ready flow was not accepted."));
        }

        internal static DataChannelParametersDataType Parameters(
            DataChannelDirection direction = DataChannelDirection.Duplex,
            DataChannelDeliveryMode delivery = DataChannelDeliveryMode.Ordered,
            uint maxMessage = 256,
            uint receiveBytes = 1024,
            double rate = 0,
            bool expiry = false)
        {
            return new DataChannelParametersDataType
            {
                ResourceUri = "urn:runtime:test:resource",
                Protocol = DataChannelProfiles.UadpPayload,
                ContentType = "application/opcua+uadp",
                Direction = direction,
                Delivery = delivery,
                Protection = DataChannelProtectionMode.Confidential,
                MaxMessageSize = maxMessage,
                MaxReceiveBytes = receiveBytes,
                MaxRate = rate,
                AllowQueueExpiry = expiry
            };
        }

        /// <summary>
        /// Creates a type-0 UADP message around synthetic test payload bytes.
        /// </summary>
        internal static ByteString UadpPayload(ByteString payload, uint publisherId = 42)
        {
            byte[] networkMessage = UadpApplicationPayloadHeaderCodec.Encode(
                new Variant(publisherId), payload, int.MaxValue).ToArray();
            networkMessage[2] = 0;
            return new ByteString(networkMessage);
        }

        internal static ByteString TestPayload(DataChannelCarrierKind kind, ByteString payload)
        {
            return kind is DataChannelCarrierKind.DedicatedUasc or DataChannelCarrierKind.SharedUasc
                ? UadpPayload(payload)
                : payload;
        }

        internal async Task FlushAsync()
        {
            await EventuallyAsync(() => InitiatorCarrier.ActiveSends == 0 && AcceptorCarrier.ActiveSends == 0)
                .ConfigureAwait(false);
        }

        internal static async Task EventuallyAsync(Func<bool> condition)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!condition())
            {
                await Task.Delay(1, deadline.Token).ConfigureAwait(false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            InitiatorCarrier.ReleaseGates();
            AcceptorCarrier.ReleaseGates();
            FlowState[] flows = [.. (Initiator.Channels.ToArray() ?? [])
                .Concat(Acceptor.Channels.ToArray() ?? [])
                .Select(channel => channel.StateData)];
            try
            {
                await Task.WhenAll(Initiator.DisposeAsync().AsTask(), Acceptor.DisposeAsync().AsTask())
                    .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }
            catch (TimeoutException exception)
            {
                throw new TimeoutException($"Direct lanes: {string.Join("; ", flows.Select(flow =>
                    $"release={flow.ReleaseStarted}/{flow.Released.Task.Status}, state={flow.State}, " +
                    $"cancel={flow.DirectLane?.Shutdown.IsCancellationRequested}, " +
                    $"send={flow.DirectLane?.SendTask.Status}, receive={flow.DirectLane?.ReceiveTask.Status}, " +
                    $"sender={flow.DirectLane?.Sender.Task.Status}"))}; active baseline sends: " +
                    $"{InitiatorCarrier.ActiveSends}/{AcceptorCarrier.ActiveSends}", exception);
            }
            if (m_telemetry is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        private readonly ITelemetryContext m_telemetry;
    }

    internal readonly record struct FlowPair(DataChannel Local, DataChannel Remote);

    /// <summary>
    /// Configurable explicit authorization; every request is captured for binding/lifetime assertions.
    /// </summary>
    internal sealed class TestFlowAuthorizer : IDataChannelAuthorizer
    {
        internal Func<DataChannelAuthorizationRequest, CancellationToken, ValueTask<DataChannelAuthorization>>?
            Handler
        { get; set; }

        internal ConcurrentQueue<DataChannelAuthorizationRequest> Requests { get; } = new();

        public ValueTask<DataChannelAuthorization> AuthorizeAsync(
            DataChannelAuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(request);
            if (Handler != null)
            {
                return Handler(request, cancellationToken);
            }
            return new ValueTask<DataChannelAuthorization>(
                new DataChannelAuthorization(DataChannelResult.Good, request.Parameters));
        }
    }

    /// <summary>
    /// Verifies simulated stream ownership without attributing these tests to QUIC/TLS implementations.
    /// </summary>
    internal sealed class TestFlowAssociation
    {
        internal ulong Open(bool initiator)
        {
            lock (m_gate)
            {
                ulong id = initiator ? m_nextInitiator : m_nextAcceptor;
                if (initiator)
                {
                    m_nextInitiator += 4;
                }
                else
                {
                    m_nextAcceptor += 4;
                }
                m_streams.Add(id, initiator);
                return id;
            }
        }

        internal bool Verify(ulong stream, bool initiator)
        {
            lock (m_gate)
            {
                return m_streams.TryGetValue(stream, out bool owner) && owner == initiator;
            }
        }

        private readonly Lock m_gate = new();
        private readonly Dictionary<ulong, bool> m_streams = [];
        private ulong m_nextInitiator = 4;
        private ulong m_nextAcceptor = 1;
    }

    /// <summary>
    /// In-process carrier with explicit pre/post-commit gates and bounded fault injection.
    /// </summary>
    internal sealed class TestFlowCarrier :
        IDataChannelCarrier, IDataChannelFlowFinisher, IDataChannelDirectBindingProvider,
        IDataChannelDirectStreamProvider,
        IDataChannelDirectDatagramProvider
    {
        internal TestFlowCarrier(
            DataChannelCarrierKind kind,
            bool initiator,
            TestFlowAssociation association,
            bool enableDirectBinding = false)
        {
            Kind = kind;
            m_initiator = initiator;
            m_association = association;
            IsAvailable = enableDirectBinding;
            ByteString keyA = new(Enumerable.Repeat((byte)0x31, 32).ToArray());
            ByteString keyB = new(Enumerable.Repeat((byte)0x72, 32).ToArray());
            Identity = new DataChannelPeerIdentity(
                initiator ? "urn:runtime:initiator" : "urn:runtime:acceptor",
                initiator ? "urn:runtime:acceptor" : "urn:runtime:initiator",
                initiator ? keyA : keyB,
                initiator ? keyB : keyA,
                DataChannelProtectionMode.Confidential,
                initiator);
            MaxDatagramSize = IsNative ? 2048 : 0;
        }

        public DataChannelCarrierKind Kind { get; }
        public DataChannelPeerIdentity Identity { get; set; }
        public bool IsAvailable { get; set; }
        public string TransportProfileUri { get; set; } = DataChannelProfiles.UascQuic;
        public string Capability { get; set; } = "quic-direct-draft";
        public string Authority { get; set; } = "localhost:4840";
        public string Path { get; set; } = "/ua/quic?direct=1";
        internal bool ExporterUnavailable { get; set; }

        internal TestDirectStreamNetwork? DirectStreams { get; set; }

        /// <summary>The finite, genuinely separate datagram ingress used by the provider seam.</summary>
        private readonly Channel<ByteString> m_directDatagrams = Channel.CreateBounded<ByteString>(128);

        public bool AreDirectStreamsAvailable => DirectStreams != null;

        public int MaxDirectDatagramSize { get; set; }

        internal DataChannelResult DirectDatagramReceiveResult { get; set; } = DataChannelResult.Good;

        internal TestFlowCarrier? PeerCarrier { get; set; }

        public ValueTask<DataChannelResult> SendDirectDatagramAsync(
            DataChannelWrite write, CancellationToken cancellationToken = default)
        {
            return SendAsync(write, cancellationToken);
        }

        public ValueTask<ByteString> ReceiveDirectDatagramAsync(CancellationToken cancellationToken = default)
        {
            if (DirectDatagramReceiveResult != DataChannelResult.Good)
            {
                throw new DataChannelException(DirectDatagramReceiveResult);
            }
            return m_directDatagrams.Reader.ReadAsync(cancellationToken);
        }

        public ValueTask<System.IO.Stream> OpenDirectStreamAsync(
            Guid associationToken, CancellationToken cancellationToken = default)
        {
            return DirectStreams?.OpenAsync(associationToken, cancellationToken) ??
                throw new DataChannelException(DataChannelResult.Unsupported);
        }

        public ValueTask<System.IO.Stream> AcceptDirectStreamAsync(
            Guid associationToken, CancellationToken cancellationToken = default)
        {
            return DirectStreams?.AcceptAsync(associationToken, cancellationToken) ??
                throw new DataChannelException(DataChannelResult.Unsupported);
        }

        internal byte[]? LastExporterMaterial { get; private set; }
        public int MaxRecordSize { get; set; } = 4096;
        public int MaxDatagramSize { get; set; }
        public ulong ManagementStreamId => 0;
        internal bool IsNative => Kind is DataChannelCarrierKind.Quic or DataChannelCarrierKind.WebTransport;
        internal DataChannelContext? Peer { get; set; }
        internal ConcurrentQueue<CapturedFlowWrite> Writes { get; } = new();
        internal ConcurrentQueue<CapturedFlowWrite> DeliveredWrites { get; } = new();
        internal Func<DataChannelWrite, bool>? Drop { get; set; }
        internal Func<DataChannelWrite, DataChannelResult>? SendOutcome { get; set; }
        internal Func<DataChannelWrite, bool>? GateBefore { get; set; }
        internal Func<DataChannelWrite, bool>? GateAfter { get; set; }
        internal Func<DataChannelWrite, bool>? GateAfterDelivery { get; set; }
        internal bool SkipCommit { get; set; }
        internal bool RejectBinding { get; set; }
        internal bool UseQueuedUascIngress { get; set; }
        internal bool BlockFlowClose { get; set; }
        internal bool BlockFlowFinish { get; set; }
        internal bool ReleaseManagementOnFlowClose { get; set; }
        internal int SegmentSize { get; set; } = int.MaxValue;
        internal int ActiveSends => Volatile.Read(ref m_active);
        internal int ClosedFlows => Volatile.Read(ref m_closedFlows);
        internal int FinishedFlows => Volatile.Read(ref m_finishedFlows);
        internal int EndDirectionWrites => Volatile.Read(ref m_endDirectionWrites);
        internal int CloseCalls => Volatile.Read(ref m_closeCalls);
        internal TaskCompletionSource<bool> BeforeEntered { get; } = NewSignal();
        internal TaskCompletionSource<bool> AfterEntered { get; } = NewSignal();
        internal TaskCompletionSource<bool> BeforeRelease { get; } = NewSignal();
        internal TaskCompletionSource<bool> AfterRelease { get; } = NewSignal();
        internal TaskCompletionSource<bool> Delivered { get; } = NewSignal();
        internal TaskCompletionSource<bool> DeliveryRelease { get; } = NewSignal();
        internal TaskCompletionSource<bool> FlowCloseEntered { get; } = NewSignal();
        internal TaskCompletionSource<bool> FlowCloseRelease { get; } = NewSignal();
        internal TaskCompletionSource<bool> FlowFinishEntered { get; } = NewSignal();
        internal TaskCompletionSource<bool> FlowFinishRelease { get; } = NewSignal();

        public ValueTask<ulong> OpenStreamAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<ulong>(m_association.Open(m_initiator));
        }

        public ValueTask<ByteString> ExportKeyingMaterialAsync(
            ByteString transcriptDigest,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsAvailable || ExporterUnavailable)
            {
                throw new DataChannelException(DataChannelResult.Unsupported);
            }
            return new ValueTask<ByteString>(CreateExporterValue(transcriptDigest));
        }

        public ValueTask<bool> BindStreamAsync(
            ulong streamId,
            uint channelId,
            bool locallyOpened,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<bool>(!RejectBinding &&
                streamId != ManagementStreamId &&
                m_association.Verify(streamId, locallyOpened ? m_initiator : !m_initiator));
        }

        public async ValueTask<DataChannelResult> SendAsync(
            DataChannelWrite write,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref m_active);
            try
            {
                if (GateBefore?.Invoke(write) == true)
                {
                    BeforeEntered.TrySetResult(true);
                    await BeforeRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                if (SkipCommit)
                {
                    return DataChannelResult.Good;
                }
                if (!write.TryCommit())
                {
                    return write.Result;
                }
                if (write.EndsDirection)
                {
                    Interlocked.Increment(ref m_endDirectionWrites);
                }
                if (GateAfter?.Invoke(write) == true)
                {
                    AfterEntered.TrySetResult(true);
                    await AfterRelease.Task.ConfigureAwait(false);
                }
                var captured = new CapturedFlowWrite(
                    write.Path, write.ChannelId, write.StreamId, write.RequestId, write.Payload.Copy(), write.IsData);
                Writes.Enqueue(captured);
                DataChannelResult outcome = SendOutcome?.Invoke(write) ?? DataChannelResult.Good;
                if (outcome != DataChannelResult.Good)
                {
                    return outcome;
                }
                if (Drop?.Invoke(write) != true && Peer != null)
                {
                    await DeliverAsync(captured).ConfigureAwait(false);
                    DeliveredWrites.Enqueue(captured);
                }
                if (GateAfterDelivery?.Invoke(write) == true)
                {
                    Delivered.TrySetResult(true);
                    await DeliveryRelease.Task.ConfigureAwait(false);
                }
                return DataChannelResult.Good;
            }
            finally
            {
                Interlocked.Decrement(ref m_active);
            }
        }

        internal async Task<DataChannelResult> DeliverAsync(CapturedFlowWrite write)
        {
            if (!IsNative && write.Path == DataChannelPath.Datagram)
            {
                PeerCarrier!.m_directDatagrams.Writer.TryWrite(write.Payload.Copy());
                return DataChannelResult.Good;
            }
            if (IsNative && write.Path == DataChannelPath.Datagram)
            {
                return await Peer!.ReceiveDatagramAsync(write.Payload).ConfigureAwait(false);
            }
            Task previous;
            TaskCompletionSource<bool> completed = NewSignal();
            lock (m_deliveryOrder)
            {
                ulong stream = IsNative ? write.StreamId : 0;
                previous = m_deliveryTasks.TryGetValue(stream, out Task? prior) ? prior : Task.CompletedTask;
                m_deliveryTasks[stream] = completed.Task;
            }
            await previous.ConfigureAwait(false);
            try
            {
                if (!IsNative)
                {
                    if (UseQueuedUascIngress)
                    {
                        Peer!.QueueVerifiedUasc(
                            write.IsData ? DataChannelUascMessageKind.Flo : DataChannelUascMessageKind.Flc,
                            (byte)'F',
                            write.RequestId,
                            write.Payload);
                        return DataChannelResult.Good;
                    }
                    return await Peer!.ReceiveUascAsync(
                        write.IsData ? DataChannelUascMessageKind.Flo : DataChannelUascMessageKind.Flc,
                        (byte)'F',
                        write.RequestId,
                        write.Payload).ConfigureAwait(false);
                }
                DataChannelResult result = DataChannelResult.Good;
                for (int offset = 0; offset < write.Payload.Length; offset += SegmentSize)
                {
                    int count = Math.Min(SegmentSize, write.Payload.Length - offset);
                    result = await Peer!.ReceiveStreamAsync(
                        write.StreamId, new ByteString(write.Payload.Memory.Slice(offset, count)))
                        .ConfigureAwait(false);
                    if (result != DataChannelResult.Good)
                    {
                        return result;
                    }
                    if (count == write.Payload.Length - offset)
                    {
                        break;
                    }
                }
                return result;
            }
            finally
            {
                completed.TrySetResult(true);
            }
        }

        internal static FlowRecord Record(DataChannelWrite write, DataChannelCarrierKind kind)
        {
            if (kind is DataChannelCarrierKind.DedicatedUasc or DataChannelCarrierKind.SharedUasc or
                DataChannelCarrierKind.QuicUasc or DataChannelCarrierKind.WebTransportUasc)
            {
                UascFlowMessage decoded = UascFlowMessageCodec.Decode(
                    FlowModel.ToWire(kind),
                    write.IsData ? DataChannelUascMessageKind.Flo : DataChannelUascMessageKind.Flc,
                    (byte)'F',
                    write.RequestId,
                    write.Payload,
                    new FlowCodecLimits());
                return decoded.Record;
            }
            int prefix = kind is DataChannelCarrierKind.DedicatedUasc or DataChannelCarrierKind.SharedUasc ||
                write.Path == DataChannelPath.Datagram ? 4 : 0;
            return FlowRecordCodec.DecodeExact(write.Payload[prefix..], new FlowCodecLimits());
        }

        internal static FlowOperation? Operation(DataChannelWrite write, DataChannelCarrierKind kind)
        {
            FlowRecord record = Record(write, kind);
            return record.Kind == FlowRecordKind.Control
                ? FlowControlCodec.Decode(in record, new FlowCodecLimits()).Operation : null;
        }

        internal static FlowRecord Record(CapturedFlowWrite write, DataChannelCarrierKind kind)
        {
            if (kind is DataChannelCarrierKind.DedicatedUasc or DataChannelCarrierKind.SharedUasc or
                DataChannelCarrierKind.QuicUasc or DataChannelCarrierKind.WebTransportUasc)
            {
                UascFlowMessage decoded = UascFlowMessageCodec.Decode(
                    FlowModel.ToWire(kind),
                    write.IsData ? DataChannelUascMessageKind.Flo : DataChannelUascMessageKind.Flc,
                    (byte)'F',
                    write.RequestId,
                    write.Payload,
                    new FlowCodecLimits());
                return decoded.Record;
            }
            int prefix = kind is DataChannelCarrierKind.DedicatedUasc or DataChannelCarrierKind.SharedUasc ||
                write.Path == DataChannelPath.Datagram ? 4 : 0;
            return FlowRecordCodec.DecodeExact(write.Payload[prefix..], new FlowCodecLimits());
        }

        internal static FlowOperation? Operation(CapturedFlowWrite write, DataChannelCarrierKind kind)
        {
            FlowRecord record = Record(write, kind);
            return record.Kind == FlowRecordKind.Control
                ? FlowControlCodec.Decode(in record, new FlowCodecLimits()).Operation : null;
        }

        internal Task<DataChannelResult> InjectDataAsync(
            uint channelId,
            uint revision,
            uint messageId,
            ByteString payload,
            bool datagram = false,
            ulong streamId = 0)
        {
            var limits = new FlowCodecLimits();
            var record = new FlowRecord(FlowRecordKind.Data, revision, messageId, payload);
            uint requestId = 0;
            ByteString encoded = !IsNative
                ? UascFlowMessageCodec.Encode(
                    FlowModel.ToWire(Kind), channelId, in record, limits, out requestId)
                : datagram
                    ? FlowCarrierCodec.EncodeDatagram(FlowModel.ToWire(Kind), channelId, in record, 65536, limits)
                    : FlowRecordCodec.Encode(in record, limits);
            return DeliverAsync(new CapturedFlowWrite(
                datagram ? DataChannelPath.Datagram : DataChannelPath.Flow,
                channelId, streamId, requestId, encoded, true));
        }

        internal Task<DataChannelResult> InjectControlAsync(
            FlowControl control,
            uint revision,
            uint messageId,
            DataChannelPath path = DataChannelPath.Management,
            ulong streamId = 0)
        {
            var limits = new FlowCodecLimits();
            FlowRecord record = FlowControlCodec.Encode(control, revision, messageId, limits);
            ByteString payload;
            uint requestId = 0;
            if (IsNative)
            {
                payload = FlowRecordCodec.Encode(in record, limits);
            }
            else
            {
                payload = UascFlowMessageCodec.Encode(
                    FlowModel.ToWire(Kind), control.Flow, in record, limits, out requestId);
            }
            return DeliverAsync(new CapturedFlowWrite(path, control.Flow, streamId, requestId, payload, false));
        }

        public async ValueTask CloseFlowAsync(
            ulong streamId,
            DataChannelResult result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref m_closedFlows);
            if (ReleaseManagementOnFlowClose)
            {
                BeforeRelease.TrySetResult(true);
            }
            if (BlockFlowClose)
            {
                FlowCloseEntered.TrySetResult(true);
                await FlowCloseRelease.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            if (Peer != null)
            {
                await Peer.ReceiveStreamResetAsync(streamId, result, cancellationToken).ConfigureAwait(false);
            }
        }

        public ValueTask FinishFlowAsync(
            ulong streamId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref m_finishedFlows);
            if (!BlockFlowFinish)
            {
                return default;
            }
            FlowFinishEntered.TrySetResult(true);
            return new ValueTask(FlowFinishRelease.Task.WaitAsync(cancellationToken));
        }

        public async ValueTask CloseAsync(
            DataChannelResult result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref m_closeCalls);
            ReleaseGates();
            if (Interlocked.Exchange(ref m_closed, 1) == 0 && Peer != null)
            {
                await Peer.InvalidateAsync(result, cancellationToken).ConfigureAwait(false);
            }
        }

        internal void ReleaseGates()
        {
            BeforeRelease.TrySetResult(true);
            AfterRelease.TrySetResult(true);
            DeliveryRelease.TrySetResult(true);
            FlowCloseRelease.TrySetResult(true);
            FlowFinishRelease.TrySetResult(true);
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private ByteString CreateExporterValue(ByteString transcriptDigest)
        {
            byte[] key = Enumerable.Repeat((byte)0xA7, 32).ToArray();
            try
            {
                using var hmac = new HMACSHA256(key);
                LastExporterMaterial = hmac.ComputeHash(transcriptDigest.ToArray());
                return new ByteString(LastExporterMaterial);
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }
        }

        private readonly bool m_initiator;
        private readonly TestFlowAssociation m_association;
        private readonly Lock m_deliveryOrder = new();
        private readonly Dictionary<ulong, Task> m_deliveryTasks = [];
        private int m_active;
        private int m_closedFlows;
        private int m_finishedFlows;
        private int m_endDirectionWrites;
        private int m_closed;
        private int m_closeCalls;
    }

    internal sealed record CapturedFlowWrite(
        DataChannelPath Path,
        uint ChannelId,
        ulong StreamId,
        uint RequestId,
        ByteString Payload,
        bool IsData);
}
