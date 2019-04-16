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
using System.Text;
using System.Collections.Generic;
using System.Reflection;
using Opc.Ua;
using Opc.Ua.Server;

namespace Opc.Ua.Com.Client
{
    #region HdaParsedNodeId Class
    /// <summary>
    /// Stores the elements of a NodeId after it is parsed.
    /// </summary>
    /// <remarks>
    /// The NodeIds used by the samples are strings with an optional path appended.
    /// The RootType identifies the type of Root Node. The RootId is the unique identifier
    /// for the Root Node. The ComponentPath is constructed from the SymbolicNames
    /// of one or more children of the Root Node. 
    /// </remarks>
    public class HdaParsedNodeId : ParsedNodeId
    {
        #region Public Interface
        /// <summary>
        /// Gets or sets the aggregate id.
        /// </summary>
        /// <value>The aggregate id.</value>
        public uint AggregateId
        {
            get { return m_aggregateId; }
            set { m_aggregateId = value; }
        }

        /// <summary>
        /// Gets or sets the attribute id.
        /// </summary>
        /// <value>The attribute id.</value>
        public uint AttributeId
        {
            get { return m_attributeId; }
            set { m_attributeId = value; }
        }

        /// <summary>
        /// Parses the specified node identifier.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns>The parsed node identifier. Null if the identifier cannot be parsed.</returns>
        public static new HdaParsedNodeId Parse(NodeId nodeId)
        {
            // can only parse non-null string node identifiers.
            if (NodeId.IsNull(nodeId))
            {
                return null;
            }

            string identifier = nodeId.Identifier as string;

            if (String.IsNullOrEmpty(identifier))
            {
                return null;
            }

            HdaParsedNodeId parsedNodeId = new HdaParsedNodeId();
            parsedNodeId.NamespaceIndex = nodeId.NamespaceIndex;

            int start = 0;

            // extract the type of identifier.
            parsedNodeId.RootType = (int)ExtractNumber(identifier, ref start);

            if (start >= identifier.Length || identifier[start] != ':')
            {
                return null;
            }

            // extract any component path.
            StringBuilder buffer = new StringBuilder();

            int index = start+1;

            parsedNodeId.RootId = ExtractAndUnescapeString(identifier, ref index, '&', '?');

            // extract any component.
            int end = index+1;
            parsedNodeId.ComponentPath = null;

            // extract the component path.
            if (end < identifier.Length)
            {
                parsedNodeId.ComponentPath = identifier.Substring(end);
            }

            // extract the category and condition name.
            start = 0;
            identifier = parsedNodeId.RootId;

            switch (parsedNodeId.RootType)
            {
                case HdaModelUtils.HdaAggregate:
                {
                    parsedNodeId.AggregateId = ExtractNumber(identifier, ref start);

                    if (start < identifier.Length)
                    {
                        return null;
                    }

                    break;
                }
            }

            // extract the attribute id.
            if (!String.IsNullOrEmpty(parsedNodeId.ComponentPath))
            {
                start = 0;
                identifier = parsedNodeId.ComponentPath;

                switch (parsedNodeId.RootType)
                {
                    case HdaModelUtils.HdaItemAttribute:
                    {
                        parsedNodeId.AttributeId = ExtractNumber(identifier, ref start);

                        if (start < identifier.Length)
                        {
                            return null;
                        }

                        break;
                    }
                }
            }

            return parsedNodeId;
        }

        /// <summary>
        /// Constructs a node identifier.
        /// </summary>
        /// <returns>The node identifier.</returns>
        public new NodeId Construct()
        {
            return Construct(this.RootType, this.RootId, this.ComponentPath, this.NamespaceIndex);
        }

        /// <summary>
        /// Constructs a node identifier.
        /// </summary>
        /// <returns>The node identifier.</returns>
        public static NodeId Construct(int rootType, string rootId, string componentPath, ushort namespaceIndex)
        {
            StringBuilder buffer = new StringBuilder();

            // add the root type.
            buffer.Append(rootType);
            buffer.Append(':');

            // add the root identifier.
            EscapeAndAppendString(buffer, rootId, '&', '?');

            // add the component path.
            if (!String.IsNullOrEmpty(componentPath))
            {
                buffer.Append('?');
                buffer.Append(componentPath);
            }

            // construct the node id with the namespace index provided.
            return new NodeId(buffer.ToString(), namespaceIndex);
        }
        #endregion

        #region Private Fields
        private uint m_aggregateId;
        private uint m_attributeId;
        #endregion
    }
    #endregion
}
