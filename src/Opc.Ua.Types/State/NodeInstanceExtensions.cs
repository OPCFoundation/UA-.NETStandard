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

namespace Opc.Ua
{
    /// <summary>
    /// Helpers for materialising type instances at runtime.
    /// </summary>
    /// <remarks>
    /// Deliberately a distinct type from <see cref="NodeStateExtensions"/>
    /// (which is redefined per stack assembly) so the source-generated
    /// <c>CreateInstanceOf&lt;Type&gt;</c> factories can reference it by a
    /// single, unambiguous fully-qualified name from any generated model.
    /// </remarks>
    public static class NodeInstanceExtensions
    {
        /// <summary>
        /// Recursively assigns per-instance NodeIds to every descendant of
        /// <paramref name="node"/> using the active
        /// <see cref="ISystemContext.NodeIdFactory"/>.
        /// </summary>
        /// <remarks>
        /// Rebases a dynamically instantiated subtree (created via a
        /// generated <c>CreateInstanceOf&lt;Type&gt;</c> factory, which stamps
        /// TYPE NodeIds on children) onto per-instance NodeIds derived from
        /// the parent, so multiple instances of the same type never collide
        /// on those NodeIds. Supports factories that derive an ID from the
        /// current node as well as factories that allocate only for null IDs.
        /// Walks top-down so each child's NodeId derives from its already-
        /// rebased parent, then updates references that target the previous
        /// declaration IDs. No-op when the context has no
        /// <see cref="ISystemContext.NodeIdFactory"/>.
        /// </remarks>
        /// <param name="context">
        /// The system context supplying the NodeIdFactory.
        /// </param>
        /// <param name="node">
        /// The instance whose descendants are rebased.
        /// </param>
        public static void AssignInstanceChildNodeIds(
            this ISystemContext context,
            NodeState node)
        {
            context.AssignInstanceChildNodeIds(node, NodeId.Null);
        }

        /// <summary>
        /// Assigns a per-instance NodeId to <paramref name="node"/> and
        /// returns its previous NodeId.
        /// </summary>
        /// <remarks>
        /// The factory first sees the declaration NodeId so parent/path based
        /// implementations can use it. If it preserves that ID, the factory
        /// is invoked again with a null NodeId to support allocators such as
        /// the default custom NodeManagers. A null-ID allocation that collides
        /// with the declaration ID is retried once; if no call produces a
        /// fresh ID, the previous ID is preserved for compatibility.
        /// </remarks>
        /// <param name="context">
        /// The system context supplying the NodeIdFactory.
        /// </param>
        /// <param name="node">The instance whose NodeId is assigned.</param>
        /// <returns>The NodeId before assignment.</returns>
        public static NodeId AssignInstanceNodeId(
            this ISystemContext context,
            NodeState node)
        {
            if (context?.NodeIdFactory == null || node == null)
            {
                return node?.NodeId ?? NodeId.Null;
            }

            NodeId previousNodeId = node.NodeId;
            NodeId assignedNodeId = context.NodeIdFactory.New(context, node);
            if (assignedNodeId.IsNull || assignedNodeId.Equals(previousNodeId))
            {
                assignedNodeId = previousNodeId;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    node.NodeId = NodeId.Null;
                    NodeId allocatedNodeId = context.NodeIdFactory.New(context, node);
                    if (!allocatedNodeId.IsNull &&
                        !allocatedNodeId.Equals(previousNodeId))
                    {
                        assignedNodeId = allocatedNodeId;
                        break;
                    }
                }
            }

            node.NodeId = assignedNodeId;
            return previousNodeId;
        }

        /// <summary>
        /// Recursively assigns per-instance NodeIds to every descendant and
        /// updates references that target the previous root or descendant IDs.
        /// </summary>
        /// <param name="context">
        /// The system context supplying the NodeIdFactory.
        /// </param>
        /// <param name="node">
        /// The instance whose descendants are rebased.
        /// </param>
        /// <param name="previousNodeId">
        /// The root NodeId before it was reassigned, or
        /// <see cref="NodeId.Null"/> when the root did not change.
        /// </param>
        public static void AssignInstanceChildNodeIds(
            this ISystemContext context,
            NodeState node,
            NodeId previousNodeId)
        {
            context.AssignInstanceChildNodeIds(
                node,
                previousNodeId,
                node);
        }

        /// <summary>
        /// Recursively assigns per-instance NodeIds to every descendant and
        /// updates references from the specified owning subtree.
        /// </summary>
        /// <param name="context">
        /// The system context supplying the NodeIdFactory.
        /// </param>
        /// <param name="node">
        /// The instance whose descendants are rebased.
        /// </param>
        /// <param name="previousNodeId">
        /// The root NodeId before it was reassigned, or
        /// <see cref="NodeId.Null"/> when the root did not change.
        /// </param>
        /// <param name="referenceRoot">
        /// The owning subtree whose references must be updated.
        /// </param>
        public static void AssignInstanceChildNodeIds(
            this ISystemContext context,
            NodeState node,
            NodeId previousNodeId,
            NodeState referenceRoot)
        {
            if (context == null || node == null || referenceRoot == null)
            {
                return;
            }

            var mappingTable = new Dictionary<NodeId, NodeId>();
            if (!previousNodeId.IsNull &&
                !node.NodeId.IsNull &&
                !previousNodeId.Equals(node.NodeId))
            {
                mappingTable[previousNodeId] = node.NodeId;
            }

            if (context.NodeIdFactory != null)
            {
                AssignChildNodeIds(context, node, mappingTable);
            }

            if (mappingTable.Count > 0)
            {
                referenceRoot.UpdateReferenceTargets(context, mappingTable);
            }
        }

        private static void AssignChildNodeIds(
            ISystemContext context,
            NodeState node,
            Dictionary<NodeId, NodeId> mappingTable)
        {
            var children = new List<BaseInstanceState>();
            node.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                NodeId previousNodeId = context.AssignInstanceNodeId(child);
                if (!previousNodeId.IsNull &&
                    !child.NodeId.IsNull &&
                    !previousNodeId.Equals(child.NodeId))
                {
                    mappingTable[previousNodeId] = child.NodeId;
                }
                AssignChildNodeIds(context, child, mappingTable);
            }
        }
    }
}
