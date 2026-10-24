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
        /// Queues one verified FLO body after UASC security and sequence checks.
        /// ChannelId selects the flow; configured UADP source/writer routes remain an authorization check.
        /// </summary>
        internal void QueueVerifiedUasc(
            DataChannelUascMessageKind kind,
            byte chunkType,
            uint requestId,
            ByteString borrowedPlaintext)
        {
            bool isData = kind == DataChannelUascMessageKind.Flo;
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
                UascFlowMessage message = UascFlowMessageCodec.Decode(
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
                            message.ApplicationBody, out envelope);
                        if (route != DataChannelResult.Good)
                        {
                            CloseLocked(route);
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
                lock (m_gate)
                {
                    CloseLocked(FlowModel.FromWire(exception.Status));
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
        /// Validates the selected flow against any configured UADP association.
        /// </summary>
        private DataChannelResult CreateUascDataEnvelopeLocked(
            ByteString applicationBody,
            out FlowWireEnvelope envelope)
        {
            envelope = default;
            UadpNetworkMessageHeader networkMessageHeader;
            try
            {
                networkMessageHeader = UadpApplicationPayloadHeaderCodec.DecodePublisherHeader(applicationBody);
            }
            catch (FlowCodecException)
            {
                return DataChannelResult.InvalidArgument;
            }

            string? resourceUri = null;
            bool typedStreaming = networkMessageHeader.NetworkMessageType == 3 &&
                FlowModel.SharesServices(m_carrier.Kind);
            if (!typedStreaming && m_uascPublisherRoutes.Count > 0)
            {
                DataChannelResult routeResult = ResolveUascPublisherRoute(networkMessageHeader, out resourceUri);
                if (routeResult != DataChannelResult.Good)
                {
                    return routeResult;
                }
            }

            FlowState? target = null;
            foreach (FlowState flow in m_flows.Values)
            {
                if (!flow.Ready || flow.IsTerminal || !flow.MayReceive || flow.IsDatagram || flow.DirectLane != null ||
                    !DataChannelProfiles.IsUadpPayloadProtocol(flow.Parameters.Protocol) ||
                    (typedStreaming && !flow.UascStreamingPublisherIds.Contains(networkMessageHeader.PublisherId)) ||
                    (resourceUri != null && !string.Equals(
                        flow.Parameters.ResourceUri, resourceUri, StringComparison.Ordinal)))
                {
                    continue;
                }
                bool messageTypeSupported = networkMessageHeader.NetworkMessageType switch
                {
                    0 or 1 or 2 => flow.Parameters.Protocol == DataChannelProfiles.UadpPayload,
                    3 => flow.Parameters.Protocol is DataChannelProfiles.UadpAvApplicationPayload or
                        DataChannelProfiles.UadpOpaqueApplicationPayload,
                    _ => false
                };
                if (!messageTypeSupported)
                {
                    continue;
                }
                if (target != null)
                {
                    return DataChannelResult.InvalidState;
                }
                target = flow;
            }
            if (target == null)
            {
                return DataChannelResult.UnknownChannel;
            }

            var record = new FlowRecord(FlowRecordKind.Data, 0, 0, applicationBody);
            envelope = new FlowWireEnvelope(target.Id, record, null);
            return DataChannelResult.Good;
        }

        /// <summary>
        /// Resolves one complete visible UADP source/writer association to exactly one resource.
        /// </summary>
        private DataChannelResult ResolveUascPublisherRoute(
            UadpNetworkMessageHeader header,
            out string? resourceUri)
        {
            resourceUri = null;
            List<UascPublisherRoute> candidates = [.. m_uascPublisherRoutes.Where(route =>
                route.PublisherId.Equals(header.PublisherId) || route.PublisherId.IsNull)];
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
            HashSet<string>? matchingResources = null;
            if (header.DataSetWriterIds.Length == 0)
            {
                matchingResources = candidates
                    .Select(static route => route.ResourceUri)
                    .ToHashSet(StringComparer.Ordinal);
            }
            else
            {
                foreach (ushort dataSetWriterId in header.DataSetWriterIds)
                {
                    List<UascPublisherRoute> exactWriterRoutes = [.. candidates.Where(route =>
                        route.DataSetWriterId == dataSetWriterId)];
                    List<UascPublisherRoute> matchingWriterRoutes = exactWriterRoutes.Count > 0
                        ? exactWriterRoutes
                        : [.. candidates.Where(static route => !route.DataSetWriterId.HasValue)];
                    HashSet<string> writerResources = matchingWriterRoutes
                        .Select(static route => route.ResourceUri)
                        .ToHashSet(StringComparer.Ordinal);
                    if (matchingResources is null)
                    {
                        matchingResources = writerResources;
                    }
                    else
                    {
                        matchingResources.IntersectWith(writerResources);
                    }
                }
            }

            if (matchingResources is null || matchingResources.Count == 0)
            {
                return DataChannelResult.UnknownChannel;
            }
            if (matchingResources.Count != 1)
            {
                return DataChannelResult.InvalidState;
            }
            resourceUri = matchingResources.Single();
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
                        await ReceiveControlAsync(control, item.Envelope.Record, DataChannelPath.Management, 0,
                            m_shutdown.Token).ConfigureAwait(false);
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
                            return ResetLocked(flow, DataChannelResult.InvalidState);
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
                    }
                    else if (valid != DataChannelResult.WouldBlock)
                    {
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
            if (cost > m_options.MaxContextBytes - m_receiveBytes - m_uascDataBytes ||
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

        private readonly Queue<UascIngress> m_uascIngress = [];
        private Task m_ingressTask = Task.CompletedTask;
        private bool m_ingressRunning;
        private long m_uascDataBytes;
        private int m_uascDataMessages;
    }
}
