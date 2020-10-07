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
    /// Stores the elements of a NodeId after it is parsed.
    /// </summary>
    /// <remarks>
    /// The NodeIds used by the samples are strings with an optional path appended.
    /// The RootType identifies the type of Root Node. The RootId is the unique identifier
    /// for the Root Node. The ComponentPath is constructed from the SymbolicNames
    /// of one or more children of the Root Node. 
    /// </remarks>
    public class ParsedNodeId
    {
        #region Public Interface
        /// <summary>
        /// The namespace index that qualified the NodeId.
        /// </summary>
        public ushort NamespaceIndex
        {
            get { return m_namespaceIndex; }
            set { m_namespaceIndex = value; }
        }

        /// <summary>
        /// The identifier for the root of the NodeId.
        /// </summary>
        public string RootId
        {
            get { return m_rootId; }
            set { m_rootId = value; }
        }

        /// <summary>
        /// The type of root node.
        /// </summary>
        public int RootType
        {
            get { return m_rootType; }
            set { m_rootType = value; }
        }

        /// <summary>
        /// The relative path to the component identified by the NodeId.
        /// </summary>
        public string ComponentPath
        {
            get { return m_componentPath; }
            set { m_componentPath = value; }
        }

        /// <summary>
        /// Parses the specified node identifier.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns>The parsed node identifier. Null if the identifier cannot be parsed.</returns>
        public static ParsedNodeId Parse(NodeId nodeId)
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

            ParsedNodeId parsedNodeId = new ParsedNodeId();
            parsedNodeId.NamespaceIndex = nodeId.NamespaceIndex;

            // extract the type of identifier.
            parsedNodeId.RootType = 0;

            int start = 0;

            for (int ii = 0; ii < identifier.Length; ii++)
            {
                if (!Char.IsDigit(identifier[ii]))
                {
                    start = ii;
                    break;
                }

                parsedNodeId.RootType *= 10;
                parsedNodeId.RootType += (byte)(identifier[ii] - '0');
            }

            if (start >= identifier.Length || identifier[start] != ':')
            {
                return null;
            }

            // extract any component path.
            StringBuilder buffer = new StringBuilder();

            int index = start+1;
            int end = identifier.Length;

            bool escaped = false;

            while (index < end)
            {
                char ch = identifier[index++];

                // skip any escape character but keep the one after it.
                if (ch == '&')
                {
                    escaped = true;
                    continue;
                }

                if (!escaped && ch == '?')
                {
                    end = index;
                    break;
                }

                buffer.Append(ch);
                escaped = false;
            }

            // extract any component.
            parsedNodeId.RootId = buffer.ToString();
            parsedNodeId.ComponentPath = null;

            if (end < identifier.Length)
            {
                parsedNodeId.ComponentPath = identifier.Substring(end);
            }

            return parsedNodeId;
        }


        /// <summary>
        /// Constructs a node identifier from the component pieces.
        /// </summary>
        public static NodeId Construct(int rootType, string rootId, ushort namespaceIndex, params string[] componentNames)
        {
            ParsedNodeId pnd = new ParsedNodeId();

            pnd.RootType = rootType;
            pnd.RootId = rootId;
            pnd.NamespaceIndex = namespaceIndex;

            if (componentNames != null)
            {
                StringBuilder path = new StringBuilder();

                for (int ii = 0; ii < componentNames.Length; ii++)
                {
                    if (path.Length > 0)
                    {
                        path.Append('/');
                    }

                    path.Append(componentNames[ii]);
                }

                pnd.ComponentPath = path.ToString();
            }

            return pnd.Construct(null);
        }

        /// <summary>
        /// Constructs a node identifier.
        /// </summary>
        /// <returns>The node identifier.</returns>
        public NodeId Construct()
        {
            return Construct(null);
        }

        /// <summary>
        /// Constructs a node identifier for a component with the specified name.
        /// </summary>
        /// <returns>The node identifier.</returns>
        public NodeId Construct(string componentName)
        {
            StringBuilder buffer = new StringBuilder();

            // add the root type.
            buffer.Append(RootType);
            buffer.Append(':');

            // add the root identifier.
            if (this.RootId != null)
            {
                for (int ii = 0; ii < this.RootId.Length; ii++)
                {
                    char ch = this.RootId[ii];

                    // escape any special characters.
                    if (ch == '&' || ch == '?')
                    {
                        buffer.Append('&');
                    }

                    buffer.Append(ch);
                }
            }

            // add the component path.
            if (!String.IsNullOrEmpty(this.ComponentPath))
            {
                buffer.Append('?');
                buffer.Append(this.ComponentPath);
            }

            // add the component name.
            if (!String.IsNullOrEmpty(componentName))
            {
                if (String.IsNullOrEmpty(this.ComponentPath))
                {
                    buffer.Append('?');
                }
                else
                {
                    buffer.Append('/');
                }

                buffer.Append(componentName);
            }

            // construct the node id with the namespace index provided.
            return new NodeId(buffer.ToString(), this.NamespaceIndex);
        }

        /// <summary>
        /// Constructs the node identifier for a component.
        /// </summary>
        public static NodeId CreateIdForComponent(NodeState component, ushort namespaceIndex)
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
        #endregion

        #region Private Fields
        private ushort m_namespaceIndex;
        private string m_rootId;
        private int m_rootType;
        private string m_componentPath;
        #endregion
    }
}
