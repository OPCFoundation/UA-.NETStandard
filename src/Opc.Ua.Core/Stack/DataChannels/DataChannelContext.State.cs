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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.DataChannels
{
    public sealed partial class DataChannelContext
    {
        /// <summary>
        /// Validates carrier evidence and reserves context-wide resources before starting workers.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        private void Initialize()
        {
            if (m_carrier.MaxRecordSize < (IsNative ? FlowRecordCodec.HeaderSize : 1) ||
                m_carrier.Identity == null ||
                (IsNative && m_carrier.Identity.Protection != DataChannelProtectionMode.Confidential) ||
                (m_carrier.Identity.Protection == DataChannelProtectionMode.Integrity && !m_options.AllowIntegrity) ||
                (!IsNative && (m_carrier.ManagementStreamId != 0 || m_carrier.MaxDatagramSize != 0)))
            {
                throw new DataChannelException(DataChannelResult.SecurityFailure);
            }
            if (!m_budget.TryReserve(FlowResource.Context, 1))
            {
                throw new DataChannelException(DataChannelResult.WouldBlock);
            }
            m_contextReserved = true;
            if (!m_budget.TryReserveControlHeadroom(out m_controlHeadroom))
            {
                ReleaseContextReservationLocked();
                throw new DataChannelException(DataChannelResult.WouldBlock);
            }
            if (IsNative && !m_budget.TryReserve(FlowResource.Stream, 1))
            {
                ReleaseContextReservationLocked();
                throw new DataChannelException(DataChannelResult.WouldBlock);
            }
            m_managementStreamReserved = IsNative;
            m_localChallenge = RandomBytes(32);
            m_nextChannelId = m_carrier.Identity.IsContextInitiator ? 1UL : 2UL;
            if (m_carrier.Identity.IsContextInitiator)
            {
                do
                {
                    m_contextId = Guid.NewGuid();
                }
                while (m_contextId == Guid.Empty);
                m_generation = BinaryPrimitives.ReadUInt64LittleEndian(RandomBytes(8).Span);
                if (m_generation == 0)
                {
                    m_generation = 1;
                }
                if (!m_budget.RegisterContext(m_contextId))
                {
                    ReleaseContextReservationLocked();
                    throw new DataChannelException(DataChannelResult.WouldBlock);
                }
            }
            if (IsNative)
            {
                if (!TryCreateStreamInputLocked(0, m_carrier.ManagementStreamId, out FlowStreamInput? input))
                {
                    ReleaseContextReservationLocked();
                    throw new DataChannelException(DataChannelResult.WouldBlock);
                }
                m_streams.Add(m_carrier.ManagementStreamId, input);
            }
            m_timer = m_clock.CreateTimer(_ => OnTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            m_scheduler = RunSchedulerAsync();
        }

        /// <summary>
        /// Gets whether the carrier is native rather than UASC.
        /// </summary>
        private bool IsNative => m_carrier.Kind is DataChannelCarrierKind.Quic or DataChannelCarrierKind.WebTransport;

        /// <summary>
        /// Gets whether shared encoded-byte credit is required.
        /// </summary>
        private bool UsesUascCredit => !IsNative;

        /// <summary>
        /// Direct lanes use transport backpressure rather than baseline additive FLO credit.
        /// </summary>
        private bool UsesFlowCredit(FlowState flow) => UsesUascCredit && flow.DirectLane == null;

        /// <summary>
        /// Gets the DATA cost in this delivery path.
        /// </summary>
        private int DataOverhead(FlowState flow)
        {
            if (flow.DirectLane != null)
            {
                return flow.IsDatagram ? DirectDatagramCodec.HeaderSize : DirectDeliveryFrameCodec.HeaderSize;
            }
            return IsNative ? 13 + (flow.IsDatagram ? 4 : 0) : 0;
        }

        /// <summary>
        /// Creates cryptographically fresh context binding material.
        /// </summary>
        private static ByteString RandomBytes(int length)
        {
            byte[] bytes = new byte[length];
            using var generator = RandomNumberGenerator.Create();
            generator.GetBytes(bytes);
            return new ByteString(bytes);
        }

        /// <summary>
        /// Returns an immutable identity only after mutual HELLO confirmation.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        private DataChannelContextIdentity GetIdentity()
        {
            lock (m_gate)
            {
                return m_identity ?? throw new DataChannelException(DataChannelResult.InvalidState);
            }
        }

        /// <summary>
        /// Observes readiness without consulting a carrier or a Session.
        /// </summary>
        private bool GetIsReady()
        {
            lock (m_gate)
            {
                return m_identity != null && !m_closed;
            }
        }

        /// <summary>
        /// Returns a bounded active-flow snapshot.
        /// </summary>
        private ArrayOf<DataChannel> GetChannels()
        {
            lock (m_gate)
            {
                return new ArrayOf<DataChannel>([.. m_flows.Values.Select(flow => flow.Facade!)]);
            }
        }

        /// <summary>
        /// Returns the current generated flow status.
        /// </summary>
        internal DataChannelStatusDataType GetStatus(DataChannel channel)
        {
            lock (m_gate)
            {
                return StatusLocked(channel.StateData);
            }
        }

        /// <summary>
        /// Builds a generated snapshot while state is stable.
        /// </summary>
        private DataChannelStatusDataType StatusLocked(FlowState flow)
        {
            return new DataChannelStatusDataType
            {
                ChannelId = flow.Id,
                ContextId = m_contextId,
                Generation = m_generation,
                TransportStreamId = 0,
                SourceNodeId = flow.SourceNodeId,
                SessionId = flow.SessionId,
                State = flow.State,
                Revision = flow.Revision,
                PendingRevision = flow.Update?.Revision ?? 0,
                OperationId = flow.Update?.Id ?? flow.LastOperationId,
                Result = flow.Result,
                Parameters = FlowModel.Copy(flow.Parameters),
                OpenerEnded = flow.IsLocalOpener ? flow.LocalEnded : flow.RemoteEnded,
                AcceptorEnded = flow.IsLocalOpener ? flow.RemoteEnded : flow.LocalEnded,
                StartedAt = flow.StartedAt
            };
        }

        /// <summary>
        /// Returns counters whose ownership is independent of optional model projection.
        /// </summary>
        internal DataChannelDiagnosticsDataType GetDiagnostics(DataChannel channel)
        {
            lock (m_gate)
            {
                FlowState flow = channel.StateData;
                return new DataChannelDiagnosticsDataType
                {
                    ChannelId = flow.Id,
                    ContextId = m_contextId,
                    Generation = m_generation,
                    MessagesSent = flow.MessagesSent,
                    MessagesReceived = flow.MessagesReceived,
                    BytesSent = flow.BytesSent,
                    BytesReceived = flow.BytesReceived,
                    QueueExpiryDrops = flow.QueueExpiryDrops,
                    AdmissionDrops = flow.AdmissionDrops,
                    ReplayDrops = flow.ReplayDrops,
                    RevisionDrops = flow.RevisionDrops,
                    CreditStalls = flow.CreditStalls,
                    RoundTripTime = flow.RoundTripTime
                };
            }
        }

        /// <summary>
        /// Returns a recorded proposal outcome without applying it again.
        /// </summary>
        internal DataChannelStatusDataType GetOperationStatus(DataChannel channel, uint operationId)
        {
            lock (m_gate)
            {
                FlowState flow = channel.StateData;
                DataChannelStatusDataType status = StatusLocked(flow);
                status.OperationId = operationId;
                if (flow.Operations.TryGetValue(operationId, out FlowUpdate? update))
                {
                    status.Result = update.Result;
                    status.PendingRevision = update.Completed ? 0 : update.Revision;
                    if (update.Committed)
                    {
                        status.Revision = update.Revision;
                    }
                }
                else
                {
                    status.Result = DataChannelResult.InvalidState;
                    status.PendingRevision = 0;
                }
                return status;
            }
        }

        /// <summary>
        /// Reserves both local data directions independently, including concurrent revisions.
        /// </summary>
        private bool TryReserveRevisionLocked(
            FlowState flow,
            uint number,
            DataChannelParametersDataType parameters,
            bool pending)
        {
            long send = flow.MaySend ? parameters.MaxReceiveBytes : 0;
            long receive = flow.MayReceive ? parameters.MaxReceiveBytes : 0;
            if (send > m_options.MaxContextBytes - m_reservedSendBytes ||
                receive > m_options.MaxContextBytes - m_reservedReceiveBytes ||
                !m_budget.TryReserve(FlowResource.ReservedBytes, send + receive))
            {
                return false;
            }
            m_reservedSendBytes += send;
            m_reservedReceiveBytes += receive;
            var revision = new FlowRevision(number, parameters)
            {
                ReservedSend = send,
                ReservedReceive = receive,
                Pending = pending,
                SendActive = !pending,
                ReceiveActive = !pending
            };
            revision.ReceiveRate.Configure(parameters.MaxRate, parameters.MaxReceiveBytes,
                m_clock.GetTimestamp(), true);
            flow.Revisions.Add(number, revision);
            return true;
        }

        /// <summary>
        /// Retires a revision only after its network lifetime and retained buffers have ended.
        /// </summary>
        private void RetireRevisionsLocked(FlowState flow)
        {
            List<uint>? retired = null;
            foreach (FlowRevision revision in flow.Revisions.Values)
            {
                if (!revision.Pending && !revision.SendActive && revision.SendBytes == 0 && revision.ReservedSend != 0)
                {
                    m_reservedSendBytes -= revision.ReservedSend;
                    m_budget.Release(FlowResource.ReservedBytes, revision.ReservedSend);
                    revision.ReservedSend = 0;
                }
                if (!revision.Pending &&
                    !revision.ReceiveActive &&
                    !revision.Grace &&
                    revision.ReceiveBytes == 0 &&
                    revision.ReservedReceive != 0)
                {
                    m_reservedReceiveBytes -= revision.ReservedReceive;
                    m_budget.Release(FlowResource.ReservedBytes, revision.ReservedReceive);
                    revision.ReservedReceive = 0;
                }
                if (revision.Pending ||
                    revision.SendActive ||
                    revision.ReceiveActive ||
                    revision.Grace ||
                    revision.SendBytes != 0 ||
                    revision.ReceiveBytes != 0)
                {
                    continue;
                }
                m_reservedSendBytes -= revision.ReservedSend;
                m_reservedReceiveBytes -= revision.ReservedReceive;
                m_budget.Release(FlowResource.ReservedBytes, revision.ReservedSend + revision.ReservedReceive);
                (retired ??= []).Add(revision.Number);
            }
            if (retired != null)
            {
                foreach (uint number in retired)
                {
                    flow.Revisions.Remove(number);
                }
                PulseCapacityLocked();
            }
        }

        /// <summary>
        /// Reserves logical credit bytes and actual buffered bytes before copying a body.
        /// </summary>
        private bool TryReserveMessageLocked(
            FlowState flow,
            FlowRevision revision,
            int cost,
            bool sending,
            bool applicationReserved = false,
            int? storageCost = null)
        {
            int bufferCost = storageCost ?? cost;
            long bytes = sending ? revision.SendBytes : revision.ReceiveBytes;
            long contextBytes = sending ? m_sendBytes : m_receiveBytes;
            int messages = sending ? flow.SendMessages : flow.ReceiveMessages;
            int contextMessages = sending ? m_sendMessages : m_receiveMessages;
            if (cost > revision.Parameters.MaxReceiveBytes - bytes ||
                bufferCost > m_options.MaxContextBytes - contextBytes ||
                messages >= m_options.MaxQueueMessages ||
                contextMessages >= m_options.MaxContextMessages ||
                (!applicationReserved && !m_budget.TryReserveMessage(bufferCost)))
            {
                return false;
            }
            if (sending)
            {
                revision.SendBytes += cost;
                flow.SendBytes += cost;
                flow.SendMessages++;
                m_sendBytes += bufferCost;
                m_sendMessages++;
            }
            else
            {
                revision.ReceiveBytes += cost;
                flow.ReceiveBytes += cost;
                flow.ReceiveMessages++;
                m_receiveBytes += bufferCost;
                m_receiveMessages++;
            }
            return true;
        }

        /// <summary>
        /// Releases one exact DATA reservation, including an application-held old revision.
        /// </summary>
        private void ReleaseMessageLocked(
            FlowState flow,
            uint revisionNumber,
            int cost,
            bool sending,
            int? storageCost = null)
        {
            int bufferCost = storageCost ?? cost;
            FlowRevision revision = flow.Revisions[revisionNumber];
            if (sending)
            {
                revision.SendBytes -= cost;
                flow.SendBytes -= cost;
                flow.SendMessages--;
                m_sendBytes -= bufferCost;
                m_sendMessages--;
            }
            else
            {
                revision.ReceiveBytes -= cost;
                flow.ReceiveBytes -= cost;
                flow.ReceiveMessages--;
                m_receiveBytes -= bufferCost;
                m_receiveMessages--;
            }
            m_budget.Release(FlowResource.BufferedBytes, bufferCost);
            m_budget.Release(FlowResource.Message, 1);
            RetireRevisionsLocked(flow);
            PulseCapacityLocked();
            if (!sending && UsesFlowCredit(flow) && flow.Ready && !flow.IsTerminal)
            {
                GrantCreditLocked(flow);
            }
            WakeLocked();
        }

        /// <summary>
        /// Releases application ownership without racing closure or a revision transition.
        /// </summary>
        private void ReleaseReceived(FlowState flow, uint revision, int cost, int storageCost)
        {
            lock (m_gate)
            {
                ReleaseMessageLocked(flow, revision, cost, false, storageCost);
            }
        }

        /// <summary>
        /// Wakes capacity waiters after state or admission changes.
        /// </summary>
        private void PulseCapacityLocked()
        {
            TaskSignal previous = m_capacityChanged;
            m_capacityChanged = new TaskSignal();
            previous.Complete();
        }

        /// <summary>
        /// Wakes a bounded single scheduler notification.
        /// </summary>
        private void WakeLocked()
        {
            m_work.Complete();
        }

        /// <summary>
        /// Wakes a scheduler from a caller cancellation callback.
        /// </summary>
        private void Wake()
        {
            lock (m_gate)
            {
                WakeLocked();
            }
        }

        /// <summary>
        /// Captures bounded model notifications without calling application code under synchronization.
        /// </summary>
        private void NotifyStateLocked(FlowState flow)
        {
            if (flow.Facade != null && (StateChanged != null || flow.Facade.HasStateObservers))
            {
                if (m_stateNotifications.Count >= m_options.MaxPendingControls)
                {
                    m_notificationOverflow = true;
                }
                else
                {
                    m_stateNotifications.Enqueue(new DataChannelStateChangedEventArgs(flow.Facade, StatusLocked(flow)));
                }
            }
            m_logger.FlowStateChanged(flow.Id, flow.State, flow.Result);
            WakeLocked();
        }

        /// <summary>
        /// Ends a flow without reclaiming carrier-owned or application-owned message storage.
        /// </summary>
        private void TerminateFlowLocked(FlowState flow, DataChannelResult result, bool discardReceived = true)
        {
            if (flow.IsTerminal)
            {
                return;
            }
            flow.State = result == DataChannelResult.Good ? DataChannelState.Closed : DataChannelState.Faulted;
            flow.Result = result;
            flow.LocalEnded = true;
            flow.RemoteEnded = true;
            flow.Ready = false;
            flow.UascStreamingPublisherIds = default;
            m_directAssociations.Retire(flow);
            if (m_pings.Remove(flow.Id, out FlowPing? ping))
            {
                m_budget.Release(FlowResource.Probe, 1);
                ping.Completion.TrySetResult(DataChannelResult.Closed);
            }
            m_retiredRevocations.Enqueue(flow.RevocationRegistration);
            flow.RevocationRegistration = default;
            flow.RevocationToken = default;
            if (flow.OpeningOperationReserved)
            {
                flow.OpeningOperationReserved = false;
                m_budget.Release(FlowResource.Operation, 1);
            }
            if (flow.Update is { } update)
            {
                update.Completed = true;
                update.Result = result;
                update.Completion.TrySetResult(result);
                ReleaseUpdateOperationLocked(update);
                flow.Update = null;
            }
            foreach (FlowRevision revision in flow.Revisions.Values)
            {
                revision.Pending = false;
                revision.SendActive = false;
                revision.ReceiveActive = false;
                revision.Grace = false;
            }
            while (flow.Outgoing.First is { } first)
            {
                flow.Outgoing.RemoveFirst();
                ReleaseWriteLocked(first.Value, result == DataChannelResult.Good ? DataChannelResult.Closed : result);
            }
            if (flow.Barrier != null)
            {
                ReleaseWriteLocked(flow.Barrier, result);
                flow.Barrier = null;
            }
            if (discardReceived)
            {
                while (flow.Incoming.Count != 0)
                {
                    flow.Incoming.Dequeue().Dispose();
                }
            }
            if (flow.FlowReserved)
            {
                flow.FlowReserved = false;
                m_budget.Release(FlowResource.Flow, 1);
            }
            if (flow.StreamReserved && m_streams.Remove(flow.StreamId, out FlowStreamInput? input))
            {
                ReleaseStreamInputLocked(input, result);
            }
            m_flows.Remove(flow.Id);
            flow.Opened.TrySetResult(result == DataChannelResult.Good ? DataChannelResult.Closed : result);
            flow.Ended.TrySetResult(result);
            flow.ReadChanged.Complete();
            RetireRevisionsLocked(flow);
            NotifyStateLocked(flow);
            PulseCapacityLocked();
        }

        /// <summary>
        /// Releases only context-level accounting; retained message leases remain charged.
        /// </summary>
        private void ReleaseContextReservationLocked()
        {
            m_directAssociations.Clear();
            m_controlHeadroom?.Dispose();
            if (m_contextReserved)
            {
                m_contextReserved = false;
                m_budget.Release(FlowResource.Context, 1);
            }
            if (m_managementStreamReserved)
            {
                m_managementStreamReserved = false;
                m_budget.Release(FlowResource.Stream, 1);
            }
        }

        /// <summary>
        /// Faults all context-owned state immediately, independently of asynchronous transport shutdown.
        /// </summary>
        private void CloseLocked(DataChannelResult result)
        {
            if (m_closed)
            {
                return;
            }
            m_closed = true;
            m_closeResult = result;
            m_closedNotification = true;
            m_uascPublisherRoutes.Clear();
            foreach (FlowState flow in m_flows.Values.Concat(m_releasingFlows.Values).Distinct().ToArray())
            {
                DataChannelResult flowResult = result == DataChannelResult.Good
                    ? DataChannelResult.Closed : result;
                if (flow.State == DataChannelState.Closed && result != DataChannelResult.Good)
                {
                    flow.State = DataChannelState.Faulted;
                    flow.Result = flowResult;
                    flow.Ended.TrySetResult(flowResult);
                    flow.ReadChanged.Complete();
                    NotifyStateLocked(flow);
                }
                else
                {
                    TerminateFlowLocked(flow, flowResult);
                }
                QueueFlowReleaseLocked(flow, Task.CompletedTask, result);
            }
            while (m_management.Count != 0)
            {
                ReleaseWriteLocked(m_management.Dequeue(), DataChannelResult.Closed);
            }
            while (m_uascIngress.Count != 0)
            {
                ReleaseUascIngressLocked(m_uascIngress.Dequeue());
            }
            foreach (FlowPing ping in m_pings.Values)
            {
                ping.Completion.TrySetResult(DataChannelResult.Closed);
                m_budget.Release(FlowResource.Probe, 1);
            }
            m_pings.Clear();
            foreach (FlowStreamInput input in m_streams.Values)
            {
                ReleaseStreamInputLocked(input, result);
            }
            m_streams.Clear();
            m_budget.Release(FlowResource.Flow, m_reservations.Count);
            m_budget.Release(FlowResource.Operation, m_reservations.Count);
            m_reservations.Clear();
            m_accepted.Clear();
            ReleaseContextReservationLocked();
            m_ready.TrySetResult(result == DataChannelResult.Good ? DataChannelResult.Closed : result);
            CompleteDirectBindingLocked(result == DataChannelResult.Good ? DataChannelResult.Closed : result);
            m_acceptChanged.Complete();
            PulseCapacityLocked();
            m_timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            WakeLocked();
        }

        /// <summary>
        /// Allows an existing carrier's synchronous close notification to retire state immediately.
        /// </summary>
        internal void CarrierClosed(DataChannelResult result)
        {
            lock (m_gate)
            {
                m_carrierCloseStarted = true;
                CloseLocked(result);
            }
            CancelShutdown();
        }

        /// <summary>
        /// Closes an invalid context and requests real carrier shutdown.
        /// </summary>
        private async ValueTask CloseCoreAsync(DataChannelResult result, CancellationToken cancellationToken)
        {
            bool closeCarrier;
            lock (m_gate)
            {
                CloseLocked(result);
                closeCarrier = !m_carrierCloseStarted;
                m_carrierCloseStarted = true;
            }
            CancelShutdown();
            if (closeCarrier)
            {
                try
                {
                    if (IsNative)
                    {
                        await WaitForFlowReleasesAsync().ConfigureAwait(false);
                    }
                    await m_carrier.CloseAsync(result, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    m_logger.FlowCarrierFailure(exception);
                }
            }
        }

        /// <summary>
        /// Keeps a native association alive until its already-owned per-stream release operations finish.
        /// </summary>
        private async Task WaitForFlowReleasesAsync()
        {
            while (true)
            {
                Task wait;
                lock (m_gate)
                {
                    if (m_pendingFlowReleases == 0)
                    {
                        return;
                    }
                    wait = m_capacityChanged.Task;
                }
                await wait.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Joins workers and actual writer ownership before disposing their coordination resources.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        private async ValueTask DisposeCoreAsync()
        {
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
            }
            await CloseCoreAsync(DataChannelResult.Closed, CancellationToken.None).ConfigureAwait(false);
            await m_scheduler.ConfigureAwait(false);
            await m_ingressTask.ConfigureAwait(false);
            if (m_directBindingTask != null)
            {
                await m_directBindingTask.ConfigureAwait(false);
            }
            if (m_directDatagramTask != null)
            {
                await m_directDatagramTask.ConfigureAwait(false);
            }
            while (true)
            {
                Task wait;
                lock (m_gate)
                {
                    if (m_activeWrites == 0 && m_activeAuthorizations == 0 && m_pendingFlowReleases == 0)
                    {
                        break;
                    }
                    wait = m_capacityChanged.Task;
                }
                await wait.ConfigureAwait(false);
            }
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                m_timer?.Dispose();
                m_shutdown.Dispose();
            }
            if (m_flowReleaseFailed)
            {
                throw new DataChannelException(DataChannelResult.Closed);
            }
        }

        /// <summary>
        /// Signals cancellation once; later duplicate close notifications cannot touch a disposed source.
        /// </summary>
        private void CancelShutdown()
        {
            if (Interlocked.Exchange(ref m_shutdownSignalled, 1) == 0)
            {
                m_shutdown.Cancel();
            }
        }

        /// <summary>
        /// Confirms that a composed carrier retains the selected profile and UASC-authenticated identity.
        /// </summary>
        internal bool UsesCarrierIdentity(DataChannelCarrierKind kind, DataChannelPeerIdentity identity)
        {
            return m_carrier.Kind == kind && ReferenceEquals(m_carrier.Identity, identity);
        }

        /// <summary>
        /// Checks that a context uses the exact carrier selected by its host.
        /// </summary>
        internal bool UsesCarrier(IDataChannelCarrier carrier)
        {
            return ReferenceEquals(m_carrier, carrier);
        }

        private readonly Lock m_gate = new();
        private static readonly ByteString s_zeroChallenge = new(new byte[32]);
        private static readonly FlowCodecLimits s_containerInspectionLimits =
            new(int.MaxValue - FlowRecordCodec.HeaderSize, int.MaxValue - FlowRecordCodec.HeaderSize, int.MaxValue);

        private readonly CancellationTokenSource m_shutdown = new();
        private readonly Dictionary<uint, FlowState> m_flows = [];
        private readonly Dictionary<uint, FlowState> m_releasingFlows = [];
        private readonly Dictionary<uint, long> m_reservations = [];
        private readonly Dictionary<ulong, FlowStreamInput> m_streams = [];
        private readonly HashSet<ulong> m_usedStreams = [];
        private readonly Queue<DataChannel> m_accepted = [];
        private readonly Queue<FlowWriteItem> m_management = [];
        private readonly Queue<DataChannelStateChangedEventArgs> m_stateNotifications = [];
        private readonly Queue<CancellationTokenRegistration> m_retiredRevocations = [];
        private readonly Dictionary<uint, FlowPing> m_pings = [];
        private readonly List<UascPublisherRoute> m_uascPublisherRoutes = [];
        private readonly TaskCompletionSource<DataChannelResult> m_ready = FlowState.NewCompletion();
        private TaskSignal m_work = new();
        private TaskSignal m_capacityChanged = new();
        private TaskSignal m_acceptChanged = new();
        private Task m_scheduler = Task.CompletedTask;
        private ITimer? m_timer;
        private bool m_timerArmed;
        private long m_timerStartedAt;
        private TimeSpan m_timerDelay;
        private DataChannelContextIdentity? m_identity;
        private ByteString m_localChallenge;
        private ByteString m_peerChallenge;
        private Guid m_contextId;
        private ulong m_generation;
        private ulong m_nextChannelId;
        private uint m_peerChannelHighWater;
        private uint m_localControlId;
        private uint m_peerControlId;
        private uint m_peerMaxMessageSize;
        private uint m_peerMaxReceiveBytes;
        private uint m_peerMaxFlows;
        private uint m_sendConnectionCredit;
        private uint m_receiveConnectionCredit;
        private uint m_lastConnectionGrant;
        private long m_reservedSendBytes;
        private long m_reservedReceiveBytes;
        private long m_sendBytes;
        private long m_receiveBytes;
        private DataChannelApplicationBudget.ControlHeadroom? m_controlHeadroom;
        private long m_startedAt;
        private long m_lastPingAt;
        private long m_lastPeerPingAt;
        private long m_lastErrorAt;
        private ulong m_nextPingTimestamp;
        private int m_sendMessages;
        private int m_receiveMessages;
        private int m_pendingControls;
        private int m_activeWrites;
        private int m_activeAuthorizations;
        private int m_roundRobin;
        private int m_provisionalStreams;
        private int m_pendingFlowReleases;
        private bool m_flowReleaseFailed;
        private int m_shutdownSignalled;
        private bool m_contextReserved;
        private bool m_managementStreamReserved;
        private bool m_started;
        private bool m_helloSent;
        private bool m_helloReceived;
        private bool m_managementBusy;
        private bool m_closed;
        private bool m_disposed;
        private bool m_carrierCloseStarted;
        private bool m_closedNotification;
        private bool m_notificationOverflow;
        private bool m_pingSent;
        private bool m_peerPingReceived;
        private bool m_errorSent;
        private bool m_replenishConnectionCredit;
        private DataChannelResult m_closeResult;
    }
}
