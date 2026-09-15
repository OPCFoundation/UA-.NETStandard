/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Bindings;

namespace Opc.Ua.Server
{
    public partial class StandardServer
    {
        /// <summary>
        /// The data channel sources this Server hosts, resolved by NodeId.
        /// </summary>
        public DataChannelSourceRegistry DataChannelSources { get; } = new();

        /// <summary>
        /// What this Server advertises it can carry.
        /// </summary>
        public DataChannelServerCapabilities DataChannelCapabilities { get; set; } = new()
        {
            MaxDataChannels = 16,
            MaxFrameSize = 64 * 1024,
            MaxCreditPerChannel = 1024 * 1024,
            SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered],
            SupportedTransportProfileUris = [Profiles.UaTcpTransport]
        };

        /// <summary>
        /// Decides whether a Session may open a channel on a source, or
        /// <c>null</c> to authorize as an equivalent Read and Write of the
        /// source Node.
        /// </summary>
        public IDataChannelAuthorizer? DataChannelAuthorizer { get; set; }

        /// <summary>
        /// Records every open attempt, or <c>null</c> to raise the standard
        /// <c>AuditOpenDataChannelEventType</c>.
        /// </summary>
        public IDataChannelAuditor? DataChannelAuditor { get; set; }

        /// <summary>
        /// The transport that carries the frames, or <c>null</c> to carry them
        /// inline on the SecureChannel the request arrived on.
        /// </summary>
        public IServerDataChannelTransport? DataChannelTransport { get; set; }

        /// <summary>
        /// How often open channels are re-authorized, which is what makes a
        /// revoked permission take effect on a channel already running.
        /// </summary>
        public TimeSpan DataChannelAuthorizationRecheckInterval { get; set; } =
            TimeSpan.FromMilliseconds(DataChannelConstants.DefaultAuthorizationRecheckInterval);

        /// <inheritdoc/>
        public override async ValueTask<OpenDataChannelResponse> OpenDataChannelAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            NodeId sourceNodeId,
            uint offerId,
            ulong transportChannelId,
            DataChannelParametersDataType? requestedParameters,
            RequestLifetime requestLifetime)
        {
            OperationContext context = await ValidateRequestAsync(
                secureChannelContext,
                requestHeader,
                RequestType.OpenDataChannel,
                requestLifetime).ConfigureAwait(false);

            try
            {
                DataChannelServiceHandler handler = GetDataChannelHandler(secureChannelContext);
                OpenDataChannelResponse response = await handler.OpenDataChannelAsync(
                    CreateDataChannelRequestContext(context, secureChannelContext, transportChannelId),
                    sourceNodeId,
                    offerId,
                    requestedParameters,
                    requestLifetime.CancellationToken).ConfigureAwait(false);

                response.ResponseHeader = CreateResponse(requestHeader, context.StringTable);

                // §7.4 and Part 4 errata §5.1: no frame may name this
                // ChannelId until the response carrying it has been handed to
                // the transport. The channel therefore stays in Opening until
                // the transport reports the response on its way, rather than
                // being opened here where the response object has not even
                // been encoded yet.
                uint channelId = response.ChannelId;
                secureChannelContext.ResponseDispatched += () => handler.OnResponseSent(channelId);
                return response;
            }
            catch (ServiceResultException e)
            {
                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <inheritdoc/>
        public override async ValueTask<ModifyDataChannelResponse> ModifyDataChannelAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            uint channelId,
            DataChannelParametersDataType? requestedParameters,
            RequestLifetime requestLifetime)
        {
            OperationContext context = await ValidateRequestAsync(
                secureChannelContext,
                requestHeader,
                RequestType.ModifyDataChannel,
                requestLifetime).ConfigureAwait(false);

            try
            {
                ModifyDataChannelResponse response = await GetDataChannelHandler(secureChannelContext)
                    .ModifyDataChannelAsync(
                        CreateDataChannelRequestContext(context, secureChannelContext, 0),
                        channelId,
                        requestedParameters,
                        requestLifetime.CancellationToken).ConfigureAwait(false);

                response.ResponseHeader = CreateResponse(requestHeader, context.StringTable);
                return response;
            }
            catch (ServiceResultException e)
            {
                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <inheritdoc/>
        public override async ValueTask<CloseDataChannelResponse> CloseDataChannelAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            uint channelId,
            StatusCode reason,
            bool deleteQueued,
            RequestLifetime requestLifetime)
        {
            OperationContext context = await ValidateRequestAsync(
                secureChannelContext,
                requestHeader,
                RequestType.CloseDataChannel,
                requestLifetime).ConfigureAwait(false);

            try
            {
                CloseDataChannelResponse response = await GetDataChannelHandler(secureChannelContext)
                    .CloseDataChannelAsync(
                        CreateDataChannelRequestContext(context, secureChannelContext, 0),
                        channelId,
                        reason,
                        deleteQueued,
                        requestLifetime.CancellationToken).ConfigureAwait(false);

                response.ResponseHeader = CreateResponse(requestHeader, context.StringTable);
                return response;
            }
            catch (ServiceResultException e)
            {
                throw TranslateException(context, e);
            }
            finally
            {
                OnRequestComplete(context);
            }
        }

        /// <summary>
        /// Aborts data channels authorized by a Session as it closes.
        /// </summary>
        protected virtual void OnDataChannelSessionClosing(ISession session, SessionEventReason reason)
        {
            AbortDataChannelsOfSession(session.Id, StatusCodes.BadSessionClosed);
            ReapDataChannelStates();
        }

        /// <summary>
        /// Faults every data channel riding on a SecureChannel.
        /// </summary>
        /// <remarks>
        /// Part 6 errata §5.13 lists "SecureChannel closed, transport lost" as
        /// a transition to <c>Faulted</c> from any state. A channel is not a
        /// Session-scoped resource, so nothing in the Session lifecycle
        /// notices this on its own.
        /// </remarks>
        /// <param name="secureChannelId">The SecureChannel that went away.</param>
        /// <param name="reason">Why it went away.</param>
        public void AbortDataChannelsOfSecureChannel(string secureChannelId, StatusCode reason)
        {
            if (string.IsNullOrEmpty(secureChannelId) ||
                !m_dataChannelStates.TryRemove(
                    secureChannelId,
                    out DataChannelSecureChannelState? state) ||
                state == null)
            {
                return;
            }

            state.Manager.ChannelStateChanged -= OnDataChannelStateChanged;
            state.Manager.AbortAll(reason);

            // A transport that holds per-SecureChannel state of its own gets
            // told too, so its streams are released rather than left bound to
            // channels that no longer exist.
            DataChannelTransport?.AbortSecureChannel(state.SecureChannel, reason);

            _ = state.Manager.DisposeAsync().AsTask();
        }

        /// <summary>
        /// Releases the per-SecureChannel data channel state once no Session
        /// remains on that SecureChannel.
        /// </summary>
        /// <remarks>
        /// The state holds a <see cref="DataChannelManager"/> with a running
        /// scheduler, so retaining one per SecureChannel ever seen would let
        /// any peer that can open and close a SecureChannel accumulate them
        /// without bound — and a data channel cannot be authorized on a
        /// SecureChannel with no Session anyway, so nothing is lost by
        /// releasing it.
        /// </remarks>
        private void ReapDataChannelStates()
        {
            if (m_dataChannelStates.IsEmpty)
            {
                return;
            }

            var live = new HashSet<string>(StringComparer.Ordinal);

            foreach (ISession candidate in ServerInternal.SessionManager.GetSessions())
            {
                string? id = candidate?.SecureChannelId;

                if (!string.IsNullOrEmpty(id))
                {
                    live.Add(id!);
                }
            }

            foreach (string secureChannelId in m_dataChannelStates.Keys)
            {
                if (!live.Contains(secureChannelId))
                {
                    AbortDataChannelsOfSecureChannel(
                        secureChannelId,
                        StatusCodes.BadSecureChannelClosed);
                }
            }
        }

        /// <summary>
        /// Aborts data channels when a Session changes identity or SecureChannel.
        /// </summary>
        protected virtual void OnDataChannelSessionActivated(ISession session, SessionEventReason reason)
        {
            AbortDataChannelsOfSession(session.Id, StatusCodes.BadUserAccessDenied);
            _ = RecheckDataChannelAuthorizationAsync(CancellationToken.None).AsTask();
        }

        /// <summary>
        /// Rechecks open data-channel authorization after Role configuration changes.
        /// </summary>
        protected virtual void OnDataChannelRoleConfigurationChanged(
            object? sender,
            RoleConfigurationChangedEventArgs e)
        {
            _ = RecheckDataChannelAuthorizationAsync(CancellationToken.None).AsTask();
        }

        /// <summary>
        /// Re-evaluates authorization for every open data channel.
        /// </summary>
        protected virtual async ValueTask<int> RecheckDataChannelAuthorizationAsync(
            CancellationToken ct)
        {
            int aborted = 0;
            foreach (DataChannelSecureChannelState state in m_dataChannelStates.Values)
            {
                aborted += await state.Handler.RecheckAuthorizationAsync(
                    sessionId => CreateRecheckContext(state, sessionId),
                    ct).ConfigureAwait(false);
            }

            return aborted;
        }

        private void InitializeDataChannelServices()
        {
            ServerInternal.SessionManager.SessionClosing += OnDataChannelSessionClosing;
            ServerInternal.SessionManager.SessionActivated += OnDataChannelSessionActivated;
            ServerInternal.RoleManager.RoleConfigurationChanged += OnDataChannelRoleConfigurationChanged;
            m_dataChannelAuthorizationTimer = TimeProvider.CreateTimer(
                _ => _ = RecheckDataChannelAuthorizationAsync(CancellationToken.None).AsTask(),
                null,
                DataChannelAuthorizationRecheckInterval,
                DataChannelAuthorizationRecheckInterval);
        }

        private void ShutdownDataChannelServices()
        {
            m_dataChannelAuthorizationTimer?.Dispose();
            m_dataChannelAuthorizationTimer = null;

            try
            {
                ServerInternal.SessionManager.SessionClosing -= OnDataChannelSessionClosing;
                ServerInternal.SessionManager.SessionActivated -= OnDataChannelSessionActivated;
                ServerInternal.RoleManager.RoleConfigurationChanged -= OnDataChannelRoleConfigurationChanged;
            }
            catch (ServiceResultException)
            {
            }

            foreach (DataChannelSecureChannelState state in m_dataChannelStates.Values)
            {
                state.Manager.AbortAll(StatusCodes.BadServerHalted);
            }

            m_dataChannelStates.Clear();
        }

        private DataChannelServiceHandler GetDataChannelHandler(
            SecureChannelContext secureChannelContext)
        {
            return m_dataChannelStates.GetOrAdd(
                secureChannelContext.SecureChannelId,
                _ => CreateDataChannelState(secureChannelContext)).Handler;
        }

        private DataChannelSecureChannelState CreateDataChannelState(
            SecureChannelContext secureChannelContext)
        {
            DataChannelManager manager;
            uint maxFrameSize;
            bool isReliable;

            // CA2000 cannot follow manager ownership through the transport out parameter
            // and the state record that owns the manager.
            // TODO: Replace the out-parameter contract with an ownership-bearing result type.
#pragma warning disable CA2000
            if (DataChannelTransport?.TryGetManager(
                secureChannelContext,
                DataChannelCapabilities,
                ServerInternal.Telemetry,
                out manager!,
                out maxFrameSize,
                out isReliable) != true &&
                !s_inlineDataChannelTransport.TryGetManager(
                    secureChannelContext,
                    DataChannelCapabilities,
                    ServerInternal.Telemetry,
                    out manager!,
                    out maxFrameSize,
                    out isReliable))
            {
                // Accepting the Service and then dropping every frame is what
                // Part 6 errata §5.16 forbids: a capability difference is
                // refused at the Service level "and never by dropping frames",
                // because a sender that believes it was delivered gets no gap
                // to detect and no GAP frame will ever arrive.
                throw ServiceResultException.Create(
                    StatusCodes.BadDataChannelTransportUnsupported,
                    "No data channel transport is attached to the SecureChannel carrying this request.");
            }

            var state = new DataChannelSecureChannelState(
                secureChannelContext,
                manager,
                new DataChannelServiceHandler(
                    manager,
                    DataChannelSources,
                    DataChannelCapabilities,
                    DataChannelAuthorizer ?? new ReadEquivalentDataChannelAuthorizer(ServerInternal),
                    DataChannelAuditor ?? new ServerDataChannelAuditor(ServerInternal),
                    new ServerDataChannelStreamAllocator(this, secureChannelContext),
                    TimeProvider),
                maxFrameSize,
                isReliable);
#pragma warning restore CA2000

            manager.ChannelStateChanged += OnDataChannelStateChanged;
            return state;
        }

        private DataChannelRequestContext CreateDataChannelRequestContext(
            OperationContext context,
            SecureChannelContext secureChannelContext,
            ulong transportChannelId)
        {
            DataChannelSecureChannelState state = m_dataChannelStates.GetOrAdd(
                secureChannelContext.SecureChannelId,
                _ => CreateDataChannelState(secureChannelContext));

            return new DataChannelRequestContext
            {
                SessionId = context.Session.Id,
                IsSessionActivated = context.Session.Activated,
                SecurityMode = secureChannelContext.EndpointDescription?.SecurityMode ??
                    MessageSecurityMode.Invalid,
                TransportProfileUri = secureChannelContext.EndpointDescription?.TransportProfileUri ??
                    Profiles.UaTcpTransport,
                TransportChannelId = transportChannelId,
                TransportIsReliable = state.TransportIsReliable,
                TransportMaxFrameSize = state.TransportMaxFrameSize,
                ClientAuditEntryId = context.AuditEntryId,
                ClientUserId = context.UserIdentity?.DisplayName
            };
        }

        private DataChannelRequestContext? CreateRecheckContext(
            DataChannelSecureChannelState state,
            NodeId sessionId)
        {
            foreach (ISession session in ServerInternal.SessionManager.GetSessions())
            {
                if (session.Id == sessionId)
                {
                    return new DataChannelRequestContext
                    {
                        SessionId = session.Id,
                        IsSessionActivated = session.Activated,
                        SecurityMode = state.SecureChannel.EndpointDescription?.SecurityMode ??
                            MessageSecurityMode.Invalid,
                        TransportProfileUri =
                            state.SecureChannel.EndpointDescription?.TransportProfileUri ??
                            Profiles.UaTcpTransport,
                        TransportIsReliable = state.TransportIsReliable,
                        TransportMaxFrameSize = state.TransportMaxFrameSize,
                        ClientUserId = session.EffectiveIdentity?.DisplayName
                    };
                }
            }

            return null;
        }

        private void AbortDataChannelsOfSession(NodeId sessionId, StatusCode reason)
        {
            foreach (DataChannelSecureChannelState state in m_dataChannelStates.Values)
            {
                state.Handler.AbortChannelsOfSession(sessionId, reason);
            }
        }

        private void OnDataChannelStateChanged(
            object? sender,
            DataChannelStateChangedEventArgs e)
        {
            ISystemContext systemContext = ServerInternal.DefaultSystemContext;
            var values = DataChannelModel.BuildStateChangeEvent(e);
            var ev = new DataChannelStateChangeEventState(null);

            // Initialize is what populates EventId, EventType, Time and
            // Severity. Without it TypeDefinitionId is null, so an EventFilter
            // selecting OfType(DataChannelStateChangeEventType) never matches
            // and the Event is emitted but invisible.
            ev.Initialize(
                systemContext,
                null,
                EventSeverity.Low,
                new LocalizedText("DataChannelStateChangeEvent"));

            ev.SetChildValue(systemContext, BrowseNames.SourceNode, ObjectIds.Server, false);
            ev.SetChildValue(systemContext, BrowseNames.SourceName, "DataChannel/StateChange", false);
            ev.SetChildValue(systemContext, BrowseNames.ChannelId, values.ChannelId, false);
            ev.SetChildValue(systemContext, BrowseNames.State, (int)values.State, false);
            ev.SetChildValue(systemContext, BrowseNames.Status, values.Status, false);
            ServerInternal.ReportEvent(systemContext, ev);
        }

        private sealed record DataChannelSecureChannelState(
            SecureChannelContext SecureChannel,
            DataChannelManager Manager,
            DataChannelServiceHandler Handler,
            uint TransportMaxFrameSize,
            bool TransportIsReliable);

        private sealed class ServerDataChannelStreamAllocator(
            StandardServer server,
            SecureChannelContext secureChannelContext) : IDataChannelTransportStreamAllocator
        {
            public ValueTask<ulong> AllocateServerStreamAsync(
                DataChannelRequestContext context,
                uint channelId,
                DataChannelDirection direction,
                CancellationToken ct)
            {
                IServerDataChannelTransport? transport = server.DataChannelTransport;
                if (transport == null)
                {
                    throw new ServiceResultException(StatusCodes.BadDataChannelTransportUnsupported);
                }

                return transport.AllocateServerStreamAsync(
                    secureChannelContext,
                    channelId,
                    direction,
                    ct);
            }

            public ValueTask BindClientStreamAsync(
                DataChannelRequestContext context,
                uint channelId,
                ulong streamId,
                DataChannelDirection direction,
                CancellationToken ct)
            {
                IServerDataChannelTransport? transport = server.DataChannelTransport;
                if (transport == null)
                {
                    throw new ServiceResultException(StatusCodes.BadDataChannelTransportUnsupported);
                }

                return transport.BindClientStreamAsync(
                    secureChannelContext,
                    channelId,
                    streamId,
                    direction,
                    ct);
            }
        }

        /// <summary>
        /// Authorizes a data channel exactly as a Read of the same content
        /// would be authorized.
        /// </summary>
        /// <remarks>
        /// Part 4 errata §7.2 requires that a Server "shall not grant a data
        /// channel where it would refuse a Read of the same content", and
        /// that the decision be re-evaluated rather than granted once. This
        /// resolves the Session, builds an OperationContext that carries the
        /// request SecureChannel security properties, and applies the same
        /// permission metadata composition as <see cref="MasterNodeManager"/>:
        /// RolePermissions/UserRolePermissions first, then AccessRestrictions.
        /// It fails closed: a Session that cannot be resolved, a source with
        /// no owning NodeManager, or any error during validation denies the
        /// request. Registry-only sources therefore require an explicit
        /// <see cref="DataChannelAuthorizer"/> configured by the application.
        /// </remarks>
        private sealed class ReadEquivalentDataChannelAuthorizer(IServerInternal server)
            : IDataChannelAuthorizer
        {
            public async ValueTask<bool> IsAuthorizedAsync(
                DataChannelRequestContext context,
                NodeId sourceNodeId,
                DataChannelDirection direction,
                CancellationToken ct)
            {
                if (context == null || sourceNodeId.IsNull)
                {
                    return false;
                }

                try
                {
                    ISession? session = null;

                    foreach (ISession candidate in server.SessionManager.GetSessions())
                    {
                        if (candidate != null && candidate.Id == context.SessionId)
                        {
                            session = candidate;
                            break;
                        }
                    }

                    if (session == null)
                    {
                        return false;
                    }

                    (object? nodeHandle, IAsyncNodeManager? nodeManager) = await server.NodeManager
                        .GetManagerHandleAsync(sourceNodeId, ct)
                        .ConfigureAwait(false);

                    if (nodeManager == null || nodeHandle == null)
                    {
                        return false;
                    }

                    using var operationContext = CreateReadOperationContext(context, session);
                    NodeMetadata? nodeMetadata = await nodeManager.GetPermissionMetadataAsync(
                            operationContext,
                            nodeHandle,
                            BrowseResultMask.NodeClass,
                            [],
                            permissionsOnly: true,
                            ct)
                        .ConfigureAwait(false);

                    nodeMetadata ??= await nodeManager.GetNodeMetadataAsync(
                            operationContext,
                            nodeHandle,
                            BrowseResultMask.NodeClass,
                            ct)
                        .ConfigureAwait(false);

                    if (nodeMetadata == null)
                    {
                        // Without metadata there is nothing to evaluate, and
                        // an unevaluated source is not an authorized one.
                        // Granting here would reopen the fail-open path that
                        // treating an unknown Node as permitted created.
                        return false;
                    }

                    // Each required permission is validated on its own:
                    // ValidateRolePermissions treats a combined mask as "any
                    // of these", so asking for Read|Write in one call would
                    // pass for a user who only has Read - which is exactly the
                    // case §7.2 exists to refuse.
                    ServiceResult result = ServiceResult.Good;

                    foreach (PermissionType required in RequiredPermissions(direction))
                    {
                        result = MasterNodeManager.ValidateRolePermissions(
                            operationContext,
                            nodeMetadata,
                            required);

                        if (ServiceResult.IsBad(result))
                        {
                            break;
                        }
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        result = MasterNodeManager.ValidateAccessRestrictions(
                            operationContext,
                            nodeMetadata);
                    }

                    return ServiceResult.IsGood(result);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // An authorization decision that threw is a denial, not a
                    // grant. Anything else would make an unrelated fault in
                    // the NodeManager open the channel.
                    return false;
                }
            }

            /// <summary>
            /// The permissions a channel of this direction requires on the
            /// source Node, each of which has to hold on its own.
            /// </summary>
            /// <remarks>
            /// Part 4 errata §7.2: a channel that carries payload towards the
            /// source is a write, so read permission alone does not grant it.
            /// A <c>Bidirectional</c> channel needs both, because it is both.
            /// </remarks>
            /// <param name="direction">The negotiated direction.</param>
            private static PermissionType[] RequiredPermissions(DataChannelDirection direction)
            {
                return direction switch
                {
                    DataChannelDirection.SourceToSink => [PermissionType.Read],
                    DataChannelDirection.SinkToSource => [PermissionType.Write],
                    DataChannelDirection.Bidirectional
                        => [PermissionType.Read, PermissionType.Write],

                    // An unrecognized direction is not one this authorizer can
                    // reason about, so it demands everything a channel could
                    // need rather than the least.
                    _ => [PermissionType.Read, PermissionType.Write]
                };
            }

            private static OperationContext CreateReadOperationContext(
                DataChannelRequestContext context,
                ISession session)
            {
                string transportProfileUri = string.IsNullOrEmpty(context.TransportProfileUri)
                    ? Profiles.UaTcpTransport
                    : context.TransportProfileUri;

                var endpoint = new EndpointDescription
                {
                    SecurityMode = context.SecurityMode,
                    TransportProfileUri = transportProfileUri
                };

                var secureChannel = new SecureChannelContext(
                    session.SecureChannelId ?? string.Empty,
                    endpoint,
                    RequestEncoding.Binary);

                return new OperationContext(
                    new RequestHeader(),
                    secureChannel,
                    RequestType.Read,
                    RequestLifetime.None,
                    session);
            }
        }

        private sealed class ServerDataChannelAuditor(IServerInternal server) : IDataChannelAuditor
        {
            public void OnOpenDataChannel(
                DataChannelRequestContext context,
                NodeId sourceNodeId,
                DataChannelParametersDataType parameters,
                uint? channelId,
                StatusCode status)
            {
                if (!server.Auditing)
                {
                    return;
                }

                ISystemContext systemContext = server.DefaultAuditContext;
                var ev = new AuditOpenDataChannelEventState(null);
                ev.Initialize(
                    systemContext,
                    null,
                    EventSeverity.Min,
                    new LocalizedText("AuditOpenDataChannelEvent"),
                    StatusCode.IsGood(status),
                    DateTime.UtcNow);
                ev.SetChildValue(systemContext, BrowseNames.SourceNode, ObjectIds.Server, false);
                ev.SetChildValue(systemContext, BrowseNames.SourceName, "Session/OpenDataChannel", false);
                ev.SetChildValue(systemContext, BrowseNames.ClientAuditEntryId, context.ClientAuditEntryId!, false);
                ev.SetChildValue(systemContext, BrowseNames.ClientUserId, context.ClientUserId!, false);
                ev.SetChildValue(systemContext, BrowseNames.DataChannelSourceNodeId, sourceNodeId, false);
                ev.SetChildValue(systemContext, BrowseNames.Parameters, parameters, false);
                ev.SetChildValue(systemContext, BrowseNames.ChannelId, channelId.GetValueOrDefault(), false);
                server.ReportAuditEvent(systemContext, ev);
            }
        }

        private static readonly InlineServerDataChannelTransport s_inlineDataChannelTransport = new();

        private readonly ConcurrentDictionary<string, DataChannelSecureChannelState> m_dataChannelStates = new();
        private ITimer? m_dataChannelAuthorizationTimer;
    }
}
