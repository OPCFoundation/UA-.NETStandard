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
    #region AeParsedNodeId Class
    /// <summary>
    /// Stores the elements of a NodeId after it is parsed.
    /// </summary>
    /// <remarks>
    /// The NodeIds used by the samples are strings with an optional path appended.
    /// The RootType identifies the type of Root Node. The RootId is the unique identifier
    /// for the Root Node. The ComponentPath is constructed from the SymbolicNames
    /// of one or more children of the Root Node. 
    /// </remarks>
    public class AeParsedNodeId : ParsedNodeId
    {
        #region Public Interface
        /// <summary>
        /// Gets or sets the category id.
        /// </summary>
        /// <value>The category id.</value>
        public int CategoryId
        {
            get { return m_categoryId; }
            set { m_categoryId = value; }
        }

        /// <summary>
        /// Gets or sets the source for a condition.
        /// </summary>
        /// <value>The source id of the condition.</value>
        public string SourceId
        {
            get { return m_sourceId; }
            set { m_sourceId = value; }
        }

        /// <summary>
        /// Gets or sets the name of the condition.
        /// </summary>
        /// <value>The name of the condition.</value>
        public string ConditionName
        {
            get { return m_conditionName; }
            set { m_conditionName = value; }
        }

        /// <summary>
        /// Gets or sets the attribute id.
        /// </summary>
        /// <value>The attribute id.</value>
        public string AttributeName
        {
            get { return m_attributeName; }
            set { m_attributeName = value; }
        }

        /// <summary>
        /// Parses the specified node identifier.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns>The parsed node identifier. Null if the identifier cannot be parsed.</returns>
        public static new AeParsedNodeId Parse(NodeId nodeId)
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

            AeParsedNodeId parsedNodeId = new AeParsedNodeId();
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
                case AeModelUtils.AeEventTypeMapping:
                {
                    EventTypeMapping mapping = (EventTypeMapping)(int)ExtractNumber(identifier, ref start);

                    if (start < identifier.Length)
                    {
                        return null;
                    }

                    parsedNodeId.CategoryId = (int)mapping;
                    break;
                }

                case AeModelUtils.AeSimpleEventType:
                case AeModelUtils.AeTrackingEventType:
                {
                    parsedNodeId.CategoryId = Utils.ToInt32(ExtractNumber(identifier, ref start));
                    
                    if (start < identifier.Length)
                    {
                        return null;
                    }
                    
                    break;
                }

                case AeModelUtils.AeConditionEventType:
                {
                    parsedNodeId.CategoryId = Utils.ToInt32(ExtractNumber(identifier, ref start));

                    if (start < identifier.Length)
                    {
                        if (identifier[start] != ':')
                        {
                            return null;
                        }

                        parsedNodeId.ConditionName = identifier.Substring(start+1);
                    }

                    break;
                }

                case AeModelUtils.AeCondition:
                {
                    parsedNodeId.SourceId = ExtractAndUnescapeString(identifier, ref start, '0', ':');
                    
                    if (start < identifier.Length && identifier[start] != ':')
                    {
                        return null;
                    }

                    start++;

                    parsedNodeId.CategoryId = Utils.ToInt32(ExtractNumber(identifier, ref start));

                    if (start < identifier.Length)
                    {
                        if (identifier[start] != ':')
                        {
                            return null;
                        }

                        parsedNodeId.ConditionName = identifier.Substring(start+1);
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
                    case AeModelUtils.AeSimpleEventType:
                    case AeModelUtils.AeTrackingEventType:
                    case AeModelUtils.AeConditionEventType:
                    {
                        parsedNodeId.AttributeName = identifier.Substring(start+1);
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
        /// Constructs the NodeId for the specified event type mapping node.
        /// </summary>
        internal static NodeId Construct(EventTypeMapping mapping, ushort namespaceIndex)
        {
            return Construct(AeModelUtils.AeEventTypeMapping, ((int)mapping).ToString(), null, namespaceIndex);
        }

        /// <summary>
        /// Constructs the NodeId for the specified event type node.
        /// </summary>
        internal static NodeId Construct(EventType eventType, string componentPath, ushort namespaceIndex)
        {
            StringBuilder buffer = new StringBuilder();

            buffer.Append(Utils.ToUInt32(eventType.CategoryId));

            if (!String.IsNullOrEmpty(eventType.ConditionName))
            {
                buffer.Append(':');
                buffer.Append(eventType.ConditionName);
            }

            return Construct(eventType.EventTypeId, buffer.ToString(), componentPath, namespaceIndex);
        }

        /// <summary>
        /// Constructs the NodeId for the specified event type.
        /// </summary>
        /// <param name="eventType">Type of the event.</param>
        /// <param name="categoryId">The category id.</param>
        /// <param name="conditionName">Name of the condition.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns>The NodeId</returns>
        public static NodeId Construct(int eventType, int categoryId, string conditionName, ushort namespaceIndex)
        {
            StringBuilder buffer = new StringBuilder();

            buffer.Append(Utils.ToUInt32(categoryId));

            if (!String.IsNullOrEmpty(conditionName))
            {
                buffer.Append(':');
                buffer.Append(conditionName);
            }

            return Construct(eventType, buffer.ToString(), null, namespaceIndex);
        }

        /// <summary>
        /// Constructs the NodeId for the attribute of the specified event type.
        /// </summary>
        /// <param name="eventType">Type of the event.</param>
        /// <param name="categoryId">The category id.</param>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns>The NodeId</returns>
        public static NodeId Construct(int eventType, int categoryId, int attributeId, ushort namespaceIndex)
        {
            return Construct(eventType, categoryId.ToString(), attributeId.ToString(), namespaceIndex);
        }

        /// <summary>
        /// Constructs the NodeId for the attribute of the specified event type.
        /// </summary>
        /// <param name="eventType">Type of the event.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns>The NodeId</returns>
        public static NodeId Construct(int eventType, ushort namespaceIndex)
        {
            return Construct(eventType, String.Empty, null, namespaceIndex);
        }

        /// <summary>
        /// Constructs the id for condition.
        /// </summary>
        /// <param name="sourceId">The source id.</param>
        /// <param name="categoryId">The category id.</param>
        /// <param name="conditionName">Name of the condition.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns></returns>
        public static NodeId ConstructIdForCondition(string sourceId, int categoryId, string conditionName, ushort namespaceIndex)
        {
            StringBuilder buffer = new StringBuilder();

            EscapeAndAppendString(buffer, sourceId, '0', ':');
            buffer.Append(':');
            buffer.Append(Utils.ToUInt32(categoryId));
            buffer.Append(':');
            buffer.Append(conditionName);

            return Construct(AeModelUtils.AeCondition, buffer.ToString(), null, namespaceIndex);
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
        private string m_sourceId;
        private int m_categoryId;
        private string m_conditionName;
        private string m_attributeName;
        #endregion
    }
    #endregion
}
