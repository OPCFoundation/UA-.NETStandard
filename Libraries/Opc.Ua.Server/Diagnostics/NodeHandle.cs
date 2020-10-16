/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
