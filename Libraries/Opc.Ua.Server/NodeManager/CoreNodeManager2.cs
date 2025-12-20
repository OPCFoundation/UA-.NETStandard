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

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The core node manager for the server based on CustomNodeManager2.
    /// </summary>
    /// <remarks>
    /// Every Server has one instance of this NodeManager.
    /// It manages the built-in OPC UA nodes and provides core functionality.
    /// This is a refactored version of CoreNodeManager that inherits from CustomNodeManager2
    /// to consolidate the NodeManager implementations in the server library.
    /// </remarks>
    public class CoreNodeManager2 : CustomNodeManager2
    {
        /// <summary>
        /// Initializes the node manager with default values.
        /// </summary>
        public CoreNodeManager2(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ushort dynamicNamespaceIndex)
            : base(
                  server,
                  configuration,
                  true, // Enable SamplingGroups
                  server.Telemetry.CreateLogger<CoreNodeManager2>(),
                  Array.Empty<string>()) // CoreNodeManager manages namespaces 0 and 1 by default
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            // Store the dynamic namespace index (typically namespace index 1)
            m_dynamicNamespaceIndex = dynamicNamespaceIndex;

            // Use namespace 1 if out of range
            if (m_dynamicNamespaceIndex == 0 ||
                m_dynamicNamespaceIndex >= server.NamespaceUris.Count)
            {
                m_dynamicNamespaceIndex = 1;
            }

            // Set up namespaces - CoreNodeManager handles namespace 0 (UA) and 1 (server namespace)
            SetNamespaceIndexes([0, m_dynamicNamespaceIndex]);
        }

        /// <summary>
        /// Acquires the lock on the node manager.
        /// </summary>
        /// <remarks>
        /// This property provides compatibility with the old CoreNodeManager API.
        /// It maps to the Lock property from CustomNodeManager2.
        /// </remarks>
        public object DataLock => Lock;

        /// <summary>
        /// Returns an opaque handle identifying the node to the node manager.
        /// </summary>
        public override object GetManagerHandle(NodeId nodeId)
        {
            lock (Lock)
            {
                if (NodeId.IsNull(nodeId))
                {
                    return null;
                }

                // Check if it's in namespace 0 (UA standard namespace) or the dynamic namespace
                if (nodeId.NamespaceIndex != 0 && nodeId.NamespaceIndex != m_dynamicNamespaceIndex)
                {
                    return null;
                }

                // Try to find the node in predefined nodes
                NodeState node = Find(nodeId);
                if (node != null)
                {
                    return new NodeHandle(nodeId, node);
                }

                // Return null if not found (will be handled by other node managers)
                return null;
            }
        }

        /// <summary>
        /// Creates a unique node identifier.
        /// </summary>
        public NodeId CreateUniqueNodeId()
        {
            return CreateUniqueNodeId(m_dynamicNamespaceIndex);
        }

        /// <summary>
        /// Creates a new unique identifier for a node in the specified namespace.
        /// </summary>
        private NodeId CreateUniqueNodeId(ushort namespaceIndex)
        {
            return new NodeId(Utils.IncrementIdentifier(ref m_lastId), namespaceIndex);
        }

        /// <summary>
        /// Imports the nodes from a dictionary of NodeState objects.
        /// </summary>
        public void ImportNodes(ISystemContext context, IEnumerable<NodeState> predefinedNodes)
        {
            ImportNodes(context, predefinedNodes, false);
        }

        /// <summary>
        /// Imports the nodes from a dictionary of NodeState objects.
        /// </summary>
        internal void ImportNodes(
            ISystemContext context,
            IEnumerable<NodeState> predefinedNodes,
            bool isInternal)
        {
            lock (Lock)
            {
                foreach (NodeState node in predefinedNodes)
                {
                    // Add the node to the predefined nodes dictionary
                    AddPredefinedNode(context, node);
                }
            }
        }

        /// <summary>
        /// Attaches a node to the address space.
        /// </summary>
        /// <remarks>
        /// This method is provided for compatibility with the old CoreNodeManager.
        /// It maps to AddPredefinedNode from CustomNodeManager2.
        /// </remarks>
        internal void AttachNode(NodeState node, bool isInternal)
        {
            AddPredefinedNode(SystemContext, node);
        }

        /// <summary>
        /// Returns the namespace index used for dynamically created nodes.
        /// </summary>
        public ushort DynamicNamespaceIndex => m_dynamicNamespaceIndex;

        private uint m_lastId;
        private readonly ushort m_dynamicNamespaceIndex;
    }
}
