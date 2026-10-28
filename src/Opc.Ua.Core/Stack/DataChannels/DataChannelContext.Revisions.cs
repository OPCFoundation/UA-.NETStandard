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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.DataChannels
{
    public sealed partial class DataChannelContext
    {
        /// <summary>
        /// Initiates or retries the opener's complete proposal without exposing readiness choreography.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal async ValueTask<DataChannelResult> UpdateAsync(
            DataChannel channel,
            DataChannelParametersDataType parameters,
            uint operationId,
            CancellationToken cancellationToken)
        {
            DataChannelParametersDataType proposal = FlowModel.Copy(parameters);
            FlowState flow = channel.StateData;
            FlowUpdate update;
            bool authorize;
            lock (m_gate)
            {
                if (!flow.IsLocalOpener || !flow.Ready || flow.IsTerminal || flow.Draining)
                {
                    return DataChannelResult.InvalidState;
                }
                if (AuthorizationPendingLocked(flow))
                {
                    return DataChannelResult.WouldBlock;
                }
                if (!FlowModel.SameDescriptor(flow.Parameters, proposal))
                {
                    return DataChannelResult.InvalidArgument;
                }
                try
                {
                    proposal = AdmitParametersLocked(proposal, false);
                }
                catch (DataChannelException exception)
                {
                    return exception.Result;
                }
                if (operationId != 0 && flow.Operations.TryGetValue(operationId, out FlowUpdate? recorded))
                {
                    if (!FlowModel.SameParameters(recorded.Parameters, proposal))
                    {
                        return DataChannelResult.InvalidArgument;
                    }
                    if (recorded.Completed)
                    {
                        return recorded.Result;
                    }
                    update = recorded;
                    authorize = false;
                    if (update.Completion.Task.IsCompleted)
                    {
                        update.Completion = FlowState.NewCompletion();
                    }
                    if (update.Accepted)
                    {
                        TryQueueCommitLocked(flow, update);
                    }
                    else
                    {
                        QueuePrepareLocked(flow, update);
                    }
                }
                else
                {
                    if (operationId == 0)
                    {
                        if (flow.LastOperationId == uint.MaxValue)
                        {
                            return DataChannelResult.InvalidState;
                        }
                        operationId = flow.LastOperationId + 1;
                    }
                    DataChannelResult created = CreateUpdateLocked(
                        flow, operationId, flow.Revision + 1, proposal, out FlowUpdate? admitted);
                    if (created != DataChannelResult.Good)
                    {
                        return created;
                    }
                    update = admitted ??
                        throw new InvalidOperationException("Successful revision admission did not create an update.");
                    authorize = true;
                }
            }
            if (authorize)
            {
                DataChannelAuthorization authorization = await AuthorizeAsync(
                    flow, DataChannelAuthorizationPurpose.Update, proposal,
                    update.Revision, operationId, cancellationToken).ConfigureAwait(false);
                lock (m_gate)
                {
                    if (update.Completed || flow.IsTerminal)
                    {
                        return update.Result;
                    }
                    if (authorization.Result != DataChannelResult.Good ||
                        authorization.Parameters == null ||
                        authorization.RevocationToken.IsCancellationRequested ||
                        !FlowModel.SameParameters(proposal, authorization.Parameters))
                    {
                        AbortUpdateLocked(flow, update, DataChannelResult.NotAuthorized);
                        return DataChannelResult.NotAuthorized;
                    }
                    update.Authorized = true;
                    InstallRevocationLocked(flow, authorization.RevocationToken);
                    QueuePrepareLocked(flow, update);
                }
            }
            return await update.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Admits one next-revision proposal while retaining current windows and bounded history.
        /// </summary>
        private DataChannelResult CreateUpdateLocked(
            FlowState flow,
            uint operationId,
            uint revision,
            DataChannelParametersDataType proposal,
            out FlowUpdate? update)
        {
            update = null;
            if (operationId == 0 ||
                operationId <= flow.LastOperationId ||
                flow.Revision == uint.MaxValue ||
                revision != flow.Revision + 1)
            {
                return DataChannelResult.InvalidArgument;
            }
            if (flow.Update != null)
            {
                return DataChannelResult.WouldBlock;
            }
            if (flow.PreviousReceiveRevision is { Grace: true } previous)
            {
                if (m_clock.GetElapsedTime(previous.GraceStarted) < m_options.DatagramRevisionGrace)
                {
                    return DataChannelResult.WouldBlock;
                }
                previous.Grace = false;
                flow.PreviousReceiveRevision = null;
                RetireRevisionsLocked(flow);
            }
            if (!m_budget.TryReserve(FlowResource.Operation, 1))
            {
                return DataChannelResult.WouldBlock;
            }
            if (!TryReserveRevisionLocked(flow, revision, proposal, true))
            {
                m_budget.Release(FlowResource.Operation, 1);
                return DataChannelResult.WouldBlock;
            }
            update = new FlowUpdate(operationId, revision, proposal, m_clock.GetTimestamp())
            {
                OperationReserved = true
            };
            flow.LastOperationId = operationId;
            flow.Operations.Add(operationId, update);
            flow.OperationOrder.Enqueue(operationId);
            while (flow.OperationOrder.Count > 64)
            {
                flow.Operations.Remove(flow.OperationOrder.Dequeue());
            }
            flow.Update = update;
            flow.State = DataChannelState.Updating;
            flow.Result = DataChannelResult.Good;
            NotifyStateLocked(flow);
            ArmTimerLocked();
            return DataChannelResult.Good;
        }

        /// <summary>
        /// Sends the exact complete proposal under the opener's stable operation identifier.
        /// </summary>
        private void QueuePrepareLocked(FlowState flow, FlowUpdate update)
        {
            QueueControlLocked(
                new FlowPrepare(flow.Id, FlowModel.Limits(update.Parameters)),
                update.Revision, update.Id, DataChannelPath.Management, flow: flow);
        }

        /// <summary>
        /// Authorizes PREPARE, including the optional provider's Session-staged operation check.
        /// </summary>
        private async ValueTask<DataChannelResult> ReceivePrepareAsync(
            FlowPrepare prepare,
            FlowRecord record,
            CancellationToken cancellationToken)
        {
            FlowState flow;
            FlowUpdate update;
            DataChannelParametersDataType proposal;
            lock (m_gate)
            {
                if (!m_flows.TryGetValue(prepare.Flow, out FlowState? found))
                {
                    return IsRetiredChannelIdLocked(prepare.Flow)
                        ? DataChannelResult.Good
                        : QueueAdvisoryLocked(prepare.Flow, record.Revision, record.MessageId,
                            DataChannelResult.UnknownChannel);
                }
                flow = found;
                if (flow.IsLocalOpener || !flow.Ready || flow.Draining || flow.IsTerminal)
                {
                    return QueueAdvisoryLocked(prepare.Flow, record.Revision, record.MessageId,
                        DataChannelResult.InvalidState, flow);
                }
                proposal = FlowModel.WithLimits(flow.Parameters, prepare.Limits);
                if (flow.Operations.TryGetValue(record.MessageId, out FlowUpdate? recorded))
                {
                    if (recorded.Revision != record.Revision ||
                        !FlowModel.SameParameters(recorded.Parameters, proposal))
                    {
                        QueueStatusLocked(flow, record.MessageId, DataChannelResult.InvalidArgument);
                        return DataChannelResult.InvalidArgument;
                    }
                    ReplayUpdateLocked(flow, recorded);
                    return recorded.Result;
                }
                if (record.MessageId <= flow.LastOperationId)
                {
                    QueueStatusLocked(flow, record.MessageId, DataChannelResult.InvalidState);
                    return DataChannelResult.InvalidState;
                }
                try
                {
                    proposal = AdmitParametersLocked(proposal, false);
                }
                catch (DataChannelException exception)
                {
                    QueueStatusLocked(flow, record.MessageId, exception.Result);
                    return exception.Result;
                }
                DataChannelResult created = CreateUpdateLocked(
                    flow, record.MessageId, record.Revision, proposal, out FlowUpdate? candidate);
                if (created != DataChannelResult.Good)
                {
                    QueueStatusLocked(flow, record.MessageId, created);
                    return created;
                }
                update = candidate!;
            }
            DataChannelAuthorization authorization = await AuthorizeAsync(
                flow, DataChannelAuthorizationPurpose.Update, proposal,
                record.Revision, record.MessageId, cancellationToken).ConfigureAwait(false);
            lock (m_gate)
            {
                if (flow.IsTerminal || update.Completed)
                {
                    return update.Result;
                }
                if (authorization.Result != DataChannelResult.Good ||
                    authorization.Parameters == null ||
                    authorization.RevocationToken.IsCancellationRequested ||
                    !FlowModel.SameParameters(proposal, authorization.Parameters))
                {
                    AbortUpdateLocked(flow, update, DataChannelResult.NotAuthorized);
                    QueueStatusLocked(flow, update.Id, DataChannelResult.NotAuthorized);
                    return DataChannelResult.NotAuthorized;
                }
                update.Authorized = true;
                InstallRevocationLocked(flow, authorization.RevocationToken);
                update.Accepted = true;
                QueueControlLocked(
                    new FlowSignal(FlowOperation.AcceptUpdate, flow.Id),
                    update.Revision, update.Id, DataChannelPath.Management, flow: flow);
                if (flow.DirectLane != null && (!flow.MaySend || !flow.MayReceive))
                {
                    TryQueueCommitLocked(flow, update);
                }
                return DataChannelResult.Good;
            }
        }

        /// <summary>
        /// Implements directional barriers, idempotent responses and committed-outcome recovery.
        /// </summary>
        private DataChannelResult ReceiveRevisionControlLocked(FlowState flow, FlowControl control, FlowRecord record)
        {
            if (!flow.Operations.TryGetValue(record.MessageId, out FlowUpdate? update))
            {
                return QueueStatusLocked(flow, record.MessageId, DataChannelResult.InvalidState);
            }
            if (record.Revision != update.Revision)
            {
                return QueueStatusLocked(flow, record.MessageId, DataChannelResult.InvalidArgument);
            }
            if (control.Operation == FlowOperation.AbortUpdate)
            {
                if (!update.Committed)
                {
                    AbortUpdateLocked(flow, update, DataChannelResult.Closed);
                }
                QueueStatusLocked(flow, update.Id, update.Result);
                return update.Committed ? DataChannelResult.InvalidState : DataChannelResult.Good;
            }
            if (update.Completed)
            {
                if (control is FlowCommit repeated && repeated.LastOldMessageId != update.RemoteLastOld)
                {
                    return QueueStatusLocked(flow, record.MessageId, DataChannelResult.InvalidArgument);
                }
                if (control is FlowCommit)
                {
                    QueueAckLocked(flow, update);
                }
                return update.Result;
            }
            switch (control)
            {
                case FlowSignal { Operation: FlowOperation.AcceptUpdate }:
                    if (!flow.IsLocalOpener || !update.Authorized)
                    {
                        return QueueStatusLocked(flow, record.MessageId, DataChannelResult.InvalidState);
                    }
                    update.Accepted = true;
                    PulseCapacityLocked();
                    return TryQueueCommitLocked(flow, update);
                case FlowCommit commit:
                    if (!update.Accepted)
                    {
                        return QueueStatusLocked(flow, record.MessageId, DataChannelResult.InvalidState);
                    }
                    if (update.RemoteBarrier && commit.LastOldMessageId != update.RemoteLastOld)
                    {
                        return QueueStatusLocked(flow, record.MessageId, DataChannelResult.InvalidArgument);
                    }
                    if (!update.RemoteBarrier)
                    {
                        if (flow.IsDatagram
                            ? commit.LastOldMessageId < flow.LastReceivedDataId
                            : commit.LastOldMessageId != 0)
                        {
                            return QueueStatusLocked(flow, record.MessageId, DataChannelResult.InvalidArgument);
                        }
                        CommitRemoteLocked(flow, update, commit.LastOldMessageId);
                    }
                    if (!update.LocalBarrier)
                    {
                        TryQueueCommitLocked(flow, update);
                    }
                    else
                    {
                        QueueAckLocked(flow, update);
                    }
                    return DataChannelResult.Good;
                case FlowSignal { Operation: FlowOperation.Ack }:
                    if (!update.LocalBarrier &&
                        (flow.DirectLane == null || flow.MaySend || !update.Accepted))
                    {
                        return QueueStatusLocked(flow, record.MessageId, DataChannelResult.InvalidState);
                    }
                    update.Acknowledged = true;
                    CompleteUpdateLocked(flow, update);
                    WakeLocked();
                    return DataChannelResult.Good;
                default:
                    return QueueStatusLocked(flow, record.MessageId, DataChannelResult.InvalidState);
            }
        }

        /// <summary>
        /// Ensures datagram retention is all-or-nothing; ordered messages drain before the barrier.
        /// </summary>
        private DataChannelResult TryQueueCommitLocked(FlowState flow, FlowUpdate update)
        {
            if (!update.Accepted || update.Completed)
            {
                return DataChannelResult.InvalidState;
            }
            if (update.CommitQueued || update.LocalBarrier)
            {
                return DataChannelResult.Good;
            }
            if (flow.DirectLane != null && !flow.MaySend && !update.RemoteBarrier)
            {
                return DataChannelResult.Good;
            }
            if (flow.IsDatagram)
            {
                long retained = 0;
                foreach (FlowWriteItem item in flow.Outgoing)
                {
                    if (!item.IsData)
                    {
                        continue;
                    }
                    if (item.PayloadLength > update.Parameters.MaxMessageSize)
                    {
                        update.Result = DataChannelResult.WouldBlock;
                        update.Completion.TrySetResult(DataChannelResult.WouldBlock);
                        return DataChannelResult.WouldBlock;
                    }
                    retained += item.Cost;
                }
                if (retained > update.Parameters.MaxReceiveBytes)
                {
                    update.Result = DataChannelResult.WouldBlock;
                    update.Completion.TrySetResult(DataChannelResult.WouldBlock);
                    return DataChannelResult.WouldBlock;
                }
            }
            update.CommitQueued = true;
            update.Result = DataChannelResult.Good;
            if (flow.DirectLane != null && !flow.MaySend)
            {
                return CommitLocalLocked(flow, update);
            }
            QueueControlLocked(
                new FlowCommit(flow.Id, uint.MaxValue),
                update.Revision,
                update.Id,
                DataChannelPath.Flow,
                () => CommitLocalLocked(flow, update),
                flow);
            if (flow.IsDatagram &&
                flow.Outgoing.Last is { } barrier &&
                barrier.Value.Control is FlowCommit &&
                barrier.Value.Record.MessageId == update.Id)
            {
                flow.Outgoing.RemoveLast();
                flow.Barrier = barrier.Value;
            }
            WakeLocked();
            return DataChannelResult.Good;
        }

        /// <summary>
        /// Encodes the final old message ID only after earlier ordered writes have completed.
        /// </summary>
        private void PrepareBarrierWriteLocked(FlowWriteItem item)
        {
            if (item.Control is not FlowCommit ||
                item.Flow is not { } flow ||
                !flow.Operations.TryGetValue(item.Record.MessageId, out FlowUpdate? update))
            {
                return;
            }
            uint lastOld = update.LocalBarrier ? update.LocalLastOld : flow.IsDatagram ? flow.LastSentDataId : 0;
            var commit = new FlowCommit(flow.Id, lastOld);
            item.Control = commit;
            item.Record = FlowControlCodec.Encode(commit, update.Revision, update.Id, m_codecLimits);
            EncodeWriteLocked(item);
        }

        /// <summary>
        /// Installs the sending revision at its actual ordered commitment boundary.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private DataChannelResult CommitLocalLocked(FlowState flow, FlowUpdate update)
        {
            if (flow.IsTerminal || update.Completed || !update.Accepted)
            {
                return DataChannelResult.InvalidState;
            }
            if (update.LocalBarrier)
            {
                return DataChannelResult.Good;
            }
            FlowRevision old = flow.Revisions[flow.SendRevision];
            FlowRevision next = flow.Revisions[update.Revision];
            update.LocalLastOld = flow.IsDatagram ? flow.LastSentDataId : 0;
            update.LocalBarrier = true;
            old.SendActive = false;
            next.SendActive = true;
            next.Pending = !next.ReceiveActive;
            flow.SendRevision = update.Revision;
            flow.Revision = update.Revision;
            flow.Parameters = update.Parameters;
            flow.SendCredit = 0;
            flow.SendRate.Configure(update.Parameters.MaxRate, update.Parameters.MaxReceiveBytes,
                m_clock.GetTimestamp(), false);
            if (flow.IsDatagram)
            {
                foreach (FlowWriteItem item in flow.Outgoing)
                {
                    if (!item.IsData)
                    {
                        continue;
                    }
                    FlowRecord record = new(
                        FlowRecordKind.Data, update.Revision, item.Record.MessageId, item.Record.Body);
                    if (!MemoryMarshal.TryGetArray(item.Encoded.Memory, out ArraySegment<byte> storage))
                    {
                        throw new InvalidOperationException("An owned flow record lost its writable backing storage.");
                    }
                    if (flow.DirectLane != null)
                    {
                        BinaryPrimitives.WriteUInt32LittleEndian(
                            storage.AsSpan()[DirectDatagramCodec.AssociationTokenSize..], update.Revision);
                    }
                    else
                    {
                        FlowRecordCodec.Write(in record, storage.AsSpan()[4..], m_codecLimits);
                    }
                    item.Record = record;
                    old.SendBytes -= item.Cost;
                    next.SendBytes += item.Cost;
                }
            }
            if (flow.DirectLane != null && !flow.MayReceive && !update.RemoteBarrier)
            {
                CommitRemoteLocked(flow, update, 0);
            }
            if (update.RemoteBarrier)
            {
                QueueAckLocked(flow, update);
            }
            RetireRevisionsLocked(flow);
            NotifyStateLocked(flow);
            return DataChannelResult.Good;
        }

        /// <summary>
        /// Installs receive limits at the verified directional barrier and retains old datagram grace.
        /// </summary>
        private void CommitRemoteLocked(FlowState flow, FlowUpdate update, uint lastOldMessageId)
        {
            FlowRevision old = flow.Revisions[flow.ReceiveRevision];
            FlowRevision next = flow.Revisions[update.Revision];
            old.ReceiveActive = false;
            if (flow.IsDatagram)
            {
                old.Grace = true;
                old.GraceStarted = m_clock.GetTimestamp();
                old.LastOldMessageId = lastOldMessageId;
                flow.PreviousReceiveRevision = old;
            }
            next.ReceiveActive = true;
            next.Pending = !next.SendActive;
            flow.ReceiveRevision = update.Revision;
            flow.Revision = update.Revision;
            flow.Parameters = update.Parameters;
            flow.ReceiveCredit = 0;
            update.RemoteLastOld = lastOldMessageId;
            update.RemoteBarrier = true;
            if (UsesFlowCredit(flow))
            {
                GrantCreditLocked(flow);
            }
            RetireRevisionsLocked(flow);
            NotifyStateLocked(flow);
            ArmTimerLocked();
        }

        /// <summary>
        /// Acknowledges a received barrier on the bound flow path, after the local sending barrier.
        /// </summary>
        private void QueueAckLocked(FlowState flow, FlowUpdate update)
        {
            if (!update.LocalBarrier || !update.RemoteBarrier)
            {
                return;
            }
            QueueControlLocked(
                new FlowSignal(FlowOperation.Ack, flow.Id),
                update.Revision, update.Id, DataChannelPath.Flow, () =>
                {
                    update.AckSent = true;
                    CompleteUpdateLocked(flow, update);
                    return DataChannelResult.Good;
                }, flow);
        }

        /// <summary>
        /// Completes only after both directional barriers and their acknowledgements are established.
        /// </summary>
        private void CompleteUpdateLocked(FlowState flow, FlowUpdate update)
        {
            if (update.Completed ||
                !update.LocalBarrier ||
                !update.RemoteBarrier ||
                !update.Acknowledged ||
                !update.AckSent)
            {
                return;
            }
            update.Completed = true;
            update.Result = DataChannelResult.Good;
            flow.Update = null;
            flow.State = DataChannelState.Open;
            flow.Result = DataChannelResult.Good;
            ReleaseUpdateOperationLocked(update);
            update.Completion.TrySetResult(DataChannelResult.Good);
            NotifyStateLocked(flow);
            WakeLocked();
        }

        /// <summary>
        /// Replays recorded outcomes without applying their descriptor or resource reservation again.
        /// </summary>
        private void ReplayUpdateLocked(FlowState flow, FlowUpdate update)
        {
            if (update.Committed)
            {
                if (update.LocalBarrier && !update.Acknowledged && (flow.DirectLane == null || flow.MaySend))
                {
                    QueueControlLocked(
                        new FlowCommit(flow.Id, update.LocalLastOld),
                        update.Revision, update.Id, DataChannelPath.Flow, flow: flow);
                }
                if (update.RemoteBarrier && update.LocalBarrier)
                {
                    QueueAckLocked(flow, update);
                }
                QueueStatusLocked(flow, update.Id, update.Result);
            }
            else if (update.Accepted && !update.Completed)
            {
                QueueControlLocked(
                    new FlowSignal(FlowOperation.AcceptUpdate, flow.Id),
                    update.Revision, update.Id, DataChannelPath.Management, flow: flow);
            }
            else
            {
                QueueStatusLocked(flow, update.Id, update.Result);
            }
        }

        /// <summary>
        /// Reports a known outcome with the currently effective revision.
        /// </summary>
        private DataChannelResult QueueStatusLocked(FlowState flow, uint operationId, DataChannelResult result)
        {
            return QueueAdvisoryLocked(flow.Id, flow.Revision, operationId, result, flow);
        }

        /// <summary>
        /// Aborts only an uncommitted operation; no timeout or caller cancellation rolls a revision back.
        /// </summary>
        internal ValueTask<DataChannelResult> AbortUpdateAsync(
            DataChannel channel,
            uint operationId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_gate)
            {
                FlowState flow = channel.StateData;
                if (!flow.IsLocalOpener || !flow.Operations.TryGetValue(operationId, out FlowUpdate? update))
                {
                    return new ValueTask<DataChannelResult>(DataChannelResult.InvalidState);
                }
                if (update.Committed)
                {
                    return new ValueTask<DataChannelResult>(DataChannelResult.InvalidState);
                }
                if (!update.Completed)
                {
                    QueueControlLocked(
                        new FlowSignal(FlowOperation.AbortUpdate, flow.Id),
                        update.Revision, update.Id, DataChannelPath.Management, flow: flow);
                    AbortUpdateLocked(flow, update, DataChannelResult.Closed);
                }
                return new ValueTask<DataChannelResult>(DataChannelResult.Good);
            }
        }

        /// <summary>
        /// Releases a provisional revision while retaining its operation high-water and outcome.
        /// </summary>
        private void AbortUpdateLocked(FlowState flow, FlowUpdate update, DataChannelResult result)
        {
            if (update.Committed || update.Completed)
            {
                return;
            }
            update.Completed = true;
            update.Result = result;
            if (flow.Revisions.TryGetValue(update.Revision, out FlowRevision? revision))
            {
                revision.Pending = false;
            }
            if (flow.Barrier?.Record.MessageId == update.Id)
            {
                ReleaseWriteLocked(flow.Barrier, result);
                flow.Barrier = null;
            }
            for (System.Collections.Generic.LinkedListNode<FlowWriteItem>? node = flow.Outgoing.First;
                node != null;)
            {
                System.Collections.Generic.LinkedListNode<FlowWriteItem>? next = node.Next;
                if (node.Value.Control is FlowCommit && node.Value.Record.MessageId == update.Id)
                {
                    flow.Outgoing.Remove(node);
                    ReleaseWriteLocked(node.Value, result);
                }
                node = next;
            }
            flow.Update = null;
            flow.State = DataChannelState.Open;
            flow.Result = result;
            ReleaseUpdateOperationLocked(update);
            RetireRevisionsLocked(flow);
            update.Completion.TrySetResult(result);
            NotifyStateLocked(flow);
            WakeLocked();
        }

        /// <summary>
        /// Releases one outstanding transaction reservation, independently of its bounded replay history.
        /// </summary>
        private void ReleaseUpdateOperationLocked(FlowUpdate update)
        {
            if (update.OperationReserved)
            {
                update.OperationReserved = false;
                m_budget.Release(FlowResource.Operation, 1);
            }
        }

        /// <summary>
        /// Aborts before commitment, but retains and retries committed barriers/outcomes after the deadline.
        /// </summary>
        private void CheckUpdateTimeoutLocked(FlowState flow)
        {
            FlowUpdate? update = flow.Update;
            if (update == null || m_clock.GetElapsedTime(update.Started) < m_options.UpdateTimeout)
            {
                return;
            }
            if (!update.Committed)
            {
                QueueControlLocked(
                    new FlowSignal(FlowOperation.AbortUpdate, flow.Id),
                    update.Revision, update.Id, DataChannelPath.Management, flow: flow);
                AbortUpdateLocked(flow, update, DataChannelResult.Timeout);
            }
            else
            {
                update.Started = m_clock.GetTimestamp();
                update.Completion.TrySetResult(DataChannelResult.Timeout);
                ReplayUpdateLocked(flow, update);
            }
        }
    }
}
