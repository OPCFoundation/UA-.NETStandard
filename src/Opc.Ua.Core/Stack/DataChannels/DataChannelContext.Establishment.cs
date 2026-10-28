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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels.Wire;

namespace Opc.Ua.DataChannels
{
    public sealed partial class DataChannelContext
    {
        /// <summary>
        /// Starts the protected confirmation, with the context initiator sending first.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        private async ValueTask StartCoreAsync(CancellationToken cancellationToken)
        {
            lock (m_gate)
            {
                ThrowIfClosedLocked();
                if (!m_started)
                {
                    m_started = true;
                    m_startedAt = m_clock.GetTimestamp();
                    if (m_carrier.Identity.IsContextInitiator)
                    {
                        QueueHelloLocked();
                    }
                    ArmTimerLocked();
                }
            }
            DataChannelResult result = await m_ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (result != DataChannelResult.Good)
            {
                throw new DataChannelException(result);
            }
        }

        /// <summary>
        /// Queues the local HELLO, with no implication that a resource has been authorized.
        /// </summary>
        private void QueueHelloLocked()
        {
            var hello = new FlowHello(
                1,
                m_wireCarrier,
                m_carrier.Identity.LocalApplicationUri,
                m_contextId,
                m_generation,
                m_localChallenge,
                FlowModel.ToWire(m_carrier.Identity.Protection),
                (uint)m_options.MaxMessageSize,
                m_options.MaxContextBytes,
                (uint)m_options.MaxFlows);
            QueueControlLocked(hello, 0, 0, DataChannelPath.Management, () =>
            {
                m_helloSent = true;
                CompleteHelloLocked();
                return DataChannelResult.Good;
            });
        }

        /// <summary>
        /// Confirms the actual authenticated association and clamps untrusted advertisements.
        /// </summary>
        private DataChannelResult ReceiveHelloLocked(FlowHello hello, uint messageId)
        {
            if (m_helloReceived ||
                messageId <= m_peerControlId ||
                hello.Context == Guid.Empty ||
                hello.Generation == 0 ||
                hello.Application != m_carrier.Identity.PeerApplicationUri ||
                hello.Carrier != m_wireCarrier ||
                hello.Protection != FlowModel.ToWire(m_carrier.Identity.Protection) ||
                hello.Challenge == m_localChallenge ||
                hello.Challenge == s_zeroChallenge)
            {
                CloseLocked(DataChannelResult.SecurityFailure);
                return DataChannelResult.SecurityFailure;
            }
            if (m_carrier.Identity.IsContextInitiator)
            {
                if (!m_helloSent || hello.Context != m_contextId || hello.Generation != m_generation)
                {
                    CloseLocked(DataChannelResult.SecurityFailure);
                    return DataChannelResult.SecurityFailure;
                }
            }
            else
            {
                if (!m_budget.RegisterContext(hello.Context))
                {
                    CloseLocked(DataChannelResult.SecurityFailure);
                    return DataChannelResult.SecurityFailure;
                }
                m_contextId = hello.Context;
                m_generation = hello.Generation;
                if (!m_started)
                {
                    m_started = true;
                    m_startedAt = m_clock.GetTimestamp();
                }
            }
            m_peerControlId = messageId;
            m_peerChallenge = hello.Challenge.Copy();
            m_peerMaxMessageSize = Math.Min((uint)m_options.MaxMessageSize, hello.MaxMessageSize);
            m_peerMaxReceiveBytes = Math.Min(m_options.MaxContextBytes, hello.MaxReceiveBytes);
            m_peerMaxFlows = Math.Min((uint)m_options.MaxFlows, hello.MaxFlows);
            m_helloReceived = true;
            if (!m_carrier.Identity.IsContextInitiator)
            {
                QueueHelloLocked();
            }
            CompleteHelloLocked();
            ArmTimerLocked();
            return DataChannelResult.Good;
        }

        /// <summary>
        /// Publishes the confirmed immutable identity only when both HELLOs are protected.
        /// </summary>
        private void CompleteHelloLocked()
        {
            if (!m_helloSent || !m_helloReceived || m_identity != null || m_closed)
            {
                return;
            }
            m_identity = new DataChannelContextIdentity(
                m_contextId,
                m_generation,
                m_carrier.Identity,
                m_localChallenge,
                m_peerChallenge,
                m_carrier.Kind);
            m_lastVerifiedAt = m_clock.GetTimestamp();
            m_ready.TrySetResult(DataChannelResult.Good);
            StartDirectBindingLocked();
            PulseCapacityLocked();
        }

        /// <summary>
        /// Reserves a fresh identifier and bounded provisional bookkeeping before grant issuance.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        private uint ReserveChannelIdCore()
        {
            lock (m_gate)
            {
                RequireReadyLocked();
                if (m_flows.Count + m_reservations.Count + m_pendingFlowReleases >= m_peerMaxFlows ||
                    !m_budget.TryReserve(FlowResource.Flow, 1))
                {
                    throw new DataChannelException(DataChannelResult.WouldBlock);
                }
                if (!m_budget.TryReserve(FlowResource.Operation, 1))
                {
                    m_budget.Release(FlowResource.Flow, 1);
                    throw new DataChannelException(DataChannelResult.WouldBlock);
                }
                if (m_nextChannelId > uint.MaxValue)
                {
                    m_budget.Release(FlowResource.Flow, 1);
                    m_budget.Release(FlowResource.Operation, 1);
                    throw new DataChannelException(DataChannelResult.InvalidState);
                }
                uint id = (uint)m_nextChannelId;
                m_nextChannelId += 2;
                m_reservations.Add(id, m_clock.GetTimestamp());
                ArmTimerLocked();
                return id;
            }
        }

        /// <summary>
        /// Establishes a locally opened flow while keeping authorization and transport awaits outside state.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        private async ValueTask<DataChannel> OpenCoreAsync(
            DataChannelParametersDataType parameters,
            DataChannelAuthorizationMode authorization,
            Guid? grantId,
            uint channelId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDirectBindingProviderAvailable &&
                m_carrier is IDataChannelDirectStreamProvider { AreDirectStreamsAvailable: true })
            {
                using var deadline = new CancellationTokenSource();
                deadline.CancelAfter(m_options.OpenTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, deadline.Token);
                DataChannelResult binding;
                try
                {
                    binding = await WaitForDirectBindingAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (
                    deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                {
                    throw new DataChannelException(DataChannelResult.Timeout);
                }
                if (binding is not (DataChannelResult.Good or DataChannelResult.Unsupported))
                {
                    throw new DataChannelException(binding);
                }
            }
            DataChannelParametersDataType snapshot = FlowModel.Copy(parameters);
            FlowState flow;
            lock (m_gate)
            {
                RequireReadyLocked();
                ValidateAuthorization(authorization, grantId);
                snapshot = AdmitParametersLocked(snapshot, true);
                if (channelId == 0)
                {
                    channelId = ReserveChannelIdCore();
                }
                if (!m_reservations.Remove(channelId))
                {
                    throw new DataChannelException(DataChannelResult.InvalidArgument);
                }
                flow = CreateFlowLocked(channelId, true, snapshot, authorization, grantId);
            }
            try
            {
                DataChannelAuthorization admission = await AuthorizeAsync(
                    flow, DataChannelAuthorizationPurpose.Open, snapshot, 1, 0, cancellationToken)
                    .ConfigureAwait(false);
                lock (m_gate)
                {
                    ApplyOpeningAuthorizationLocked(flow, admission);
                }
                if (IsNative)
                {
                    await BindNativeAsync(flow, null, cancellationToken).ConfigureAwait(false);
                }
                lock (m_gate)
                {
                    EnsureOpeningLocked(flow);
                    QueueControlLocked(new FlowOpen(
                        flow.Id,
                        flow.StreamId,
                        FlowModel.ToWire(flow.Parameters),
                        authorization == DataChannelAuthorizationMode.Configured
                            ? FlowWireAuthorization.Configured
                            : FlowWireAuthorization.Session,
                        grantId), 1, 0,
                        IsNative ? DataChannelPath.Flow : DataChannelPath.Management, flow: flow);
                }
                DataChannelResult result = await flow.Opened.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (result != DataChannelResult.Good)
                {
                    throw new DataChannelException(result);
                }
                return flow.Facade!;
            }
            catch (Exception exception)
            {
                DataChannelResult result = exception is DataChannelException failure
                    ? failure.Result
                    : exception is OperationCanceledException
                        ? DataChannelResult.Closed
                        : DataChannelResult.InvalidState;
                await ResetAsync(flow.Facade!, result, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Handles a peer OPEN without allocating implicit state for arbitrary unknown records.
        /// </summary>
        private async ValueTask<DataChannelResult> ReceiveOpenAsync(
            FlowOpen open,
            uint messageId,
            CancellationToken cancellationToken)
        {
            FlowState flow;
            try
            {
                lock (m_gate)
                {
                    RequireReadyLocked();
                    if (m_flows.ContainsKey(open.Flow) || !RetirePeerIdLocked(open.Flow))
                    {
                        return RefuseLocked(open.Flow, 1, DataChannelResult.InvalidState, messageId);
                    }
                    DataChannelParametersDataType parameters = AdmitParametersLocked(
                        FlowModel.FromWire(open.Parameters), true);
                    if (m_flows.Count + m_reservations.Count + m_pendingFlowReleases >= m_peerMaxFlows ||
                        m_accepted.Count >= m_options.MaxFlows ||
                        !m_budget.TryReserve(FlowResource.Flow, 1))
                    {
                        return RefuseLocked(open.Flow, 1, DataChannelResult.WouldBlock, messageId);
                    }
                    if (!m_budget.TryReserve(FlowResource.Operation, 1))
                    {
                        m_budget.Release(FlowResource.Flow, 1);
                        return RefuseLocked(open.Flow, 1, DataChannelResult.WouldBlock, messageId);
                    }
                    flow = CreateFlowLocked(
                        open.Flow,
                        false,
                        parameters,
                        open.Authorization == FlowWireAuthorization.Configured
                            ? DataChannelAuthorizationMode.Configured
                            : DataChannelAuthorizationMode.Session,
                        open.Grant);
                    flow.PeerControlId = messageId;
                }
            }
            catch (DataChannelException exception)
            {
                lock (m_gate)
                {
                    return RefuseLocked(open.Flow, 1, exception.Result, messageId);
                }
            }
            try
            {
                DataChannelAuthorization admission = await AuthorizeAsync(
                    flow,
                    DataChannelAuthorizationPurpose.Open,
                    flow.Parameters,
                    1,
                    0,
                    cancellationToken).ConfigureAwait(false);
                lock (m_gate)
                {
                    ApplyOpeningAuthorizationLocked(flow, admission);
                }
                if (IsNative)
                {
                    await BindNativeAsync(flow, open.Stream, cancellationToken).ConfigureAwait(false);
                }
                lock (m_gate)
                {
                    EnsureOpeningLocked(flow);
                    QueueControlLocked(
                        new FlowAccept(flow.Id, flow.StreamId, FlowModel.ToWire(flow.Parameters)),
                        1,
                        0,
                        DataChannelPath.Management,
                        flow: flow);
                }
                if (flow.DirectLane != null)
                {
                    await SendDirectAssociationsAsync(flow).ConfigureAwait(false);
                }
                return DataChannelResult.Good;
            }
            catch (Exception exception)
            {
                DataChannelResult result = exception is DataChannelException failure
                    ? failure.Result
                    : DataChannelResult.NotAuthorized;
                await ResetAsync(flow.Facade!, result, CancellationToken.None, messageId).ConfigureAwait(false);
                return result;
            }
        }

        /// <summary>
        /// Monotonically retires a peer identifier, even if its subsequent admission is refused.
        /// </summary>
        private bool RetirePeerIdLocked(uint id)
        {
            uint peerParity = m_carrier.Identity.IsContextInitiator ? 0u : 1u;
            if (id == 0 || (id & 1) != peerParity || id <= m_peerChannelHighWater)
            {
                return false;
            }
            m_peerChannelHighWater = id;
            return true;
        }

        /// <summary>
        /// Recognizes retired identifiers from the monotonic namespaces without retaining per-flow tombstones.
        /// </summary>
        private bool IsRetiredChannelIdLocked(uint id)
        {
            if (id == 0 || m_flows.ContainsKey(id) || m_reservations.ContainsKey(id))
            {
                return false;
            }
            uint localParity = m_carrier.Identity.IsContextInitiator ? 1u : 0u;
            return (id & 1) == localParity ? id < m_nextChannelId : id <= m_peerChannelHighWater;
        }

        /// <summary>
        /// Creates bounded provisional state after identifier and accounting admission.
        /// </summary>
        private FlowState CreateFlowLocked(
            uint id,
            bool local,
            DataChannelParametersDataType parameters,
            DataChannelAuthorizationMode authorization,
            Guid? grant)
        {
            var flow = new FlowState(
                id,
                local,
                parameters,
                authorization,
                grant,
                m_clock.GetTimestamp(),
                m_clock.GetUtcNow().UtcDateTime)
            {
                FlowReserved = true,
                OpeningOperationReserved = true,
                DirectLane = HasSelectedDirectStreams ? new FlowDirectLane() : null
            };
            flow.Facade = new DataChannel(this, flow);
            m_flows.Add(id, flow);
            NotifyStateLocked(flow);
            ArmTimerLocked();
            return flow;
        }

        /// <summary>
        /// Installs narrowed authorization without changing immutable behavior.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        private void ApplyOpeningAuthorizationLocked(FlowState flow, DataChannelAuthorization authorization)
        {
            EnsureOpeningLocked(flow);
            if (authorization.Result != DataChannelResult.Good || authorization.Parameters == null)
            {
                throw new DataChannelException(authorization.Result == DataChannelResult.Good
                    ? DataChannelResult.NotAuthorized : authorization.Result);
            }
            DataChannelParametersDataType admitted = AdmitParametersLocked(authorization.Parameters, false);
            if (!FlowModel.IsNarrowing(flow.Parameters, admitted) ||
                (flow.Authorization == DataChannelAuthorizationMode.Configured && !authorization.SessionId.IsNull) ||
                authorization.RevocationToken.IsCancellationRequested)
            {
                throw new DataChannelException(DataChannelResult.NotAuthorized);
            }
            flow.Parameters = admitted;
            BindUascStreamingPublishersLocked(flow);
            if (!TryReserveRevisionLocked(flow, 1, admitted, false))
            {
                throw new DataChannelException(DataChannelResult.WouldBlock);
            }
            flow.SourceNodeId = authorization.SourceNodeId;
            flow.SessionId = authorization.SessionId;
            flow.AuthorizationLifetime = authorization.Lifetime;
            flow.AuthorizedAt = m_clock.GetTimestamp();
            flow.LastAuthorizationCheck = flow.AuthorizedAt;
            InstallRevocationLocked(flow, authorization.RevocationToken);
        }

        /// <summary>
        /// Verifies a real opener-owned native stream before accepting its association.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        private async ValueTask BindNativeAsync(
            FlowState flow,
            ulong? remoteStream,
            CancellationToken cancellationToken)
        {
            bool provisional = false;
            lock (m_gate)
            {
                EnsureOpeningLocked(flow);
                if (remoteStream.HasValue &&
                    m_streams.TryGetValue(remoteStream.Value, out FlowStreamInput? existing) &&
                    existing.Provisional)
                {
                    provisional = true;
                    existing.Provisional = false;
                    existing.FlowId = flow.Id;
                    m_provisionalStreams--;
                    flow.StreamId = remoteStream.Value;
                }
                else if (m_usedStreams.Count >= m_options.MaxStreamHistory ||
                    !m_budget.TryReserve(FlowResource.Stream, 1))
                {
                    throw new DataChannelException(DataChannelResult.WouldBlock);
                }
                flow.StreamReserved = true;
            }
            ulong stream = remoteStream ??
                await m_carrier.OpenStreamAsync(cancellationToken).ConfigureAwait(false);
            lock (m_gate)
            {
                EnsureOpeningLocked(flow);
                if (stream == m_carrier.ManagementStreamId || (!provisional && !m_usedStreams.Add(stream)))
                {
                    throw new DataChannelException(DataChannelResult.InvalidArgument);
                }
                flow.StreamId = stream;
                if (!provisional)
                {
                    if (!TryCreateStreamInputLocked(flow.Id, stream, out FlowStreamInput? input))
                    {
                        throw new DataChannelException(DataChannelResult.WouldBlock);
                    }
                    m_streams.Add(stream, input);
                }
            }
            if (!await m_carrier.BindStreamAsync(stream, flow.Id, flow.IsLocalOpener, cancellationToken)
                .ConfigureAwait(false))
            {
                throw new DataChannelException(DataChannelResult.InvalidArgument);
            }
            lock (m_gate)
            {
                EnsureOpeningLocked(flow);
            }
        }

        /// <summary>
        /// Validates complete descriptors through the shared codec and actual local/carrier limits.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        private DataChannelParametersDataType AdmitParametersLocked(
            DataChannelParametersDataType parameters,
            bool clamp)
        {
            try
            {
                var control = new FlowOpen(1, 0, FlowModel.ToWire(parameters), FlowWireAuthorization.Configured, null);
                FlowControlCodec.GetEncodedBodyLength(control, 1, 1, m_codecLimits);
                if ((IsNative &&
                    parameters.Protocol is not (DataChannelProfiles.OpaquePayload or DataChannelProfiles.JsonPayload) &&
                    !DataChannelProfiles.IsUadpPayloadProtocol(parameters.Protocol)) ||
                    (!IsNative && !DataChannelProfiles.IsUadpPayloadProtocol(parameters.Protocol)))
                {
                    throw new DataChannelException(DataChannelResult.Unsupported);
                }
                if (parameters.Protection == DataChannelProtectionMode.Confidential &&
                    m_carrier.Identity.Protection != DataChannelProtectionMode.Confidential)
                {
                    throw new DataChannelException(DataChannelResult.SecurityFailure);
                }
                if (parameters.Protection == DataChannelProtectionMode.Integrity &&
                    (!m_options.AllowIntegrity || IsNative))
                {
                    throw new DataChannelException(DataChannelResult.Unsupported);
                }
                if (parameters.ResourceUri == DataChannelProfiles.ClientServerResource &&
                    (parameters.Protocol != DataChannelProfiles.OpaquePayload ||
                        parameters.Direction != DataChannelDirection.Duplex ||
                        parameters.Delivery != DataChannelDeliveryMode.Ordered ||
                        parameters.Protection != DataChannelProtectionMode.Confidential ||
                        parameters.AllowQueueExpiry))
                {
                    throw new DataChannelException(DataChannelResult.InvalidArgument);
                }
                int overhead = HasSelectedDirectStreams
                    ? parameters.Delivery == DataChannelDeliveryMode.Datagram
                        ? DirectDatagramCodec.HeaderSize : DirectDeliveryFrameCodec.HeaderSize :
                    IsNative ? FlowRecordCodec.HeaderSize +
                        (parameters.Delivery == DataChannelDeliveryMode.Datagram ? 4 : 0) : 0;
                uint maxMessage = Math.Min((uint)m_options.MaxMessageSize, m_peerMaxMessageSize);
                maxMessage = Math.Min(
                    maxMessage,
                    (uint)Math.Max(0, m_carrier.MaxRecordSize -
                        (HasSelectedDirectStreams ? overhead : IsNative
                            ? FlowRecordCodec.HeaderSize : UascFlowMessageCodec.TypeIdSize)));
                uint maxReceive = Math.Min(m_options.MaxReceiveBytes, m_peerMaxReceiveBytes);
                if (parameters.Delivery == DataChannelDeliveryMode.Datagram)
                {
                    if (HasSelectedDirectStreams)
                    {
                        if (DirectDatagramCapacity <= DirectDatagramCodec.HeaderSize)
                        {
                            throw new DataChannelException(DataChannelResult.Unsupported);
                        }
                        maxMessage = Math.Min(
                            maxMessage, (uint)(DirectDatagramCapacity - DirectDatagramCodec.HeaderSize));
                    }
                    else
                    {
                        if (!IsNative || m_carrier.MaxDatagramSize <= 17)
                        {
                            throw new DataChannelException(DataChannelResult.Unsupported);
                        }
                        maxMessage = Math.Min(maxMessage, (uint)(m_carrier.MaxDatagramSize - 17));
                    }
                }
                if (clamp)
                {
                    parameters = FlowModel.Copy(parameters);
                    parameters.MaxReceiveBytes = Math.Min(parameters.MaxReceiveBytes, maxReceive);
                    parameters.MaxMessageSize = Math.Min(parameters.MaxMessageSize, maxMessage);
                    if (parameters.MaxReceiveBytes > overhead)
                    {
                        parameters.MaxMessageSize = Math.Min(
                            parameters.MaxMessageSize, parameters.MaxReceiveBytes - (uint)overhead);
                    }
                }
                if (parameters.MaxMessageSize == 0 ||
                    parameters.MaxMessageSize > maxMessage ||
                    parameters.MaxReceiveBytes > maxReceive ||
                    (ulong)parameters.MaxMessageSize + (uint)overhead > parameters.MaxReceiveBytes)
                {
                    throw new DataChannelException(DataChannelResult.TooLarge);
                }
                return parameters;
            }
            catch (FlowCodecException exception)
            {
                throw new DataChannelException(FlowModel.FromWire(exception.Status));
            }
        }

        /// <summary>
        /// Checks authorization syntax without treating an identifier as a grant.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        private static void ValidateAuthorization(DataChannelAuthorizationMode mode, Guid? grant)
        {
            if ((mode == DataChannelAuthorizationMode.Configured && grant.HasValue) ||
                (mode == DataChannelAuthorizationMode.Session && (!grant.HasValue || grant == Guid.Empty)) ||
                mode is not (DataChannelAuthorizationMode.Configured or DataChannelAuthorizationMode.Session))
            {
                throw new DataChannelException(DataChannelResult.InvalidArgument);
            }
        }

        /// <summary>
        /// Confirms ACCEPT before installing receive state and transmitting READY.
        /// </summary>
        private DataChannelResult ReceiveAcceptLocked(FlowState flow, FlowAccept accept, uint operationId)
        {
            if (!flow.IsLocalOpener ||
                flow.State != DataChannelState.Opening ||
                accept.Stream != flow.StreamId ||
                flow.Revisions.Count == 0)
            {
                return RefuseLocked(flow.Id, flow.Revision, DataChannelResult.InvalidState, operationId);
            }
            DataChannelParametersDataType admitted = AdmitParametersLocked(
                FlowModel.FromWire(accept.Parameters), false);
            if (!FlowModel.IsNarrowing(flow.Parameters, admitted))
            {
                return ResetLocked(flow, DataChannelResult.InvalidArgument, operationId);
            }
            FlowRevision old = flow.Revisions[1];
            old.SendActive = false;
            old.ReceiveActive = false;
            RetireRevisionsLocked(flow);
            if (!TryReserveRevisionLocked(flow, 1, admitted, false))
            {
                return ResetLocked(flow, DataChannelResult.WouldBlock, operationId);
            }
            flow.Parameters = admitted;
            if (flow.DirectLane != null)
            {
                flow.DirectLane.Accepted = true;
                TryQueueDirectReadyLocked(flow);
                return DataChannelResult.Good;
            }
            QueueControlLocked(new FlowSignal(FlowOperation.Ready, flow.Id), 1, 0,
                IsNative ? DataChannelPath.Flow : DataChannelPath.Management, () =>
            {
                if (flow.IsTerminal)
                {
                    return flow.Result;
                }
                MarkReadyLocked(flow);
                return DataChannelResult.Good;
            }, flow);
            return DataChannelResult.Good;
        }

        /// <summary>
        /// Enters readiness asymmetrically at READY commitment or receipt.
        /// </summary>
        private void MarkReadyLocked(FlowState flow)
        {
            if (flow.Ready || flow.IsTerminal)
            {
                return;
            }
            flow.Ready = true;
            flow.State = DataChannelState.Open;
            if (flow.DirectLane is { } lane)
            {
                lane.Ready.TrySetResult(true);
                if (!flow.IsDatagram && flow.MaySend &&
                    m_directAssociations.TryGetUsable(flow, true, out var association))
                {
                    lane.SendTask = Task.Run(() => OpenDirectSenderAsync(flow, association));
                }
            }
            flow.LocalEnded = !flow.MaySend;
            flow.RemoteEnded = !flow.MayReceive;
            long now = m_clock.GetTimestamp();
            flow.SendRate.Configure(flow.Parameters.MaxRate, flow.Parameters.MaxReceiveBytes, now, true);
            if (flow.OpeningOperationReserved)
            {
                flow.OpeningOperationReserved = false;
                m_budget.Release(FlowResource.Operation, 1);
            }
            if (!flow.IsLocalOpener)
            {
                m_accepted.Enqueue(flow.Facade!);
                m_acceptChanged.Complete();
                m_acceptChanged = new TaskSignal();
                flow.Opened.TrySetResult(DataChannelResult.Good);
            }
            if (UsesFlowCredit(flow))
            {
                GrantCreditLocked(flow);
            }
            NotifyStateLocked(flow);
            WakeLocked();
            ArmTimerLocked();
        }

        /// <summary>
        /// Awaits ready remote flows, including when called before local StartAsync.
        /// </summary>
        private async ValueTask<DataChannel?> AcceptCoreAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                Task wait;
                lock (m_gate)
                {
                    while (m_accepted.Count != 0)
                    {
                        DataChannel channel = m_accepted.Dequeue();
                        if (!channel.StateData.IsTerminal)
                        {
                            return channel;
                        }
                    }
                    if (m_closed)
                    {
                        return null;
                    }
                    wait = m_acceptChanged.Task;
                }
                await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Refuses access before context confirmation.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        private void RequireReadyLocked()
        {
            ThrowIfClosedLocked();
            if (m_identity == null)
            {
                throw new DataChannelException(DataChannelResult.InvalidState);
            }
        }

        /// <summary>
        /// Refuses continuation after the bounded opening phase has ended.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        private void EnsureOpeningLocked(FlowState flow)
        {
            ThrowIfClosedLocked();
            if (flow.State != DataChannelState.Opening)
            {
                throw new DataChannelException(flow.IsTerminal ? flow.Result : DataChannelResult.InvalidState);
            }
        }

        /// <summary>
        /// Reports the stable terminal context outcome.
        /// </summary>
        /// <exception cref="DataChannelException"></exception>
        private void ThrowIfClosedLocked()
        {
            if (m_closed)
            {
                throw new DataChannelException(m_closeResult);
            }
        }
    }
}
