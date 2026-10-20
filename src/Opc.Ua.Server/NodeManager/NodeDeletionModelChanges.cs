/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Opc.Ua.Server.NodeManager
{
    /// <summary>
    /// Captures deletion eligibility and reports only model changes that actually committed.
    /// </summary>
    internal sealed class NodeDeletionModelChanges
    {
        /// <summary>
        /// Captures the registered subtree and its surviving reference endpoints before mutation.
        /// </summary>
        internal NodeDeletionModelChanges(
            ISystemContext context,
            NodeId nodeId,
            NodeIdDictionary<NodeState> nodes,
            bool enabled,
            bool requireNodeVersion)
        {
            m_context = context;
            m_nodes = nodes;
            if (!enabled || !nodes.TryGetValue(nodeId, out NodeState? root))
            {
                return;
            }

            var subtree = new List<NodeState> { root };
            var visited = new HashSet<NodeId>();
            for (int index = 0; index < subtree.Count; index++)
            {
                NodeState node = subtree[index];
                if (!visited.Add(node.NodeId) ||
                    !nodes.TryGetValue(node.NodeId, out NodeState? registered) ||
                    !ReferenceEquals(node, registered))
                {
                    continue;
                }

                m_deletedNodes.Add((
                    node.NodeId, node, GetTypeDefinition(node),
                    !requireNodeVersion || node.HasNodeVersion()));
                var children = new List<BaseInstanceState>();
                node.GetChildren(context, children);
                subtree.AddRange(children);

                if (node is BaseInstanceState { Parent: NodeState parent } child)
                {
                    if (IsRegistered(parent.NodeId, parent))
                    {
                        m_references.Add((parent.NodeId, parent, child.ReferenceTypeId, false, node.NodeId, child));
                    }
                    else
                    {
                        m_externalReferences.Add((parent.NodeId, node.NodeId));
                    }
                }

                var references = new List<IReference>();
                node.GetReferences(context, references);
                foreach (IReference reference in references)
                {
                    if (reference.TargetId.IsAbsolute)
                    {
                        continue;
                    }
                    NodeId targetId = ExpandedNodeId.ToNodeId(reference.TargetId, context.NamespaceUris);
                    if (nodes.TryGetValue(targetId, out NodeState? target))
                    {
                        if (target.ReferenceExists(reference.ReferenceTypeId, !reference.IsInverse, node.NodeId))
                        {
                            m_references.Add((
                                targetId, target, reference.ReferenceTypeId, !reference.IsInverse, node.NodeId, null));
                        }
                    }
                    else
                    {
                        m_externalReferences.Add((targetId, node.NodeId));
                    }
                }
            }
        }

        /// <summary>
        /// Reports committed changes and then propagates the original failure, preserving reporting failures too.
        /// </summary>
        internal void Complete(
            Exception? deletionFailure,
            bool deleteTargetReferences,
            Action<ArrayOf<ModelChangeStructureDataType>> reportChanges)
        {
            try
            {
                ArrayOf<ModelChangeStructureDataType> changes =
                    GetCommittedChanges(deletionFailure == null && deleteTargetReferences);
                if (changes.Count != 0)
                {
                    reportChanges(changes);
                }
            }
            catch (Exception reportingFailure) when (
                deletionFailure != null && reportingFailure is not OutOfMemoryException)
            {
                throw new AggregateException(
                    "Node deletion and model-change completion failed.", deletionFailure, reportingFailure);
            }
            if (deletionFailure != null)
            {
                ExceptionDispatchInfo.Capture(deletionFailure).Throw();
            }
        }

        private ArrayOf<ModelChangeStructureDataType> GetCommittedChanges(bool targetReferenceCleanupCompleted)
        {
            var changes = new Dictionary<NodeId, ModelChangeStructureDataType>();
            foreach ((NodeId id, NodeState node, NodeId type, bool eligible) in m_deletedNodes)
            {
                if (eligible && !IsRegistered(id, node))
                {
                    changes[id] = new ModelChangeStructureDataType
                    {
                        Affected = id,
                        AffectedType = type,
                        Verb = (byte)ModelChangeVerbs.NodeDeleted
                    };
                }
            }

            foreach ((NodeId id, NodeState source, NodeId type, bool inverse,
                NodeId target, BaseInstanceState? child) in m_references)
            {
                if (!IsRegistered(id, source))
                {
                    continue;
                }
                bool stillExists;
                if (child != null)
                {
                    var children = new List<BaseInstanceState>();
                    source.GetChildren(m_context, children);
                    stillExists = children.Contains(child);
                }
                else
                {
                    stillExists = source.ReferenceExists(type, inverse, target);
                }
                if (!stillExists)
                {
                    changes[id] = new ModelChangeStructureDataType
                    {
                        Affected = id,
                        AffectedType = GetTypeDefinition(source),
                        Verb = (byte)ModelChangeVerbs.ReferenceDeleted
                    };
                }
            }

            // Foreign reference endpoints cannot be inspected after partial or skipped cleanup.
            if (targetReferenceCleanupCompleted)
            {
                foreach ((NodeId target, NodeId source) in m_externalReferences)
                {
                    if (!m_nodes.ContainsKey(source))
                    {
                        changes[target] = new ModelChangeStructureDataType
                        {
                            Affected = target,
                            Verb = (byte)ModelChangeVerbs.ReferenceDeleted
                        };
                    }
                }
            }
            return [.. changes.Values];
        }

        private bool IsRegistered(NodeId id, NodeState node)
        {
            return m_nodes.TryGetValue(id, out NodeState? registered) && ReferenceEquals(registered, node);
        }

        private static NodeId GetTypeDefinition(NodeState node)
        {
            return node is BaseInstanceState instance ? instance.TypeDefinitionId : NodeId.Null;
        }

        private readonly ISystemContext m_context;
        private readonly NodeIdDictionary<NodeState> m_nodes;
        private readonly List<(NodeId Id, NodeState Node, NodeId Type, bool Eligible)> m_deletedNodes = [];
        private readonly List<(NodeId Id, NodeState Source, NodeId Type, bool Inverse,
            NodeId Target, BaseInstanceState? Child)> m_references = [];
        private readonly List<(NodeId Target, NodeId Source)> m_externalReferences = [];
    }
}
