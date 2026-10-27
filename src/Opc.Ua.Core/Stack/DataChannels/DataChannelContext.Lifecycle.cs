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
        /// Waits for an owned complete message or remote half-close, never returning a partial record.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        internal async ValueTask<DataChannelMessage?> ReadAsync(
            DataChannel channel,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                Task wait;
                lock (m_gate)
                {
                    FlowState flow = channel.StateData;
                    if (!flow.MayReceive)
                    {
                        throw new DataChannelException(DataChannelResult.InvalidState);
                    }
                    if (flow.Incoming.Count != 0)
                    {
                        return flow.Incoming.Dequeue();
                    }
                    if (flow.State == DataChannelState.Faulted)
                    {
                        throw new DataChannelException(flow.Result);
                    }
                    if (flow.RemoteEnded || flow.State == DataChannelState.Closed || m_closed)
                    {
                        return null;
                    }
                    wait = flow.ReadChanged.Task;
                }
                await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Drains the local direction before placing END on that same ordered path.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        internal async ValueTask<DataChannelResult> EndAsync(DataChannel channel, CancellationToken cancellationToken)
        {
            FlowState flow = channel.StateData;
            lock (m_gate)
            {
                if (flow.IsTerminal)
                {
                    return flow.Result;
                }
                if (!flow.Ready)
                {
                    return DataChannelResult.InvalidState;
                }
                if (flow.Update is { } pending)
                {
                    if (pending.Committed)
                    {
                        return DataChannelResult.WouldBlock;
                    }
                    QueueControlLocked(
                        new FlowSignal(FlowOperation.AbortUpdate, flow.Id),
                        pending.Revision, pending.Id, DataChannelPath.Management, flow: flow);
                    AbortUpdateLocked(flow, pending, DataChannelResult.Closed);
                }
                if (flow.LocalEnded)
                {
                    return DataChannelResult.Good;
                }
                if (!flow.Draining)
                {
                    flow.Draining = true;
                    flow.DrainStarted = m_clock.GetTimestamp();
                    flow.State = DataChannelState.Draining;
                    QueueControlLocked(
                        new FlowSignal(FlowOperation.End, flow.Id),
                        flow.SendRevision,
                        0,
                        DataChannelPath.Flow,
                        () =>
                        {
                            if (flow.IsTerminal)
                            {
                                return DataChannelResult.Closed;
                            }
                            flow.LocalEnded = true;
                            flow.Draining = false;
                            flow.Ended.TrySetResult(DataChannelResult.Good);
                            CheckEndedLocked(flow);
                            return DataChannelResult.Good;
                        },
                        flow);
                    NotifyStateLocked(flow);
                    ArmTimerLocked();
                }
            }
            DataChannelResult result = await flow.Ended.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            Task<DataChannelResult>? release;
            lock (m_gate)
            {
                release = flow.ReleaseStarted ? flow.Released.Task : null;
            }
            if (release != null)
            {
                DataChannelResult released = await release.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (released != DataChannelResult.Good)
                {
                    throw new DataChannelException(released);
                }
            }
            return result;
        }

        /// <summary>
        /// Closes only once both application directions have ended.
        /// </summary>
        private void CheckEndedLocked(FlowState flow)
        {
            if (flow.LocalEnded && flow.RemoteEnded)
            {
                TerminateFlowLocked(flow, DataChannelResult.Good, false);
                QueueFlowReleaseLocked(flow, flow.ActiveWrite?.Completion.Task ?? Task.CompletedTask,
                    DataChannelResult.Good,
                    interruptNative: false,
                    finishNativeStream: IsNative && flow.StreamReserved && flow.DirectLane == null);
            }
            else
            {
                flow.State = flow.Draining ? DataChannelState.Draining :
                    flow.Update != null ? DataChannelState.Updating : DataChannelState.Open;
                NotifyStateLocked(flow);
            }
        }

        /// <summary>
        /// Requests immediate per-flow reset without waiting for data credit or the application.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        internal async ValueTask ResetAsync(
            DataChannel channel,
            DataChannelResult result,
            CancellationToken cancellationToken)
        {
            Task<DataChannelResult>? sent;
            lock (m_gate)
            {
                FlowState flow = channel.StateData;
                if (flow.IsTerminal)
                {
                    sent = flow.ReleaseStarted ? flow.Released.Task : Task.FromResult(DataChannelResult.Good);
                }
                else
                {
                    sent = QueueResetLocked(flow, result);
                }
            }
            DataChannelResult released = await sent.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (released != DataChannelResult.Good)
            {
                throw new DataChannelException(released);
            }
        }

        /// <summary>
        /// Emits the scoped reset and retires local state synchronously.
        /// </summary>
        private DataChannelResult ResetLocked(FlowState flow, DataChannelResult result)
        {
            if (!flow.IsTerminal)
            {
                QueueResetLocked(flow, result);
            }
            return result;
        }

        /// <summary>
        /// Places RESET ahead of blocked data and releases only unsecured queue ownership.
        /// </summary>
        private Task<DataChannelResult> QueueResetLocked(FlowState flow, DataChannelResult result)
        {
            if (flow.ResetSent)
            {
                return flow.Released.Task;
            }
            flow.ResetSent = true;
            bool interruptNative = flow.ActiveWrite?.Committed == true;
            bool finishNativeStream = !interruptNative &&
                flow.Ready &&
                IsNative &&
                flow.StreamReserved &&
                flow.DirectLane == null;
            Task<DataChannelResult> sent = QueueControlLocked(
                new FlowOutcome(FlowOperation.Reset, flow.Id, FlowModel.ToWire(result)),
                flow.SendRevision,
                0,
                DataChannelPath.Management,
                flow: flow,
                urgent: true);
            TerminateFlowLocked(flow, result);
            return QueueFlowReleaseLocked(
                flow,
                sent,
                result,
                interruptNative: interruptNative || !finishNativeStream,
                finishNativeStream: finishNativeStream);
        }

        /// <summary>
        /// Closes the whole context when an orderly native stream does not complete its FIN exchange in time.
        /// </summary>
        private async ValueTask CloseContextAfterDrainTimeoutAsync()
        {
            bool closeCarrier;
            lock (m_gate)
            {
                CloseLocked(DataChannelResult.Timeout);
                closeCarrier = !m_carrierCloseStarted;
                m_carrierCloseStarted = true;
            }
            CancelShutdown();
            if (closeCarrier)
            {
                try
                {
                    await m_carrier.CloseAsync(DataChannelResult.Timeout, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    m_logger.FlowCarrierFailure(exception);
                }
            }
        }

        /// <summary>
        /// Requests only per-flow native shutdown; UASC must preserve unrelated Services.
        /// </summary>
        private async Task CloseFlowTransportAsync(
            ulong stream,
            DataChannelResult result,
            FlowStreamInput? input = null)
        {
            if (!IsNative || stream == m_carrier.ManagementStreamId)
            {
                return;
            }
            lock (m_gate)
            {
                m_pendingFlowReleases++;
            }
            await Task.Yield();
            try
            {
                await m_carrier.CloseFlowAsync(stream, result, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                m_logger.FlowCarrierFailure(exception);
                lock (m_gate)
                {
                    m_flowReleaseFailed = true;
                }
            }
            finally
            {
                lock (m_gate)
                {
                    if (input?.PendingStreamRelease == true)
                    {
                        input.PendingStreamRelease = false;
                        m_budget.Release(FlowResource.Stream, 1);
                    }
                    m_pendingFlowReleases--;
                    PulseCapacityLocked();
                }
            }
        }

        /// <summary>
        /// Sends a rate-limited error for a well-framed unknown/invalid flow without allocating one.
        /// </summary>
        private DataChannelResult RefuseLocked(uint channelId, uint revision, DataChannelResult result)
        {
            if (channelId != 0 && (!m_errorSent || m_clock.GetElapsedTime(m_lastErrorAt) >= TimeSpan.FromSeconds(1)))
            {
                m_errorSent = true;
                m_lastErrorAt = m_clock.GetTimestamp();
                QueueControlLocked(
                    new FlowOutcome(FlowOperation.Reset, channelId, FlowModel.ToWire(result)),
                    Math.Max(1u, revision),
                    0,
                    DataChannelPath.Management,
                    urgent: true);
            }
            return result;
        }

        /// <summary>
        /// Admits one timestamp probe per flow, at most eight per context and one per second.
        /// </summary>
        internal async ValueTask<DataChannelResult> PingAsync(uint channelId, CancellationToken cancellationToken)
        {
            FlowPing ping;
            lock (m_gate)
            {
                RequireReadyLocked();
                FlowState? flow = null;
                if (channelId != 0 && (!m_flows.TryGetValue(channelId, out flow) || !flow.Ready))
                {
                    return DataChannelResult.UnknownChannel;
                }
                if (m_pings.Count >= 8 ||
                    m_pings.ContainsKey(channelId) ||
                    (m_pingSent && m_clock.GetElapsedTime(m_lastPingAt) < TimeSpan.FromSeconds(1)) ||
                    m_nextPingTimestamp == ulong.MaxValue ||
                    !m_budget.TryReserve(FlowResource.Probe, 1))
                {
                    return DataChannelResult.WouldBlock;
                }
                m_pingSent = true;
                m_lastPingAt = m_clock.GetTimestamp();
                ping = new FlowPing(channelId, ++m_nextPingTimestamp, m_lastPingAt);
                m_pings.Add(channelId, ping);
                QueueControlLocked(
                    new FlowProbe(FlowOperation.Ping, channelId, ping.Timestamp),
                    flow?.SendRevision ?? 0,
                    0,
                    DataChannelPath.Management,
                    flow: flow);
                ArmTimerLocked();
            }
            return await ping.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs bounded provider work with a monotonic deadline and retains its accounting until it exits.
        /// </summary>
        private async ValueTask<DataChannelAuthorization> AuthorizeAsync(
            FlowState flow,
            DataChannelAuthorizationPurpose purpose,
            DataChannelParametersDataType parameters,
            uint revision,
            uint operationId,
            CancellationToken cancellationToken)
        {
            DataChannelAuthorizationRequest request;
            lock (m_gate)
            {
                if (m_closed ||
                    flow.IsTerminal ||
                    m_identity == null ||
                    !m_budget.TryReserve(FlowResource.Operation, 1))
                {
                    return new DataChannelAuthorization(DataChannelResult.WouldBlock);
                }
                m_activeAuthorizations++;
                request = new DataChannelAuthorizationRequest(
                    m_identity, flow.Id, flow.IsLocalOpener, flow.Authorization, flow.Grant,
                    purpose, revision, operationId, parameters);
            }
            CancellationTokenSource timeout = m_clock.CreateCancellationTokenSource(m_options.OpenTimeout);
            var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, m_shutdown.Token, timeout.Token);
            Task<DataChannelAuthorization>? task = null;
            try
            {
                task = m_authorizer.AuthorizeAsync(request, linked.Token).AsTask();
                return await task.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new DataChannelAuthorization(timeout.IsCancellationRequested
                    ? DataChannelResult.Timeout : DataChannelResult.NotAuthorized);
            }
            catch (Exception exception)
            {
                m_logger.FlowAuthorizationFailure(exception);
                return new DataChannelAuthorization(DataChannelResult.NotAuthorized);
            }
            finally
            {
                if (task != null && !task.IsCompleted)
                {
                    _ = FinishAuthorizationAsync(task, linked, timeout);
                }
                else
                {
                    FinishAuthorization(linked, timeout);
                }
            }
        }

        /// <summary>
        /// Observes late provider completion without leaking exceptions or releasing its operation early.
        /// </summary>
        private async Task FinishAuthorizationAsync(
            Task<DataChannelAuthorization> task,
            CancellationTokenSource linked,
            CancellationTokenSource timeout)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                m_logger.FlowAuthorizationFailure(exception);
            }
            finally
            {
                FinishAuthorization(linked, timeout);
            }
        }

        /// <summary>
        /// Releases exactly one provider operation and its cancellation timers.
        /// </summary>
        private void FinishAuthorization(CancellationTokenSource linked, CancellationTokenSource timeout)
        {
            linked.Dispose();
            timeout.Dispose();
            lock (m_gate)
            {
                m_activeAuthorizations--;
                m_budget.Release(FlowResource.Operation, 1);
                PulseCapacityLocked();
            }
        }

        /// <summary>
        /// Rechecks affected flows immediately while stopping new unsecured send admission.
        /// </summary>
        private async ValueTask RecheckCoreAsync(CancellationToken cancellationToken)
        {
            FlowState[] flows;
            lock (m_gate)
            {
                flows = [.. m_flows.Values.Where(flow => flow.Ready && !flow.AuthorizationChecking)];
                foreach (FlowState flow in flows)
                {
                    flow.AuthorizationChecking = true;
                }
            }
            foreach (FlowState flow in flows)
            {
                await RecheckFlowAsync(flow, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Revalidates permission without redeeming a single-use Session grant again.
        /// </summary>
        private async Task RecheckFlowAsync(FlowState flow, CancellationToken cancellationToken)
        {
            uint revision;
            DataChannelParametersDataType parameters;
            lock (m_gate)
            {
                revision = flow.Revision;
                parameters = flow.Parameters;
            }
            DataChannelAuthorization result = await AuthorizeAsync(
                flow, DataChannelAuthorizationPurpose.Recheck,
                parameters, revision, 0, cancellationToken).ConfigureAwait(false);
            lock (m_gate)
            {
                flow.AuthorizationChecking = false;
                if (flow.IsTerminal || m_closed)
                {
                    return;
                }
                if (result.Result != DataChannelResult.Good ||
                    result.Parameters == null ||
                    result.RevocationToken.IsCancellationRequested ||
                    !FlowModel.SameParameters(parameters, result.Parameters))
                {
                    ResetLocked(flow, DataChannelResult.NotAuthorized);
                }
                else if (flow.Revision != revision)
                {
                    flow.RecheckRequested = true;
                }
                else
                {
                    flow.AuthorizedAt = m_clock.GetTimestamp();
                    flow.LastAuthorizationCheck = flow.AuthorizedAt;
                    flow.AuthorizationLifetime = result.Lifetime;
                    InstallRevocationLocked(flow, result.RevocationToken);
                }
                PulseCapacityLocked();
                WakeLocked();
                ArmTimerLocked();
            }
        }

        /// <summary>
        /// Pauses admission while evidence is being refreshed, without treating a still-live
        /// authorizing Session as closed merely because its previous evidence reached its deadline.
        /// </summary>
        private bool AuthorizationPendingLocked(FlowState flow)
        {
            if (flow.AuthorizationChecking || flow.RecheckRequested)
            {
                return true;
            }
            if (flow.AuthorizationLifetime.HasValue &&
                m_clock.GetElapsedTime(flow.AuthorizedAt) >= flow.AuthorizationLifetime.Value)
            {
                flow.RecheckRequested = true;
                WakeLocked();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Installs immediate revocation while deferring old registration disposal outside state synchronization.
        /// </summary>
        private void InstallRevocationLocked(FlowState flow, CancellationToken token)
        {
            if (flow.RevocationToken == token)
            {
                return;
            }
            m_retiredRevocations.Enqueue(flow.RevocationRegistration);
            flow.RevocationToken = token;
            flow.RevocationRegistration = token.CanBeCanceled ? token.Register(Wake) : default;
            WakeLocked();
        }

        /// <summary>
        /// Applies every deadline using the injected monotonic clock, including exact boundary equality.
        /// </summary>
        private void OnTimer()
        {
            List<FlowState> recheck = [];
            lock (m_gate)
            {
                m_timerArmed = false;
                if (m_closed)
                {
                    return;
                }
                if (m_started && m_identity == null && m_clock.GetElapsedTime(m_startedAt) >= m_options.OpenTimeout)
                {
                    CloseLocked(DataChannelResult.Timeout);
                }
                foreach (uint id in m_reservations.Where(
                    item => m_clock.GetElapsedTime(item.Value) >= m_options.OpenTimeout)
                    .Select(item => item.Key).ToArray())
                {
                    m_reservations.Remove(id);
                    m_budget.Release(FlowResource.Flow, 1);
                    m_budget.Release(FlowResource.Operation, 1);
                }
                foreach (FlowState flow in m_flows.Values.ToArray())
                {
                    if (!flow.Ready && m_clock.GetElapsedTime(flow.OpenStarted) >= m_options.OpenTimeout)
                    {
                        ResetLocked(flow, DataChannelResult.Timeout);
                        continue;
                    }
                    if (flow.Draining && m_clock.GetElapsedTime(flow.DrainStarted) >= m_options.DrainTimeout)
                    {
                        if (IsNative && flow.StreamReserved && flow.DirectLane == null)
                        {
                            CloseLocked(DataChannelResult.Timeout);
                            break;
                        }
                        ResetLocked(flow, DataChannelResult.Timeout);
                        continue;
                    }
                    if (flow.PreviousReceiveRevision is { Grace: true } previous &&
                        m_clock.GetElapsedTime(previous.GraceStarted) >= m_options.DatagramRevisionGrace)
                    {
                        previous.Grace = false;
                        flow.PreviousReceiveRevision = null;
                        RetireRevisionsLocked(flow);
                    }
                    CheckUpdateTimeoutLocked(flow);
                    RemoveExpiredLocked(flow);
                    if ((flow.Ready || flow.Revisions.Count != 0) &&
                        !flow.AuthorizationChecking &&
                        (m_clock.GetElapsedTime(flow.LastAuthorizationCheck) >=
                        m_options.AuthorizationRecheckInterval ||
                            (flow.AuthorizationLifetime.HasValue &&
                                m_clock.GetElapsedTime(flow.AuthorizedAt) >= flow.AuthorizationLifetime.Value)))
                    {
                        flow.AuthorizationChecking = true;
                        flow.RecheckRequested = false;
                        recheck.Add(flow);
                    }
                }
                foreach (ulong stream in m_streams.Where(
                    item => item.Value.Provisional &&
                        m_clock.GetElapsedTime(item.Value.Started) >= m_options.OpenTimeout)
                    .Select(item => item.Key).ToArray())
                {
                    ReleaseStreamInputLocked(m_streams[stream], DataChannelResult.Timeout);
                    m_streams.Remove(stream);
                }
                foreach (uint id in m_pings.Where(
                    item => m_clock.GetElapsedTime(item.Value.Started) >= m_options.PingTimeout)
                    .Select(item => item.Key).ToArray())
                {
                    FlowPing ping = m_pings[id];
                    m_pings.Remove(id);
                    m_budget.Release(FlowResource.Probe, 1);
                    ping.Completion.TrySetResult(DataChannelResult.Timeout);
                }
                WakeLocked();
                ArmTimerLocked();
            }
            foreach (FlowState flow in recheck)
            {
                _ = RecheckFlowAsync(flow, CancellationToken.None);
            }
        }

        /// <summary>
        /// Arms the earliest real deadline instead of using wall-clock polling or per-message timers.
        /// </summary>
        private void ArmTimerLocked()
        {
            if (m_closed || m_timer == null)
            {
                return;
            }
            // A clock read may synchronously deliver timers. Do it before enumerating mutable state.
            long now = m_clock.GetTimestamp();
            if (m_closed)
            {
                return;
            }
            TimeSpan? due = null;
            void Include(long start, TimeSpan duration)
            {
                TimeSpan remaining = duration - m_clock.GetElapsedTime(start, now);
                if (!due.HasValue || remaining < due.Value)
                {
                    due = remaining;
                }
            }
            if (m_started && m_identity == null)
            {
                Include(m_startedAt, m_options.OpenTimeout);
            }
            foreach (long reservation in m_reservations.Values)
            {
                Include(reservation, m_options.OpenTimeout);
            }
            foreach (FlowState flow in m_flows.Values)
            {
                if (!flow.Ready)
                {
                    Include(flow.OpenStarted, m_options.OpenTimeout);
                }
                if (flow.Draining)
                {
                    Include(flow.DrainStarted, m_options.DrainTimeout);
                }
                if (flow.Ready && !flow.AuthorizationChecking)
                {
                    Include(flow.LastAuthorizationCheck, m_options.AuthorizationRecheckInterval);
                }
                if (flow.AuthorizationLifetime.HasValue && !flow.AuthorizationChecking)
                {
                    Include(flow.AuthorizedAt, flow.AuthorizationLifetime.Value);
                }
                if (flow.Update is { } update)
                {
                    Include(update.Started, m_options.UpdateTimeout);
                }
                if (flow.PreviousReceiveRevision is { Grace: true } previous)
                {
                    Include(previous.GraceStarted, m_options.DatagramRevisionGrace);
                }
                foreach (FlowWriteItem item in flow.Outgoing)
                {
                    if (item.TimeToLive.HasValue)
                    {
                        Include(item.QueuedAt, item.TimeToLive.Value);
                    }
                }
                if (!flow.SendBusy && flow.Outgoing.First?.Value is { IsData: true } head)
                {
                    TimeSpan rateDelay = flow.SendRate.Delay(head.Cost, m_clock, now);
                    if (rateDelay > TimeSpan.Zero && (!due.HasValue || rateDelay < due.Value))
                    {
                        due = rateDelay;
                    }
                }
            }
            foreach (FlowPing ping in m_pings.Values)
            {
                Include(ping.Started, m_options.PingTimeout);
            }
            foreach (FlowStreamInput input in m_streams.Values)
            {
                if (input.Provisional)
                {
                    Include(input.Started, m_options.OpenTimeout);
                }
            }
            if (!due.HasValue)
            {
                m_timerArmed = false;
                m_timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                return;
            }
            TimeSpan delay = due.Value > TimeSpan.Zero ? due.Value : TimeSpan.FromTicks(1);
            // Do not postpone an earlier wake-up by rearming a relative timer while the clock advances.
            if (m_timerArmed && delay >= m_timerDelay - m_clock.GetElapsedTime(m_timerStartedAt, now))
            {
                return;
            }
            m_timerArmed = true;
            m_timerStartedAt = now;
            m_timerDelay = delay;
            m_timer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }
}
