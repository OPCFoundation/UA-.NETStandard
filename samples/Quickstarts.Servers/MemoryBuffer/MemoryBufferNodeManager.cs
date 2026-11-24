// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;

namespace MemoryBuffer
{
    /// <summary>
    /// The factory to create the node manager for memory buffers.
    /// </summary>
    public class MemoryBufferNodeManagerFactory : IAsyncNodeManagerFactory
    {
        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
#pragma warning disable CA2000 // ownership of the node manager transfers to the caller via the returned ValueTask<IAsyncNodeManager>
            return new ValueTask<IAsyncNodeManager>(
                new MemoryBufferNodeManager(server, configuration, [.. NamespacesUris]));
#pragma warning restore CA2000
        }

        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris
            => [Namespaces.MemoryBuffer, Namespaces.MemoryBuffer + "/Instance"];
    }

    /// <summary>
    /// A node manager for a variety of memory buffers.
    /// </summary>
    /// <remarks>
    /// The tags inside a buffer are virtual: they are not registered as
    /// predefined nodes but resolved from their identifier on every
    /// operation. Value subscriptions on tags use push monitored items that
    /// the buffer feeds from its own scan, so the server never samples the
    /// short-lived tag objects.
    /// </remarks>
    public class MemoryBufferNodeManager : AsyncCustomNodeManager
    {
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public MemoryBufferNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            string[] namespaceUris)
            : base(
                server,
                configuration,
                server.Telemetry.CreateLogger<MemoryBufferNodeManager>(),
                namespaceUris)
        {
            // get the configuration for the node manager.
            m_configuration =
                configuration.ParseExtension<MemoryBufferConfiguration>() ??
                new MemoryBufferConfiguration();
        }

        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.
        /// </remarks>
        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);

            ushort namespaceIndex = Server.NamespaceUris
                .GetIndexOrAppend(Namespaces.MemoryBuffer);

            BaseInstanceState root = FindPredefinedNode<BaseInstanceState>(
                new NodeId(Objects.MemoryBuffers, namespaceIndex))!;

            namespaceIndex = Server.NamespaceUris
                .GetIndexOrAppend(Namespaces.MemoryBuffer + "/Instance");

            // the buffers live in the instance namespace, so their
            // children are minted there too rather than in the model
            // namespace the base class would otherwise have adopted.
            NodeIdFactory = NodeIdFactory.WithDefaultNamespaceIndex(namespaceIndex);

            if (m_configuration.Buffers.IsNull)
            {
                return;
            }

            for (int ii = 0; ii < m_configuration.Buffers.Count; ii++)
            {
                MemoryBufferInstance instance = m_configuration.Buffers[ii]!;

                // create a new buffer.
                var bufferNode = new MemoryBufferState(SystemContext, instance);

                // assign node ids.
                bufferNode.Create(
                    SystemContext,
                    new NodeId(bufferNode.SymbolicName, namespaceIndex),
                    new QualifiedName(bufferNode.SymbolicName, namespaceIndex),
                    default,
                    true);

                bufferNode.CreateBuffer(instance.DataType, instance.TagCount);
                bufferNode.InitializeMonitoring(Server);

                // link to root.
                root.AddChild(bufferNode);

                // Register the buffer subtree. Its own properties -
                // StartAddress and SizeInBytes - are real nodes with
                // real identifiers, and a client that browses the
                // buffer is handed them; without this they resolve to
                // nothing. The virtual tags below are still ephemeral
                // and are not registered.
                await AddPredefinedNodeAsync(SystemContext, bufferNode, cancellationToken)
                    .ConfigureAwait(false);

                // save the buffers for easy look up later. The dictionary
                // is only written while the address space is created.
                m_buffers[bufferNode.SymbolicName] = bufferNode;
            }
        }

        /// <summary>
        /// Loads a node set from a file or resource and adds them to the set of predefined nodes.
        /// </summary>
        protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<NodeStateCollection>(
                new NodeStateCollection().AddMemoryBuffer(context));
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override async ValueTask DeleteAddressSpaceAsync(
            CancellationToken cancellationToken = default)
        {
            foreach (MemoryBufferState buffer in m_buffers.Values)
            {
                buffer.StopScanning();
            }

            await base.DeleteAddressSpaceAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Stops the buffer scans when the manager is disposed without its
        /// address space being deleted first.
        /// </summary>
        protected override async ValueTask DisposeAsyncCore()
        {
            foreach (MemoryBufferState buffer in m_buffers.Values)
            {
                buffer.StopScanning();
            }

            await base.DisposeAsyncCore().ConfigureAwait(false);
        }

        /// <summary>
        /// Refuses filters on tags, so creating and modifying an item apply
        /// the same policy.
        /// </summary>
        protected override ValueTask<ValidateMonitoringFilterResult> ValidateMonitoringFilterAsync(
            ServerSystemContext context,
            NodeHandle handle,
            uint attributeId,
            double samplingInterval,
            uint queueSize,
            ExtensionObject filter,
            CancellationToken cancellationToken = default)
        {
            if (handle.Node is MemoryTagState &&
                ExtensionObject.ToEncodeable(filter) is MonitoringFilter)
            {
                return new ValueTask<ValidateMonitoringFilterResult>(
                    new ValidateMonitoringFilterResult { StatusCode = StatusCodes.BadFilterNotAllowed });
            }

            return base.ValidateMonitoringFilterAsync(
                context,
                handle,
                attributeId,
                samplingInterval,
                queueSize,
                filter,
                cancellationToken);
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        /// <remarks>
        /// Tag ids have the syntax <c>&lt;bufferName&gt;[&lt;address&gt;]</c>
        /// and resolve to a tag created for the operation. An id in any other
        /// shape is an ordinary registered node - the buffer and its own
        /// properties - so it falls through to the base lookup.
        /// </remarks>
        protected override ValueTask<NodeHandle> GetManagerHandleAsync(
            ServerSystemContext context,
            NodeId nodeId,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            if (IsNodeIdInNamespace(nodeId) &&
                nodeId.TryGetValue(out string? id) &&
                ResolveVirtualTag(id!) is MemoryTagState tag)
            {
                return new ValueTask<NodeHandle>(new NodeHandle(nodeId, tag)
                {
                    Validated = true
                });
            }

            return base.GetManagerHandleAsync(context, nodeId, cache, cancellationToken);
        }

        /// <summary>
        /// Resolves an identifier of the form <c>&lt;bufferName&gt;[&lt;address&gt;]</c>
        /// to a tag inside that buffer.
        /// </summary>
        /// <remarks>
        /// The tags carry all the metadata the UA operations need plus
        /// pointers into the buffer object, and are discarded once the
        /// operation completes. That is what lets the server expose millions
        /// of nodes without holding millions of objects in memory.
        /// </remarks>
        /// <param name="id">The string identifier to parse.</param>
        /// <returns>The tag, or <c>null</c> when the id is not a tag id.</returns>
        private MemoryTagState? ResolveVirtualTag(string id)
        {
            if (id.Length == 0 || id[^1] != ']')
            {
                return null;
            }

            int index = id.IndexOf('[', StringComparison.Ordinal);

            if (index == -1)
            {
                return null;
            }

            if (!m_buffers.TryGetValue(id[..index], out MemoryBufferState? buffer))
            {
                return null;
            }

            string offsetText = id.Substring(index + 1, id.Length - index - 2);

            if (offsetText.Length == 0)
            {
                return null;
            }

            for (int ii = 0; ii < offsetText.Length; ii++)
            {
                if (!char.IsDigit(offsetText[ii]))
                {
                    return null;
                }
            }

            if (!uint.TryParse(
                    offsetText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out uint offset) ||
                offset >= buffer.SizeInBytes!.Value)
            {
                return null;
            }

            return new MemoryTagState(buffer, offset);
        }

        /// <summary>
        /// Decides how a monitored item on a tag is created.
        /// </summary>
        /// <remarks>
        /// Value subscriptions on tags become push items fed by the buffer
        /// scan. Other attributes of a tag are static and use the default
        /// item. Filters, index ranges and data encodings are not supported
        /// on tags.
        /// </remarks>
        protected override ValueTask<MonitoredItemCreateDecision> OnCreatingMonitoredItemAsync(
            MonitoredItemCreateContext context,
            CancellationToken cancellationToken = default)
        {
            if (context.Source is not MemoryTagState)
            {
                return base.OnCreatingMonitoredItemAsync(context, cancellationToken);
            }

            ReadValueId itemToMonitor = context.Request.ItemToMonitor;

            if (ExtensionObject.ToEncodeable(context.Request.RequestedParameters.Filter)
                is MonitoringFilter)
            {
                return Decide(MonitoredItemCreateDecision.Refuse(StatusCodes.BadFilterNotAllowed));
            }

            if (!itemToMonitor.ParsedIndexRange.IsNull)
            {
                return Decide(MonitoredItemCreateDecision.Refuse(StatusCodes.BadIndexRangeInvalid));
            }

            if (!itemToMonitor.DataEncoding.IsNull)
            {
                return Decide(MonitoredItemCreateDecision.Refuse(StatusCodes.BadDataEncodingUnsupported));
            }

            if (itemToMonitor.AttributeId != Attributes.Value)
            {
                return Decide(MonitoredItemCreateDecision.UseDefault());
            }

            return Decide(MonitoredItemCreateDecision.Use(
                static factoryContext => factoryContext.CreatePushMonitoredItem()));

            static ValueTask<MonitoredItemCreateDecision> Decide(MonitoredItemCreateDecision decision)
            {
                return new ValueTask<MonitoredItemCreateDecision>(decision);
            }
        }

        /// <summary>
        /// Connects a new value item on a tag to the buffer scan.
        /// </summary>
        /// <remarks>
        /// Also runs for items restored after a restart. A push item gets no
        /// initial value from the stack, so the current value is queued here;
        /// a restored item gets one too, since it resumes with fresh data.
        /// </remarks>
        protected override void OnMonitoredItemCreated(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem)
        {
            if (TryGetTag(handle, monitoredItem, out MemoryBufferState? buffer, out uint offset))
            {
                buffer!.AddMonitoredItem(monitoredItem, offset);
                monitoredItem.QueueValue(buffer.ReadDataValue(offset), null);
            }

            base.OnMonitoredItemCreated(context, handle, monitoredItem);
        }

        /// <summary>
        /// Disconnects a deleted value item from the buffer scan.
        /// </summary>
        protected override ValueTask OnMonitoredItemDeletedAsync(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default)
        {
            if (TryGetTag(handle, monitoredItem, out MemoryBufferState? buffer, out uint offset))
            {
                buffer!.RemoveMonitoredItem(monitoredItem, offset);
            }

            return base.OnMonitoredItemDeletedAsync(context, handle, monitoredItem, cancellationToken);
        }

        /// <summary>
        /// Connects a value item that moved to this manager to the buffer scan.
        /// </summary>
        protected override ValueTask OnMonitoredItemAttachedAsync(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default)
        {
            if (TryGetTag(handle, monitoredItem, out MemoryBufferState? buffer, out uint offset))
            {
                buffer!.AddMonitoredItem(monitoredItem, offset);
            }

            return base.OnMonitoredItemAttachedAsync(context, handle, monitoredItem, cancellationToken);
        }

        /// <summary>
        /// Disconnects a value item that left this manager from the buffer scan.
        /// </summary>
        protected override ValueTask OnMonitoredItemDetachedAsync(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            CancellationToken cancellationToken = default)
        {
            if (TryGetTag(handle, monitoredItem, out MemoryBufferState? buffer, out uint offset))
            {
                buffer!.RemoveMonitoredItem(monitoredItem, offset);
            }

            return base.OnMonitoredItemDetachedAsync(context, handle, monitoredItem, cancellationToken);
        }

        /// <summary>
        /// Provides an immediate update when a value item on a tag is enabled again.
        /// </summary>
        protected override ValueTask OnMonitoringModeChangedAsync(
            ServerSystemContext context,
            NodeHandle handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            MonitoringMode previousMode,
            MonitoringMode monitoringMode,
            CancellationToken cancellationToken = default)
        {
            if (previousMode == MonitoringMode.Disabled &&
                monitoringMode != MonitoringMode.Disabled &&
                TryGetTag(handle, monitoredItem, out MemoryBufferState? buffer, out uint offset))
            {
                monitoredItem.QueueValue(buffer!.ReadDataValue(offset), null);
            }

            return base.OnMonitoringModeChangedAsync(
                context,
                handle,
                monitoredItem,
                previousMode,
                monitoringMode,
                cancellationToken);
        }

        /// <summary>
        /// Gets the buffer and offset behind a value item on a tag.
        /// </summary>
        private static bool TryGetTag(
            NodeHandle? handle,
            ISampledDataChangeMonitoredItem monitoredItem,
            out MemoryBufferState? buffer,
            out uint offset)
        {
            if (monitoredItem.AttributeId == Attributes.Value &&
                handle?.Node is MemoryTagState { Parent: MemoryBufferState parent } tag)
            {
                buffer = parent;
                offset = tag.Offset;
                return true;
            }

            buffer = null;
            offset = 0;
            return false;
        }

        /// <summary>
        /// The buffers created for the address space, by symbolic name.
        /// </summary>
        internal IReadOnlyDictionary<string, MemoryBufferState> Buffers => m_buffers;

        private readonly MemoryBufferConfiguration m_configuration;
        private readonly Dictionary<string, MemoryBufferState> m_buffers = [];
    }
}
