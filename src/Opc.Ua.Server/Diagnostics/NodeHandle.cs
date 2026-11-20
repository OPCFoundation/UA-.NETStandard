// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server
{
    /// <summary>
    /// Stores information about a NodeId specified by the client.
    /// </summary>
    /// <remarks>
    /// A NodeHandle is created when GetManagerHandle is called and will only contain
    /// information found by parsing the NodeId. The ValidateNode method is used to
    /// verify that the NodeId refers to a real Node and find a NodeState object that
    /// can be used to access the Node.
    /// </remarks>
    public class NodeHandle
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NodeHandle"/> class.
        /// </summary>
        public NodeHandle()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NodeHandle"/> class.
        /// </summary>
        /// <param name="nodeId">The node id.</param>
        /// <param name="node">The node.</param>
        public NodeHandle(NodeId nodeId, NodeState node)
        {
            NodeId = nodeId;
            Validated = true;
            Node = node;
        }

        /// <summary>
        /// The NodeId provided by the client.
        /// </summary>
        public NodeId NodeId { get; set; }

        /// <summary>
        /// The parsed identifier (must not be null if Validated == False).
        /// </summary>
        public object ParsedNodeId { get; set; } = null!;

        /// <summary>
        /// A unique string identifier for the root of a complex object tree.
        /// </summary>
        public NodeId RootId { get; set; }

        /// <summary>
        /// A path to a component within the tree identified by the root id.
        /// </summary>
        public string ComponentPath { get; set; } = null!;

        /// <summary>
        /// An index associated with the handle.
        /// </summary>
        /// <remarks>
        /// This is used to keep track of the position in the complete list of Nodes provided by the Client.
        /// </remarks>
        public int Index { get; set; }

        /// <summary>
        /// Whether the handle has been validated.
        /// </summary>
        /// <remarks>
        /// When validation is complete the Node property must have a valid object.
        /// </remarks>
        public bool Validated { get; set; }

        /// <summary>
        /// An object that can be used to access the Node identified by the NodeId.
        /// </summary>
        /// <remarks>
        /// Not set until after the handle is validated.
        /// </remarks>
        public NodeState Node { get; set; } = null!;

        /// <summary>
        /// An object that can be used to manage the items which are monitoring the node.
        /// </summary>
        public MonitoredNode2 MonitoredNode { get; set; } = null!;

        /// <summary>
        /// The node manager that returned this resolved handle while a Read request was
        /// validated; it reads the node through this handle instead of resolving it again.
        /// </summary>
        internal AsyncCustomNodeManager? RoutedBy { get; set; }
    }
}
