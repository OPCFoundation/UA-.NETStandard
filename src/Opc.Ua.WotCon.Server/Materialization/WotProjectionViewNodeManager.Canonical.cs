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
 *
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
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;

namespace Opc.Ua.WotCon.Server.Materialization
{
    internal sealed partial class WotProjectionViewNodeManager
    {
        internal WotProjectionViewNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger,
            WotCanonicalViewState state,
            IAsyncNodeManager? previous)
            : base(server, configuration, logger, CanonicalNamespaces(state))
        {
            m_canonicalState = state;
            m_previousCanonicalManager = previous;
        }

        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            await base.CreateAddressSpaceAsync(externalReferences, cancellationToken).ConfigureAwait(false);
            if (m_canonicalState is null)
            {
                return;
            }
            await ValidateCanonicalImageAsync(m_canonicalState, cancellationToken).ConfigureAwait(false);
            var nodes = new Dictionary<string, NodeState>(StringComparer.Ordinal);
            var views = new Dictionary<string, ViewState>(StringComparer.Ordinal);
            foreach (WotCanonicalViewPublication publication in m_canonicalState.Views)
            {
                if (!publication.Active)
                {
                    continue;
                }
                NodeId viewId = Local(publication.ViewNodeId);
                var view = new ViewState
                {
                    NodeId = viewId,
                    SymbolicName = ViewSymbolicName,
                    BrowseName = new QualifiedName(ViewSymbolicName, viewId.NamespaceIndex),
                    DisplayName = new LocalizedText(ViewSymbolicName),
                    WriteMask = AttributeWriteMask.None,
                    UserWriteMask = AttributeWriteMask.None,
                    EventNotifier = EventNotifiers.None,
                    ContainsNoLoops = true
                };
                nodes.Add(publication.ViewNodeId.ToString(), view);
                views.Add(publication.ResourceXid, view);
                var membership = new HashSet<NodeId> { viewId };
                foreach (ExpandedNodeId member in publication.Membership)
                {
                    membership.Add(Local(member));
                }
                m_canonicalMembership.Add(viewId, membership);
                m_canonicalVersions.Add(viewId, publication.ViewVersion);
            }
            foreach (WotCanonicalViewNode descriptor in m_canonicalState.Nodes)
            {
                if (descriptor.Role != WotCanonicalViewNodeRole.Group)
                {
                    continue;
                }
                ViewState owner = views[descriptor.ResourceXid];
                NodeId nodeId = Local(descriptor.NodeId);
                var group = new WoTProjectionGroupState(owner)
                {
                    NodeId = nodeId,
                    SymbolicName = descriptor.BrowseName.Name,
                    BrowseName = BrowseName(descriptor),
                    DisplayName = new LocalizedText(descriptor.BrowseName.Name),
                    TypeDefinitionId = Local(descriptor.TypeDefinition),
                    ReferenceTypeId = Ua.ReferenceTypeIds.Organizes,
                    WriteMask = AttributeWriteMask.None,
                    UserWriteMask = AttributeWriteMask.None,
                    EventNotifier = EventNotifiers.None
                };
                owner.AddChild(group);
                nodes.Add(descriptor.NodeId.ToString(), group);
            }
            foreach (WotCanonicalViewNode descriptor in m_canonicalState.Nodes)
            {
                if (descriptor.Role is not (WotCanonicalViewNodeRole.ProjectionRoot or
                    WotCanonicalViewNodeRole.ViewVersion))
                {
                    continue;
                }
                WotCanonicalViewReference parentReference = m_canonicalState.References.ToArray()!.Single(
                    reference => reference.TargetId == descriptor.NodeId && !reference.IsInverse &&
                        reference.ReferenceTypeId == new ExpandedNodeId(Ua.ReferenceTypeIds.HasProperty));
                NodeState parent = nodes[parentReference.SourceId.ToString()];
                BaseVariableState property;
                if (descriptor.Role == WotCanonicalViewNodeRole.ProjectionRoot)
                {
                    PropertyState<NodeId> root = PropertyState<NodeId>.With<VariantBuilder>(parent);
                    root.Value = Local(descriptor.NodeIdValue);
                    if (parent is not WoTProjectionGroupState group)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNodeClassInvalid, "ProjectionRoot must belong to an organizational group.");
                    }
                    group.ProjectionRoot = root;
                    property = root;
                }
                else
                {
                    PropertyState<uint> version = PropertyState<uint>.With<VariantBuilder>(parent);
                    version.Value = descriptor.VersionValue;
                    property = version;
                    m_canonicalMembership[parent.NodeId].Add(Local(descriptor.NodeId));
                }
                property.NodeId = Local(descriptor.NodeId);
                property.SymbolicName = descriptor.BrowseName.Name;
                property.BrowseName = BrowseName(descriptor);
                property.DisplayName = new LocalizedText(descriptor.BrowseName.Name);
                property.TypeDefinitionId = Local(descriptor.TypeDefinition);
                property.ReferenceTypeId = Ua.ReferenceTypeIds.HasProperty;
                property.DataType = Local(descriptor.DataType);
                property.ValueRank = ValueRanks.Scalar;
                property.AccessLevel = AccessLevels.CurrentRead;
                property.UserAccessLevel = AccessLevels.CurrentRead;
                property.WriteMask = AttributeWriteMask.None;
                property.UserWriteMask = AttributeWriteMask.None;
                property.Historizing = false;
                property.StatusCode = StatusCodes.Good;
                if (descriptor.Role == WotCanonicalViewNodeRole.ViewVersion)
                {
                    parent.AddChild(property);
                }
                nodes.Add(descriptor.NodeId.ToString(), property);
            }
            foreach (WotCanonicalViewReference reference in m_canonicalState.References)
            {
                NodeId sourceId = Local(reference.SourceId);
                NodeId targetId = Local(reference.TargetId);
                NodeId referenceType = Local(reference.ReferenceTypeId);
                if (nodes.TryGetValue(reference.SourceId.ToString(), out NodeState? source))
                {
                    if (IsImplicitCanonicalReference(source, targetId, referenceType, reference.IsInverse, nodes))
                    {
                        continue;
                    }
                    source.AddReference(referenceType, reference.IsInverse, targetId);
                }
                else
                {
                    if (!externalReferences.TryGetValue(sourceId, out IList<IReference>? references))
                    {
                        references = new List<IReference>();
                        externalReferences.Add(sourceId, references);
                    }
                    references.Add(new ReferenceNode
                    {
                        ReferenceTypeId = referenceType,
                        IsInverse = reference.IsInverse,
                        TargetId = targetId
                    });
                }
            }
            foreach (ViewState view in views.Values)
            {
                await AddPredefinedNodeAsync(SystemContext, view, cancellationToken).ConfigureAwait(false);
            }
        }

        protected override void ValidateViewDescription(ServerSystemContext context, ViewDescription view)
        {
            if (m_canonicalState is null || ViewDescription.IsDefault(view))
            {
                base.ValidateViewDescription(context, view);
                return;
            }
            if (!m_canonicalVersions.TryGetValue(view.ViewId, out uint version))
            {
                throw new ServiceResultException(StatusCodes.BadViewIdUnknown);
            }
            if (view.Timestamp != DateTimeUtc.MinValue)
            {
                throw new ServiceResultException(StatusCodes.BadViewTimestampInvalid);
            }
            if (view.ViewVersion != 0 && view.ViewVersion != version)
            {
                throw new ServiceResultException(StatusCodes.BadViewVersionInvalid);
            }
        }

        protected override bool IsNodeInView(ServerSystemContext context, NodeId viewId, NodeState node)
        {
            return m_canonicalState is null
                ? base.IsNodeInView(context, viewId, node)
                : m_canonicalMembership.TryGetValue(viewId, out HashSet<NodeId>? members) && members.Contains(node.NodeId);
        }

        protected override bool IsReferenceInView(
            ServerSystemContext context, ContinuationPoint continuationPoint, IReference reference)
        {
            if (m_canonicalState is null || ViewDescription.IsDefault(continuationPoint.View))
            {
                return base.IsReferenceInView(context, continuationPoint, reference);
            }
            return m_canonicalMembership.TryGetValue(continuationPoint.View!.ViewId, out HashSet<NodeId>? members) &&
                members.Contains(Local(reference.TargetId));
        }

        internal static string[] CanonicalNamespaces(WotCanonicalViewState state)
        {
            var uris = new HashSet<string>(StringComparer.Ordinal) { Namespaces.WotCon };
            foreach (WotCanonicalViewNode node in state.Nodes)
            {
                uris.Add(node.NodeId.NamespaceUri ?? global::Opc.Ua.Namespaces.OpcUa);
            }
            return uris.OrderBy(uri => uri, StringComparer.Ordinal).ToArray();
        }

        private async ValueTask ValidateCanonicalImageAsync(
            WotCanonicalViewState state, CancellationToken cancellationToken)
        {
            using var context = new OperationContext(
                new RequestHeader(), null, RequestType.Read, RequestLifetime.None);
            for (int i = 0; i < state.SourceFacts.Count; i++)
            {
                CanonicalViewSource source = state.SourceFacts[i];
                NodeId nodeId = ExpandedNodeId.ToNodeId(ExpandedNodeId.Parse(source.NodeId), Server.NamespaceUris);
                if (nodeId.IsNull && !source.Available)
                {
                    continue;
                }
                (object? handle, IAsyncNodeManager? owner) = await Server.NodeManager
                    .GetManagerHandleAsync(nodeId, cancellationToken).ConfigureAwait(false);
                bool present = handle is not null && owner is not null;
                if (source.Available != present)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdUnknown, "The candidate source image does not match the canonical catalogue.");
                }
                if (present)
                {
                    NodeMetadata metadata = await owner!.GetNodeMetadataAsync(
                        context, handle!, BrowseResultMask.NodeClass, cancellationToken).ConfigureAwait(false);
                    if (metadata is null || metadata.NodeClass != source.NodeClass)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNodeClassInvalid, "A canonical source Node changed its captured NodeClass.");
                    }
                }
            }
            for (int i = 0; i < state.Views.Count; i++)
            {
                WotCanonicalViewPublication view = state.Views[i];
                if (view.Active)
                {
                    (object? resource, IAsyncNodeManager? owner) = await Server.NodeManager
                        .GetManagerHandleAsync(Local(view.ResourceNodeId), cancellationToken).ConfigureAwait(false);
                    if (resource is null || owner is null)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNodeIdUnknown, "A canonical View's logical Resource is not in the candidate image.");
                    }
                }
            }
            for (int i = 0; i < state.Nodes.Count; i++)
            {
                WotCanonicalViewNode node = state.Nodes[i];
                (object? handle, IAsyncNodeManager? owner) = await Server.NodeManager
                    .GetManagerHandleAsync(Local(node.NodeId), cancellationToken).ConfigureAwait(false);
                if (handle is not null && !ReferenceEquals(owner, m_previousCanonicalManager))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdExists, "A canonical candidate cannot replace another Node owner.");
                }
            }
        }

        private QualifiedName BrowseName(WotCanonicalViewNode node)
        {
            if (string.IsNullOrEmpty(node.BrowseName.NamespaceUri))
            {
                return new QualifiedName(node.BrowseName.Name);
            }
            int index = Server.NamespaceUris.GetIndex(node.BrowseName.NamespaceUri);
            if (index < 0)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "An organizational namespace is missing.");
            }
            return new QualifiedName(node.BrowseName.Name, (ushort)index);
        }

        private NodeId Local(ExpandedNodeId nodeId)
        {
            NodeId local = ExpandedNodeId.ToNodeId(nodeId, Server.NamespaceUris);
            if (local.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "A candidate NodeId cannot be resolved.");
            }
            return local;
        }

        private static bool IsImplicitCanonicalReference(
            NodeState source,
            NodeId targetId,
            NodeId referenceType,
            bool inverse,
            Dictionary<string, NodeState> nodes)
        {
            if (source is BaseInstanceState instance)
            {
                if (inverse && instance.Parent is { } parent && parent.NodeId == targetId &&
                    instance.ReferenceTypeId == referenceType)
                {
                    return true;
                }
                if (!inverse && referenceType == Ua.ReferenceTypeIds.HasTypeDefinition &&
                    instance.TypeDefinitionId == targetId)
                {
                    return true;
                }
            }
            if (!inverse)
            {
                foreach (NodeState node in nodes.Values)
                {
                    if (node is BaseInstanceState child && child.NodeId == targetId &&
                        ReferenceEquals(child.Parent, source) && child.ReferenceTypeId == referenceType)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private readonly WotCanonicalViewState? m_canonicalState;
        private readonly IAsyncNodeManager? m_previousCanonicalManager;
        private readonly Dictionary<NodeId, HashSet<NodeId>> m_canonicalMembership = new();
        private readonly Dictionary<NodeId, uint> m_canonicalVersions = new();
    }
}
