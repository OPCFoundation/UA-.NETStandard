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
        /// Admits and owns a complete DATA record before the caller can change its source storage.
        /// </summary>
        internal ValueTask<DataChannelResult> SendAsync(
            DataChannel channel,
            ByteString payload,
            TimeSpan? timeToLive,
            CancellationToken cancellationToken)
        {
            return SendCoreAsync(channel, payload, timeToLive, false, cancellationToken);
        }

        /// <summary>
        /// Waits for temporary pre-admission pressure without resubmitting an admitted transport chunk.
        /// </summary>
        internal ValueTask<DataChannelResult> SendWithAdmissionAsync(
            DataChannel channel,
            ByteString payload,
            CancellationToken cancellationToken)
        {
            return SendCoreAsync(channel, payload, null, true, cancellationToken);
        }

        /// <summary>
        /// Shares validation and ownership across immediate admission and transport backpressure.
        /// </summary>
        private async ValueTask<DataChannelResult> SendCoreAsync(
            DataChannel channel,
            ByteString payload,
            TimeSpan? timeToLive,
            bool waitForAdmission,
            CancellationToken cancellationToken)
        {
            FlowWriteItem? item = null;
            while (item == null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Task changed;
                Task applicationChanged;
                lock (m_gate)
                {
                    changed = waitForAdmission ? m_capacityChanged.Task : Task.CompletedTask;
                    applicationChanged = waitForAdmission ? m_budget.CapacityChanged : Task.CompletedTask;
                    DataChannelResult result = TryQueueDataLocked(
                        channel, payload, timeToLive, cancellationToken, out item, out bool mayWait);
                    if (result != DataChannelResult.Good && (!waitForAdmission || !mayWait))
                    {
                        return result;
                    }
                }
                if (item == null)
                {
                    await Task.WhenAny(changed, applicationChanged).WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            using CancellationTokenRegistration registration = cancellationToken.Register(Wake);
            return await item.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Assigns an identifier only after every admission check and exact resource reservation succeeds.
        /// </summary>
        private DataChannelResult TryQueueDataLocked(
            DataChannel channel,
            ByteString payload,
            TimeSpan? timeToLive,
            CancellationToken cancellationToken,
            out FlowWriteItem? item,
            out bool mayWait)
        {
            item = null;
            mayWait = false;
            FlowState flow = channel.StateData;
            if (flow.IsTerminal || m_closed)
            {
                return DataChannelResult.Closed;
            }
            if (!flow.Ready || !flow.MaySend || flow.LocalEnded || flow.Draining)
            {
                return DataChannelResult.InvalidState;
            }
            if (timeToLive.HasValue && (!flow.Parameters.AllowQueueExpiry || timeToLive.Value < TimeSpan.Zero))
            {
                return DataChannelResult.InvalidArgument;
            }
            if (AuthorizationPendingLocked(flow))
            {
                mayWait = true;
                return DataChannelResult.WouldBlock;
            }
            if (flow.Update is { CommitQueued: true, LocalBarrier: false })
            {
                return DataChannelResult.WouldBlock;
            }
            FlowRevision revision = flow.Revisions[flow.SendRevision];
            if (payload.Length > revision.Parameters.MaxMessageSize ||
                payload.Length > m_carrier.MaxRecordSize - 13 ||
                (flow.IsDatagram && payload.Length > m_carrier.MaxDatagramSize - 17))
            {
                return DataChannelResult.TooLarge;
            }
            if (flow.LastDataId == uint.MaxValue)
            {
                return ResetLocked(flow, DataChannelResult.InvalidState);
            }
            int cost = checked(payload.Length + DataOverhead(flow));
            if (!TryReserveMessageLocked(flow, revision, cost, true))
            {
                mayWait = true;
                return DataChannelResult.WouldBlock;
            }
            uint id = ++flow.LastDataId;
            var record = new FlowRecord(FlowRecordKind.Data, flow.SendRevision, id, payload);
            try
            {
                item = new FlowWriteItem
                {
                    Flow = flow,
                    ChannelId = flow.Id,
                    StreamId = flow.StreamId,
                    Path = flow.IsDatagram ? DataChannelPath.Datagram : DataChannelPath.Flow,
                    Record = record,
                    PayloadLength = payload.Length,
                    Cost = cost,
                    QueuedAt = m_clock.GetTimestamp(),
                    TimeToLive = timeToLive,
                    Cancellation = cancellationToken,
                    PreviousControl = flow.LastControlWrite
                };
                EncodeWriteLocked(item);
                // Record.Body must not retain the caller's mutable backing storage.
                item.Record = FlowRecordCodec.DecodeExact(
                    item.Encoded[(!IsNative || flow.IsDatagram ? 4 : 0)..], m_codecLimits);
                flow.Outgoing.AddLast(item);
                WakeLocked();
                ArmTimerLocked();
                return DataChannelResult.Good;
            }
            catch
            {
                ReleaseMessageLocked(flow, revision.Number, cost, true);
                throw;
            }
        }

        /// <summary>
        /// Queues bounded control independently of DATA credit and native backpressure.
        /// </summary>
        private Task<DataChannelResult> QueueControlLocked(
            FlowControl control,
            uint revision,
            uint operationId,
            DataChannelPath path,
            Func<DataChannelResult>? commit = null,
            FlowState? flow = null,
            Action? discard = null,
            bool urgent = false)
        {
            if (m_closed)
            {
                return Task.FromResult(DataChannelResult.Closed);
            }
            uint id = operationId;
            if (id == 0)
            {
                uint current = flow?.LastControlId ?? m_localControlId;
                if (current == uint.MaxValue)
                {
                    CloseLocked(DataChannelResult.InvalidState);
                    return Task.FromResult(DataChannelResult.InvalidState);
                }
                id = current + 1;
                if (flow != null)
                {
                    flow.LastControlId = id;
                }
                else
                {
                    m_localControlId = id;
                }
            }
            int bodyLength = FlowControlCodec.GetEncodedBodyLength(control, revision, id, m_codecLimits);
            int cost = checked((bodyLength + (IsNative ? 13 : 17)) * (control is FlowCommit ? 4 : 2));
            if (!TryReserveReceiveControlLocked(cost))
            {
                discard?.Invoke();
                return Task.FromResult(DataChannelResult.WouldBlock);
            }
            FlowWriteItem item;
            try
            {
                item = new FlowWriteItem
                {
                    Flow = flow,
                    ChannelId = control.Flow,
                    StreamId = path == DataChannelPath.Management ? m_carrier.ManagementStreamId : flow?.StreamId ?? 0,
                    Control = control,
                    Record = FlowControlCodec.Encode(control, revision, id, m_codecLimits),
                    Path = path,
                    CommitAction = commit,
                    DiscardAction = discard,
                    QueuedAt = m_clock.GetTimestamp(),
                    Cost = cost,
                    ControlReserved = true
                };
                EncodeWriteLocked(item);
            }
            catch
            {
                ReleaseReceiveControlLocked(cost);
                discard?.Invoke();
                throw;
            }
            if (flow != null && !urgent)
            {
                item.PreviousControl = flow.LastControlWrite;
                flow.LastControlWrite = item.Completion.Task;
            }
            if (path == DataChannelPath.Flow && flow != null && !urgent)
            {
                flow.Outgoing.AddLast(item);
            }
            else
            {
                m_management.Enqueue(item);
            }
            WakeLocked();
            return item.Completion.Task;
        }

        /// <summary>
        /// Uses only the common codec for complete carrier payloads.
        /// </summary>
        private void EncodeWriteLocked(FlowWriteItem item)
        {
            FlowRecord record = item.Record;
            if (!IsNative)
            {
                item.Encoded = FlowCarrierCodec.EncodeUascPayload(
                    m_wireCarrier, item.ChannelId, in record, m_codecLimits, out uint requestId);
                item.RequestId = requestId;
            }
            else if (item.Path == DataChannelPath.Datagram)
            {
                item.Encoded = FlowCarrierCodec.EncodeDatagram(
                    m_wireCarrier, item.ChannelId, in record, m_carrier.MaxDatagramSize, m_codecLimits);
            }
            else
            {
                item.Encoded = FlowRecordCodec.Encode(in record, m_codecLimits);
            }
        }

        /// <summary>
        /// Runs a bounded fair scheduler, independently dispatching at most one write per flow.
        /// An ordered stream blocked in its carrier cannot block management or another stream.
        /// </summary>
        private async Task RunSchedulerAsync()
        {
            while (true)
            {
                Task wait;
                List<FlowWriteItem> writes = [];
                List<DataChannelStateChangedEventArgs> notifications = [];
                List<CancellationTokenRegistration> revocations = [];
                List<FlowState> rechecks = [];
                bool notifyClosed;
                bool closeCarrier;
                bool stop;
                lock (m_gate)
                {
                    if (m_notificationOverflow && !m_closed)
                    {
                        CloseLocked(DataChannelResult.WouldBlock);
                    }
                    m_work = new TaskSignal();
                    foreach (FlowState flow in m_flows.Values.ToArray())
                    {
                        if (flow.RevocationToken.IsCancellationRequested)
                        {
                            ResetLocked(flow, DataChannelResult.NotAuthorized);
                        }
                        else if (flow.RecheckRequested && !flow.AuthorizationChecking)
                        {
                            flow.RecheckRequested = false;
                            flow.AuthorizationChecking = true;
                            rechecks.Add(flow);
                        }
                    }
                    while (m_retiredRevocations.Count != 0)
                    {
                        revocations.Add(m_retiredRevocations.Dequeue());
                    }
                    while (m_stateNotifications.Count != 0)
                    {
                        notifications.Add(m_stateNotifications.Dequeue());
                    }
                    notifyClosed = m_closedNotification;
                    m_closedNotification = false;
                    closeCarrier = m_closed && !m_carrierCloseStarted;
                    if (!m_closed)
                    {
                        if (m_replenishConnectionCredit)
                        {
                            m_replenishConnectionCredit = false;
                            ReplenishConnectionCreditLocked();
                        }
                        if (!m_managementBusy && m_management.Count != 0)
                        {
                            FlowWriteItem next = m_management.Peek();
                            if (next.PreviousControl.IsCompleted &&
                                (next.Flow == null ||
                                    !RequiresAuthorization(next) ||
                                    !AuthorizationPendingLocked(next.Flow)))
                            {
                                m_management.Dequeue();
                                m_managementBusy = true;
                                m_activeWrites++;
                                writes.Add(next);
                            }
                        }
                        FlowState[] flows = [.. m_flows.Values.OrderByDescending(flow => flow.Parameters.Priority)];
                        for (int i = 0; i < flows.Length; i++)
                        {
                            FlowState flow = flows[(i + m_roundRobin) % flows.Length];
                            if (flow.SendBusy || flow.IsTerminal)
                            {
                                continue;
                            }
                            RemoveExpiredLocked(flow);
                            FlowWriteItem? item = flow.Barrier ?? flow.Outgoing.First?.Value;
                            if (item == null || !CanDispatchLocked(flow, item))
                            {
                                continue;
                            }
                            if (ReferenceEquals(item, flow.Barrier))
                            {
                                flow.Barrier = null;
                            }
                            else
                            {
                                flow.Outgoing.RemoveFirst();
                            }
                            PrepareBarrierWriteLocked(item);
                            flow.ActiveWrite = item;
                            flow.SendBusy = true;
                            m_activeWrites++;
                            writes.Add(item);
                        }
                        m_roundRobin = flows.Length == 0 ? 0 : (m_roundRobin + 1) % flows.Length;
                        ArmTimerLocked();
                    }
                    wait = m_replenishConnectionCredit
                        ? Task.WhenAny(m_work.Task, m_budget.CapacityChanged) : m_work.Task;
                    stop = m_closed;
                    if (m_closed &&
                        writes.Count == 0 &&
                        notifications.Count == 0 &&
                        revocations.Count == 0 &&
                        !notifyClosed &&
                        !closeCarrier)
                    {
                        return;
                    }
                }
                foreach (CancellationTokenRegistration registration in revocations)
                {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
                    await registration.DisposeAsync().ConfigureAwait(false);
#else
                    registration.Dispose();
#endif
                }
                foreach (DataChannelStateChangedEventArgs notification in notifications)
                {
                    try
                    {
                        notification.Channel.NotifyStateChanged(notification);
                        StateChanged?.Invoke(notification);
                    }
                    catch (Exception exception)
                    {
                        m_logger.FlowObserverFailure(exception);
                    }
                }
                if (notifyClosed)
                {
                    try
                    {
                        Closed?.Invoke(m_closeResult);
                    }
                    catch (Exception exception)
                    {
                        m_logger.FlowObserverFailure(exception);
                    }
                }
                foreach (FlowWriteItem write in writes)
                {
                    _ = ExecuteWriteAsync(write);
                }
                foreach (FlowState flow in rechecks)
                {
                    _ = RecheckFlowAsync(flow, CancellationToken.None);
                }
                if (closeCarrier)
                {
                    await CloseCoreAsync(m_closeResult, CancellationToken.None).ConfigureAwait(false);
                    continue;
                }
                if (stop)
                {
                    return;
                }
                await wait.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Selects only records that can make progress before entering carrier ownership.
        /// </summary>
        private bool CanDispatchLocked(FlowState flow, FlowWriteItem item)
        {
            if (!item.PreviousControl.IsCompleted)
            {
                return false;
            }
            if (!item.IsData)
            {
                return !RequiresAuthorization(item) || !AuthorizationPendingLocked(flow);
            }
            if (!flow.Ready || AuthorizationPendingLocked(flow))
            {
                return false;
            }
            if (flow.IsDatagram && flow.Update is { CommitQueued: true, Acknowledged: false })
            {
                return false;
            }
            if (IsShared && (flow.SendCredit < item.Cost || m_sendConnectionCredit < item.Cost))
            {
                flow.CreditStalls++;
                return false;
            }
            return flow.SendRate.Delay(item.Cost, m_clock) == TimeSpan.Zero;
        }

        /// <summary>
        /// Discards only queued, unsecured messages whose local expiry/cancellation is already known.
        /// </summary>
        private void RemoveExpiredLocked(FlowState flow)
        {
            LinkedListNode<FlowWriteItem>? node = flow.Outgoing.First;
            while (node != null)
            {
                LinkedListNode<FlowWriteItem>? next = node.Next;
                FlowWriteItem item = node.Value;
                if (item.IsData && (item.Cancellation.IsCancellationRequested || Expired(item)))
                {
                    flow.Outgoing.Remove(node);
                    if (Expired(item))
                    {
                        flow.QueueExpiryDrops++;
                    }
                    ReleaseWriteLocked(item, item.Cancellation.IsCancellationRequested
                        ? DataChannelResult.Closed : DataChannelResult.Timeout);
                }
                node = next;
            }
        }

        /// <summary>
        /// Checks expiry using only the receiver-independent local monotonic clock.
        /// </summary>
        private bool Expired(FlowWriteItem item)
        {
            return item.TimeToLive.HasValue && m_clock.GetElapsedTime(item.QueuedAt) >= item.TimeToLive.Value;
        }

        /// <summary>
        /// Executes one real carrier operation and retains storage until that operation finishes.
        /// </summary>
        private async Task ExecuteWriteAsync(FlowWriteItem item)
        {
            DataChannelResult result;
            bool invalidCarrierCommit = false;
            var write = new DataChannelWrite(
                item.Path,
                item.ChannelId,
                item.StreamId,
                item.RequestId,
                item.Encoded,
                item.IsData,
                item.Flow?.Parameters.Priority ?? 0,
                () => CommitWrite(item));
            try
            {
                result = await m_carrier.SendAsync(write, m_shutdown.Token).ConfigureAwait(false);
                if (result == DataChannelResult.Good && !write.IsCommitted)
                {
                    invalidCarrierCommit = write.Result == DataChannelResult.Good;
                    result = write.Result == DataChannelResult.Good
                        ? DataChannelResult.InvalidState : write.Result;
                }
            }
            catch (OperationCanceledException)
            {
                result = DataChannelResult.Closed;
            }
            catch (Exception exception)
            {
                m_logger.FlowCarrierFailure(exception);
                result = DataChannelResult.Closed;
            }
            lock (m_gate)
            {
                FlowState? flow = item.Flow;
                if (item.Path == DataChannelPath.Management || flow == null)
                {
                    m_managementBusy = false;
                }
                else
                {
                    flow.SendBusy = false;
                    flow.ActiveWrite = null;
                }
                m_activeWrites--;
                if (result == DataChannelResult.WouldBlock &&
                    !item.Committed &&
                    flow != null &&
                    !flow.IsTerminal &&
                    !m_closed)
                {
                    if (item.Path == DataChannelPath.Management)
                    {
                        FlowWriteItem[] later = [.. m_management];
                        m_management.Clear();
                        m_management.Enqueue(item);
                        foreach (FlowWriteItem queued in later)
                        {
                            m_management.Enqueue(queued);
                        }
                    }
                    else
                    {
                        flow.Outgoing.AddFirst(item);
                    }
                }
                else
                {
                    if (result == DataChannelResult.Good && flow != null)
                    {
                        if (item.IsData)
                        {
                            flow.MessagesSent++;
                            flow.BytesSent += (uint)item.Cost;
                        }
                        else if (item.Control!.Operation == FlowOperation.Ready)
                        {
                            flow.Opened.TrySetResult(DataChannelResult.Good);
                        }
                    }
                    ReleaseWriteLocked(item, result);
                    if (item.Committed && result != DataChannelResult.Good)
                    {
                        if (!IsNative ||
                            item.Path == DataChannelPath.Management ||
                            result == DataChannelResult.SecurityFailure)
                        {
                            CloseLocked(result);
                        }
                        else if (flow != null &&
                            !flow.IsTerminal &&
                            !(item.Path == DataChannelPath.Datagram &&
                                result is DataChannelResult.TooLarge or DataChannelResult.WouldBlock))
                        {
                            ResetLocked(flow, result);
                        }
                    }
                    else if (invalidCarrierCommit && !m_closed)
                    {
                        // An adapter returning success without TryCommit is not a successful carrier.
                        CloseLocked(result);
                    }
                    if (flow?.Update is { RemoteBarrier: true, CommitQueued: false } update && !flow.IsTerminal)
                    {
                        TryQueueCommitLocked(flow, update);
                    }
                }
                PulseCapacityLocked();
                WakeLocked();
            }
        }

        /// <summary>
        /// Applies the last expiry/authorization/capacity checks atomically at the carrier cut-off.
        /// </summary>
        private DataChannelResult CommitWrite(FlowWriteItem item)
        {
            lock (m_gate)
            {
                if (m_closed || item.Released)
                {
                    return DataChannelResult.Closed;
                }
                FlowState? flow = item.Flow;
                if (flow != null &&
                    flow.RevocationToken.IsCancellationRequested &&
                    item.Control?.Operation is not (FlowOperation.Reset or FlowOperation.Status))
                {
                    ResetLocked(flow, DataChannelResult.NotAuthorized);
                    return DataChannelResult.NotAuthorized;
                }
                if (!item.IsData &&
                    flow != null &&
                    flow.IsTerminal &&
                    item.Control!.Operation is not (FlowOperation.Reset or FlowOperation.Status))
                {
                    return DataChannelResult.Closed;
                }
                if (flow != null && RequiresAuthorization(item) && AuthorizationPendingLocked(flow))
                {
                    return DataChannelResult.WouldBlock;
                }
                if (item.IsData)
                {
                    if (flow == null || flow.IsTerminal || !flow.Ready || flow.LocalEnded)
                    {
                        return DataChannelResult.Closed;
                    }
                    if (item.Cancellation.IsCancellationRequested)
                    {
                        return DataChannelResult.Closed;
                    }
                    if (Expired(item))
                    {
                        flow.QueueExpiryDrops++;
                        return DataChannelResult.Timeout;
                    }
                    if (IsShared && (flow.SendCredit < item.Cost || m_sendConnectionCredit < item.Cost))
                    {
                        return DataChannelResult.WouldBlock;
                    }
                    if (!flow.SendRate.TryConsume(item.Cost, m_clock))
                    {
                        return DataChannelResult.WouldBlock;
                    }
                    if (IsShared)
                    {
                        flow.SendCredit -= (uint)item.Cost;
                        m_sendConnectionCredit -= (uint)item.Cost;
                    }
                    flow.LastSentDataId = item.Record.MessageId;
                }
                DataChannelResult result = item.CommitAction?.Invoke() ?? DataChannelResult.Good;
                if (result == DataChannelResult.Good)
                {
                    item.Committed = true;
                }
                return result;
            }
        }

        /// <summary>
        /// Keeps closure and acknowledgement feedback usable while payload permission is refreshed.
        /// </summary>
        private static bool RequiresAuthorization(FlowWriteItem item)
        {
            return item.IsData ||
                item.Control!.Operation is FlowOperation.Open or FlowOperation.Accept or
                    FlowOperation.Ready or FlowOperation.Prepare or FlowOperation.AcceptUpdate or
                    FlowOperation.Commit or FlowOperation.Credit;
        }

        /// <summary>
        /// Releases one queue reservation after the writer or discard path relinquishes ownership.
        /// </summary>
        private void ReleaseWriteLocked(FlowWriteItem item, DataChannelResult result)
        {
            if (item.Released)
            {
                return;
            }
            item.Released = true;
            if (!item.Committed)
            {
                item.DiscardAction?.Invoke();
            }
            if (item.IsData)
            {
                ReleaseMessageLocked(item.Flow!, item.Record.Revision, item.Cost, true);
            }
            else if (item.ControlReserved)
            {
                item.ControlReserved = false;
                ReleaseReceiveControlLocked(item.Cost);
            }
            item.Encoded = ByteString.Empty;
            item.Record = default;
            item.Completion.TrySetResult(result);
        }

        /// <summary>
        /// Grants shared credit only after readiness and only within currently available receive capacity.
        /// </summary>
        private void GrantCreditLocked(FlowState flow)
        {
            if (!flow.MayReceive || flow.RemoteEnded || flow.IsTerminal || !flow.Ready)
            {
                return;
            }
            FlowRevision revision = flow.Revisions[flow.ReceiveRevision];
            uint available = (uint)Math.Max(0, revision.Parameters.MaxReceiveBytes - flow.ReceiveBytes);
            uint connectionAvailable = (uint)Math.Max(0, m_options.MaxContextBytes - m_receiveBytes);
            uint threshold = Math.Max(flow.LastCreditGrant / 2, revision.Parameters.MaxMessageSize + 17);
            uint connectionThreshold = Math.Max(m_lastConnectionGrant / 2, revision.Parameters.MaxMessageSize + 17);
            uint channelGrant = flow.ReceiveCredit < threshold && available > flow.ReceiveCredit
                ? available - flow.ReceiveCredit : 0;
            uint connectionGrant = m_receiveConnectionCredit < connectionThreshold &&
                connectionAvailable > m_receiveConnectionCredit
                    ? connectionAvailable - m_receiveConnectionCredit : 0;
            if (channelGrant == 0 && connectionGrant == 0)
            {
                return;
            }
            if (channelGrant != 0)
            {
                flow.ReceiveCredit += channelGrant;
                flow.LastCreditGrant = channelGrant;
                uint grantRevision = flow.ReceiveRevision;
                QueueControlLocked(
                    new FlowCredit(flow.Id, channelGrant, 0),
                    grantRevision,
                    0,
                    DataChannelPath.Management,
                    () => flow.IsTerminal || flow.ReceiveRevision != grantRevision
                        ? DataChannelResult.Closed : DataChannelResult.Good,
                    flow,
                    () =>
                    {
                        if (flow.ReceiveRevision == grantRevision)
                        {
                            flow.ReceiveCredit -= channelGrant;
                        }
                    });
            }
            if (connectionGrant != 0)
            {
                QueueConnectionCreditLocked(connectionGrant);
            }
        }

        /// <summary>
        /// Restores aggregate capacity when an unsecured old-revision grant is invalidated.
        /// A connection-only grant cannot inherit the retired flow revision or block unrelated flows.
        /// </summary>
        private void ReplenishConnectionCreditLocked()
        {
            if (!IsShared || m_identity == null || m_flows.Count == 0)
            {
                return;
            }
            uint available = (uint)Math.Max(0, m_options.MaxContextBytes - m_receiveBytes);
            if (available <= m_receiveConnectionCredit)
            {
                return;
            }
            uint grant = available - m_receiveConnectionCredit;
            QueueConnectionCreditLocked(grant);
        }

        /// <summary>
        /// Keeps aggregate grants at revision zero: a peer's directional COMMIT can cross an
        /// already sent old-flow grant, which must not strand connection capacity when rejected.
        /// </summary>
        private void QueueConnectionCreditLocked(uint grant)
        {
            m_receiveConnectionCredit += grant;
            m_lastConnectionGrant = grant;
            QueueControlLocked(new FlowCredit(0, 0, grant), 0, 0, DataChannelPath.Management,
                discard: () =>
                {
                    m_receiveConnectionCredit -= grant;
                    m_replenishConnectionCredit = true;
                });
        }
    }
}
