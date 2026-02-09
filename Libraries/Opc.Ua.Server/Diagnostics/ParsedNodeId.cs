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
using System.Text;

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
        /// <summary>
        /// The namespace index that qualified the NodeId.
        /// </summary>
        public ushort NamespaceIndex { get; set; }

        /// <summary>
        /// The identifier for the root of the NodeId.
        /// </summary>
        public string RootId { get; set; }

        /// <summary>
        /// The type of root node.
        /// </summary>
        public int RootType { get; set; }

        /// <summary>
        /// The relative path to the component identified by the NodeId.
        /// </summary>
        public string ComponentPath { get; set; }

        /// <summary>
        /// Parses the specified node identifier.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns>The parsed node identifier. Null if the identifier cannot be parsed.</returns>
        public static ParsedNodeId Parse(NodeId nodeId)
        {
            // can only parse non-null string node identifiers.
            if (nodeId.IsNullNodeId)
            {
                return null;
            }

            if (!nodeId.TryGetIdentifier(out string identifier) ||
                string.IsNullOrEmpty(identifier))
            {
                return null;
            }

            var parsedNodeId = new ParsedNodeId
            {
                NamespaceIndex = nodeId.NamespaceIndex,

                // extract the type of identifier.
                RootType = 0
            };

            int start = 0;

            for (int ii = 0; ii < identifier.Length; ii++)
            {
                if (!char.IsDigit(identifier[ii]))
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
            var buffer = new StringBuilder();

            int index = start + 1;
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
                parsedNodeId.ComponentPath = identifier[end..];
            }

            return parsedNodeId;
        }

        /// <summary>
        /// Constructs a node identifier from the component pieces.
        /// </summary>
        public static NodeId Construct(
            int rootType,
            string rootId,
            ushort namespaceIndex,
            params string[] componentNames)
        {
            var pnd = new ParsedNodeId
            {
                RootType = rootType,
                RootId = rootId,
                NamespaceIndex = namespaceIndex
            };

            if (componentNames != null)
            {
                var path = new StringBuilder();

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
            var buffer = new StringBuilder();

            // add the root type.
            buffer.Append(RootType)
                .Append(':');

            // add the root identifier.
            if (RootId != null)
            {
                for (int ii = 0; ii < RootId.Length; ii++)
                {
                    char ch = RootId[ii];

                    // escape any special characters.
                    if (ch is '&' or '?')
                    {
                        buffer.Append('&');
                    }

                    buffer.Append(ch);
                }
            }

            // add the component path.
            if (!string.IsNullOrEmpty(ComponentPath))
            {
                buffer.Append('?')
                    .Append(ComponentPath);
            }

            // add the component name.
            if (!string.IsNullOrEmpty(componentName))
            {
                if (string.IsNullOrEmpty(ComponentPath))
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
            return new NodeId(buffer.ToString(), NamespaceIndex);
        }

        /// <summary>
        /// Constructs the node identifier for a component.
        /// </summary>
        public static NodeId CreateIdForComponent(NodeState component, ushort namespaceIndex)
        {
            if (component == null)
            {
                return default;
            }

            // components must be instances with a parent.

            if (component is not BaseInstanceState instance || instance.Parent == null)
            {
                return component.NodeId;
            }

            // parent must have a string identifier.
            if (!instance.Parent.NodeId.TryGetIdentifier(out string parentId))
            {
                return default;
            }

            var buffer = new StringBuilder();
            buffer.Append(parentId);

            // check if the parent is another component.
            int index = parentId.IndexOf('?', StringComparison.Ordinal);

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
