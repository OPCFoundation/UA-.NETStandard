/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Text;
using Opc.Ua;

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
        #region Constructors
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
            this.NodeId = nodeId;
            this.Validated = true;
            this.Node = node;
        }
        #endregion
        
        #region Public Interface
        /// <summary>
        /// The NodeId provided by the client.
        /// </summary>
        public NodeId NodeId { get; set; }

        /// <summary>
        /// The parsed identifier (must not be null if Validated == False).
        /// </summary>
        public object ParsedNodeId { get; set; }

        /// <summary>
        /// A unique string identifier for the root of a complex object tree.
        /// </summary>
        public NodeId RootId { get; set; }

        /// <summary>
        /// A path to a component within the tree identified by the root id.
        /// </summary>
        public string ComponentPath { get; set; }

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
        public NodeState Node { get; set; }

        /// <summary>
        /// An object that can be used to manage the items which are monitoring the node.
        /// </summary>
        public MonitoredNode2 MonitoredNode { get; set; }
        #endregion
    }
}
