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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Programs;

namespace Opc.Ua.Server.Programs
{
    /// <summary>
    /// Owns registered Program types and their in-memory invocation lifetimes on one server.
    /// Registration does not create or start an invocation.
    /// </summary>
    public sealed partial class ProgramNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// The default namespace for invocation nodes, separate from the SDK type model.
        /// </summary>
        public const string DefaultInstanceNamespaceUri = "http://opcfoundation.org/UA/Server/Programs/";

        /// <summary>
        /// Creates a Program host. Additional application models are supplied as generated node collections.
        /// Only types listed in <paramref name="addNodesTypes"/> accept the AddNodes service.
        /// </summary>
        public ProgramNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ArrayOf<ProgramDefinition> definitions,
            string instanceNamespaceUri = DefaultInstanceNamespaceUri,
            ArrayOf<ExpandedNodeId> addNodesTypes = default,
            Func<ISystemContext, NodeStateCollection>? modelLoader = null)
            : base(server, configuration, GetNamespaceUris(definitions, instanceNamespaceUri))
        {
            ValidateDefinitions(definitions, addNodesTypes);
            m_definitions = definitions.Span.ToArray();
            m_addNodesTypes = addNodesTypes.Span.ToArray();
            m_modelLoader = modelLoader;
            m_timeProvider = (server as ITimeProviderProvider)?.TimeProvider ?? TimeProvider.System;
            m_programLogger = server.Telemetry.CreateLogger<ProgramNodeManager>();
            m_autoDeletion = ProcessAutoDeletionAsync();
        }

        /// <summary>
        /// Gets the invocation folder identifier after address-space creation.
        /// All managed invocations are direct children of this folder.
        /// </summary>
        public NodeId RootId => m_root?.NodeId ?? NodeId.Null;

        /// <inheritdoc/>
        public override bool AllowNodeManagement => true;

        /// <summary>
        /// Finds an invocation without transferring ownership of its runtime.
        /// </summary>
        public bool TryGetRuntime(NodeId invocationId, out ProgramRuntime? runtime)
        {
            return m_invocations.TryGetValue(invocationId, out runtime);
        }

        /// <summary>
        /// Gets the committed live invocation count for a registered concrete type.
        /// </summary>
        /// <exception cref="ArgumentException">The concrete type is not registered.</exception>
        public uint GetInstanceCount(ExpandedNodeId typeId)
        {
            NodeId id = ExpandedNodeId.ToNodeId(typeId, Server.NamespaceUris);
            return m_types.TryGetValue(id, out TypeRegistration? registration)
                ? Volatile.Read(ref registration.InstanceCount)
                : throw new ArgumentException("The concrete Program type is not registered.", nameof(typeId));
        }

        /// <summary>
        /// Creates a server-owned invocation through the same capacity and initialization checks as a client.
        /// Creatable only restricts client creation; all invocations consume capacity.
        /// </summary>
        public ValueTask<ProgramRuntime> CreateInvocationAsync(
            ExpandedNodeId typeId,
            QualifiedName browseName,
            CancellationToken cancellationToken = default)
        {
            return RegisterInvocationAsync(
                SystemContext,
                ExpandedNodeId.ToNodeId(typeId, Server.NamespaceUris),
                RootId,
                browseName,
                null,
                NodeId.Null,
                false,
                cancellationToken);
        }

        /// <summary>
        /// Adopts an exclusively owned, unindexed generated invocation, consuming the same capacity as creation.
        /// The caller must not use the node after successful ownership transfer.
        /// </summary>
        public ValueTask<ProgramRuntime> AdoptInvocationAsync(
            ExpandedNodeId typeId,
            ProgramStateMachineState node,
            CancellationToken cancellationToken = default)
        {
            if (node is null)
            {
                throw new ArgumentNullException(nameof(node));
            }
            return RegisterInvocationAsync(
                SystemContext,
                ExpandedNodeId.ToNodeId(typeId, Server.NamespaceUris),
                RootId,
                node.BrowseName,
                node,
                node.NodeId,
                false,
                cancellationToken);
        }

        /// <inheritdoc/>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            return !node.NodeId.IsNull && node.NodeId.NamespaceIndex == NamespaceIndex
                ? node.NodeId
                : base.New(context, node);
        }

        /// <inheritdoc/>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await m_lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (m_stopping || m_root is not null)
                {
                    throw new InvalidOperationException("The Program host has already been started or stopped.");
                }
                NodeStateCollection models = new NodeStateCollection().AddOpcUaPrograms(SystemContext);
                if (m_modelLoader is not null)
                {
                    models.AddRange(m_modelLoader(SystemContext));
                }
                foreach (NodeState model in models)
                {
                    if (model.NodeId.NamespaceIndex == 0)
                    {
                        throw new ArgumentException("Program models must not replace namespace-zero nodes.");
                    }
                    await AddPredefinedNodeAsync(SystemContext, model, cancellationToken).ConfigureAwait(false);
                }
                for (int index = 0; index < m_definitions.Count; index++)
                {
                    ProgramDefinition definition = m_definitions[index];
                    NodeId typeId = ExpandedNodeId.ToNodeId(definition.TypeId, Server.NamespaceUris);
                    if (Find(typeId) is not BaseObjectTypeState { IsAbstract: false } type)
                    {
                        throw new InvalidOperationException(
                            $"Program type {typeId} is absent from the loaded model or is abstract.");
                    }
                    if (!Server.TypeTree.IsTypeOf(typeId, ObjectTypeIds.ProgramStateMachineType))
                    {
                        throw new InvalidOperationException(
                            $"Program type {typeId}, supertype {type.SuperTypeId}, " +
                            "does not derive from ProgramStateMachineType.");
                    }
                    TypeRegistration registration = ConfigureType(type, definition);
                    if (!m_types.TryAdd(typeId, registration))
                    {
                        throw new ArgumentException("A concrete Program type may be registered only once.");
                    }
                    await AddPredefinedNodeAsync(SystemContext, type, cancellationToken).ConfigureAwait(false);
                }
                m_root = new FolderState(null)
                {
                    BrowseName = new QualifiedName("Programs", NamespaceIndex),
                    DisplayName = new LocalizedText("Programs"),
                    TypeDefinitionId = ObjectTypeIds.FolderType,
                    ReferenceTypeId = ReferenceTypeIds.Organizes,
                    EventNotifier = EventNotifiers.SubscribeToEvents
                };
                m_root.Create(SystemContext, NodeId.Null, m_root.BrowseName, default, true);
                m_root.EventNotifier = EventNotifiers.SubscribeToEvents;
                m_root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
                await AddPredefinedNodeAsync(SystemContext, m_root, cancellationToken).ConfigureAwait(false);
                await AddRootNotifierAsync(m_root, cancellationToken).ConfigureAwait(false);
                await AddReverseReferencesAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_lifecycle.Release();
            }
        }

        /// <inheritdoc/>
        public override async ValueTask DeleteAddressSpaceAsync(CancellationToken cancellationToken = default)
        {
            await StopInvocationsAsync().ConfigureAwait(false);
            await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Cancel a provider's in-flight admission before the base waits for admitted operations to drain.
                RequestStop();
            }
            base.Dispose(disposing);
        }

        /// <inheritdoc/>
        protected override async ValueTask DisposeAsyncCore()
        {
            try
            {
                await StopInvocationsAsync().ConfigureAwait(false);
                await base.DisposeAsyncCore().ConfigureAwait(false);
            }
            finally
            {
                m_lifetime.Dispose();
                m_lifecycle.Dispose();
            }
        }

        private async ValueTask<ProgramRuntime> RegisterInvocationAsync(
            ServerSystemContext context,
            NodeId typeId,
            NodeId parentId,
            QualifiedName browseName,
            ProgramStateMachineState? adoptedNode,
            NodeId requestedId,
            bool byClient,
            CancellationToken cancellationToken,
            ObjectAttributes? attributes = null,
            ArrayOf<Variant> creationArguments = default,
            bool activate = true)
        {
            using NodeManagerOperation operation = BeginNodeManagerOperation();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_lifetime.Token);
            await m_lifecycle.WaitAsync(linked.Token).ConfigureAwait(false);
            ProgramRuntime? runtime = null;
            ProgramStateMachineState? node = null;
            bool reserved = false;
            bool counted = false;
            bool staged = false;
            TypeRegistration? registration = null;
            try
            {
                if (m_stopping || m_root is null)
                {
                    throw new ServiceResultException(StatusCodes.BadServerHalted);
                }
                if (!m_types.TryGetValue(typeId, out registration))
                {
                    throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid);
                }
                if (parentId != m_root.NodeId)
                {
                    throw new ServiceResultException(StatusCodes.BadParentNodeIdInvalid);
                }
                if (browseName.IsNull || string.IsNullOrEmpty(browseName.Name) ||
                    browseName.NamespaceIndex != NamespaceIndex)
                {
                    throw new ServiceResultException(StatusCodes.BadBrowseNameInvalid);
                }
                if (byClient && !registration.Definition.Creatable)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
                if (byClient)
                {
                    ServiceResult permission = await ValidateCreationPermissionAsync(context, linked.Token)
                        .ConfigureAwait(false);
                    if (ServiceResult.IsBad(permission))
                    {
                        throw new ServiceResultException(permission);
                    }
                }
                if ((ulong)registration.InstanceCount + registration.Reservations >=
                    registration.Definition.MaxInstanceCount &&
                    registration.Definition.MaxInstanceCount != uint.MaxValue)
                {
                    throw new ServiceResultException(StatusCodes.BadTooManyOperations);
                }
                if (m_root.FindChildWithQualifiedName(context, browseName) is not null)
                {
                    throw new ServiceResultException(StatusCodes.BadBrowseNameDuplicated);
                }
                registration.Reservations++;
                reserved = true;
                node = adoptedNode ?? registration.Definition.NodeFactory(m_root) ??
                    throw new ServiceResultException(StatusCodes.BadConfigurationError);
                if (node.Parent is not null && node.Parent != m_root)
                {
                    throw new ServiceResultException(StatusCodes.BadParentNodeIdInvalid);
                }
                RejectIndexedSubtree(node);
                if (adoptedNode is null || node.CurrentState?.Id is null)
                {
                    node.Create(context, requestedId, browseName, default, true);
                }
                if (node.TypeDefinitionId != typeId)
                {
                    throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid);
                }
                node.ReferenceTypeId = ReferenceTypeIds.Organizes;
                if (attributes is not null)
                {
                    if ((attributes.SpecifiedAttributes & (uint)NodeAttributesMask.DisplayName) != 0)
                    {
                        node.DisplayName = attributes.DisplayName;
                    }
                    if ((attributes.SpecifiedAttributes & (uint)NodeAttributesMask.Description) != 0)
                    {
                        node.Description = attributes.Description;
                    }
                }
                PrepareAuthoredNodeIdsForRegistration(node);
                ValidateInvocationNodeIds(node);
                runtime = await ProgramRuntime.AttachAsync(
                    node,
                    registration.Definition,
                    context,
                    m_timeProvider,
                    QueueAutoDeletion,
                    creationArguments,
                    false,
                    linked.Token).ConfigureAwait(false);
                PrepareAuthoredNodeIdsForRegistration(node);
                ValidateInvocationNodeIds(node);
                staged = true;
                m_root.AddNotifier(context, ReferenceTypeIds.HasNotifier, false, node);
                // BaseInstanceState already propagates events to Parent; the inverse is metadata only.
                node.AddReference(ReferenceTypeIds.HasNotifier, true, m_root.NodeId);
                // The base registration preserves prepared ids, refreshes the parent cache and emits model changes.
                await base.AddNodeAsync(context, parentId, node, linked.Token).ConfigureAwait(false);
                if (!m_invocations.TryAdd(node.NodeId, runtime))
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdExists);
                }
                registration.InstanceCount = checked(registration.InstanceCount + 1);
                counted = true;
                UpdateInstanceCount(registration);
                if (activate)
                {
                    await runtime.ActivateAsync(linked.Token).ConfigureAwait(false);
                }
                return runtime;
            }
            catch
            {
                try
                {
                    if (staged && node is not null)
                    {
                        m_root?.RemoveNotifier(SystemContext, node, true);
                        if (Find(node.NodeId) == node)
                        {
                            await base.DeleteNodeAsync(SystemContext, node.NodeId, CancellationToken.None)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            m_root?.RemoveChild(node);
                        }
                    }
                }
                finally
                {
                    try
                    {
                        if (counted && node is not null && m_invocations.TryRemove(node.NodeId, out _))
                        {
                            registration!.InstanceCount--;
                            UpdateInstanceCount(registration);
                        }
                    }
                    finally
                    {
                        if (runtime is not null)
                        {
                            await runtime.DisposeAsync().ConfigureAwait(false);
                        }
                    }
                }
                throw;
            }
            finally
            {
                if (reserved)
                {
                    registration!.Reservations--;
                }
                m_lifecycle.Release();
            }
        }

        private void ValidateInvocationNodeIds(NodeState root)
        {
            var pending = new List<NodeState> { root };
            var identities = new HashSet<NodeId>();
            for (int index = 0; index < pending.Count; index++)
            {
                NodeState node = pending[index];
                if (node.NodeId.IsNull || node.NodeId.NamespaceIndex != NamespaceIndex ||
                    !identities.Add(node.NodeId) || PredefinedNodes.ContainsKey(node.NodeId))
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdExists);
                }
                var children = new List<BaseInstanceState>();
                node.GetChildren(SystemContext, children);
                pending.AddRange(children);
            }
        }

        private void RejectIndexedSubtree(NodeState root)
        {
            var pending = new List<NodeState> { root };
            for (int index = 0; index < pending.Count; index++)
            {
                NodeState node = pending[index];
                if (!node.NodeId.IsNull && ReferenceEquals(Find(node.NodeId), node))
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdExists);
                }
                var children = new List<BaseInstanceState>();
                node.GetChildren(SystemContext, children);
                pending.AddRange(children);
            }
        }

        private async ValueTask<ServiceResult> RemoveInvocationAsync(
            ProgramRuntime runtime,
            bool byClient,
            CancellationToken cancellationToken)
        {
            if (!m_invocations.TryGetValue(runtime.Node.NodeId, out ProgramRuntime? current) ||
                current != runtime)
            {
                return StatusCodes.BadNodeIdUnknown;
            }
            ServiceResult result = await runtime.PrepareDeleteAsync(byClient, cancellationToken).ConfigureAwait(false);
            if (ServiceResult.IsBad(result) && byClient)
            {
                return result;
            }
            try
            {
                // Accepted deletion is not rolled back by a disconnected client's cancellation.
                await base.DeleteNodeAsync(SystemContext, runtime.Node.NodeId, CancellationToken.None)
                    .ConfigureAwait(false);
                return ServiceResult.Good;
            }
            finally
            {
                if (Find(runtime.Node.NodeId) is null &&
                    m_invocations.TryRemove(runtime.Node.NodeId, out _))
                {
                    m_root?.RemoveNotifier(SystemContext, runtime.Node, true);
                    TypeRegistration registration = m_types[runtime.Node.TypeDefinitionId];
                    registration.InstanceCount--;
                    try
                    {
                        UpdateInstanceCount(registration);
                    }
                    finally
                    {
                        await runtime.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        private void QueueAutoDeletion(ProgramRuntime runtime)
        {
            if (runtime.Definition.AutoDelete)
            {
                m_terminated.Writer.TryWrite(runtime);
            }
        }

        private async Task ProcessAutoDeletionAsync()
        {
            await foreach (ProgramRuntime runtime in m_terminated.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await m_lifecycle.WaitAsync().ConfigureAwait(false);
                try
                {
                    await RemoveInvocationAsync(runtime, false, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    m_programLogger.AutoDeleteFailed(exception, runtime.Node.NodeId);
                }
                finally
                {
                    m_lifecycle.Release();
                }
            }
        }

        private async ValueTask StopInvocationsAsync()
        {
            RequestStop();
            List<Exception>? failures = m_stopError is null ? null : [m_stopError];
            // Cancel execution before waiting for the deletion worker, which may be waiting on an active control.
            var stops = new List<Task>();
            foreach (ProgramRuntime runtime in m_invocations.Values)
            {
                stops.Add(runtime.DisposeAsync().AsTask());
            }
            try
            {
                await Task.WhenAll(stops).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
            await m_autoDeletion.ConfigureAwait(false);
            await m_lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                foreach (ProgramRuntime runtime in m_invocations.Values)
                {
                    try
                    {
                        await RemoveInvocationAsync(runtime, false, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        (failures ??= []).Add(exception);
                        try
                        {
                            await runtime.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception disposalFailure)
                        {
                            failures.Add(disposalFailure);
                        }
                    }
                }
                if (failures is not null)
                {
                    throw new AggregateException(failures);
                }
            }
            finally
            {
                m_lifecycle.Release();
            }
        }

        private void RequestStop()
        {
            if (Interlocked.Exchange(ref m_stopRequested, 1) != 0)
            {
                return;
            }
            m_stopping = true;
            m_terminated.Writer.TryComplete();
            try
            {
                m_lifetime.Cancel();
            }
            catch (Exception exception)
            {
                m_stopError = exception;
            }
        }

        private void UpdateInstanceCount(TypeRegistration registration)
        {
            registration.Count.WrappedValue = registration.InstanceCount;
            registration.Count.Timestamp = m_timeProvider.GetUtcNow().UtcDateTime;
            registration.Count.ClearChangeMasks(SystemContext, false);
        }

        /// <summary>
        /// Validates duplicate and unsupported registrations before server startup.
        /// </summary>
        internal static void ValidateDefinitions(
            ArrayOf<ProgramDefinition> definitions,
            ArrayOf<ExpandedNodeId> addNodesTypes)
        {
            var ids = new HashSet<ExpandedNodeId>();
            foreach (ProgramDefinition definition in definitions)
            {
                if (definition is null || !ids.Add(definition.TypeId))
                {
                    throw new ArgumentException(
                        "Program definitions must be non-null and unique.", nameof(definitions));
                }
                if (definition.TypeId.ServerIndex != 0 ||
                    string.IsNullOrEmpty(definition.TypeId.NamespaceUri) ||
                    definition.TypeId.NamespaceUri == Opc.Ua.Namespaces.OpcUa)
                {
                    throw new ArgumentException(
                        "Use namespace-qualified local Program type ids outside namespace zero.", nameof(definitions));
                }
            }
            foreach (ExpandedNodeId typeId in addNodesTypes)
            {
                if (!ids.Contains(typeId))
                {
                    throw new ArgumentException("AddNodes requires a registered Program type.", nameof(addNodesTypes));
                }
            }
        }

        /// <summary>
        /// Gets the explicitly owned instance and model namespaces.
        /// </summary>
        internal static string[] GetNamespaceUris(ArrayOf<ProgramDefinition> definitions, string instanceNamespaceUri)
        {
            if (!Uri.TryCreate(instanceNamespaceUri, UriKind.Absolute, out _) ||
                instanceNamespaceUri == Opc.Ua.Namespaces.OpcUa ||
                instanceNamespaceUri == Opc.Ua.Programs.Namespaces.OpcUaPrograms)
            {
                throw new ArgumentException(
                    "Use a distinct absolute instance namespace URI.", nameof(instanceNamespaceUri));
            }
            var uris = new List<string> { instanceNamespaceUri, Opc.Ua.Programs.Namespaces.OpcUaPrograms };
            foreach (ProgramDefinition definition in definitions)
            {
                if (definition is not null && !string.IsNullOrEmpty(definition.TypeId.NamespaceUri) &&
                    !uris.Contains(definition.TypeId.NamespaceUri))
                {
                    uris.Add(definition.TypeId.NamespaceUri);
                }
            }
            return [.. uris];
        }

        private sealed class TypeRegistration(ProgramDefinition definition, PropertyState count)
        {
            public ProgramDefinition Definition { get; } = definition;

            public PropertyState Count { get; } = count;

            public uint InstanceCount;

            public uint Reservations;
        }

        private readonly ArrayOf<ProgramDefinition> m_definitions;
        private readonly ArrayOf<ExpandedNodeId> m_addNodesTypes;
        private readonly Func<ISystemContext, NodeStateCollection>? m_modelLoader;
        private readonly TimeProvider m_timeProvider;
        private readonly ILogger m_programLogger;
        private readonly SemaphoreSlim m_lifecycle = new(1, 1);
        private readonly CancellationTokenSource m_lifetime = new();
        private readonly ConcurrentDictionary<NodeId, TypeRegistration> m_types = new();
        private readonly ConcurrentDictionary<NodeId, ProgramRuntime> m_invocations = new();
        private readonly Channel<ProgramRuntime> m_terminated = Channel.CreateUnbounded<ProgramRuntime>(
            new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
        private readonly Task m_autoDeletion;
        private FolderState? m_root;
        private Exception? m_stopError;
        private int m_stopRequested;
        private volatile bool m_stopping;
    }

    internal static partial class ProgramNodeManagerLog
    {
        [LoggerMessage(EventId = ServerEventIds.ProgramNodeManager, Level = LogLevel.Error,
            Message = "Automatic deletion failed for Program {InvocationId}; shutdown will retry cleanup.")]
        internal static partial void AutoDeleteFailed(this ILogger logger, Exception exception, NodeId invocationId);
    }
}
