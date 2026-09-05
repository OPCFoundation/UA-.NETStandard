/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;

namespace Opc.Ua.Server.Fluent
{
    internal static class FluentNodeRegistration
    {
        internal static void AssignNodeId(
            INodeManagerBuilder builder,
            NodeState node,
            NodeState parent)
        {
            if (parent is null || parent.NodeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Cannot assign a NodeId to node '{0}' without a parent NodeId.",
                    node.BrowseName);
            }
            if (node.BrowseName.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameInvalid,
                    "Cannot assign a NodeId to a node without a browse name.");
            }

            NodeId previousNodeId = node.NodeId;
            AssignNodeIdValue(builder, node, parent);
            if (node.NodeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The deterministic NodeId formatter did not assign an id to node '{0}'.",
                    node.BrowseName);
            }
            var mappings = new Dictionary<NodeId, NodeId>();
            AddMapping(mappings, previousNodeId, node.NodeId);
            AssignDescendantNodeIds(builder, node, mappings);
            if (mappings.Count > 0)
            {
                node.UpdateReferenceTargets(builder.Context, mappings);
            }
        }

        internal static void AssignDescendantNodeIds(
            INodeManagerBuilder builder,
            NodeState node)
        {
            var mappings = new Dictionary<NodeId, NodeId>();
            AssignDescendantNodeIds(builder, node, mappings);
            if (mappings.Count > 0)
            {
                node.UpdateReferenceTargets(builder.Context, mappings);
            }
        }

        private static void AssignDescendantNodeIds(
            INodeManagerBuilder builder,
            NodeState parent,
            Dictionary<NodeId, NodeId> mappings)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(builder.Context, children);
            for (int i = 0; i < children.Count; i++)
            {
                BaseInstanceState child = children[i];
                NodeId previousNodeId = child.NodeId;
                AssignNodeIdValue(builder, child, parent);
                AddMapping(mappings, previousNodeId, child.NodeId);
                AssignDescendantNodeIds(builder, child, mappings);
            }
        }

        private static void AssignNodeIdValue(
            INodeManagerBuilder builder,
            NodeState node,
            NodeState parent)
        {
            node.NodeId = NodeId.Null;
            if (builder.Context.NodeIdFactory is { } factory)
            {
                node.NodeId = factory.New(builder.Context, node);
                return;
            }

            ushort namespaceIndex = parent.NodeId.NamespaceIndex;
            if (namespaceIndex == 0)
            {
                namespaceIndex = builder is NodeManagerBuilder nodeManagerBuilder
                    ? nodeManagerBuilder.DefaultNamespaceIndex
                    : node.BrowseName.NamespaceIndex;
            }
            node.NodeId = Nodes.NodeSourceNodeIdFactory.CreateChildNodeId(
                parent.NodeId,
                node.BrowseName,
                namespaceIndex,
                builder.Context.NamespaceUris);
        }

        private static void AddMapping(
            Dictionary<NodeId, NodeId> mappings,
            NodeId previousNodeId,
            NodeId nodeId)
        {
            if (!previousNodeId.IsNull &&
                !nodeId.IsNull &&
                previousNodeId != nodeId)
            {
                mappings[previousNodeId] = nodeId;
            }
        }

        internal static void RegisterCreatedNode(
            INodeManagerBuilder builder,
            NodeState node)
        {
            if (builder.NodeManager is AsyncCustomNodeManager manager)
            {
                manager.AddPredefinedNodeSynchronously(node);
            }
        }

        internal static void RegisterAlarmEventSource(
            INodeManagerBuilder builder,
            NodeState source)
        {
            BaseObjectState? firstSource = null;
            for (NodeState? current = source; current != null;)
            {
                if (current is BaseObjectState notifier)
                {
                    firstSource ??= notifier;
                    notifier.EventNotifier |= EventNotifiers.SubscribeToEvents;
                }

                current = current is BaseInstanceState instance ? instance.Parent : null;
            }

            if (firstSource != null &&
                builder.NodeManager is AsyncCustomNodeManager manager)
            {
                manager.AddRootNotifierSynchronously(firstSource);
            }
        }
    }
}
