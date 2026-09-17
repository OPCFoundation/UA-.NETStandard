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
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Export;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings;
using Opc.Ua.WotCon.Server.Materialization;
using Opc.Ua.WotCon.Server.Registry;
using Opc.Ua.WotCon.Server.ThingDescriptions;
using WotAffordanceKind = Opc.Ua.WotCon.Bindings.WotAffordanceKind;

namespace Opc.Ua.WotCon.Server.Assets
{
    internal sealed partial class AssetRegistry
    {
        internal async ValueTask<ServiceResult> RestoreAssetAsync(
            string assetName, ThingDescription td, ByteString content, CancellationToken ct)
        {
            var candidate = new AssetEntry(assetName, new IWoTAssetState(null)
            {
                NodeId = m_manager.AllocateAssetNodeId(assetName)
            });
            WotLegacyPreparedGraph? native;
            try
            {
                native = await PrepareNativeGraphAsync(candidate, content, ct).ConfigureAwait(false);
            }
            catch (ServiceResultException exception)
            {
                m_logger.NativePreparationRejected(exception, assetName);
                return ServiceResult.Create(exception.StatusCode,
                    "The persisted native declarations or type binding could not be prepared.");
            }
            (ServiceResult created, NodeId assetId) = await CreateAssetAsync(assetName, ct).ConfigureAwait(false);
            if (ServiceResult.IsBad(created))
            {
                return created;
            }
            AssetEntry entry = FindByNodeId(assetId) ??
                throw new InvalidOperationException("A restored asset disappeared before materialization.");
            ServiceResult result = await RebuildPreparedAsync(
                entry, td, content, native, persistOnSuccess: false, ct).ConfigureAwait(false);
            if (ServiceResult.IsGood(result))
            {
                entry.FileManager?.UpdatePersistedContent(content.Span.ToArray());
            }
            return result;
        }

        private async ValueTask<WotLegacyPreparedGraph?> PrepareNativeGraphAsync(
            AssetEntry entry, ByteString content, CancellationToken cancellationToken)
        {
            var options = new WotNodeSetConverterOptions
            {
                MaxJsonDocumentSize = m_options.MaxThingDescriptionSize,
                MaxResolverDocumentBytes = m_options.MaxThingDescriptionSize,
                MaxJsonDepth = m_options.MaxThingDescriptionJsonDepth
            };
            using WotDocument document = WotDocument.Parse(content.Memory, options);
            bool preserveIdentities = document.TryGetEnvelope(out _) || document.TryGetNativeProjection(out _);
            var addressSpace = new AddressSpaceWotNodeResolver(m_manager.Server);
            bool native = await WotNodeSetConverter.RequiresNativeMappingAsync(
                document, addressSpace, cancellationToken).ConfigureAwait(false);
            if (!native)
            {
                return null;
            }
            bool rootNameAuthored = document.TryGetUav("browseName", out JsonElement authoredName);

            IWotDocumentConverter converter = m_options.DocumentConverter ??
                new WotNodeSetDocumentConverter(options, addressSpace);
            if (converter is WotNodeSetDocumentConverter stock)
            {
                stock.AddressSpace = addressSpace;
            }
            DateTime now = DateTime.UtcNow;
            var version = new WotResourceVersion(
                "current", WotContentDigest.Compute(content), content.Length,
                "application/td+json", "WoT-TD/1.1", now, now);
            var resource = new WotResource(
                "legacy-assets", entry.Name, WoTDocumentKindEnum.ThingDescription, [version],
                defaultVersionId: version.VersionId, desiredVersionId: version.VersionId,
                thingId: document.Id, title: document.Title);
            var group = new WotResourceGroup(
                resource.GroupId, resource.Kind,
                ImmutableDictionary<string, WotResource>.Empty.Add(resource.ResourceId, resource));
            var snapshot = new WotRegistrySnapshot(
                0, ImmutableDictionary<string, WotResourceGroup>.Empty.Add(group.GroupId, group));
            var contents = new Dictionary<string, ByteString>(StringComparer.Ordinal)
            {
                [version.DigestHex] = content
            };
            WotConversionOutput converted = await converter.ConvertAsync(
                resource, content, snapshot, contents, cancellationToken).ConfigureAwait(false);
            if (!converted.Succeeded || converted.NodeSet is null || converted.RootNodeId.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadConfigurationError,
                    string.Join("; ", converted.Errors));
            }
            if (converted.ProjectedAffordances.Contains(affordance => affordance.Kind == WotAffordanceKind.Event))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "Native event execution requires the event materialization provider.");
            }

            var context = new SystemContext(m_manager.Server.Telemetry)
            {
                NamespaceUris = m_manager.SystemContext.NamespaceUris,
                ServerUris = m_manager.SystemContext.ServerUris,
                TypeTable = m_manager.SystemContext.TypeTable,
                EncodeableFactory = m_manager.SystemContext.EncodeableFactory,
                NodeStateFactory = m_manager.SystemContext.NodeStateFactory
            };
            if (document.TryGetUav("id", out JsonElement authoredId) &&
                (authoredId.ValueKind != JsonValueKind.String ||
                 authoredId.GetString() is not { } rootText ||
                 !ExpandedNodeId.TryParse(rootText, out ExpandedNodeId explicitId) ||
                 ExpandedNodeId.ToNodeId(explicitId, context.NamespaceUris) != entry.Asset.NodeId))
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid,
                    "An authored root identity conflicts with the existing legacy asset.");
            }
            var nodes = new NodeStateCollection();
            converted.NodeSet.Import(context, nodes);
            UANodeSet.LinkParentChildRelationships(context, nodes, new NodeSetImportLinkOptions());
            NodeId rootId = ExpandedNodeId.ToNodeId(converted.RootNodeId, context.NamespaceUris);
            if (preserveIdentities && rootId != entry.Asset.NodeId)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid,
                    "A native or preserved root must identify the existing legacy asset.");
            }
            var argumentIds = new HashSet<NodeId>();
            foreach (MethodState method in nodes.OfType<MethodState>())
            {
                if (method.InputArguments is not null)
                {
                    argumentIds.Add(method.InputArguments.NodeId);
                }
                if (method.OutputArguments is not null)
                {
                    argumentIds.Add(method.OutputArguments.NodeId);
                }
            }
            nodes = new NodeStateCollection();
            converted.NodeSet.Import(context, nodes, (nodeClass, nodeId, _) =>
                nodeClass == NodeClass.Variable && !argumentIds.Contains(nodeId)
                    ? new BaseDataVariableState(null) : null);
            UANodeSet.LinkParentChildRelationships(context, nodes, new NodeSetImportLinkOptions());
            BaseObjectState root = nodes.SingleOrDefault(node => node.NodeId == rootId) as BaseObjectState ??
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "A legacy asset requires one converter-selected Object root.");
            if (rootNameAuthored &&
                (authoredName.ValueKind != JsonValueKind.String ||
                 root.BrowseName.Name != entry.Name ||
                 (authoredName.GetString()!.Contains(':', StringComparison.Ordinal) &&
                  root.BrowseName.NamespaceIndex != m_manager.AssetNamespaceIndex)))
            {
                throw new ServiceResultException(StatusCodes.BadBrowseNameInvalid,
                    "An authored root BrowseName conflicts with the existing legacy asset.");
            }
            var properties = new Dictionary<NodeId, WotProjectedAffordance>();
            var actions = new Dictionary<NodeId, WotProjectedAffordance>();
            var assigned = new Dictionary<NodeId, NodeId> { [rootId] = entry.Asset.NodeId };
            foreach (WotProjectedAffordance affordance in converted.ProjectedAffordances)
            {
                if (!TryValidateChildName(entry.Name, affordance.Kind.ToString(), affordance.Name))
                {
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                        "A native interaction has an invalid legacy child name.");
                }
                NodeId nodeId = ExpandedNodeId.ToNodeId(
                    ExpandedNodeId.Parse(affordance.NodeId), context.NamespaceUris);
                if (affordance.Kind == WotAffordanceKind.Property)
                {
                    properties.Add(nodeId, affordance);
                    assigned[nodeId] = m_manager.AllocateChildNodeId(entry.Name, "props", affordance.Name);
                }
                else if (affordance.Kind == WotAffordanceKind.Action)
                {
                    actions.Add(nodeId, affordance);
                    assigned[nodeId] = m_manager.AllocateChildNodeId(entry.Name, "actions", affordance.Name);
                    MethodState method = nodes.Single(node => node.NodeId == nodeId) as MethodState ??
                        throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                            "An action does not identify a converted Method.");
                    if (method.InputArguments is not null)
                    {
                        assigned[method.InputArguments.NodeId] =
                            m_manager.AllocateChildNodeId(entry.Name, "actions", affordance.Name + "/InputArguments");
                    }
                    if (method.OutputArguments is not null)
                    {
                        assigned[method.OutputArguments.NodeId] =
                            m_manager.AllocateChildNodeId(entry.Name, "actions", affordance.Name + "/OutputArguments");
                    }
                }
            }
            foreach (BaseDataVariableState variable in nodes.OfType<BaseDataVariableState>())
            {
                if (ReferenceEquals(variable.Parent, root) && !assigned.ContainsKey(variable.NodeId) &&
                    !string.IsNullOrEmpty(variable.BrowseName.Name))
                {
                    assigned[variable.NodeId] = m_manager.AllocateChildNodeId(
                        entry.Name, "props", variable.BrowseName.Name);
                }
            }
            var propertyNodes = properties.ToDictionary(
                pair => nodes.Single(node => node.NodeId == pair.Key) as BaseDataVariableState ??
                    throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                        "A property does not identify a converted Variable."),
                pair => pair.Value);
            var actionNodes = actions.ToDictionary(
                pair => (MethodState)nodes.Single(node => node.NodeId == pair.Key), pair => pair.Value);
            var remap = new Dictionary<NodeId, NodeId>();
            context.NodeIdFactory = new LegacyGraphNodeIdFactory(
                m_manager, entry.Name, assigned, preserveIdentities);
            foreach (NodeState node in nodes.Where(node => node is not BaseInstanceState instance ||
                instance.Parent is null))
            {
                node.AssignNodeIds(context, remap);
            }
            foreach (NodeState node in nodes)
            {
                node.UpdateReferenceTargets(context, remap);
                if (node is BaseInstanceState instance)
                {
                    instance.TypeDefinitionId = Remap(instance.TypeDefinitionId, remap);
                    instance.ReferenceTypeId = Remap(instance.ReferenceTypeId, remap);
                }
                if (node is BaseVariableState variable)
                {
                    variable.DataType = Remap(variable.DataType, remap);
                    if (variable.Value.TryGetStructure(out ArrayOf<Argument> arguments))
                    {
                        foreach (Argument argument in arguments)
                        {
                            argument.DataType = Remap(argument.DataType, remap);
                        }
                    }
                }
                if (node is MethodState method)
                {
                    method.MethodDeclarationId = Remap(method.MethodDeclarationId, remap);
                }
            }
            var reserved = new Dictionary<NodeId, string>();
            // The owner root is reused; only its fixed descendants are reserved.
            m_manager.CreateAssetNode(entry.Name).GetInstanceHierarchy(
                m_manager.SystemContext, string.Empty, reserved);
            var unique = new HashSet<NodeId>();
            foreach (NodeState node in nodes)
            {
                if (string.IsNullOrEmpty(node.BrowseName.Name))
                {
                    throw new ServiceResultException(StatusCodes.BadBrowseNameInvalid,
                        "A native declaration requires a BrowseName.");
                }
                if (!unique.Add(node.NodeId))
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdExists,
                        "Native identities collide in the legacy asset's published graph.");
                }
                if (reserved.ContainsKey(node.NodeId))
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdExists,
                        "A native identity belongs to a fixed legacy asset node.");
                }
                NodeState? existing = m_manager.FindPredefinedNode<NodeState>(node.NodeId);
                if (existing is not null && !ReferenceEquals(existing, entry.Asset) &&
                    !entry.Properties.ContainsKey(node.NodeId) && !entry.Actions.ContainsKey(node.NodeId) &&
                    entry.NativeGraph?.Nodes.Contains(old => old.NodeId == node.NodeId) != true)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdExists,
                        "A native identity belongs to another published node.");
                }
            }
            NodeId hasWotComponent = ExpandedNodeId.ToNodeId(
                ReferenceTypeIds.HasWoTComponent, context.NamespaceUris);
            foreach (BaseDataVariableState variable in nodes.OfType<BaseDataVariableState>())
            {
                root.AddReferenceIfMissing(hasWotComponent, false, variable.NodeId);
                variable.AddReferenceIfMissing(hasWotComponent, true, root.NodeId);
            }
            return new WotLegacyPreparedGraph(root, nodes.ToArrayOf(), propertyNodes, actionNodes);
        }

        private static NodeId Remap(NodeId nodeId, Dictionary<NodeId, NodeId> mapping)
        {
            return mapping.TryGetValue(nodeId, out NodeId replacement) ? replacement : nodeId;
        }

        private async ValueTask PublishNativeGraphAsync(
            AssetEntry entry, WotLegacyPreparedGraph graph, ThingDescription td, CancellationToken ct)
        {
            var context = m_manager.SystemContext;
            var references = new List<IReference>();
            graph.Root.GetReferences(context, references);
            foreach (IReference reference in references)
            {
                if (!reference.IsInverse && reference.ReferenceTypeId != Ua.ReferenceTypeIds.HasTypeDefinition &&
                    entry.Asset.AddReferenceIfMissing(reference.ReferenceTypeId, false, reference.TargetId))
                {
                    graph.RootReferences.Add(reference);
                }
            }
            var children = new List<BaseInstanceState>();
            graph.Root.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                graph.Root.RemoveChild(child);
                entry.Asset.AddChild(child);
            }
            foreach (BaseDataVariableState variable in graph.Nodes.ToList().OfType<BaseDataVariableState>())
            {
                bool authored = graph.Properties.TryGetValue(variable, out WotProjectedAffordance? affordance);
                string name = authored ? affordance!.Name : variable.BrowseName.Name ??
                    throw new ServiceResultException(StatusCodes.BadBrowseNameInvalid);
                WotProperty? property = null;
                td.Properties?.TryGetValue(name, out property);
                bool readOnly = (variable.AccessLevel & AccessLevels.CurrentWrite) == 0;
                JsonElement? form = property?.Forms is { Count: > 0 } forms ? forms[0] : null;
                var tag = new WotPropertyTag(
                    name, variable.NodeId, variable.DataType, variable.ValueRank,
                    readOnly, property?.Observable ?? false, form);
                if (variable.Value.IsNull)
                {
                    variable.Value = TypeInfo.GetDefaultVariantValue(variable.DataType, variable.ValueRank);
                }
                variable.StatusCode = StatusCodes.Good;
                if (authored && form is not null)
                {
                    variable.OnSimpleReadValueAsync = (_, _, token) => ReadFromProviderAsync(entry, tag, token);
                    if (!readOnly)
                    {
                        variable.OnSimpleWriteValueAsync = (_, _, value, token) =>
                            WriteToProviderAsync(entry, tag, value, token);
                    }
                }
                entry.Properties.Add(variable.NodeId, (variable, tag));
            }
            foreach (KeyValuePair<MethodState, WotProjectedAffordance> pair in graph.Actions)
            {
                MethodState method = pair.Key;
                WotProjectedAffordance affordance = pair.Value;
                WotAction? action = null;
                td.Actions?.TryGetValue(affordance.Name, out action);
                ArrayOf<Argument> inputs = method.InputArguments is null ? [] : method.InputArguments.Value;
                ArrayOf<Argument> outputs = method.OutputArguments is null ? [] : method.OutputArguments.Value;
                JsonElement? form = action?.Forms is { Count: > 0 } forms ? forms[0] : null;
                var tag = new WotActionTag(
                    affordance.Name, method.NodeId, inputs.ToList(), outputs.ToList(), form,
                    affordance.ConditionAction, affordance.ActsOn);
                method.OnCallMethod2Async = (_, _, _, input, output, token) =>
                    InvokeActionAsync(entry, tag, input, output, token);
                entry.Actions.Add(method.NodeId, (method, tag));
            }
            foreach (NodeState node in graph.Nodes.ToList())
            {
                if (!ReferenceEquals(node, graph.Root) &&
                    (node is not BaseInstanceState instance || instance.Parent is null ||
                     ReferenceEquals(instance.Parent, entry.Asset)))
                {
                    await m_manager.AddPredefinedNodeAsync(node, ct).ConfigureAwait(false);
                }
            }
            entry.NativeGraph = graph;
        }

        private async ValueTask ClearNativeGraphAsync(AssetEntry entry, CancellationToken ct)
        {
            if (entry.NativeGraph is not { } graph)
            {
                return;
            }
            foreach (IReference reference in graph.RootReferences)
            {
                entry.Asset.RemoveReference(reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
            }
            foreach (NodeState node in graph.Nodes.ToList())
            {
                if (!ReferenceEquals(node, graph.Root) &&
                    m_manager.FindPredefinedNode<NodeState>(node.NodeId) is not null)
                {
                    await m_manager.DeleteNodeAsync(m_manager.SystemContext, node.NodeId, ct).ConfigureAwait(false);
                    if (node is BaseInstanceState instance && ReferenceEquals(instance.Parent, entry.Asset))
                    {
                        entry.Asset.RemoveChild(instance);
                    }
                }
            }
            entry.NativeGraph = null;
        }

        private sealed class LegacyGraphNodeIdFactory : INodeIdFactory
        {
            public LegacyGraphNodeIdFactory(
                WotConnectivityNodeManager manager,
                string assetName,
                Dictionary<NodeId, NodeId> assigned,
                bool preserveIdentities)
            {
                m_manager = manager;
                m_assetName = assetName;
                m_assigned = assigned;
                m_preserveIdentities = preserveIdentities;
            }

            public NodeId New(ISystemContext context, NodeState node)
            {
                if (m_preserveIdentities)
                {
                    if (node.NodeId.NamespaceIndex != m_manager.AssetNamespaceIndex)
                    {
                        throw new ServiceResultException(StatusCodes.BadNodeIdInvalid,
                            "A native declaration must use the legacy manager's asset namespace.");
                    }
                    return node.NodeId;
                }
                if (m_assigned.TryGetValue(node.NodeId, out NodeId assigned))
                {
                    return assigned;
                }
                if (node is not BaseInstanceState && node.NodeId.NamespaceIndex != m_manager.AssetNamespaceIndex)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "A new type declaration requires an explicitly owned legacy asset namespace.");
                }
                return m_manager.AllocateChildNodeId(m_assetName, "native",
                    Uri.EscapeDataString(NodeId.ToExpandedNodeId(node.NodeId, context.NamespaceUris).ToString()));
            }

            private readonly WotConnectivityNodeManager m_manager;
            private readonly string m_assetName;
            private readonly Dictionary<NodeId, NodeId> m_assigned;
            private readonly bool m_preserveIdentities;
        }
    }

    internal sealed class WotLegacyPreparedGraph
    {
        public WotLegacyPreparedGraph(
            BaseObjectState root,
            ArrayOf<NodeState> nodes,
            Dictionary<BaseDataVariableState, WotProjectedAffordance> properties,
            Dictionary<MethodState, WotProjectedAffordance> actions)
        {
            Root = root;
            Nodes = nodes;
            Properties = properties;
            Actions = actions;
        }

        public BaseObjectState Root { get; }
        public ArrayOf<NodeState> Nodes { get; }
        public Dictionary<BaseDataVariableState, WotProjectedAffordance> Properties { get; }
        public Dictionary<MethodState, WotProjectedAffordance> Actions { get; }
        public List<IReference> RootReferences { get; } = [];
    }
}
