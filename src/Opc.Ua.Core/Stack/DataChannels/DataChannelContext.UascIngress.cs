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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.DataChannels
{
    public sealed partial class DataChannelContext
    {
        /// <summary>
        /// Bridges the synchronous UASC receive callback without blocking Service dispatch on application reads.
        /// HELLO is processed synchronously so a following shared MSG sees the confirmed profile.
        /// </summary>
        internal void QueueVerifiedUasc(byte chunkType, uint requestId, ByteString borrowedPlaintext)
        {
            bool isData = borrowedPlaintext.Length >= 17 && borrowedPlaintext.Span[8] == (byte)FlowRecordKind.Data;
            int cost = checked(borrowedPlaintext.Length * (isData ? 1 : 3));
            lock (m_gate)
            {
                if (m_closed)
                {
                    return;
                }
                if (!(isData ? TryReserveUascDataLocked(cost) : TryReserveReceiveControlLocked(cost)))
                {
                    uint channelId = borrowedPlaintext.Length >= 4
                        ? BinaryPrimitives.ReadUInt32LittleEndian(borrowedPlaintext.Span) : 0;
                    if (isData && m_flows.TryGetValue(channelId, out FlowState? flow))
                    {
                        ResetLocked(flow, DataChannelResult.WouldBlock);
                    }
                    else
                    {
                        CloseLocked(DataChannelResult.WouldBlock);
                    }
                    return;
                }
            }
            bool queued = false;
            try
            {
                ByteString owned = borrowedPlaintext.Copy();
                FlowWireEnvelope envelope = FlowCarrierCodec.DecodeUascPayload(
                    m_wireCarrier, chunkType, requestId, owned, m_codecLimits);
                lock (m_gate)
                {
                    if (envelope.Control is FlowHello hello)
                    {
                        ReceiveHelloLocked(hello, envelope.Record.MessageId);
                        return;
                    }
                    if (m_closed)
                    {
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
                    uint channelId = borrowedPlaintext.Length >= 4
                        ? BinaryPrimitives.ReadUInt32LittleEndian(borrowedPlaintext.Span) : 1;
                    DataChannelResult result = FlowModel.FromWire(exception.Status);
                    if (IsFatalCodecFailureLocked(
                        exception, channelId, true, HasTrustedUascContainer(exception, borrowedPlaintext)))
                    {
                        CloseLocked(result);
                    }
                    else if (m_flows.TryGetValue(channelId, out FlowState? flow))
                    {
                        ResetLocked(flow, result);
                    }
                    else
                    {
                        RefuseLocked(channelId, 1, result);
                    }
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
                        await ReceiveControlAsync(control, item.Envelope.Record, DataChannelPath.Flow, 0,
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
                    DataChannelResult valid = ValidateDataLocked(
                        flow, record.Revision, record.MessageId, record.Body.Length, false, out FlowRevision? revision);
                    if (valid == DataChannelResult.Good)
                    {
                        if (IsShared && (flow.ReceiveCredit < item.Cost || m_receiveConnectionCredit < item.Cost))
                        {
                            return ResetLocked(flow, DataChannelResult.InvalidState);
                        }
                        if (TryReserveMessageLocked(flow, revision!, item.Cost, false, applicationReserved: true))
                        {
                            m_uascDataBytes -= item.Cost;
                            m_uascDataMessages--;
                            item.DataTransferred = true;
                            try
                            {
                                return DeliverDataLocked(flow, record, record.Body, item.Cost, false);
                            }
                            catch
                            {
                                ReleaseMessageLocked(flow, record.Revision, item.Cost, false);
                                throw;
                            }
                        }
                    }
                    else if (valid != DataChannelResult.WouldBlock)
                    {
                        return valid;
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
