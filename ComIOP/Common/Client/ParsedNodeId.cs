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
using Opc.Ua;

namespace Opc.Ua.Com.Client
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
        /// Constructs a node identifier.
        /// </summary>
        /// <returns>The node identifier.</returns>
        public NodeId Construct()
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

            // construct the node id with the namespace index provided.
            return new NodeId(buffer.ToString(), this.NamespaceIndex);
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

        /// <summary>
        /// Extracts a number from the string.
        /// </summary>
        /// <param name="identifier">The identifier.</param>
        /// <param name="start">The start. Set the first non-digit character.</param>
        /// <returns>The number</returns>
        protected static uint ExtractNumber(string identifier, ref int start)
        {
            uint number = 0;

            for (int ii = start; ii < identifier.Length; ii++)
            {
                if (!Char.IsDigit(identifier[ii]))
                {
                    start = ii;
                    return number;
                }

                number *= 10;
                number += (byte)(identifier[ii] - '0');
            }

            start = identifier.Length;
            return number;
        }

        /// <summary>
        /// Escapes and appends a string.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="text">The text.</param>
        /// <param name="specialChars">The special chars.</param>
        protected static void EscapeAndAppendString(StringBuilder buffer, string text, params char[] specialChars)
        {
            // add the root identifier.
            if (text != null)
            {
                for (int ii = 0; ii < text.Length; ii++)
                {
                    char ch = text[ii];

                    // escape any special characters.
                    for (int jj = 0; jj < specialChars.Length; jj++)
                    {
                        if (specialChars[jj] == ch)
                        {
                            buffer.Append(specialChars[0]);
                        }
                    }

                    buffer.Append(ch);
                }
            }
        }

        /// <summary>
        /// Extracts the and unescapes a string.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="start">The start.</param>
        /// <param name="specialChars">The special chars.</param>
        /// <returns></returns>
        protected static string ExtractAndUnescapeString(string text, ref int start, params char[] specialChars)
        {
            StringBuilder buffer = new StringBuilder();

            int index = start;
            bool escaped = false;

            while (index < text.Length)
            {
                char ch = text[index++];

                if (!escaped)
                {
                    // skip any escape character but keep the one after it.
                    if (ch == specialChars[0])
                    {
                        escaped = true;
                        continue;
                    }

                    // terminate on any special char other than the escape char.
                    for (int jj = 1; jj < specialChars.Length; jj++)
                    {
                        if (specialChars[jj] == ch)
                        {
                            start = index-1;
                            return buffer.ToString();
                        }
                    }
                }

                buffer.Append(ch);
                escaped = false;
            }

            start = text.Length;
            return buffer.ToString();
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
