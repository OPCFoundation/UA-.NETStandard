/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.DataChannels;
using MetadataExtensions = Opc.Ua.DataChannelMetadata.OpcUaDataChannelMetadataExtensions;

namespace Opc.Ua.Server.DataChannels
{
    /// <summary>
    /// Opt-in installation of the generated Data Channel model and ordinary Call handlers.
    /// </summary>
    internal sealed class DataChannelNodeManagerFactory(ServerDataChannelProvider provider) : IAsyncNodeManagerFactory
    {
        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris => DataChannelNodeManager.GetNamespaceUris(provider);

        /// <inheritdoc/>
        public async ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            var manager = new DataChannelNodeManager(server, configuration, provider);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return manager;
            }
            catch
            {
                await manager.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
    }

    /// <summary>
    /// Hosts exact generated singleton factories, runtime-indexed metadata and Session-projected source state.
    /// </summary>
    internal sealed class DataChannelNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// Creates the independently opt-in model host.
        /// </summary>
        internal DataChannelNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ServerDataChannelProvider provider)
            : base(server, configuration, true, GetNamespaceUris(provider).ToArray() ?? [])
        {
            m_provider = provider;
            m_provider.AttachServer(server);
            m_eventLogger = server.Telemetry.CreateLogger<DataChannelNodeManager>();
            m_clock = (server as ITimeProviderProvider)?.TimeProvider ?? TimeProvider.System;
            m_events = Channel.CreateBounded<IFilterTarget>(
                new BoundedChannelOptions(provider.Options.MaxPendingControls)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = false
                });
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            DataChannelManagerState manager = OpcUaExtensions.CreateDataChannelManager(SystemContext, false);
            DataChannelCapabilitiesState capabilities =
                OpcUaExtensions.CreateDataChannelCapabilities(SystemContext, false);
            manager.AddDiagnostics(SystemContext, default);
            PrepareAuthoredNodeIdsForRegistration(manager.Diagnostics!);
            manager.OpenDataChannel!.OnCallMethod2Async = OnCallAsync;
            manager.ModifyDataChannel!.OnCallMethod2Async = OnCallAsync;
            manager.CloseDataChannel!.OnCallMethod2Async = OnCallAsync;
            manager.OpenDataChannel.OnCallValidationFailedAsync = OnValidationFailedAsync;
            manager.ModifyDataChannel.OnCallValidationFailedAsync = OnValidationFailedAsync;
            manager.CloseDataChannel.OnCallValidationFailedAsync = OnValidationFailedAsync;
            BindProjection(manager.Channels!, null, Projection.Channels);
            BindProjection(manager.Diagnostics!, null, Projection.Diagnostics);
            Link(externalReferences, ObjectIds.Server, ReferenceTypeIds.HasComponent, manager);
            Link(externalReferences, ObjectIds.Server_ServerCapabilities, ReferenceTypeIds.HasComponent, capabilities);
            PopulateCapabilities(capabilities);
            await AddPredefinedNodeAsync(manager, cancellationToken).ConfigureAwait(false);
            await AddPredefinedNodeAsync(capabilities, cancellationToken).ConfigureAwait(false);

            NamespaceMetadataState metadata = MetadataExtensions.CreateDataChannelNamespace(SystemContext, false);
            Link(externalReferences, ObjectIds.Server_Namespaces, ReferenceTypeIds.HasComponent, metadata);
            await AddPredefinedNodeAsync(metadata, cancellationToken).ConfigureAwait(false);
            foreach (DataChannelSource source in m_provider.Sources.Sources.ToList())
            {
                if (source.SourceNodeId.IsNull)
                {
                    continue;
                }
                NodeId nodeId = ExpandedNodeId.ToNodeId(source.SourceNodeId, Server.NamespaceUris);
                if (nodeId.IsNull || nodeId.NamespaceIndex == 0 || nodeId.NamespaceIndex ==
                    Server.NamespaceUris.GetIndex(kMetadataNamespace))
                {
                    throw new InvalidOperationException("A source Object needs its own application-namespace NodeId.");
                }
                DataChannelSourceState state = SystemContext.CreateInstanceOfDataChannelSourceType();
                state.NodeId = nodeId;
                state.BrowseName = new QualifiedName(source.ResourceUri, nodeId.NamespaceIndex);
                state.DisplayName = new LocalizedText(source.ResourceUri);
                state.AddActiveChannelCount(SystemContext, default)
                    .AddChannels(SystemContext, default)
                    .AddDiagnostics(SystemContext, default);
                PrepareAuthoredNodeIdsForRegistration(state);
                PopulateSource(state, source);
                List<NodeState> sourceNodes = [state];
                for (int ii = 0; ii < sourceNodes.Count; ii++)
                {
                    NodeState sourceNode = sourceNodes[ii];
                    m_sourceNodes.Add(sourceNode.NodeId, source);
                    List<BaseInstanceState> children = [];
                    sourceNode.GetChildren(SystemContext, children);
                    sourceNodes.AddRange(children);
                }
                Link(externalReferences, ObjectIds.ObjectsFolder, ReferenceTypeIds.Organizes, state);
                foreach (ExpandedNodeId content in source.ContentNodeIds)
                {
                    Link(externalReferences, ExpandedNodeId.ToNodeId(content, Server.NamespaceUris),
                        ReferenceTypeIds.HasDataChannel, state);
                }
                await AddPredefinedNodeAsync(state, cancellationToken).ConfigureAwait(false);
            }
            m_provider.StateChanged += OnStateChanged;
            m_provider.Audited += OnAudit;
            m_provider.Offered += OnOffered;
            m_publisher = PublishEventsAsync();
        }

        /// <inheritdoc/>
        public override async ValueTask<ServiceResult> ValidateRolePermissionsAsync(
            OperationContext context,
            NodeId nodeId,
            PermissionType permission,
            CancellationToken cancellationToken = default)
        {
            ServiceResult result = await base.ValidateRolePermissionsAsync(
                context, nodeId, permission, cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(result) || !m_sourceNodes.TryGetValue(nodeId, out DataChannelSource? source))
            {
                return result;
            }
            return context.Session != null &&
                await m_provider.CanInspectSourceAsync(context.Session, source, cancellationToken).ConfigureAwait(false)
                    ? ServiceResult.Good
                    : StatusCodes.BadUserAccessDenied;
        }

        /// <inheritdoc/>
        public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            StopPublishing();
            await m_publisher.ConfigureAwait(false);
            await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Includes namespace zero only for the exact assigned singleton nodes.
        /// Dynamic children use an application namespace; metadata contains only its eight registered nodes.
        /// </summary>
        internal static ArrayOf<string> GetNamespaceUris(ServerDataChannelProvider provider)
        {
            List<string> uris = [kRuntimeNamespace, global::Opc.Ua.Namespaces.OpcUa, kMetadataNamespace];
            foreach (DataChannelSource source in provider.Sources.Sources)
            {
                string? uri = source.SourceNodeId.NamespaceUri;
                if (!string.IsNullOrEmpty(uri) && !uris.Contains(uri!))
                {
                    uris.Add(uri!);
                }
            }
            return [.. uris];
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopPublishing();
            }
            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        protected override async ValueTask DisposeAsyncCore()
        {
            StopPublishing();
            await m_publisher.ConfigureAwait(false);
            await base.DisposeAsyncCore().ConfigureAwait(false);
            m_lifetime.Dispose();
        }

        /// <summary>
        /// Invokes only the exact generated manager Methods over the existing Call service.
        /// </summary>
        private async ValueTask<ServiceResult> OnCallAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> input,
            List<Variant> output,
            CancellationToken cancellationToken)
        {
            ServiceResult result;
            ManagerCallAudit? previousAudit = m_callAudit.Value;
            var callAudit = new ManagerCallAudit();
            m_callAudit.Value = callAudit;
            try
            {
                ISession session = GetSession(context) ??
                    throw new ServiceResultException(StatusCodes.BadSessionNotActivated);
                if (objectId != ObjectIds.DataChannelManager)
                {
                    result = StatusCodes.BadMethodInvalid;
                }
                else
                {
                    output.Clear();
                    if (method.NodeId == MethodIds.DataChannelManager_OpenDataChannel &&
                        input.Count == 4 && input[0].TryGetValue(out uint openId) &&
                        input[1].TryGetValue(out NodeId sourceId) &&
                        input[2].TryGetStructure<DataChannelBindingDataType>(
                            out DataChannelBindingDataType? binding) &&
                        binding != null &&
                        input[3].TryGetStructure<DataChannelParametersDataType>(
                            out DataChannelParametersDataType? parameters) && parameters != null)
                    {
                        DataChannelGrantDataType grant = await m_provider.OpenAsync(
                            session, openId, sourceId, binding, parameters, cancellationToken).ConfigureAwait(false);
                        output.Add(Variant.FromStructure(grant));
                        result = ServiceResult.Good;
                    }
                    else if (method.NodeId == MethodIds.DataChannelManager_ModifyDataChannel &&
                        input.Count == 4 && input[0].TryGetValue(out uint modifyId) &&
                        input[1].TryGetValue(out uint operationId) && input[2].TryGetValue(out uint revision) &&
                        input[3].TryGetStructure<DataChannelParametersDataType>(
                            out DataChannelParametersDataType? proposed) && proposed != null)
                    {
                        DataChannelStatusDataType status = await m_provider.ModifyAsync(
                            session, modifyId, operationId, revision, proposed, cancellationToken)
                            .ConfigureAwait(false);
                        output.Add(Variant.FromStructure(status));
                        result = MethodResult(status.Result);
                    }
                    else if (method.NodeId == MethodIds.DataChannelManager_CloseDataChannel &&
                        input.Count == 2 && input[0].TryGetValue(out uint closeId) &&
                        input[1].TryGetValue(out bool discard))
                    {
                        DataChannelStatusDataType status = await m_provider.CloseAsync(
                            session, closeId, discard, cancellationToken).ConfigureAwait(false);
                        output.Add(Variant.FromStructure(status));
                        result = MethodResult(status.Result);
                    }
                    else
                    {
                        result = StatusCodes.BadInvalidArgument;
                    }
                }
            }
            catch (ServiceResultException exception)
            {
                result = exception.Result;
            }
            catch (DataChannelException exception)
            {
                result = DataChannelControlModel.ToStatusCode(exception.Result);
            }
            finally
            {
                callAudit.Complete();
                m_callAudit.Value = previousAudit;
            }
            ReportManagerCallAudit(context, method, input, [.. output], result.StatusCode, callAudit.Record);
            return result;
        }

        /// <summary>
        /// Audits rejected argument and executable validation without retaining untrusted argument data.
        /// </summary>
        private ValueTask OnValidationFailedAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> input,
            ServiceResult error,
            CancellationToken cancellationToken)
        {
            ISession? session = GetSession(context);
            var audit = new DataChannelControlAudit(
                method.BrowseName.Name ?? string.Empty, ExpandedNodeId.Null, string.Empty,
                session?.ClientApplicationUri ?? string.Empty, Guid.Empty, 0, 0,
                session?.Id ?? NodeId.Null, 0, 0,
                error.StatusCode == StatusCodes.BadUserAccessDenied
                    ? DataChannelResult.NotAuthorized
                    : DataChannelResult.InvalidArgument);
            ReportManagerCallAudit(context, method, input, [], error.StatusCode, audit);
            return default;
        }

        /// <summary>
        /// Emits the fieldless Method audit and its privileged, EventId-correlated operation outcome.
        /// </summary>
        private void ReportManagerCallAudit(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> input,
            ArrayOf<Variant> output,
            StatusCode status,
            DataChannelControlAudit? operation)
        {
            if (!Server.Auditing)
            {
                return;
            }
            AuditDataChannelEventState audit = context.CreateInstanceOfAuditDataChannelEventType();
            audit.Initialize(context, null, EventSeverity.Low,
                new LocalizedText($"Data channel {method.BrowseName.Name}: {status}."),
                StatusCode.IsGood(status), m_clock.GetUtcNow().UtcDateTime);
            audit.SourceNode!.Value = ObjectIds.DataChannelManager;
            audit.SourceName!.Value = "Attribute/Call";
            audit.MethodId!.Value = method.NodeId;
            audit.InputArguments!.Value = RedactAuditArguments(method.NodeId, input, status);
            audit.AddStatusCodeId(context, default);
            audit.StatusCodeId!.Value = status;
            audit.AddOutputArguments(context, default);
            audit.OutputArguments!.Value = method.NodeId == MethodIds.DataChannelManager_OpenDataChannel
                ? [.. Enumerable.Repeat(Variant.Null, output.Count)] : output;
            audit.ClientAuditEntryId!.Value = context.AuditEntryId ?? string.Empty;
            audit.ClientUserId!.Value =
                (context as ISessionSystemContext)?.UserIdentity?.DisplayName ?? string.Empty;
            QueueEvent(audit, CanReceiveAudit);
            operation ??= new DataChannelControlAudit(
                method.BrowseName.Name ?? string.Empty, ExpandedNodeId.Null, string.Empty,
                string.Empty, Guid.Empty, 0, 0, GetSession(context)?.Id ?? NodeId.Null, 0, 0,
                StatusCode.IsGood(status) ? DataChannelResult.Good : DataChannelResult.InvalidArgument);
            ReportOperationEvent(operation, audit.EventId!.Value, audit.Status!.Value, audit.ClientUserId.Value);
        }

        /// <summary>
        /// Preserves method argument positions while removing binding material and unauthorized flow identifiers.
        /// </summary>
        internal static ArrayOf<Variant> RedactAuditArguments(
            NodeId methodId,
            ArrayOf<Variant> input,
            StatusCode statusCode)
        {
            List<Variant> arguments = [];
            foreach (Variant argument in input)
            {
                arguments.Add(argument);
            }
            if (methodId == MethodIds.DataChannelManager_OpenDataChannel && arguments.Count > 2)
            {
                arguments[2] = Variant.Null;
            }
            else if (StatusCode.IsBad(statusCode) &&
                (statusCode == StatusCodes.BadUserAccessDenied || statusCode == StatusCodes.BadNodeIdUnknown))
            {
                int identifierIndex = methodId == MethodIds.DataChannelManager_ModifyDataChannel ? 0
                    : methodId == MethodIds.DataChannelManager_CloseDataChannel ? 0 : -1;
                if (identifierIndex >= 0 && identifierIndex < arguments.Count)
                {
                    arguments[identifierIndex] = Variant.Null;
                }
            }
            return [.. arguments];
        }

        /// <summary>
        /// Treats a staged operation as an accepted Call, not completed data-path success.
        /// </summary>
        private static ServiceResult MethodResult(DataChannelResult result)
        {
            return result is DataChannelResult.Good or DataChannelResult.WouldBlock
                ? ServiceResult.Good
                : DataChannelControlModel.ToStatusCode(result);
        }

        /// <summary>
        /// Resolves the actual operation Session; no input value can substitute for it.
        /// </summary>
        private ISession? GetSession(ISystemContext context)
        {
            if (context is not ISessionSystemContext sessionContext ||
                sessionContext.SessionId is not { } sessionId || sessionId.IsNull)
            {
                return null;
            }
            foreach (ISession session in Server.SessionManager.GetSessions())
            {
                if (session.Id == sessionId && session.Activated &&
                    !session.IsClosing && !session.HasExpired)
                {
                    return session;
                }
            }
            return null;
        }

        /// <summary>
        /// Advertises actual registered protocol capabilities and live available endpoint profiles.
        /// </summary>
        private void PopulateCapabilities(DataChannelCapabilitiesState capabilities)
        {
            capabilities.MaxFlows!.Value = (uint)m_provider.Options.MaxFlows;
            capabilities.MaxMessageSize!.Value = (uint)m_provider.Options.MaxMessageSize;
            capabilities.MaxReceiveBytes!.Value = m_provider.Options.MaxReceiveBytes;
            HashSet<string> protocols = new(StringComparer.Ordinal);
            HashSet<DataChannelDeliveryMode> delivery = [];
            foreach (DataChannelSource source in m_provider.Sources.Sources)
            {
                foreach (string protocol in source.SupportedProtocols)
                {
                    protocols.Add(protocol);
                }
                foreach (DataChannelDeliveryMode mode in source.SupportedDeliveryModes)
                {
                    delivery.Add(mode);
                }
            }
            capabilities.SupportedProtocols!.Value = [.. protocols];
            capabilities.SupportedDeliveryModes!.Value = [.. delivery];
            capabilities.SupportedTransportProfileUris!.OnSimpleReadValueAsync = (context, node, ct) =>
                new ValueTask<AttributeSimpleReadResult>(new AttributeSimpleReadResult(
                    ServiceResult.Good, new Variant(m_provider.GetSupportedTransportProfiles())));
        }

        /// <summary>
        /// Populates real source declarations and installs sampled per-Session projections.
        /// </summary>
        private void PopulateSource(DataChannelSourceState state, DataChannelSource source)
        {
            state.ResourceUri!.Value = source.ResourceUri;
            state.SupportedProtocols!.Value = source.SupportedProtocols;
            state.SupportedDirections!.Value = source.SupportedDirections;
            state.SupportedDeliveryModes!.Value = source.SupportedDeliveryModes;
            state.SupportedProtectionModes!.Value = source.SupportedProtectionModes;
            state.ContentType!.Value = source.ContentType;
            state.MaxMessageSize!.Value = source.MaxMessageSize;
            state.MaxReceiveBytes!.Value = source.MaxReceiveBytes;
            state.MaxRate!.Value = source.MaxRate;
            BindProjection(state.ActiveChannelCount!, source.ResourceUri, Projection.Count);
            BindProjection(state.Channels!, source.ResourceUri, Projection.Channels);
            BindProjection(state.Diagnostics!, source.ResourceUri, Projection.Diagnostics);
        }

        /// <summary>
        /// Sampling reads the value in each MonitoredItem's Session context. No shared reporter-value
        /// change notification is emitted for these user-dependent variables.
        /// </summary>
        private void BindProjection(BaseVariableState variable, string? resource, Projection projection)
        {
            variable.MinimumSamplingInterval = 100;
            variable.OnSimpleReadValueAsync = async (context, node, cancellationToken) =>
            {
                ISession? session = GetSession(context);
                if (session == null)
                {
                    return new AttributeSimpleReadResult(StatusCodes.BadUserAccessDenied, Variant.Null);
                }
                if (projection == Projection.Diagnostics)
                {
                    return new AttributeSimpleReadResult(ServiceResult.Good, Variant.FromStructure(
                        await m_provider.GetDiagnosticsAsync(session, resource, cancellationToken)
                            .ConfigureAwait(false)));
                }
                ArrayOf<DataChannelStatusDataType> statuses = await m_provider.GetChannelsAsync(
                    session, resource, cancellationToken).ConfigureAwait(false);
                if (projection == Projection.Count)
                {
                    uint active = 0;
                    foreach (DataChannelStatusDataType status in statuses)
                    {
                        if (status.State is DataChannelState.Open or DataChannelState.Updating or
                            DataChannelState.Draining)
                        {
                            active++;
                        }
                    }
                    return new AttributeSimpleReadResult(ServiceResult.Good, new Variant(active));
                }
                return new AttributeSimpleReadResult(ServiceResult.Good, Variant.FromStructure(statuses));
            };
        }

        /// <summary>
        /// Adds the reverse half of a cross-manager relationship without inventing assigned identifiers.
        /// </summary>
        private static void Link(
            IDictionary<NodeId, IList<IReference>> references,
            NodeId parent,
            NodeId referenceType,
            NodeState node)
        {
            if (!references.TryGetValue(parent, out IList<IReference>? children))
            {
                children = [];
                references.Add(parent, children);
            }
            bool linked = false;
            foreach (IReference reference in children)
            {
                if (!reference.IsInverse && reference.ReferenceTypeId == referenceType &&
                    reference.TargetId.Equals(node.NodeId))
                {
                    linked = true;
                    break;
                }
            }
            if (!linked)
            {
                children.Add(new NodeStateReference(referenceType, false, node.NodeId));
            }
            if (!node.ReferenceExists(referenceType, true, parent))
            {
                node.AddReference(referenceType, true, parent);
            }
        }

        /// <summary>
        /// Converts real lifecycle transitions to recipient-scoped model Events.
        /// </summary>
        private void OnStateChanged(DataChannelStateChangedEventArgs change)
        {
            DataChannelStatusDataType status = change.Status;
            DataChannelStateChangeEventState state = SystemContext.CreateInstanceOfDataChannelStateChangeEventType();
            state.Initialize(SystemContext, null, EventSeverity.Low, new LocalizedText("Data channel state changed."));
            state.SourceNode!.Value = ObjectIds.DataChannelManager;
            state.SourceName!.Value = "DataChannel/StateChange";
            state.ChannelId!.Value = status.ChannelId;
            state.ContextId!.Value = status.ContextId;
            state.Generation!.Value = status.Generation;
            state.SessionId!.Value = status.SessionId;
            state.State!.Value = status.State;
            state.Revision!.Value = status.Revision;
            state.Result!.Value = status.Result;
            QueueEvent(state, m_provider.CreateStateEventFilter(status));
        }

        /// <summary>
        /// Produces the actual offer event, with no grant or binding evidence.
        /// </summary>
        private void OnOffered(DataChannelOffer offer)
        {
            DataChannelOfferedEventState state = SystemContext.CreateInstanceOfDataChannelOfferedEventType();
            state.Initialize(SystemContext, null, EventSeverity.Low, new LocalizedText("Data channel source offered."));
            state.SourceNode!.Value = ObjectIds.DataChannelManager;
            state.SourceName!.Value = "DataChannel/Offer";
            state.SourceNodeId!.Value = ExpandedNodeId.ToNodeId(offer.SourceNodeId, Server.NamespaceUris);
            state.ResourceUri!.Value = offer.ResourceUri;
            state.Parameters!.Value = offer.Parameters;
            state.RecipientSessionId!.Value = offer.RecipientSessionId;
            state.ExpiresAt!.Value = offer.ExpiresAt;
            QueueEvent(state, RecipientFilter(offer.RecipientSessionId, offer.ExpiresAt.ToDateTime()));
        }

        /// <summary>
        /// Emits successful and refused admissions using the generated audit event and restricted consumers.
        /// </summary>
        private void OnAudit(DataChannelControlAudit audit)
        {
            if (m_callAudit.Value is { IsCompleted: false } call &&
                audit.Operation is "OpenDataChannel" or "ModifyDataChannel" or "CloseDataChannel")
            {
                call.Record = audit;
                return;
            }
            if (!Server.Auditing)
            {
                return;
            }
            ReportOperationEvent(audit, default, audit.Result == DataChannelResult.Good, string.Empty);
        }

        /// <summary>
        /// Reports privileged operation metadata without manufacturing a Method Call for autonomous actions.
        /// </summary>
        private void ReportOperationEvent(
            DataChannelControlAudit audit, ByteString auditEventId, bool succeeded, string clientUserId)
        {
            DataChannelOperationEventState state = SystemContext.CreateInstanceOfDataChannelOperationEventType();
            bool discloseFlow = audit.Result is not
                (DataChannelResult.NotAuthorized or DataChannelResult.UnknownChannel);
            state.Initialize(
                SystemContext, null, EventSeverity.Low,
                new LocalizedText(
                    $"Data channel {audit.Operation}: peer={audit.PeerApplicationUri}; " +
                    $"resource={(discloseFlow ? audit.ResourceUri : string.Empty)}; operation={audit.OperationId}."));
            state.SourceNode!.Value = ObjectIds.DataChannelManager;
            state.SourceName!.Value = "DataChannel/" + audit.Operation;
            state.SessionId!.Value = audit.SessionId;
            state.ChannelId!.Value = discloseFlow ? audit.ChannelId : 0;
            state.ContextId!.Value = discloseFlow ? audit.ContextId : Guid.Empty;
            state.Generation!.Value = discloseFlow ? audit.Generation : 0;
            state.Operation!.Value = audit.Operation;
            state.Revision!.Value = discloseFlow ? audit.Revision : 0;
            state.Result!.Value = audit.Result;
            state.DataChannelSourceNodeId!.Value =
                discloseFlow ? ExpandedNodeId.ToNodeId(audit.SourceNodeId, Server.NamespaceUris) : NodeId.Null;
            state.ClientUserId!.Value = clientUserId;
            state.ActionSucceeded!.Value = succeeded;
            state.AuditEventId!.Value = auditEventId;
            QueueEvent(state, CanReceiveAudit);
        }

        /// <summary>
        /// Restricts both correlated records before event field selection and publication.
        /// </summary>
        private static bool CanReceiveAudit(ISession session)
        {
            return session.Activated && !session.IsClosing && !session.HasExpired &&
                session.EffectiveIdentity.GrantedRoleIds.Contains(ObjectIds.WellKnownRole_SecurityAdmin);
        }

        /// <summary>
        /// Captures recipient identity continuity; changing the Session user invalidates queued hints.
        /// </summary>
        private Func<ISession, bool> RecipientFilter(NodeId recipient, DateTime? expiresAt)
        {
            foreach (ISession session in Server.SessionManager.GetSessions())
            {
                if (session.Id == recipient && ClientUserIdResolver.TryResolveContinuityKey(
                    session.IdentityToken, session.Identity, out string? identity))
                {
                    UserTokenType type = session.Identity.TokenType;
                    return candidate => m_provider.CanReceiveEvent(candidate, recipient) &&
                        (!expiresAt.HasValue || m_clock.GetUtcNow().UtcDateTime < expiresAt.Value) &&
                        candidate.Identity.TokenType == type &&
                        ClientUserIdResolver.TryResolveContinuityKey(
                            candidate.IdentityToken, candidate.Identity, out string? current) &&
                        string.Equals(current, identity, StringComparison.Ordinal);
                }
            }
            return static _ => false;
        }

        /// <summary>
        /// Captures immutable Event fields while preserving normal source/type permission evaluation.
        /// </summary>
        private void QueueEvent(BaseEventState state, Func<ISession, bool> visible)
        {
            state.Time!.Value = m_clock.GetUtcNow().UtcDateTime;
            state.ReceiveTime!.Value = state.Time.Value;
            IFilterTarget snapshot = DataChannelEventScope.Create(SystemContext, state, visible);
            if (!m_events.Writer.TryWrite(snapshot))
            {
                m_eventLogger.EventBufferFull();
            }
        }

        /// <summary>
        /// Runs one bounded asynchronous publisher for all control Events.
        /// </summary>
        private async Task PublishEventsAsync()
        {
            try
            {
                while (await m_events.Reader.WaitToReadAsync(m_lifetime.Token).ConfigureAwait(false))
                {
                    while (m_events.Reader.TryRead(out IFilterTarget? target))
                    {
                        await Server.ReportEventAsync(SystemContext, target, m_lifetime.Token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (m_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                m_eventLogger.EventDeliveryFailed(exception);
            }
        }

        /// <summary>
        /// Detaches the original provider before the server's managers disappear.
        /// </summary>
        private void StopPublishing()
        {
            if (Interlocked.Exchange(ref m_stopped, 1) != 0)
            {
                return;
            }
            m_provider.StateChanged -= OnStateChanged;
            m_provider.Audited -= OnAudit;
            m_provider.Offered -= OnOffered;
            m_provider.DetachServer(Server);
            m_events.Writer.TryComplete();
            m_lifetime.Cancel();
        }

        /// <summary>
        /// Identifies user-dependent sampled projections.
        /// </summary>
        private enum Projection
        {
            Channels,
            Diagnostics,
            Count
        }

        /// <summary>
        /// Holds the provider outcome within one asynchronous manager Call.
        /// </summary>
        private sealed class ManagerCallAudit
        {
            /// <summary>
            /// Gets whether inherited execution contexts must stop attributing autonomous work to this Call.
            /// </summary>
            internal bool IsCompleted => Volatile.Read(ref m_completed) != 0;

            /// <summary>
            /// Gets or sets the operation metadata reported by the provider.
            /// </summary>
            internal DataChannelControlAudit? Record { get; set; }

            /// <summary>
            /// Ends the Call scope before its correlated records are published.
            /// </summary>
            internal void Complete()
            {
                Volatile.Write(ref m_completed, 1);
            }

            /// <summary>Tracks scope completion across inherited execution contexts.</summary>
            private int m_completed;
        }

        private const string kRuntimeNamespace = "urn:opcfoundation.org:ua:server:datachannels";
        private const string kMetadataNamespace = "http://opcfoundation.org/UA/DataChannel/";
        private readonly ServerDataChannelProvider m_provider;
        private readonly TimeProvider m_clock;
        private readonly ILogger m_eventLogger;
        private readonly Channel<IFilterTarget> m_events;
        private readonly CancellationTokenSource m_lifetime = new();
        private readonly Dictionary<NodeId, DataChannelSource> m_sourceNodes = [];
        private readonly AsyncLocal<ManagerCallAudit?> m_callAudit = new();
        private Task m_publisher = Task.CompletedTask;
        private int m_stopped;
    }

    /// <summary>
    /// Source-generated bounded Event-delivery diagnostics.
    /// </summary>
    internal static partial class DataChannelNodeManagerLog
    {
        /// <summary>
        /// Reports an explicit bounded publisher refusal.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.DataChannels + 1, Level = LogLevel.Warning,
            Message = "Data channel event publisher capacity exhausted.")]
        internal static partial void EventBufferFull(this ILogger logger);

        /// <summary>
        /// Reports an isolated event delivery failure.
        /// </summary>
        [LoggerMessage(EventId = ServerEventIds.DataChannels + 2, Level = LogLevel.Warning,
            Message = "Data channel event delivery failed.")]
        internal static partial void EventDeliveryFailed(this ILogger logger, Exception exception);
    }
}
