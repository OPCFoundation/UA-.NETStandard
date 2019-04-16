/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.HistoricalAccessServer
{
    /// <summary>
    /// A class that builds NodeIds used by the HistoricalAccess NodeManager
    /// </summary>
    public static class ModelUtils
    {
        /// <summary>
        /// The RootType for a Segment node identfier.
        /// </summary>
        public const int Segment = 0;

        /// <summary>
        /// The RootType for a Block node identfier.
        /// </summary>
        public const int Block = 1;

        /// <summary>
        /// Constructs a node identifier for a segment.
        /// </summary>
        /// <param name="segmentPath">The segment path.</param>
        /// <param name="namespaceIndex">Index of the namespace that qualifies the identifier.</param>
        /// <returns>The new node identifier.</returns>
        public static NodeId ConstructIdForSegment(string segmentPath, ushort namespaceIndex)
        {
            ParsedNodeId parsedNodeId = new ParsedNodeId();

            parsedNodeId.RootId = segmentPath;
            parsedNodeId.NamespaceIndex = namespaceIndex;
            parsedNodeId.RootType = 0;

            return parsedNodeId.Construct();
        }

        /// <summary>
        /// Constructs a NodeId for a block.
        /// </summary>
        /// <param name="blockId">The block id.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns>The new NodeId.</returns>
        public static NodeId ConstructIdForBlock(string blockId, ushort namespaceIndex)
        {
            ParsedNodeId parsedNodeId = new ParsedNodeId();

            parsedNodeId.RootId = blockId;
            parsedNodeId.NamespaceIndex = namespaceIndex;
            parsedNodeId.RootType = 1;

            return parsedNodeId.Construct();
        }

        /// <summary>
        /// Constructs the node identifier for a component.
        /// </summary>
        /// <param name="component">The component.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns>The node identifier for a component.</returns>
        public static NodeId ConstructIdForComponent(NodeState component, ushort namespaceIndex)
        {
            if (component == null)
            {
                return null;
            }

            // components must be instances with a parent.
            BaseInstanceState instance = component as BaseInstanceState;

            if (instance == null || instance.Parent == null)
            {
                return component.NodeId;
            }

            // parent must have a string identifier.
            string parentId = instance.Parent.NodeId.Identifier as string;
            
            if (parentId == null)
            {
                return null;
            }

            StringBuilder buffer = new StringBuilder();
            buffer.Append(parentId);
            
            // check if the parent is another component.
            int index = parentId.IndexOf('?');

            if (index < 0)
            {
                buffer.Append('?');
            }
            else
            {
                buffer.Append('/');
            }

            buffer.Append(component.SymbolicName);

            // return the node identifier.
            return new NodeId(buffer.ToString(), namespaceIndex);
        }
    }
}
