// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.DataChannels
{
    public sealed partial class DataChannelContext
    {
        /// <summary>
        /// Requires both the authenticated selection and an actual ordered stream provider.
        /// </summary>
        private bool HasSelectedDirectStreams =>
            !IsNative && m_directBindingReady &&
            m_carrier is IDataChannelDirectStreamProvider { AreDirectStreamsAvailable: true };

        /// <summary>
        /// Issues one immutable token per permitted direction after ACCEPT has been queued.
        /// Receiving admission is installed before the peer can acknowledge and open its stream.
        /// </summary>
        private async ValueTask SendDirectAssociationsAsync(FlowState flow)
        {
            for (byte direction = 0; direction <= 1; direction++)
            {
                FlowAssociation proposal;
                FlowDirectAssociation association;
                lock (m_gate)
                {
                    EnsureOpeningLocked(flow);
                    if ((direction == 0 && flow.Parameters.Direction == DataChannelDirection.AcceptorToOpener) ||
                        (direction == 1 && flow.Parameters.Direction == DataChannelDirection.OpenerToAcceptor))
                    {
                        continue;
                    }
                    if (flow.LastControlId == uint.MaxValue)
                    {
                        throw new DataChannelException(DataChannelResult.InvalidState);
                    }
                    uint operation = ++flow.LastControlId;
                    proposal = new FlowAssociation(FlowOperation.Associate, flow.Id, direction, Guid.NewGuid());
                    DataChannelResult result = m_directAssociations.Admit(
                        flow, proposal, flow.Revision, operation, out FlowDirectAssociation? admitted);
                    if (result != DataChannelResult.Good)
                    {
                        throw new DataChannelException(result);
                    }
                    association = admitted!;
                }
                if (!await RegisterDirectReceiverAsync(flow, association).ConfigureAwait(false))
                {
                    throw new DataChannelException(DataChannelResult.Closed);
                }
                lock (m_gate)
                {
                    EnsureOpeningLocked(flow);
                    QueueControlLocked(proposal, association.Revision, association.OperationId,
                        DataChannelPath.Management, flow: flow);
                }
            }
        }

        /// <summary>
        /// Processes association transactions separately from generic sender high-water rejection.
        /// </summary>
        private async ValueTask<DataChannelResult> ReceiveDirectAssociationAsync(
            FlowAssociation control,
            FlowRecord record)
        {
            FlowState flow;
            FlowDirectAssociation? association = null;
            lock (m_gate)
            {
                if (!HasSelectedDirectStreams ||
                    !m_flows.TryGetValue(control.Flow, out FlowState? owner) ||
                    owner.DirectLane == null || owner.RevocationToken.IsCancellationRequested)
                {
                    return DataChannelResult.InvalidState;
                }
                flow = owner;
                if (control.Operation == FlowOperation.AssociateAck)
                {
                    if (flow.IsLocalOpener)
                    {
                        return DataChannelResult.InvalidState;
                    }
                    DataChannelResult result = m_directAssociations.Acknowledge(
                        flow, control, record.Revision, record.MessageId);
                    if (result == DataChannelResult.Good)
                    {
                        flow.PeerControlId = Math.Max(flow.PeerControlId, record.MessageId);
                        flow.LastControlId = Math.Max(flow.LastControlId, record.MessageId);
                    }
                    return result;
                }
                if (!flow.IsLocalOpener || !flow.DirectLane.Accepted)
                {
                    return DataChannelResult.InvalidState;
                }
                if (record.MessageId <= flow.PeerControlId &&
                    !m_directAssociations.IsRecorded(flow, control, record.Revision, record.MessageId))
                {
                    return DataChannelResult.InvalidState;
                }
                DataChannelResult admitted = m_directAssociations.Admit(
                    flow, control, record.Revision, record.MessageId, out association);
                if (admitted != DataChannelResult.Good)
                {
                    return admitted;
                }
                flow.PeerControlId = Math.Max(flow.PeerControlId, record.MessageId);
                flow.LastControlId = Math.Max(flow.LastControlId, record.MessageId);
            }
            if (!await RegisterDirectReceiverAsync(flow, association!).ConfigureAwait(false))
            {
                return DataChannelResult.Closed;
            }
            lock (m_gate)
            {
                if (flow.IsTerminal)
                {
                    return DataChannelResult.Closed;
                }
                var acknowledgement = new FlowAssociation(
                    FlowOperation.AssociateAck, flow.Id, control.Direction, control.Token);
                QueueControlLocked(
                    acknowledgement, record.Revision, record.MessageId, DataChannelPath.Management,
                    () =>
                    {
                        DataChannelResult result = m_directAssociations.Acknowledge(
                            flow, acknowledgement, record.Revision, record.MessageId);
                        if (result == DataChannelResult.Good)
                        {
                            TryQueueDirectReadyLocked(flow);
                        }
                        return result;
                    }, flow);
                return DataChannelResult.Good;
            }
        }

        /// <summary>
        /// Queues READY only after all permitted directions have committed their exact acknowledgements.
        /// </summary>
        private void TryQueueDirectReadyLocked(FlowState flow)
        {
            FlowDirectLane lane = flow.DirectLane!;
            if (lane.ReadyQueued || !lane.Accepted || !m_directAssociations.CanBecomeReady(flow))
            {
                return;
            }
            lane.ReadyQueued = true;
            QueueControlLocked(new FlowSignal(FlowOperation.Ready, flow.Id), 1, 0,
                DataChannelPath.Management, () =>
                {
                    if (flow.IsTerminal)
                    {
                        return flow.Result;
                    }
                    MarkReadyLocked(flow);
                    return DataChannelResult.Good;
                }, flow);
        }

        /// <summary>
        /// Installs a receiving token outside context synchronization before permitting peer stream creation.
        /// </summary>
        private async ValueTask<bool> RegisterDirectReceiverAsync(FlowState flow, FlowDirectAssociation association)
        {
            if (flow.IsDatagram)
            {
                bool admitted = await RegisterDirectDatagramReceiverAsync().ConfigureAwait(false);
                if (!admitted)
                {
                    lock (m_gate)
                    {
                        if (!flow.IsTerminal && !m_closed)
                        {
                            ResetLocked(flow, DataChannelResult.Closed);
                        }
                    }
                }
                return admitted;
            }
            Task<bool> registered;
            lock (m_gate)
            {
                if (flow.IsLocalOpener == (association.Direction == 0) ||
                    association.ReceiveRegistered != null)
                {
                    registered = association.ReceiveRegistered?.Task ?? Task.FromResult(true);
                }
                else
                {
                    association.ReceiveRegistered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    registered = association.ReceiveRegistered.Task;
                    flow.DirectLane!.ReceiveTask = Task.Run(() => ReceiveDirectStreamAsync(flow, association));
                }
            }
            return await registered.ConfigureAwait(false);
        }

        /// <summary>
        /// Admits and decodes one actual receiving stream without retaining unmanaged or unbudgeted input.
        /// </summary>
        private async Task ReceiveDirectStreamAsync(FlowState flow, FlowDirectAssociation association)
        {
            FlowDirectLane lane = flow.DirectLane!;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(m_shutdown.Token, lane.Shutdown.Token);
            try
            {
                var provider = (IDataChannelDirectStreamProvider)m_carrier;
                ValueTask<Stream> pending = provider.AcceptDirectStreamAsync(association.Token, linked.Token);
                association.ReceiveRegistered!.TrySetResult(true);
                Stream stream = await pending.ConfigureAwait(false);
                lane.Receiver = stream;
                linked.Token.ThrowIfCancellationRequested();
                await lane.Ready.Task.WaitAsync(linked.Token).ConfigureAwait(false);
                if (!m_budget.TryReserveCarrier(DirectReadBufferSize, 0, 1, out var bufferReservation))
                {
                    throw new DataChannelException(DataChannelResult.WouldBlock);
                }
                using (bufferReservation)
                using (var decoder = new DirectDeliveryFrameDecoder(
                    m_codecLimits, m_budget,
                    (in DirectDeliveryFrameHeader header) => ReserveDirectFrame(flow, in header)))
                {
                    byte[] buffer = new byte[DirectReadBufferSize];
                    while (true)
                    {
                        int count = await stream.ReadAsync(buffer.AsMemory(), linked.Token).ConfigureAwait(false);
                        if (count == 0)
                        {
                            decoder.Complete();
                            lock (m_gate)
                            {
                                if (!flow.RemoteEnded && !flow.IsTerminal)
                                {
                                    ResetLocked(flow, DataChannelResult.InvalidState);
                                }
                            }
                            return;
                        }
                        int offset = 0;
                        while (offset < count || decoder.IsWaitingForCapacity)
                        {
                            Task capacity = m_budget.CapacityChanged;
                            bool complete = decoder.Read(
                                buffer.AsSpan(offset, count - offset), out int consumed, out DirectDeliveryFrame frame,
                                deferAdmission: true);
                            offset += consumed;
                            if (decoder.IsWaitingForCapacity)
                            {
                                await capacity.WaitAsync(linked.Token).ConfigureAwait(false);
                            }
                            if (complete)
                            {
                                using (frame)
                                {
                                    DataChannelResult result = await ReceiveDirectFrameAsync(flow, frame, linked.Token)
                                        .ConfigureAwait(false);
                                    if (result != DataChannelResult.Good)
                                    {
                                        throw new DataChannelException(result);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                association.ReceiveRegistered!.TrySetResult(false);
            }
            catch (Exception exception)
            {
                // Preserve the provider result before a registration waiter can reset the flow as Closed.
                FailDirectStream(flow, exception);
                association.ReceiveRegistered!.TrySetResult(false);
            }
        }

        /// <summary>
        /// Reserves a complete frame before the decoder allocates its body.
        /// </summary>
        private IDisposable? ReserveDirectFrame(
            FlowState flow, in DirectDeliveryFrameHeader header)
        {
            lock (m_gate)
            {
                if (m_closed || flow.IsTerminal)
                {
                    throw new DataChannelException(DataChannelResult.Closed);
                }
                if (header.Kind == DirectDeliveryFrameKind.Barrier)
                {
                    int cost = checked(3 * header.TotalLength);
                    return TryReserveReceiveControlLocked(cost) ? new DirectControlReservation(this, cost) : null;
                }
                if (header.Kind == DirectDeliveryFrameKind.Data &&
                    header.BodyLength > flow.Revisions[flow.ReceiveRevision].Parameters.MaxMessageSize)
                {
                    throw new DataChannelException(DataChannelResult.TooLarge);
                }
            }
            return m_budget.TryReserveMessageLease(header.TotalLength, out var reservation) ? reservation : null;
        }

        /// <summary>
        /// Returns a direct barrier's independently reserved control headroom after dispatch or rejection.
        /// </summary>
        private void ReleaseDirectControlReservation(int cost)
        {
            lock (m_gate)
            {
                ReleaseReceiveControlLocked(cost);
            }
        }

        /// <summary>
        /// Enforces the association's source, direction and revision at each complete ordered frame.
        /// </summary>
        private async ValueTask<DataChannelResult> ReceiveDirectFrameAsync(
            FlowState flow, DirectDeliveryFrame frame, CancellationToken cancellationToken)
        {
            if (frame.Control is FlowCommit)
            {
                await WaitForDirectRevisionAcceptanceAsync(flow, frame.OperationId, cancellationToken)
                    .ConfigureAwait(false);
            }
            FlowRecord record;
            lock (m_gate)
            {
                if (!m_directAssociations.TryGetUsable(flow, false, out _))
                {
                    return DataChannelResult.InvalidState;
                }
                if (frame.Kind == DirectDeliveryFrameKind.Barrier)
                {
                    if (frame.Control?.Flow != flow.Id)
                    {
                        return DataChannelResult.InvalidArgument;
                    }
                    record = new FlowRecord(FlowRecordKind.Control, frame.Revision, frame.OperationId, frame.Body);
                }
                else
                {
                    DataChannelResult valid = ValidateDirectPayloadLocked(flow, frame.Body);
                    if (valid != DataChannelResult.Good)
                    {
                        return valid;
                    }
                    if (flow.LastRoutedDataId == uint.MaxValue)
                    {
                        return DataChannelResult.InvalidArgument;
                    }
                    record = new FlowRecord(
                        FlowRecordKind.Data, flow.ReceiveRevision, ++flow.LastRoutedDataId, frame.Body);
                }
            }
            return frame.Kind == DirectDeliveryFrameKind.Data
                ? await ReceiveDataAsync(flow.Id, record, false, cancellationToken, frame).ConfigureAwait(false)
                : await ReceiveControlAsync(
                    frame.Control!, record, DataChannelPath.Flow, 0, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// A separate stream may deliver its barrier before the secured acceptance reaches the opener.
        /// </summary>
        private async ValueTask WaitForDirectRevisionAcceptanceAsync(
            FlowState flow, uint operationId, CancellationToken cancellationToken)
        {
            while (true)
            {
                Task changed;
                lock (m_gate)
                {
                    if (flow.IsTerminal || m_closed ||
                        !flow.Operations.TryGetValue(operationId, out FlowUpdate? update) ||
                        update.Accepted || update.Completed)
                    {
                        return;
                    }
                    changed = m_capacityChanged.Task;
                }
                await changed.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Opens only a confirmed local sending direction, after READY, and publishes the actual stream.
        /// </summary>
        private async Task OpenDirectSenderAsync(FlowState flow, FlowDirectAssociation association)
        {
            FlowDirectLane lane = flow.DirectLane!;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(m_shutdown.Token, lane.Shutdown.Token);
            try
            {
                Stream stream = await ((IDataChannelDirectStreamProvider)m_carrier)
                    .OpenDirectStreamAsync(association.Token, linked.Token).ConfigureAwait(false);
                if (linked.IsCancellationRequested || !lane.Sender.TrySetResult(stream))
                {
                    await DisposeDirectStreamAsync(stream).ConfigureAwait(false);
                    lane.Sender.TrySetCanceled();
                }
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                lane.Sender.TrySetCanceled();
            }
            catch (Exception exception)
            {
                lane.Sender.TrySetCanceled();
                FailDirectStream(flow, exception);
            }
        }

        /// <summary>
        /// Isolates direct stream errors to their admitted flow and reports unexpected transport failures.
        /// </summary>
        private void FailDirectStream(FlowState flow, Exception exception)
        {
            DataChannelResult result = exception switch
            {
                DataChannelException failure => failure.Result,
                FlowCodecException failure => FlowModel.FromWire(failure.Status),
                _ => DataChannelResult.Closed
            };
            if (exception is not (DataChannelException or FlowCodecException))
            {
                m_logger.FlowCarrierFailure(exception);
            }
            lock (m_gate)
            {
                if (!flow.IsTerminal && !m_closed)
                {
                    ResetLocked(flow, result);
                }
            }
        }

        /// <summary>
        /// Disposes either modern or polyfilled stream owners without blocking asynchronous teardown.
        /// </summary>
        private static async ValueTask DisposeDirectStreamAsync(Stream stream)
        {
            if (stream is IAsyncDisposable asynchronous)
            {
                await asynchronous.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                stream.Dispose();
            }
        }

        /// <summary>
        /// Cancels pending admissions and I/O, then joins all owners before releasing their actual streams.
        /// </summary>
        private static async ValueTask ReleaseDirectLaneAsync(FlowDirectLane lane)
        {
            lane.Shutdown.Cancel();
            Stream? receiver = lane.Receiver;
            Stream? sender = lane.Sender.Task.Status == TaskStatus.RanToCompletion
                ? await lane.Sender.Task.ConfigureAwait(false) : null;
            try
            {
                Task releaseReceiver = receiver != null
                    ? DisposeDirectStreamAsync(receiver).AsTask() : Task.CompletedTask;
                Task releaseSender = sender != null && !ReferenceEquals(sender, receiver)
                    ? DisposeDirectStreamAsync(sender).AsTask() : Task.CompletedTask;
                await Task.WhenAll(releaseReceiver, releaseSender, lane.SendTask, lane.ReceiveTask).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (lane.Receiver != null && !ReferenceEquals(lane.Receiver, receiver))
                    {
                        await DisposeDirectStreamAsync(lane.Receiver).ConfigureAwait(false);
                    }
                }
                finally
                {
                    try
                    {
                        if (lane.Sender.Task.Status == TaskStatus.RanToCompletion)
                        {
                            Stream lateSender = await lane.Sender.Task.ConfigureAwait(false);
                            if (!ReferenceEquals(lateSender, sender) && !ReferenceEquals(lateSender, receiver) &&
                                !ReferenceEquals(lateSender, lane.Receiver))
                            {
                                await DisposeDirectStreamAsync(lateSender).ConfigureAwait(false);
                            }
                        }
                    }
                    finally
                    {
                        lane.Shutdown.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Keeps ordered barriers independent of saturated application-message slots.
        /// </summary>
        private sealed class DirectControlReservation(DataChannelContext context, int cost) : IDisposable
        {
            /// <inheritdoc/>
            public void Dispose()
            {
                Interlocked.Exchange(ref m_release, null)?.Invoke();
            }

            /// <summary>The release callback for this still-live control charge.</summary>
            private Action? m_release = () => context.ReleaseDirectControlReservation(cost);
        }

        /// <summary>The finite context-scoped association history.</summary>
        private readonly FlowDirectAssociationRegistry m_directAssociations;

        /// <summary>The fixed maximum amount borrowed by each ordered receiving pump.</summary>
        private const int DirectReadBufferSize = 4096;
    }
}
