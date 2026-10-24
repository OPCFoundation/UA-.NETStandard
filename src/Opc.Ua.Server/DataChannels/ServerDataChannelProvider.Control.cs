/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Server.DataChannels
{
    public sealed partial class ServerDataChannelProvider
    {
        /// <summary>
        /// Authorizes one reserved remote-opener ChannelId. The returned grant is not data readiness.
        /// </summary>
        public async ValueTask<DataChannelGrantDataType> OpenAsync(
            ISession session,
            uint channelId,
            NodeId sourceNodeId,
            DataChannelBindingDataType binding,
            DataChannelParametersDataType parameters,
            CancellationToken cancellationToken = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (binding == null)
            {
                throw new ArgumentNullException(nameof(binding));
            }
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }
            DataChannelResult result = DataChannelResult.NotAuthorized;
            DataChannelSource? source = null;
            await m_controlGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ExpireProvisionalState();
                if (!IsLiveSession(session))
                {
                    throw new ServiceResultException(StatusCodes.BadSessionNotActivated);
                }
                IServerInternal server = m_server!;
                if (!Sources.TryGet(sourceNodeId, server.NamespaceUris, out source) ||
                    source.ResourceUri != parameters.ResourceUri)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                }
                ContextRegistration? registration = FindContext(binding.ContextId, binding.Generation);
                if (registration?.Identity is not { } identity ||
                    binding.Generation == 0 ||
                    binding.Challenge.Length != 32 || binding.Challenge != identity.LocalChallenge ||
                    binding.PeerApplicationUri != identity.Peer.PeerApplicationUri ||
                    binding.PeerKeyThumbprint != identity.Peer.PeerKeyThumbprint ||
                    !MatchesSessionPeer(session, identity))
                {
                    result = DataChannelResult.SecurityFailure;
                    Throw(result);
                }
                ContextRegistration confirmed = registration!;
                identity = confirmed.Identity!;
                if (channelId == 0 ||
                    (identity.Carrier == DataChannelCarrierKind.DedicatedUasc
                        ? channelId != 1
                        : (channelId & 1) == (identity.Peer.IsTransportInitiator ? 1u : 0u)))
                {
                    result = DataChannelResult.InvalidArgument;
                    Throw(result);
                }
                result = ValidateCarrierAndSource(confirmed, source, parameters);
                Throw(result);
                if (!ClientUserIdResolver.TryResolveContinuityKey(
                    session.IdentityToken, session.Identity, out string? identityKey))
                {
                    result = DataChannelResult.NotAuthorized;
                    Throw(result);
                }
                var key = new FlowKey(identity.ContextId, identity.Generation, channelId);
                UserTokenType identityType = session.Identity.TokenType;
                if (!await CheckNodePermissionsAsync(
                    session, source, false, parameters, cancellationToken).ConfigureAwait(false))
                {
                    result = DataChannelResult.NotAuthorized;
                    Throw(result);
                }
                result = await source.Policy.AuthorizeAsync(
                    new DataChannelSourceAccess(
                        source, identity, session, false, DataChannelAuthorizationPurpose.Open, parameters),
                    cancellationToken).ConfigureAwait(false);
                Throw(result);
                TimeSpan lifetime = binding.ExpiresAt.ToDateTime() - m_clock.GetUtcNow().UtcDateTime;
                lifetime = Min(lifetime, m_options.GrantLifetime);
                lifetime = Min(lifetime, GetSessionLifetime(session));
                lifetime = Min(lifetime, session.ClientCertificate.NotAfter - m_clock.GetUtcNow().UtcDateTime);
                if (lifetime <= TimeSpan.Zero)
                {
                    result = DataChannelResult.Timeout;
                    Throw(result);
                }
                lock (m_gate)
                {
                    if (confirmed.Closed || !HasSameUser(session, identityKey, identityType) || !IsLiveSession(session))
                    {
                        result = DataChannelResult.NotAuthorized;
                        Throw(result);
                    }
                    if (confirmed.IssuedChannelIds.Contains(channelId) ||
                        HasConflictingChannelId(session.Id, key))
                    {
                        result = DataChannelResult.InvalidArgument;
                        Throw(result);
                    }
                    if (confirmed.IssuedChannelIds.Count >= m_options.MaxStreamHistory ||
                        !TryAdmit(key, source))
                    {
                        result = DataChannelResult.WouldBlock;
                        Throw(result);
                    }
                    DataChannelGrantDataType grant;
                    try
                    {
                        grant = m_grants.Issue(
                            identity, channelId, parameters, source.SourceNodeId, session.Id, lifetime);
                    }
                    catch
                    {
                        m_admitted.Remove(key);
                        throw;
                    }
                    var proposed = new SessionFlow(key, session, source, parameters, identityKey, identityType)
                    {
                        GrantId = grant.GrantId
                    };
                    confirmed.IssuedChannelIds.Add(channelId);
                    m_sessionFlows.Add(key, proposed);
                    result = DataChannelResult.Good;
                    return grant;
                }
            }
            catch (ServiceResultException exception)
            {
                result = DataChannelControlModel.FromStatusCode(exception.StatusCode);
                throw;
            }
            catch (DataChannelException exception)
            {
                result = exception.Result;
                throw;
            }
            catch (OperationCanceledException)
            {
                result = DataChannelResult.Timeout;
                throw;
            }
            catch
            {
                result = DataChannelResult.InvalidState;
                throw;
            }
            finally
            {
                m_controlGate.Release();
                Audit(new DataChannelControlAudit(
                    "OpenDataChannel", source?.SourceNodeId ?? ExpandedNodeId.Null, source?.ResourceUri ?? string.Empty,
                    session.ClientApplicationUri ?? string.Empty, binding.ContextId, binding.Generation,
                    channelId, session.Id, 0, 0, result));
            }
        }

        /// <summary>
        /// Stages an exact authorized revision. Only the matching data-path transaction can commit it.
        /// </summary>
        public async ValueTask<DataChannelStatusDataType> ModifyAsync(
            ISession session,
            uint channelId,
            uint operationId,
            uint requestedRevision,
            DataChannelParametersDataType parameters,
            CancellationToken cancellationToken = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }
            SessionFlow? flow = null;
            DataChannelResult result = DataChannelResult.NotAuthorized;
            await m_controlGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ExpireProvisionalState();
                flow = ResolveOwnedFlow(session, channelId);
                DataChannel? channel = flow.Channel;
                if (channel == null || channel.IsLocalOpener || flow.Closing ||
                    channel.State is DataChannelState.Closed or DataChannelState.Faulted)
                {
                    result = DataChannelResult.InvalidState;
                    Throw(result);
                }
                DataChannelStatusDataType current = channel!.GetStatus();
                if (operationId == 0 || requestedRevision == 0)
                {
                    result = DataChannelResult.InvalidArgument;
                    Throw(result);
                }
                if (!DataChannelControlModel.SameDescriptor(current.Parameters, parameters))
                {
                    result = DataChannelResult.Unsupported;
                    Throw(result);
                }
                ContextRegistration? registration = FindContext(flow.Key.ContextId, flow.Key.Generation);
                if (registration?.Identity is not { } identity)
                {
                    result = DataChannelResult.SecurityFailure;
                    Throw(result);
                }
                result = ValidateCarrierAndSource(registration!, flow.Source, parameters);
                Throw(result);
                if (!await CheckNodePermissionsAsync(
                    session, flow.Source, false, parameters, cancellationToken).ConfigureAwait(false))
                {
                    result = DataChannelResult.NotAuthorized;
                    Throw(result);
                }
                result = await flow.Source.Policy.AuthorizeAsync(
                    new DataChannelSourceAccess(
                        flow.Source, registration!.Identity!, session, false,
                        DataChannelAuthorizationPurpose.Update, parameters),
                    cancellationToken).ConfigureAwait(false);
                Throw(result);
                lock (m_gate)
                {
                    if (flow.Revoked || !HasSameUser(flow) || !IsLiveSession(session))
                    {
                        result = DataChannelResult.NotAuthorized;
                        Throw(result);
                    }
                    if (flow.Operations.TryGetValue(operationId, out StagedOperation? previous))
                    {
                        if (previous.Revision != requestedRevision ||
                            !DataChannelControlModel.SameParameters(previous.Parameters, parameters))
                        {
                            result = DataChannelResult.InvalidArgument;
                            Throw(result);
                        }
                        DataChannelStatusDataType repeated = GetOperationStatus(flow, previous);
                        result = repeated.Result;
                        return repeated;
                    }
                    RetireCompletedOperation(flow);
                    if (operationId <= flow.OperationHighWater)
                    {
                        result = DataChannelResult.InvalidArgument;
                        Throw(result);
                    }
                    if (flow.PendingOperation != null || current.PendingRevision != 0 ||
                        current.Revision == uint.MaxValue || requestedRevision != current.Revision + 1)
                    {
                        result = DataChannelResult.InvalidState;
                        Throw(result);
                    }
                    var operation = new StagedOperation(
                        operationId, requestedRevision, parameters, m_clock.GetTimestamp());
                    flow.Operations.Add(operationId, operation);
                    flow.OperationHighWater = operationId;
                    flow.PendingOperation = operation;
                    while (flow.Operations.Count > 64)
                    {
                        using IEnumerator<uint> ids = flow.Operations.Keys.GetEnumerator();
                        if (ids.MoveNext())
                        {
                            flow.Operations.Remove(ids.Current);
                        }
                    }
                    DataChannelStatusDataType staged = GetOperationStatus(flow, operation);
                    result = staged.Result;
                    return staged;
                }
            }
            catch (ServiceResultException exception)
            {
                result = DataChannelControlModel.FromStatusCode(exception.StatusCode);
                throw;
            }
            catch (DataChannelException exception)
            {
                result = exception.Result;
                throw;
            }
            catch (OperationCanceledException)
            {
                result = DataChannelResult.Timeout;
                throw;
            }
            catch
            {
                result = DataChannelResult.InvalidState;
                throw;
            }
            finally
            {
                m_controlGate.Release();
                Audit(new DataChannelControlAudit(
                    "ModifyDataChannel", flow?.Source.SourceNodeId ?? ExpandedNodeId.Null,
                    flow?.Source.ResourceUri ?? string.Empty, session.ClientApplicationUri ?? string.Empty,
                    flow?.Key.ContextId ?? Guid.Empty, flow?.Key.Generation ?? 0, flow?.Key.ChannelId ?? 0, session.Id,
                    operationId, flow == null ? 0 : requestedRevision, result));
            }
        }

        /// <summary>
        /// Closes only the caller's unambiguous flow, returning actual drain or reset status.
        /// </summary>
        public async ValueTask<DataChannelStatusDataType> CloseAsync(
            ISession session,
            uint channelId,
            bool deleteQueued,
            CancellationToken cancellationToken = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            SessionFlow? flow = null;
            DataChannelResult result = DataChannelResult.NotAuthorized;
            await m_controlGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                flow = ResolveOwnedFlow(session, channelId);
                if (flow.Channel == null)
                {
                    ContextRegistration? dataContext = FindContext(flow.Key.ContextId, flow.Key.Generation);
                    if (dataContext != null)
                    {
                        foreach (DataChannel candidate in dataContext.Context.Channels)
                        {
                            if (candidate.ChannelId == channelId)
                            {
                                flow.Channel = candidate;
                                break;
                            }
                        }
                    }
                }
                DataChannelParametersDataType current = flow.Channel?.Parameters ?? flow.Parameters;
                if (!await CheckNodePermissionsAsync(
                    session, flow.Source, false, current, cancellationToken).ConfigureAwait(false))
                {
                    Throw(result);
                }
                ContextRegistration? registration = FindContext(flow.Key.ContextId, flow.Key.Generation);
                if (registration?.Identity is not { } identity)
                {
                    result = DataChannelResult.SecurityFailure;
                    Throw(result);
                }
                result = await flow.Source.Policy.AuthorizeAsync(
                    new DataChannelSourceAccess(
                        flow.Source, registration!.Identity!, session, false,
                        DataChannelAuthorizationPurpose.Recheck, current),
                    cancellationToken).ConfigureAwait(false);
                Throw(result);
                lock (m_gate)
                {
                    if (flow.Revoked || !HasSameUser(flow) || !IsLiveSession(session))
                    {
                        result = DataChannelResult.NotAuthorized;
                        Throw(result);
                    }
                    flow.Closing = true;
                    flow.PendingOperation = null;
                    m_grants.RevokeGrant(flow.GrantId);
                }
                if (flow.Channel is { } channel)
                {
                    if (deleteQueued || channel.State == DataChannelState.Opening)
                    {
                        await channel.ResetAsync(DataChannelResult.Good, cancellationToken).ConfigureAwait(false);
                        result = DataChannelResult.Good;
                    }
                    else
                    {
                        result = await channel.EndAsync(cancellationToken).ConfigureAwait(false);
                    }
                    DataChannelStatusDataType status = channel.GetStatus();
                    status.Result = result;
                    return status;
                }
                lock (m_gate)
                {
                    m_admitted.Remove(flow.Key);
                    flow.Revoked = true;
                }
                flow.Revoke();
                result = DataChannelResult.Good;
                return ProvisionalStatus(flow, DataChannelState.Closed, result);
            }
            catch (ServiceResultException exception)
            {
                result = DataChannelControlModel.FromStatusCode(exception.StatusCode);
                throw;
            }
            catch (DataChannelException exception)
            {
                result = exception.Result;
                throw;
            }
            catch (OperationCanceledException)
            {
                result = DataChannelResult.Timeout;
                throw;
            }
            catch
            {
                result = DataChannelResult.InvalidState;
                throw;
            }
            finally
            {
                m_controlGate.Release();
                Audit(new DataChannelControlAudit(
                    "CloseDataChannel", flow?.Source.SourceNodeId ?? ExpandedNodeId.Null,
                    flow?.Source.ResourceUri ?? string.Empty, session.ClientApplicationUri ?? string.Empty,
                    flow?.Key.ContextId ?? Guid.Empty, flow?.Key.Generation ?? 0, flow?.Key.ChannelId ?? 0, session.Id,
                    0, flow?.Channel?.Revision ?? 0, result));
            }
        }

        /// <summary>
        /// Returns only currently permitted flow status belonging to this exact Session.
        /// </summary>
        public async ValueTask<ArrayOf<DataChannelStatusDataType>> GetChannelsAsync(
            ISession session,
            string? resourceUri = null,
            CancellationToken cancellationToken = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            List<DataChannelStatusDataType> statuses = [];
            foreach (SessionFlow flow in await GetVisibleFlowsAsync(
                session, resourceUri, cancellationToken).ConfigureAwait(false))
            {
                lock (m_gate)
                {
                    RetireCompletedOperation(flow);
                    statuses.Add(flow.PendingOperation is { } operation
                        ? GetOperationStatus(flow, operation)
                        : flow.Channel?.GetStatus() ??
                            ProvisionalStatus(flow, DataChannelState.Opening, DataChannelResult.WouldBlock));
                }
            }
            return [.. statuses];
        }

        /// <summary>
        /// Returns actual complete-message counters using the same per-Session projection as status.
        /// </summary>
        public async ValueTask<ArrayOf<DataChannelDiagnosticsDataType>> GetDiagnosticsAsync(
            ISession session,
            string? resourceUri = null,
            CancellationToken cancellationToken = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            List<DataChannelDiagnosticsDataType> diagnostics = [];
            foreach (SessionFlow flow in await GetVisibleFlowsAsync(
                session, resourceUri, cancellationToken).ConfigureAwait(false))
            {
                if (flow.Channel is { } channel)
                {
                    diagnostics.Add(channel.GetDiagnostics());
                }
            }
            return [.. diagnostics];
        }

        /// <summary>
        /// Publishes a recipient-specific source hint after current permissions are checked.
        /// It does not issue a grant, reserve a channel or start payload.
        /// </summary>
        public async ValueTask OfferAsync(
            ISession recipient,
            string resourceUri,
            DataChannelParametersDataType parameters,
            TimeSpan lifetime,
            CancellationToken cancellationToken = default)
        {
            if (recipient == null)
            {
                throw new ArgumentNullException(nameof(recipient));
            }
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }
            if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromHours(1))
            {
                throw new ArgumentOutOfRangeException(nameof(lifetime));
            }
            if (!Sources.TryGet(resourceUri, out DataChannelSource? source) ||
                source.ResourceUri != parameters.ResourceUri || !IsLiveSession(recipient))
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
            }
            if (!ClientUserIdResolver.TryResolveContinuityKey(
                recipient.IdentityToken, recipient.Identity, out string? identityKey))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            UserTokenType identityType = recipient.Identity.TokenType;
            Throw(source.Validate(parameters));
            if (!await CheckNodePermissionsAsync(
                recipient, source, false, parameters, cancellationToken).ConfigureAwait(false))
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            Throw(await source.Policy.AuthorizeAsync(
                new DataChannelSourceAccess(
                    source, null, recipient, false, DataChannelAuthorizationPurpose.Open, parameters),
                cancellationToken).ConfigureAwait(false));
            if (!IsLiveSession(recipient) || !HasSameUser(recipient, identityKey, identityType))
            {
                throw new ServiceResultException(StatusCodes.BadSessionClosed);
            }
            Offered?.Invoke(new DataChannelOffer(
                recipient.Id, source.SourceNodeId, parameters,
                m_clock.GetUtcNow().UtcDateTime + Min(lifetime, GetSessionLifetime(recipient))));
        }

        /// <summary>
        /// Checks recipient validity again at Event queue time, before client filtering or field selection.
        /// </summary>
        internal bool CanReceiveEvent(ISession session, NodeId recipient)
        {
            return session.Id == recipient && IsLiveSession(session);
        }

        /// <summary>
        /// Captures the flow's authorizing user, rather than the possibly changed user at Event delivery.
        /// </summary>
        internal Func<ISession, bool> CreateStateEventFilter(DataChannelStatusDataType status)
        {
            lock (m_gate)
            {
                if (m_sessionFlows.TryGetValue(
                    new FlowKey(status.ContextId, status.Generation, status.ChannelId), out SessionFlow? flow))
                {
                    string? identity = flow.IdentityKey;
                    UserTokenType type = flow.IdentityType;
                    NodeId recipient = flow.Session.Id;
                    return session => CanReceiveEvent(session, recipient) && session.Identity.TokenType == type &&
                        ClientUserIdResolver.TryResolveContinuityKey(
                            session.IdentityToken, session.Identity, out string? current) &&
                        string.Equals(current, identity, StringComparison.Ordinal);
                }
            }
            return static _ => false;
        }

        /// <summary>
        /// Resolves only one caller-owned live attribution; unknown and inaccessible ids are indistinguishable.
        /// </summary>
        private SessionFlow ResolveOwnedFlow(ISession session, uint channelId)
        {
            SessionFlow? selected = null;
            lock (m_gate)
            {
                if (IsLiveSession(session))
                {
                    foreach (SessionFlow candidate in m_sessionFlows.Values)
                    {
                        if (candidate.Key.ChannelId != channelId || candidate.Session.Id != session.Id ||
                            candidate.Revoked || !HasSameUser(candidate))
                        {
                            continue;
                        }
                        if (selected != null)
                        {
                            throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
                        }
                        selected = candidate;
                    }
                }
            }
            return selected ?? throw new ServiceResultException(StatusCodes.BadNodeIdUnknown);
        }

        /// <summary>
        /// Detects cross-context ChannelId collisions before issuing a Session grant.
        /// </summary>
        private bool HasConflictingChannelId(NodeId sessionId, FlowKey proposed)
        {
            foreach (SessionFlow candidate in m_sessionFlows.Values)
            {
                if (candidate.Session.Id == sessionId && candidate.Key.ChannelId == proposed.ChannelId &&
                    candidate.Key != proposed && !candidate.Revoked &&
                    candidate.Channel?.State is not (DataChannelState.Closed or DataChannelState.Faulted))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Projects an authorization-only operation until the runtime has accepted its PREPARE.
        /// </summary>
        private DataChannelStatusDataType GetOperationStatus(SessionFlow flow, StagedOperation operation)
        {
            DataChannelStatusDataType status = flow.Channel!.GetStatus();
            if (operation.Prepared)
            {
                return flow.Channel.GetOperationStatus(operation.Id);
            }
            status.OperationId = operation.Id;
            status.PendingRevision = operation.Expired ? 0 : operation.Revision;
            status.Result = operation.Expired ? DataChannelResult.Timeout : DataChannelResult.WouldBlock;
            return status;
        }

        /// <summary>
        /// Retires pending authorization only after the runtime has a completed ACK or failure outcome.
        /// </summary>
        private static void RetireCompletedOperation(SessionFlow flow)
        {
            if (flow.Channel == null || flow.PendingOperation is not { Prepared: true } operation)
            {
                return;
            }
            DataChannelStatusDataType status = flow.Channel.GetOperationStatus(operation.Id);
            if (status.OperationId == operation.Id && status.PendingRevision == 0)
            {
                flow.PendingOperation = null;
            }
        }

        /// <summary>
        /// Captures only the caller's entries before checking every source policy outside synchronization.
        /// </summary>
        private async ValueTask<List<SessionFlow>> GetVisibleFlowsAsync(
            ISession session,
            string? resourceUri,
            CancellationToken cancellationToken)
        {
            ExpireProvisionalState();
            List<SessionFlow> candidates = [];
            lock (m_gate)
            {
                foreach (SessionFlow flow in m_sessionFlows.Values)
                {
                    if (flow.Session.Id == session.Id && !flow.Revoked && HasSameUser(flow) &&
                        (resourceUri == null || flow.Source.ResourceUri == resourceUri))
                    {
                        candidates.Add(flow);
                    }
                }
            }
            List<SessionFlow> visible = [];
            foreach (SessionFlow flow in candidates)
            {
                ContextRegistration? registration = FindContext(flow.Key.ContextId, flow.Key.Generation);
                if (registration?.Identity is not { } identity || !IsLiveSession(session) ||
                    !await CheckNodePermissionsAsync(
                        session, flow.Source, false, flow.Channel?.Parameters ?? flow.Parameters, cancellationToken)
                        .ConfigureAwait(false))
                {
                    continue;
                }
                DataChannelResult result = await flow.Source.Policy.AuthorizeAsync(
                    new DataChannelSourceAccess(
                        flow.Source, identity, session, false, DataChannelAuthorizationPurpose.Recheck,
                        flow.Channel?.Parameters ?? flow.Parameters),
                    cancellationToken).ConfigureAwait(false);
                if (result == DataChannelResult.Good && !flow.Revoked && HasSameUser(flow) && IsLiveSession(session))
                {
                    visible.Add(flow);
                }
            }
            return visible;
        }

        /// <summary>
        /// Returns a truthful pending-grant state without pretending OPEN/READY completed.
        /// </summary>
        private static DataChannelStatusDataType ProvisionalStatus(
            SessionFlow flow,
            DataChannelState state,
            DataChannelResult result)
        {
            return new DataChannelStatusDataType
            {
                ChannelId = flow.Key.ChannelId,
                ContextId = flow.Key.ContextId,
                Generation = flow.Key.Generation,
                SourceNodeId = flow.Source.SourceNodeId,
                SessionId = flow.Session.Id,
                Parameters = DataChannelControlModel.Copy(flow.Parameters),
                State = state,
                Result = result
            };
        }

        /// <summary>
        /// Throws only normal Call operation errors.
        /// </summary>
        private static void Throw(DataChannelResult result)
        {
            if (result != DataChannelResult.Good)
            {
                throw new ServiceResultException(DataChannelControlModel.ToStatusCode(result));
            }
        }

        /// <summary>
        /// Selects the stricter issuer-clock bound.
        /// </summary>
        private static TimeSpan Min(TimeSpan left, TimeSpan right)
        {
            return left <= right ? left : right;
        }

        /// <summary>
        /// Session ownership and bounded operation history for one authenticated flow.
        /// </summary>
        private sealed class SessionFlow(
            FlowKey key,
            ISession session,
            DataChannelSource source,
            DataChannelParametersDataType parameters,
            string? identityKey,
            UserTokenType identityType) : IDisposable
        {
            public FlowKey Key { get; } = key;
            public ISession Session { get; } = session;
            public DataChannelSource Source { get; } = source;
            public DataChannelParametersDataType Parameters { get; } = DataChannelControlModel.Copy(parameters);
            public string? IdentityKey { get; } = identityKey;
            public UserTokenType IdentityType { get; } = identityType;
            public Guid GrantId { get; set; }
            public bool Redeemed { get; set; }
            public bool Revoked { get; set; }
            public bool Closing { get; set; }
            public DataChannel? Channel { get; set; }
            public uint OperationHighWater { get; set; }
            public StagedOperation? PendingOperation { get; set; }
            public SortedDictionary<uint, StagedOperation> Operations { get; } = [];
            public CancellationToken RevocationToken => m_revocation.Token;

            /// <summary>
            /// Invalidates uncommitted and in-flight authorization without a Session/context dependency for neighbours.
            /// </summary>
            public void Revoke()
            {
                try
                {
                    if (!m_disposed)
                    {
                        m_revocation.Cancel();
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Context retirement can dispose a scope after its Session revoke was scheduled.
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                if (!m_disposed)
                {
                    m_disposed = true;
                    m_revocation.Dispose();
                }
            }

            private readonly CancellationTokenSource m_revocation = new();
            private bool m_disposed;
        }

        /// <summary>
        /// A staged complete proposal; it cannot mutate the runtime descriptor directly.
        /// </summary>
        private sealed class StagedOperation(
            uint id,
            uint revision,
            DataChannelParametersDataType parameters,
            long created)
        {
            public uint Id { get; } = id;
            public uint Revision { get; } = revision;
            public DataChannelParametersDataType Parameters { get; } = DataChannelControlModel.Copy(parameters);
            public long Created { get; } = created;
            public bool Prepared { get; set; }
            public bool Expired { get; set; }
        }
    }
}
