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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.DataChannels
{
    public sealed partial class DataChannelContext
    {
        /// <summary>
        /// Gets the complete canonical control body bound, including its non-control prefix.
        /// </summary>
        internal int MaxUascControlBodyBytes => m_codecLimits.MaxControlBytes + UascFlowMessageCodec.ControlPrefixSize;

        /// <summary>
        /// Gets the complete canonical DATA body bound, including its TypeId.
        /// </summary>
        internal int MaxUascDataBodyBytes => m_codecLimits.MaxDataBytes + UascFlowMessageCodec.TypeIdSize;

        /// <summary>
        /// Reserves pooled partial chunks and their eventual joined copy in the shared application budget.
        /// </summary>
        internal IDisposable? TryReserveUascReassembly(int cost, bool isData)
        {
            lock (m_gate)
            {
                if (m_closed)
                {
                    return null;
                }
                if (isData)
                {
                    if (cost > m_options.MaxContextBytes - m_receiveBytes - m_uascDataBytes - m_uascReassemblyBytes ||
                        !m_budget.TryReserve(FlowResource.BufferedBytes, cost))
                    {
                        return null;
                    }
                    m_uascReassemblyBytes += cost;
                }
                else if (!TryReserveReceiveControlLocked(cost))
                {
                    return null;
                }
                return new UascReassemblyReservation(this, cost, isData);
            }
        }

        /// <summary>
        /// Queues a verified MSG after UASC security and sequence checks.
        /// DATA RequestId selects the admission; configured UADP source/writer routes validate its association.
        /// </summary>
        internal void QueueVerifiedUasc(
            DataChannelUascMessageKind kind,
            byte chunkType,
            uint requestId,
            ByteString borrowedPlaintext)
        {
            RecordVerifiedMessage();
            bool isData = kind == DataChannelUascMessageKind.Msg &&
                borrowedPlaintext.Length >= UascFlowMessageCodec.TypeIdSize &&
                borrowedPlaintext.Span[0] == 0 && borrowedPlaintext.Span[1] == 254;
            int cost = checked(borrowedPlaintext.Length * (isData ? 1 : 3));
            lock (m_gate)
            {
                if (m_closed)
                {
                    return;
                }
                if (!(isData ? TryReserveUascDataLocked(cost) : TryReserveReceiveControlLocked(cost)))
                {
                    CloseLocked(DataChannelResult.WouldBlock);
                    return;
                }
            }
            bool queued = false;
            try
            {
                ByteString owned = borrowedPlaintext.Copy();
                UascFlowMessage message = m_carrier.Kind == DataChannelCarrierKind.WebTransport
                    ? UascFlowMessageCodec.DecodeManagement(kind, chunkType, requestId, owned, m_codecLimits)
                    : UascFlowMessageCodec.Decode(
                        m_wireCarrier, kind, chunkType, requestId, owned, m_codecLimits);
                FlowWireEnvelope envelope;
                lock (m_gate)
                {
                    if (m_closed)
                    {
                        return;
                    }
                    if (isData)
                    {
                        DataChannelResult route = CreateUascDataEnvelopeLocked(
                            message.ChannelId, message.ApplicationBody, out envelope, out bool silent);
                        if (route != DataChannelResult.Good)
                        {
                            RefuseUnroutableUascDataLocked(message.ApplicationBody.Length, route, silent);
                            return;
                        }
                    }
                    else
                    {
                        envelope = new FlowWireEnvelope(
                            message.ChannelId,
                            message.Record,
                            message.Control);
                    }

                    if (envelope.Control is FlowHello hello)
                    {
                        ReceiveHelloLocked(hello, envelope.Record.MessageId);
                        return;
                    }
                    m_uascIngress.Enqueue(new UascIngress(envelope, cost, isData, m_clock.GetTimestamp()));
                    queued = true;
                    if (!m_ingressRunning)
                    {
                        m_ingressRunning = true;
                        m_ingressTask = Task.Run(RunUascIngressAsync);
                    }
                }
            }
            catch (FlowCodecException exception)
            {
                if (m_carrier.Kind == DataChannelCarrierKind.WebTransport)
                {
                    lock (m_gate)
                    {
                        CloseLocked(FlowModel.FromWire(exception.Status));
                    }
                }
                else if (!TryRefuseMalformedUascControl(borrowedPlaintext))
                {
                    throw;
                }
            }
            finally
            {
                if (!queued)
                {
                    lock (m_gate)
                    {
                        if (isData)
                        {
                            m_uascDataBytes -= cost;
                            m_uascDataMessages--;
                            m_budget.Release(FlowResource.BufferedBytes, cost);
                            m_budget.Release(FlowResource.Message, 1);
                        }
                        else
                        {
                            ReleaseReceiveControlLocked(cost);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Recovers only a defined control's complete correlation prefix after HELLO.
        /// </summary>
        private bool TryRefuseMalformedUascControl(ByteString plaintext)
        {
            lock (m_gate)
            {
                if (m_identity == null ||
                    plaintext.Length < UascFlowMessageCodec.ControlPrefixSize + 1 ||
                    plaintext.Length > MaxUascControlBodyBytes ||
                    plaintext.Span[0] != 0 ||
                    plaintext.Span[1] != 255)
                {
                    return false;
                }
                ReadOnlySpan<byte> body = plaintext.Span;
                uint revision = BinaryPrimitives.ReadUInt32LittleEndian(body[2..]);
                uint operationId = BinaryPrimitives.ReadUInt32LittleEndian(body[6..]);
                byte opcode = body[10];
                if (operationId == 0 || opcode > (byte)FlowOperation.AssociateAck)
                {
                    return false;
                }
                var operation = (FlowOperation)opcode;
                uint flow = 0;
                if (operation is not (FlowOperation.Hello or FlowOperation.Bind))
                {
                    if (body.Length < UascFlowMessageCodec.ControlPrefixSize + 5)
                    {
                        return false;
                    }
                    flow = BinaryPrimitives.ReadUInt32LittleEndian(body[11..]);
                }
                bool contextScope = operation is FlowOperation.Hello or FlowOperation.Bind or
                    FlowOperation.Credit or FlowOperation.Ping or FlowOperation.Pong or FlowOperation.Status;
                if ((flow == 0 && (!contextScope || revision != 0)) ||
                    (flow != 0 && revision == 0) ||
                    (operation is FlowOperation.Open or FlowOperation.Accept or FlowOperation.Ready && revision != 1) ||
                    (operation is FlowOperation.Prepare or FlowOperation.AcceptUpdate or FlowOperation.Commit or
                        FlowOperation.Ack or FlowOperation.AbortUpdate && revision < 2) ||
                    operation == FlowOperation.Hello)
                {
                    return false;
                }
                if (!IsRetiredChannelIdLocked(flow) &&
                    operation is not (FlowOperation.Status or FlowOperation.Reset))
                {
                    m_flows.TryGetValue(flow, out FlowState? target);
                    QueueAdvisoryLocked(flow, revision, operationId, DataChannelResult.InvalidArgument, target);
                }
                return true;
            }
        }

        /// <summary>
        /// Selects the flow named by the DATA RequestId and validates it against any configured UADP association.
        /// A retired or terminal flow sets <paramref name="silent"/> so the DATA is discarded without a STATUS.
        /// </summary>
        private DataChannelResult CreateUascDataEnvelopeLocked(
            uint channelId,
            ByteString applicationBody,
            out FlowWireEnvelope envelope,
            out bool silent)
        {
            envelope = default;
            silent = false;
            if (!m_flows.TryGetValue(channelId, out FlowState? target))
            {
                silent = IsRetiredChannelIdLocked(channelId);
                return DataChannelResult.UnknownChannel;
            }
            if (target.IsTerminal)
            {
                silent = true;
                return DataChannelResult.UnknownChannel;
            }
            UadpNetworkMessageHeader networkMessageHeader;
            bool malformedHeader = false;
            try
            {
                networkMessageHeader = UadpApplicationPayloadHeaderCodec.DecodePublisherHeader(applicationBody);
            }
            catch (FlowCodecException)
            {
                networkMessageHeader = default;
                malformedHeader = true;
            }

            bool typedStreaming = !malformedHeader && networkMessageHeader.NetworkMessageType == 3 &&
                FlowModel.SharesServices(m_carrier.Kind);
            if (!malformedHeader && !typedStreaming && m_uascPublisherRoutes.Count > 0)
            {
                DataChannelResult routeResult = ValidateUascPublisherRoutes(
                    networkMessageHeader, target.Parameters.ResourceUri);
                if (routeResult != DataChannelResult.Good)
                {
                    return routeResult;
                }
            }

            bool messageTypeSupported = malformedHeader
                ? target.Parameters.Protocol == DataChannelProfiles.UadpPayload
                : networkMessageHeader.NetworkMessageType switch
                {
                    0 or 1 or 2 => target.Parameters.Protocol == DataChannelProfiles.UadpPayload,
                    3 => target.Parameters.Protocol is DataChannelProfiles.UadpAvApplicationPayload or
                        DataChannelProfiles.UadpOpaqueApplicationPayload,
                    _ => false
                };
            if (target.IsDatagram || target.DirectLane != null ||
                !DataChannelProfiles.IsUadpPayloadProtocol(target.Parameters.Protocol) ||
                !messageTypeSupported ||
                (!malformedHeader && typedStreaming &&
                    !target.UascStreamingPublisherIds.Contains(networkMessageHeader.PublisherId)))
            {
                return DataChannelResult.UnknownChannel;
            }

            var record = new FlowRecord(FlowRecordKind.Data, 0, 0, applicationBody);
            envelope = new FlowWireEnvelope(target.Id, record, null);
            return DataChannelResult.Good;
        }

        /// <summary>
        /// Validates visible UADP source/writer associations only within the RequestId-selected resource.
        /// </summary>
        private DataChannelResult ValidateUascPublisherRoutes(
            UadpNetworkMessageHeader header,
            string? resourceUri)
        {
            List<UascPublisherRoute>? candidates = null;
            foreach (UascPublisherRoute route in m_uascPublisherRoutes)
            {
                if (string.Equals(route.ResourceUri, resourceUri, StringComparison.Ordinal))
                {
                    candidates ??= [];
                    candidates.Add(route);
                }
            }
            if (candidates is null)
            {
                return DataChannelResult.Good;
            }
            candidates.RemoveAll(route => !route.PublisherId.Equals(header.PublisherId) && !route.PublisherId.IsNull);
            if (!header.PublisherId.IsNull &&
                candidates.Exists(route => route.PublisherId.Equals(header.PublisherId)))
            {
                candidates.RemoveAll(route => route.PublisherId.IsNull);
            }
            List<UascPublisherRoute> exactTypeRoutes = [.. candidates.Where(route =>
                route.NetworkMessageType == header.NetworkMessageType)];
            if (exactTypeRoutes.Count > 0)
            {
                candidates = exactTypeRoutes;
            }
            else
            {
                candidates.RemoveAll(static route => route.NetworkMessageType.HasValue);
            }
            if (header.NetworkMessageType is 1 or 2)
            {
                if (header.DiscoveryType is byte discoveryType)
                {
                    List<UascPublisherRoute> exactDiscoveryRoutes = [.. candidates.Where(route =>
                        route.DiscoveryType == discoveryType)];
                    if (exactDiscoveryRoutes.Count > 0)
                    {
                        candidates = exactDiscoveryRoutes;
                    }
                    else
                    {
                        candidates.RemoveAll(static route => route.DiscoveryType.HasValue);
                    }
                }
                else if (candidates.Exists(static route => route.DiscoveryType.HasValue))
                {
                    return DataChannelResult.UnknownChannel;
                }
            }
            if (header.WriterGroupId is ushort writerGroupId)
            {
                List<UascPublisherRoute> exactGroupRoutes = [.. candidates.Where(route =>
                    route.WriterGroupId == writerGroupId)];
                if (exactGroupRoutes.Count > 0)
                {
                    candidates = exactGroupRoutes;
                }
                else
                {
                    candidates.RemoveAll(route => route.WriterGroupId.HasValue);
                }
            }
            if (candidates.Count == 0)
            {
                return DataChannelResult.UnknownChannel;
            }
            foreach (ushort dataSetWriterId in header.DataSetWriterIds)
            {
                bool matched = false;
                foreach (UascPublisherRoute route in candidates)
                {
                    if (!route.DataSetWriterId.HasValue || route.DataSetWriterId == dataSetWriterId)
                    {
                        matched = true;
                        break;
                    }
                }
                if (!matched)
                {
                    return DataChannelResult.UnknownChannel;
                }
            }
            return DataChannelResult.Good;
        }

        /// <summary>
        /// Drains bounded owned plaintext while keeping original receive-loop buffers independent.
        /// </summary>
        private async Task RunUascIngressAsync()
        {
            while (true)
            {
                UascIngress item;
                lock (m_gate)
                {
                    if (m_uascIngress.Count == 0)
                    {
                        m_ingressRunning = false;
                        return;
                    }
                    item = m_uascIngress.Dequeue();
                }
                try
                {
                    if (item.Envelope.Control is { } control)
                    {
                        await ReceiveControlAsync(
                            control, item.Envelope.Record, DataChannelPath.Management,
                            m_carrier.ManagementStreamId, m_shutdown.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        await ReceiveOwnedUascDataAsync(item).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Context shutdown cancels only uncommitted ingress work.
                }
                catch (Exception exception)
                {
                    m_logger.FlowCarrierFailure(exception);
                    await CloseCoreAsync(DataChannelResult.InvalidState, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    lock (m_gate)
                    {
                        ReleaseUascIngressLocked(item);
                    }
                }
            }
        }

        /// <summary>
        /// Transfers the admitted ingress allocation to a DATA queue without a second copy or a second reservation.
        /// Capacity waits use the drain deadline so a stalled application cannot indefinitely block following controls.
        /// </summary>
        private async ValueTask<DataChannelResult> ReceiveOwnedUascDataAsync(UascIngress item)
        {
            FlowRecord record = item.Envelope.Record;
            while (true)
            {
                Task wait;
                lock (m_gate)
                {
                    if (m_closed)
                    {
                        return DataChannelResult.Closed;
                    }
                    if (!m_flows.TryGetValue(item.Envelope.ChannelId, out FlowState? flow))
                    {
                        return RefuseLocked(item.Envelope.ChannelId, record.Revision, DataChannelResult.UnknownChannel);
                    }
                    if (record.Revision == 0)
                    {
                        if (flow.LastRoutedDataId == uint.MaxValue)
                        {
                            return ResetLocked(flow, DataChannelResult.InvalidState);
                        }
                        record = new FlowRecord(
                            FlowRecordKind.Data,
                            flow.ReceiveRevision,
                            ++flow.LastRoutedDataId,
                            record.Body);
                    }
                    DataChannelResult valid = ValidateDataLocked(
                        flow, record.Revision, record.MessageId, record.Body.Length, false, out FlowRevision? revision);
                    if (valid == DataChannelResult.Good)
                    {
                        int dataCost = checked(record.Body.Length + DataOverhead(flow));
                        if (UsesUascCredit && (flow.ReceiveCredit < dataCost || m_receiveConnectionCredit < dataCost))
                        {
                            ReleaseDiscardedConnectionCreditLocked(dataCost);
                            return ResetLocked(flow, DataChannelResult.WouldBlock);
                        }
                        if (TryReserveMessageLocked(
                            flow,
                            revision!,
                            dataCost,
                            false,
                            applicationReserved: true,
                            storageCost: item.Cost))
                        {
                            m_uascDataBytes -= item.Cost;
                            m_uascDataMessages--;
                            item.DataTransferred = true;
                            try
                            {
                                return DeliverDataLocked(flow, record, record.Body, dataCost, false, item.Cost);
                            }
                            catch
                            {
                                ReleaseMessageLocked(flow, record.Revision, dataCost, false, item.Cost);
                                throw;
                            }
                        }
                        if (UsesFlowCredit(flow))
                        {
                            ReleaseDiscardedConnectionCreditLocked(dataCost);
                            return ResetLocked(flow, DataChannelResult.WouldBlock);
                        }
                    }
                    else if (valid != DataChannelResult.WouldBlock)
                    {
                        ReleaseDiscardedConnectionCreditLocked(record.Body.Length);
                        return ResetLocked(flow, valid);
                    }
                    wait = m_capacityChanged.Task;
                }
                if (await WaitForUascReceiveCapacityAsync(wait, item.QueuedAt, m_shutdown.Token).ConfigureAwait(false))
                {
                    continue;
                }
                lock (m_gate)
                {
                    return m_flows.TryGetValue(item.Envelope.ChannelId, out FlowState? flow)
                        ? ResetLocked(flow, DataChannelResult.Timeout) : DataChannelResult.Closed;
                }
            }
        }

        /// <summary>
        /// Returns the aggregate grant consumed by a discarded DATA message without granting flow credit.
        /// </summary>
        private void ReleaseDiscardedConnectionCreditLocked(int cost)
        {
            if (UsesUascCredit)
            {
                m_receiveConnectionCredit -= Math.Min(m_receiveConnectionCredit, (uint)cost);
                m_replenishConnectionCredit = true;
                WakeLocked();
            }
        }

        /// <summary>
        /// Refuses unmatched DATA without closing a confirmed context or exhausting aggregate credit.
        /// </summary>
        private DataChannelResult RefuseUnroutableUascDataLocked(int cost, DataChannelResult result, bool silent)
        {
            if (m_identity == null)
            {
                CloseLocked(result);
                return result;
            }
            ReleaseDiscardedConnectionCreditLocked(cost);
            if (silent)
            {
                return result;
            }
            long now = m_clock.GetTimestamp();
            if (!m_errorSent || m_clock.GetElapsedTime(m_lastErrorAt, now) >= TimeSpan.FromSeconds(1))
            {
                m_errorSent = true;
                m_lastErrorAt = now;
                QueueAdvisoryLocked(0, 0, 0, DataChannelResult.UnknownChannel);
            }
            return result;
        }

        /// <summary>
        /// Preserves one monotonic receive deadline across unrelated capacity notifications.
        /// </summary>
        private async ValueTask<bool> WaitForUascReceiveCapacityAsync(
            Task capacity, long started, CancellationToken cancellationToken)
        {
            TimeSpan remaining = m_options.DrainTimeout - m_clock.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }
            try
            {
                await capacity.WaitAsync(remaining, m_clock, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        /// <summary>
        /// Bounds verified but not yet delivered DATA together with complete queues at context and application scope.
        /// </summary>
        private bool TryReserveUascDataLocked(int cost)
        {
            if (cost > m_options.MaxContextBytes - m_receiveBytes - m_uascDataBytes - m_uascReassemblyBytes ||
                m_receiveMessages + m_uascDataMessages >= m_options.MaxContextMessages ||
                !m_budget.TryReserveMessage(cost))
            {
                return false;
            }
            m_uascDataBytes += cost;
            m_uascDataMessages++;
            return true;
        }

        /// <summary>
        /// Releases ingress ownership unless the same DATA allocation and reservation moved to its application queue.
        /// </summary>
        private void ReleaseUascIngressLocked(UascIngress item)
        {
            if (!item.IsData)
            {
                ReleaseReceiveControlLocked(item.Cost);
            }
            else if (!item.DataTransferred)
            {
                m_uascDataBytes -= item.Cost;
                m_uascDataMessages--;
                m_budget.Release(FlowResource.BufferedBytes, item.Cost);
                m_budget.Release(FlowResource.Message, 1);
                PulseCapacityLocked();
            }
        }

        /// <summary>
        /// One admitted UASC plaintext container, independent of the transport pool lease.
        /// </summary>
        private sealed record UascIngress(FlowWireEnvelope Envelope, int Cost, bool IsData, long QueuedAt)
        {
            /// <summary>
            /// Gets or sets whether the DATA queue owns the original ingress reservation.
            /// </summary>
            internal bool DataTransferred { get; set; }
        }

        /// <summary>
        /// Holds assembly capacity until both pooled input and its temporary joined copy have been released.
        /// </summary>
        private sealed class UascReassemblyReservation(DataChannelContext context, int cost, bool isData) : IDisposable
        {
            /// <inheritdoc/>
            public void Dispose()
            {
                DataChannelContext? owner = Interlocked.Exchange(ref m_context, null);
                if (owner == null)
                {
                    return;
                }
                lock (owner.m_gate)
                {
                    if (isData)
                    {
                        owner.m_uascReassemblyBytes -= cost;
                        owner.m_budget.Release(FlowResource.BufferedBytes, cost);
                        owner.PulseCapacityLocked();
                    }
                    else
                    {
                        owner.ReleaseReceiveControlLocked(cost);
                    }
                }
            }

            // This is a borrowed owner; disposing a chunk reservation must not dispose its context.
            // TODO: Remove this suppression when CA2213 recognizes non-owning references.
#pragma warning disable CA2213
            /// <summary>Borrowed context whose assembly capacity this reservation releases exactly once.</summary>
            private DataChannelContext? m_context = context;
#pragma warning restore CA2213
        }

        /// <summary>DATA assembly capacity held in addition to admitted and queued receive messages.</summary>
        private long m_uascReassemblyBytes;
        private readonly Queue<UascIngress> m_uascIngress = [];
        private Task m_ingressTask = Task.CompletedTask;
        private bool m_ingressRunning;
        private long m_uascDataBytes;
        private int m_uascDataMessages;
    }
}
