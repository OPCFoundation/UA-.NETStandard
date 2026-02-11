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
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Extends a node id by adding a complete namespace URI.
    /// </summary>
    public readonly struct ExpandedNodeId :
        IComparable,
        IEquatable<ExpandedNodeId>,
        IEquatable<NodeId>,
        IFormattable
    {
        /// <summary>
        /// Returns an instance of a null ExpandedNodeId.
        /// </summary>
        public static readonly ExpandedNodeId Null;

        /// <summary>
        /// Creates a new instance of the object while allowing you to
        /// specify both the <see cref="NodeId"/> and the Namespace URI
        /// that applies to the NodeID.
        /// </summary>
        /// <param name="nodeId">The <see cref="NodeId"/> to wrap.</param>
        /// <param name="namespaceUri">The actual namespace URI that this
        /// node belongs to</param>
        /// <param name="serverIndex">The server index</param>
        public ExpandedNodeId(
            NodeId nodeId,
            string namespaceUri = null,
            uint serverIndex = 0u)
        {
            m_data.NamespaceUri = namespaceUri;
            m_data.ServerIndex = serverIndex;
            if (!string.IsNullOrEmpty(namespaceUri))
            {
                m_nodeId = nodeId.WithNamespaceIndex(0);
            }
            else
            {
                m_nodeId = nodeId;
            }
        }

        /// <summary>
        /// Creates a new instance of the object while accepting the numeric
        /// id value of the NodeID.
        /// </summary>
        /// <param name="value">The numeric id of a node to wrap</param>
        /// <param name="namespaceUri">The actual namespace URI that this
        /// node belongs to</param>
        /// <param name="serverIndex">The server index</param>
        public ExpandedNodeId(
            uint value,
            string namespaceUri = null,
            uint serverIndex = 0u)
        {
            m_data.NamespaceUri = namespaceUri;
            m_data.ServerIndex = serverIndex;
            m_nodeId = new NodeId(value);
        }

        /// <summary>
        /// Creates a new instance of the class while accepting both the id and namespace
        /// of the node.
        /// </summary>
        /// <param name="value">The numeric id of the node</param>
        /// <param name="namespaceIndex">The namespace index that this node
        /// belongs to</param>
        /// <param name="namespaceUri">The actual namespace URI that this
        /// node belongs to</param>
        /// <param name="serverIndex">The server index</param>
        public ExpandedNodeId(
            uint value,
            ushort namespaceIndex,
            string namespaceUri = null,
            uint serverIndex = 0u)
        {
            m_data.NamespaceUri = namespaceUri;
            m_data.ServerIndex = serverIndex;
            m_nodeId = new NodeId(
                value,
                string.IsNullOrEmpty(namespaceUri) ? namespaceIndex : (ushort)0);
        }

        /// <summary>
        /// Creates a new instance of the class while allowing you to specify
        /// both the node and namespace
        /// </summary>
        /// <param name="value">The string value/id of the node we are
        /// wrapping</param>
        /// <param name="namespaceUri">The actual namespace URI that this
        /// node belongs to</param>
        /// <param name="serverIndex">The server index</param>
        public ExpandedNodeId(
            string value,
            string namespaceUri,
            uint serverIndex = 0u)
        {
            m_data.NamespaceUri = namespaceUri;
            m_data.ServerIndex = serverIndex;
            m_nodeId = new NodeId(value, 0); // Must use 0 or else it parses
        }

        /// <summary>
        /// Initializes a string node identifier with a namespace index.
        /// Creates a new instance of the class while allowing you to
        /// specify both the node and the namespace.
        /// </summary>
        /// <param name="value">The string id/value of the node we are
        /// wrapping</param>
        /// <param name="namespaceIndex">The numeric index of the namespace
        /// within the table, that this node belongs to</param>
        /// <param name="namespaceUri">The actual namespace URI that this
        /// node belongs to</param>
        /// <param name="serverIndex">The server index</param>
        public ExpandedNodeId(
            string value,
            ushort namespaceIndex,
            string namespaceUri = null,
            uint serverIndex = 0u)
        {
            m_data.NamespaceUri = namespaceUri;
            m_data.ServerIndex = serverIndex;
            m_nodeId = new NodeId(
                value,
                string.IsNullOrEmpty(namespaceUri) ? namespaceIndex : (ushort)0);
        }

        /// <summary>
        /// Creates a new instance of the class while specifying the
        /// <see cref="Guid"/> value
        /// of the node.
        /// </summary>
        /// <param name="value">The Guid value of the node </param>
        /// <param name="namespaceUri">The actual namespace URI that this
        /// node belongs to</param>
        /// <param name="serverIndex">The server index</param>
        public ExpandedNodeId(
            Guid value,
            string namespaceUri = null,
            uint serverIndex = 0u)
        {
            m_data.NamespaceUri = namespaceUri;
            m_data.ServerIndex = serverIndex;
            m_nodeId = new NodeId(value);
        }

        /// <summary>
        /// Creates a new instance of the class while specifying the
        /// <see cref="Guid"/> value of the node and the namespaceIndex.
        /// </summary>
        /// <param name="value">The Guid value of the node</param>
        /// <param name="namespaceIndex">The index of the namespace that
        /// this node should belong to</param>
        /// <param name="namespaceUri">The actual namespace URI that this
        /// node belongs to</param>
        /// <param name="serverIndex">The server index</param>
        public ExpandedNodeId(
            Guid value,
            ushort namespaceIndex,
            string namespaceUri = null,
            uint serverIndex = 0u)
        {
            m_data.NamespaceUri = namespaceUri;
            m_data.ServerIndex = serverIndex;
            m_nodeId = new NodeId(
                value,
                string.IsNullOrEmpty(namespaceUri) ? namespaceIndex : (ushort)0);
        }

        /// <summary>
        /// Creates a new instance of the class while allowing you to
        /// specify the byte[] id of the node.
        /// </summary>
        /// <param name="value">The id of the node</param>
        /// <param name="namespaceUri">The actual namespace URI that this
        /// node belongs to</param>
        /// <param name="serverIndex">The server index</param>
        public ExpandedNodeId(
            byte[] value,
            string namespaceUri = null,
            uint serverIndex = 0u)
        {
            m_data.NamespaceUri = namespaceUri;
            m_data.ServerIndex = serverIndex;
            m_nodeId = new NodeId(value);
        }

        /// <summary>
        /// Creates a new instance of the class while allowing you to
        /// specify the node and namespace index.
        /// </summary>
        /// <param name="value">The id of the node</param>
        /// <param name="namespaceIndex">The index of the namespace that
        /// this node should belong to</param>
        /// <param name="namespaceUri">The actual namespace URI that this
        /// node belongs to</param>
        /// <param name="serverIndex">The server index</param>
        public ExpandedNodeId(
            byte[] value,
            ushort namespaceIndex,
            string namespaceUri = null,
            uint serverIndex = 0u)
        {
            m_data.NamespaceUri = namespaceUri;
            m_data.ServerIndex = serverIndex;
            m_nodeId = new NodeId(
                value,
                string.IsNullOrEmpty(namespaceUri) ? namespaceIndex : (ushort)0);
        }

        /// <summary>
        /// Creates a new instance of the class while allowing you to
        /// specify the id of the node.
        /// </summary>
        /// <param name="text">The textual id of the node</param>
        [Obsolete("Use ExpandedNodeId.Parse instead. This will be removed soon.")]
        public ExpandedNodeId(string text)
        {
            this = Parse(text);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExpandedNodeId"/> class.
        /// </summary>
        /// <param name="identifier">The identifier.</param>
        /// <param name="namespaceIndex">The namespace index.</param>
        /// <param name="namespaceUri">The actual namespace URI that this
        /// node belongs to</param>
        /// <param name="serverIndex">The server index</param>
        [Obsolete("Use concrete constructor with typed identifier values instead.")]
        [JsonConstructor]
        public ExpandedNodeId(
            object identifier,
            ushort namespaceIndex,
            string namespaceUri,
            uint serverIndex)
        {
            m_data.NamespaceUri = namespaceUri;
            m_data.ServerIndex = serverIndex;
            m_nodeId = new NodeId(
                identifier,
                string.IsNullOrEmpty(namespaceUri) ? namespaceIndex : (ushort)0);
        }

        /// <summary>
        /// The namespace that qualifies the node identifier.
        /// </summary>
        public string NamespaceUri => m_data.NamespaceUri as string;

        /// <summary>
        /// The index of the server where the node exists.
        /// </summary>
        public uint ServerIndex => m_data.ServerIndex;

        /// <summary>
        /// Returns whether or not the <see cref="NodeId"/> is null
        /// </summary>
        public bool IsNull => !IsAbsolute && m_nodeId.IsNull;

        /// <summary>
        /// Returns true if the expanded node id is an absolute identifier
        /// that contains a namespace URI instead of a server dependent index.
        /// </summary>
        public bool IsAbsolute
            => !string.IsNullOrEmpty(NamespaceUri) || ServerIndex > 0;

        /// <summary>
        /// The index of the namespace URI in the server's namespace array.
        /// </summary>
        public ushort NamespaceIndex =>
            m_nodeId.IsNull ? (ushort)0 : m_nodeId.NamespaceIndex;

        /// <summary>
        /// The type of node identifier used.
        /// </summary>
        public IdType IdType =>
            m_nodeId.IsNull ? IdType.Numeric : m_nodeId.IdType;

        /// <summary>
        /// The node identifier.
        /// </summary>
        /// <remarks>
        /// Returns the node id in whatever form, i.e.
        /// string, Guid, byte[] or uint.
        /// </remarks>
        [Obsolete("Use TryGetIdentifier<T> to get strongly typed identifier values or " +
            "consider using IdentifierAsString if you want to stringify the identifier.")]
        public object Identifier =>
            m_nodeId.IsNull ? null : m_nodeId.Identifier;

        /// <summary>
        /// Try get the numeric node identifier.
        /// </summary>
        public bool TryGetIdentifier(out uint identifier)
        {
            return m_nodeId.TryGetIdentifier(out identifier);
        }

        /// <summary>
        /// Try get the opque node identifier.
        /// </summary>
        public bool TryGetIdentifier(out byte[] identifier)
        {
            return m_nodeId.TryGetIdentifier(out identifier);
        }

        /// <summary>
        /// Try get the string node identifier.
        /// </summary>
        public bool TryGetIdentifier(out string identifier)
        {
            return m_nodeId.TryGetIdentifier(out identifier);
        }

        /// <summary>
        /// Try get the Guid node identifier.
        /// </summary>
        public bool TryGetIdentifier(out Guid identifier)
        {
            return m_nodeId.TryGetIdentifier(out identifier);
        }

        /// <summary>
        /// Get identifier as string
        /// </summary>
        public string IdentifierAsString => m_nodeId.IdentifierAsString;

        /// <summary>
        /// Returns the inner node id.
        /// </summary>
#pragma warning disable RCS1085 // Use auto-implemented property
        public NodeId InnerNodeId => m_nodeId;
#pragma warning restore RCS1085 // Use auto-implemented property

        /// <summary>
        /// Updates the namespace index.
        /// </summary>
        public ExpandedNodeId WithNamespaceIndex(ushort namespaceIndex)
        {
            return new ExpandedNodeId(
                m_nodeId.WithNamespaceIndex(namespaceIndex),
                null,
                ServerIndex);
        }

        /// <summary>
        /// Updates the namespace uri.
        /// </summary>
        public ExpandedNodeId WithNamespaceUri(string uri)
        {
            return new ExpandedNodeId(
                m_nodeId.WithNamespaceIndex(0),
                uri,
                ServerIndex);
        }

        /// <summary>
        /// Updates the server index.
        /// </summary>
        public ExpandedNodeId WithServerIndex(uint serverIndex)
        {
            return new ExpandedNodeId(
                m_nodeId,
                NamespaceUri,
                serverIndex);
        }

        /// <summary>
        /// Updates the server index.
        /// </summary>
        public ExpandedNodeId WithInnerNode(NodeId innerNodeId)
        {
            return new ExpandedNodeId(
                innerNodeId,
                NamespaceUri,
                ServerIndex);
        }

        /// <inheritdoc/>
        public int CompareTo(object obj)
        {
            // check for null.
            if (obj is null)
            {
                return IsNull ? 0 : -1;
            }

            // just compare node ids.
            if (!IsAbsolute && !m_nodeId.IsNull)
            {
                return m_nodeId.CompareTo(obj);
            }

            if (obj is NodeId nodeId)
            {
                if (IsNull && nodeId.IsNull)
                {
                    return 0;
                }
            }
            else if (obj is ExpandedNodeId expandedId)
            {
                if (IsNull && expandedId.IsNull)
                {
                    return 0;
                }

                if (ServerIndex != expandedId.ServerIndex)
                {
                    return ServerIndex.CompareTo(expandedId.ServerIndex);
                }

                if (NamespaceUri != expandedId.NamespaceUri)
                {
                    if (NamespaceUri != null)
                    {
                        return string.CompareOrdinal(NamespaceUri, expandedId.NamespaceUri);
                    }

                    return -1;
                }

                nodeId = expandedId.m_nodeId;
            }
            else
            {
                nodeId = NodeId.Null;
            }

            // check for null.
            if (!m_nodeId.IsNull)
            {
                return m_nodeId.CompareTo(nodeId);
            }

            // compare node ids.
            return nodeId.IsNull ? 0 : -1;
        }

        /// <inheritdoc/>
        public static bool operator >(ExpandedNodeId value1, object value2)
        {
            return value1.CompareTo(value2) > 0;
        }

        /// <inheritdoc/>
        public static bool operator <(ExpandedNodeId value1, object value2)
        {
            return value1.CompareTo(value2) < 0;
        }

        /// <inheritdoc/>
        public static bool operator >=(ExpandedNodeId left, ExpandedNodeId right)
        {
            return right.CompareTo(left) <= 0;
        }

        /// <inheritdoc/>
        public static bool operator <=(ExpandedNodeId left, ExpandedNodeId right)
        {
            return left.CompareTo(right) <= 0;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj switch
            {
                null => IsNull,
                ExpandedNodeId e => Equals(e),
                _ => false
            };
        }

        /// <inheritdoc/>
        public bool Equals(ExpandedNodeId other)
        {
            if (IsNull && other.IsNull)
            {
                return true;
            }
            if (ServerIndex != other.ServerIndex)
            {
                return false;
            }
            if (NamespaceUri != other.NamespaceUri)
            {
                return false;
            }
            if (m_nodeId != other.m_nodeId)
            {
                return false;
            }
            return true;
        }

        /// <inheritdoc/>
        public bool Equals(NodeId other)
        {
            if (IsAbsolute)
            {
                return false;
            }
            return m_nodeId.Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            if (m_nodeId.IsNull)
            {
                return 0;
            }

            // just compare node ids.
            if (!IsAbsolute)
            {
                return m_nodeId.GetHashCode();
            }

            var hash = new HashCode();

            if (ServerIndex != 0)
            {
                hash.Add(ServerIndex);
            }

            if (NamespaceUri != null)
            {
                hash.Add(NamespaceUri);
            }

            hash.Add(m_nodeId);

            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(ExpandedNodeId value1, object value2)
        {
            return value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator !=(ExpandedNodeId value1, object value2)
        {
            return !value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator ==(ExpandedNodeId value1, ExpandedNodeId value2)
        {
            return value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator !=(ExpandedNodeId value1, ExpandedNodeId value2)
        {
            return !value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator ==(ExpandedNodeId value1, NodeId value2)
        {
            return value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator !=(ExpandedNodeId value1, NodeId value2)
        {
            return !value1.Equals(value2);
        }

        /// <summary>
        /// Converts an ExpandedNodeId to a NodeId.
        /// </summary>
        /// <exception cref="InvalidCastException">Thrown if the ExpandedNodeId is an absolute node identifier.</exception>
        public static explicit operator NodeId(ExpandedNodeId value)
        {
            if (value.IsNull)
            {
                return NodeId.Null;
            }
            if (value.IsAbsolute)
            {
                throw new InvalidCastException(
                    "Cannot cast an absolute ExpandedNodeId to a NodeId. Use ExpandedNodeId.ToNodeId instead.");
            }
            return value.m_nodeId;
        }

        /// <summary>
        /// Converts a NodeId to an ExpandedNodeId
        /// </summary>
        public static implicit operator ExpandedNodeId(NodeId nodeId)
        {
            return new ExpandedNodeId(nodeId);
        }

        /// <summary>
        /// Converts an integer to a numeric node identifier.
        /// </summary>
        public static implicit operator ExpandedNodeId(uint value)
        {
            return new ExpandedNodeId(value);
        }

        /// <summary>
        /// Converts a guid to a guid node identifier.
        /// </summary>
        public static implicit operator ExpandedNodeId(Guid value)
        {
            return new ExpandedNodeId(value);
        }

        /// <summary>
        /// Converts a byte array to an opaque node identifier.
        /// </summary>
        public static explicit operator ExpandedNodeId(byte[] value)
        {
            return new ExpandedNodeId(value);
        }

        /// <summary>
        /// Parses a node id string and initializes a node id.
        /// </summary>
        public static explicit operator ExpandedNodeId(string text)
        {
            return Parse(text);
        }

        /// <inheritdoc/>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return Format(formatProvider);
            }

            throw new FormatException(
                CoreUtils.Format("Invalid format string: '{0}'.", format));
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Converts an <see cref="ExpandedNodeId"/> to a <see cref="NodeId"/>
        /// using a namespace table.
        /// </summary>
        /// <param name="nodeId">The ExpandedNodeId to convert to a NodeId</param>
        /// <param name="namespaceTable">The namespace table that contains all
        /// the namespaces needed to resolve the namespace index as encoded within
        /// this object. </param>
        public static NodeId ToNodeId(
            ExpandedNodeId nodeId,
            NamespaceTable namespaceTable)
        {
            // check for null.
            if (nodeId.IsNull)
            {
                return NodeId.Null;
            }

            // return a reference to the internal node id object.
            if (string.IsNullOrEmpty(nodeId.NamespaceUri) &&
                nodeId.ServerIndex == 0)
            {
                return nodeId.m_nodeId;
            }

            // create copy.
            NodeId localId = nodeId.m_nodeId;

            int index = -1;

            if (namespaceTable != null)
            {
                index = namespaceTable.GetIndex(nodeId.NamespaceUri);
            }

            if (index < 0)
            {
                // TODO: Should throw because the value will likely not be tested for null
                return NodeId.Null;
            }

            return localId.WithNamespaceIndex((ushort)index);
        }

        /// <summary>
        /// Formats a NodeId as a string.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="useUris">The NamespaceUri and/or ServerUri is used instead of the indexes.</param>
        /// <returns>The formatted identifier.</returns>
        public string Format(IServiceMessageContext context, bool useUris = false)
        {
            if (m_nodeId.IsNull)
            {
                return null;
            }

            var buffer = new StringBuilder();

            if (ServerIndex > 0)
            {
                if (useUris)
                {
                    string serverUri = context.ServerUris.GetString(ServerIndex);

                    if (!string.IsNullOrEmpty(serverUri))
                    {
                        buffer.Append("svu=")
                            .Append(CoreUtils.EscapeUri(serverUri))
                            .Append(';');
                    }
                    else
                    {
                        buffer.Append("svr=")
                            .Append(ServerIndex)
                            .Append(';');
                    }
                }
                else
                {
                    buffer.Append("svr=")
                        .Append(ServerIndex)
                        .Append(';');
                }
            }

            if (!string.IsNullOrEmpty(NamespaceUri))
            {
                buffer.Append("nsu=")
                    .Append(CoreUtils.EscapeUri(NamespaceUri))
                    .Append(';');
            }

            string id = m_nodeId.Format(context, useUris);
            buffer.Append(id);

            return buffer.ToString();
        }

        /// <summary>
        /// Parses an absolute NodeId formatted as a string and converts it a local NodeId.
        /// </summary>
        /// <param name="text">The text to parse.</param>
        /// <param name="namespaceUris">The current namespace table.</param>
        /// <returns>The local identifier.</returns>
        /// <exception cref="ServiceResultException">Thrown if the namespace URI is not in the namespace table.</exception>
        public static NodeId Parse(string text, NamespaceTable namespaceUris)
        {
            ExpandedNodeId expandedNodeId = Parse(text);

            if (!expandedNodeId.IsAbsolute)
            {
                return expandedNodeId.m_nodeId;
            }

            NodeId nodeId = ToNodeId(expandedNodeId, namespaceUris);
            if (nodeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "NamespaceUri ({0}) is not in the namespace table.",
                    expandedNodeId.NamespaceUri);
            }
            return nodeId;
        }

        /// <summary>
        /// Formats a expanded node id as a string.
        /// <para>
        /// An example of this would be:
        /// <br/></para>
        /// <para>
        /// NodeId = "hello123"<br/>
        /// NamespaceUri = "http://mycompany/"<br/>
        /// <br/> This would translate into:<br/>
        /// nsu=http://mycompany/;s=hello123
        /// <br/>
        /// </para>
        /// <para>
        /// NodeId = 5<br/>
        /// NamespaceIndex = 2<br/>
        /// <br/> This would translate into:<br/>
        /// ns=2;i=5
        /// <br/>
        /// </para>
        /// <para>
        /// Note: Only information already included in the ExpandedNodeId-Instance
        /// will be included in the result
        /// </para>
        /// </summary>
        public string Format(IFormatProvider formatProvider)
        {
            var buffer = new StringBuilder();
            Format(formatProvider ?? CultureInfo.InvariantCulture, buffer);
            return buffer.ToString();
        }

        /// <summary>
        /// Formats the node ids as string and adds it to the buffer.
        /// </summary>
        public void Format(IFormatProvider formatProvider, StringBuilder buffer)
        {
            if (!m_nodeId.IsNull)
            {
                Format(
                    formatProvider,
                    buffer,
                    m_nodeId.IdentifierAsString,
                    m_nodeId.IdType,
                    m_nodeId.NamespaceIndex,
                    NamespaceUri,
                    ServerIndex);
            }
            else
            {
                Format(
                    formatProvider,
                    buffer,
                    null,
                    IdType.Numeric,
                    0,
                    NamespaceUri,
                    ServerIndex);
            }
        }

        /// <summary>
        /// Formats the node ids as string and adds it to the buffer.
        /// </summary>
        public static void Format(
            StringBuilder buffer,
            string identifierAsString,
            IdType identifierType,
            ushort namespaceIndex,
            string namespaceUri,
            uint serverIndex)
        {
            Format(
                CultureInfo.InvariantCulture,
                buffer,
                identifierAsString,
                identifierType,
                namespaceIndex,
                namespaceUri,
                serverIndex);
        }

        /// <summary>
        /// Formats the node ids as string and adds it to the buffer.
        /// </summary>
        public static void Format(
            IFormatProvider formatProvider,
            StringBuilder buffer,
            string identifierAsString,
            IdType identifierType,
            ushort namespaceIndex,
            string namespaceUri,
            uint serverIndex)
        {
            if (serverIndex != 0)
            {
                buffer.AppendFormat(formatProvider, "svr={0};", serverIndex);
            }

            if (!string.IsNullOrEmpty(namespaceUri))
            {
                buffer.Append("nsu=")
                    .Append(CoreUtils.EscapeUri(namespaceUri))
                    .Append(';');
            }

            NodeId.Format(
                formatProvider,
                buffer,
                identifierAsString,
                identifierType,
                namespaceIndex);
        }

        /// <summary>
        /// Parses a expanded node id string, translated any namespace indexes and returns the result.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static ExpandedNodeId Parse(
            string text,
            NamespaceTable currentNamespaces,
            NamespaceTable targetNamespaces)
        {
            // parse the string.
            ExpandedNodeId expandedNodeId = Parse(text);

            // lookup the namespace uri.
            string uri = expandedNodeId.NamespaceUri;

            if (expandedNodeId.m_nodeId.NamespaceIndex != 0)
            {
                uri = currentNamespaces.GetString(
                    expandedNodeId.m_nodeId.NamespaceIndex);
            }

            // translate the namespace uri.
            ushort namespaceIndex = 0;

            if (!string.IsNullOrEmpty(uri))
            {
                int index = targetNamespaces.GetIndex(uri);

                if (index == -1)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdInvalid,
                        "Cannot map namespace URI onto an index in the target namespace table: {0}",
                        uri);
                }

                namespaceIndex = (ushort)index;
            }

            // check for absolute node id.
            if (expandedNodeId.ServerIndex != 0)
            {
                return expandedNodeId.WithNamespaceUri(uri);
            }

            // local node id.
            return expandedNodeId.WithNamespaceIndex(namespaceIndex);
        }

        /// <summary>
        /// Parses a expanded node id string and returns a node id object.
        /// </summary>
        /// <remarks>
        /// Parses a ExpandedNodeId String and returns a NodeId object
        /// </remarks>
        /// <param name="text">The ExpandedNodeId value as a string.</param>
        /// <exception cref="ServiceResultException">Thrown under a variety
        /// of circumstances, each time with a specific message.</exception>
        public static ExpandedNodeId Parse(string text)
        {
            if (!InternalTryParse(text, out ExpandedNodeId value, out NodeIdParseError error))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "Cannot parse expanded node id text: '{0}' Error: {1}",
                    text,
                    error);
            }
            return value;
        }

        /// <summary>
        /// Tries to parse an ExpandedNodeId String and returns an ExpandedNodeId
        /// object if successful.
        /// </summary>
        /// <param name="text">The ExpandedNodeId value as a string.</param>
        /// <param name="value">The parsed ExpandedNodeId if successful,
        /// otherwise ExpandedNodeId.Null.</param>
        /// <returns>True if the parsing was successful, false otherwise.</returns>
        public static bool TryParse(string text, out ExpandedNodeId value)
        {
            return InternalTryParse(text, out value, out _);
        }

        /// <summary>
        /// Tries to parse an ExpandedNodeId String and returns an ExpandedNodeId
        /// object if successful.
        /// </summary>
        /// <param name="text">The ExpandedNodeId value as a string.</param>
        /// <param name="value">The parsed ExpandedNodeId if successful,
        /// otherwise ExpandedNodeId.Null.</param>
        /// <param name="error">The error during parsing if false</param>
        /// <returns>True if the parsing was successful, false otherwise.</returns>
        public static bool TryParse(
            string text,
            out ExpandedNodeId value,
            out NodeIdParseError error)
        {
            return InternalTryParse(text, out value, out error);
        }

        /// <summary>
        /// Tries to parse an ExpandedNodeId formatted as a string and converts it to an ExpandedNodeId.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="text">The text to parse.</param>
        /// <param name="value">The parsed ExpandedNodeId if successful, otherwise ExpandedNodeId.Null.</param>
        /// <returns>True if the parsing was successful, false otherwise.</returns>
        public static bool TryParse(
            IServiceMessageContext context,
            string text,
            out ExpandedNodeId value)
        {
            return InternalTryParseWithContext(context, text, null, out value, out _);
        }

        /// <summary>
        /// Tries to parse an ExpandedNodeId formatted as a string and converts
        /// it to an ExpandedNodeId.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="text">The text to parse.</param>
        /// <param name="options">The options to use when parsing the ExpandedNodeId.</param>
        /// <param name="value">The parsed ExpandedNodeId if successful,
        /// otherwise ExpandedNodeId.Null.</param>
        /// <returns>True if the parsing was successful, false otherwise.</returns>
        public static bool TryParse(
            IServiceMessageContext context,
            string text,
            NodeIdParsingOptions options,
            out ExpandedNodeId value)
        {
            return InternalTryParseWithContext(context, text, options, out value, out _);
        }

        /// <summary>
        /// Tries to parse an ExpandedNodeId formatted as a string and converts
        /// it to an ExpandedNodeId.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="text">The text to parse.</param>
        /// <param name="options">The options to use when parsing the ExpandedNodeId.</param>
        /// <param name="value">The parsed ExpandedNodeId if successful,
        /// otherwise ExpandedNodeId.Null.</param>
        /// <param name="error">Parse error</param>
        /// <returns>True if the parsing was successful, false otherwise.</returns>
        public static bool TryParse(
            IServiceMessageContext context,
            string text,
            NodeIdParsingOptions options,
            out ExpandedNodeId value,
            out NodeIdParseError error)
        {
            return InternalTryParseWithContext(context, text, options, out value, out error);
        }

        /// <summary>
        /// Parses an ExpandedNodeId formatted as a string and converts it a local NodeId.
        /// </summary>
        /// <param name="context">The current context,</param>
        /// <param name="text">The text to parse.</param>
        /// <param name="options">The options to use when parsing the ExpandedNodeId.</param>
        /// <returns>The local identifier.</returns>
        /// <exception cref="ServiceResultException">Thrown if the namespace URI
        /// is not in the namespace table.</exception>
        public static ExpandedNodeId Parse(
            IServiceMessageContext context,
            string text,
            NodeIdParsingOptions options = null)
        {
            if (!InternalTryParseWithContext(
                context,
                text,
                options,
                out ExpandedNodeId value,
                out NodeIdParseError error))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "Cannot parse expanded node id text: '{0}' Error: {1}",
                    text,
                    error);
            }
            return value;
        }

        /// <summary>
        /// Unescapes any reserved characters in the uri.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static void UnescapeUri(
            string text,
            int start,
            int index,
            StringBuilder buffer)
        {
            for (int ii = start; ii < index; ii++)
            {
                char ch = text[ii];

                switch (ch)
                {
                    case '%':
                        if (ii + 2 >= index)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNodeIdInvalid,
                                "Invalid escaped character in namespace uri.");
                        }

                        ushort value = 0;

                        int digit = kHexDigits.IndexOf(
                            char.ToUpperInvariant(text[++ii]),
                            StringComparison.Ordinal);

                        if (digit == -1)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNodeIdInvalid,
                                "Invalid escaped character in namespace uri.");
                        }

                        value += (ushort)digit;
                        value <<= 4;

                        digit = kHexDigits.IndexOf(
                            char.ToUpperInvariant(text[++ii]),
                            StringComparison.Ordinal);

                        if (digit == -1)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadNodeIdInvalid,
                                "Invalid escaped character in namespace uri.");
                        }

                        value += (ushort)digit;

                        char unencodedChar = Convert.ToChar(value);

                        buffer.Append(unencodedChar);
                        break;
                    default:
                        buffer.Append(ch);
                        break;
                }
            }
        }

        /// <summary>
        /// Internal try parse method that returns error message on failure.
        /// </summary>
        /// <param name="text">The ExpandedNodeId value as string.</param>
        /// <param name="value">The parsed ExpandedNodeId if successful,
        /// otherwise ExpandedNodeId.Null.</param>
        /// <param name="error">Error message if parsing fails.</param>
        /// <returns>True if parsing was successful, false otherwise.</returns>
        private static bool InternalTryParse(
            string text,
            out ExpandedNodeId value,
            out NodeIdParseError error)
        {
            error = NodeIdParseError.None;
            value = Null;

            if (string.IsNullOrEmpty(text))
            {
                value = Null;
                return true;
            }

            uint serverIndex = 0;
            string namespaceUri = null;

            try
            {
                // parse the server index if present.
                if (text.StartsWith("svr=", StringComparison.Ordinal))
                {
                    int index = text.IndexOf(';', StringComparison.Ordinal);

                    if (index == -1)
                    {
                        error = NodeIdParseError.InvalidServerIndex;
                        return false;
                    }

                    if (!uint.TryParse(
                        text[4..index],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out serverIndex))
                    {
                        error = NodeIdParseError.InvalidServerIndex;
                        return false;
                    }

                    text = text[(index + 1)..];
                }

                // parse the namespace uri if present.
                if (text.StartsWith("nsu=", StringComparison.Ordinal))
                {
                    int index = text.IndexOf(';', StringComparison.Ordinal);

                    if (index == -1)
                    {
                        error = NodeIdParseError.InvalidNamespaceUri;
                        return false;
                    }

                    var buffer = new StringBuilder();
                    UnescapeUri(text, 4, index, buffer);
                    namespaceUri = buffer.ToString();
                    text = text[(index + 1)..];
                }
            }
            catch
            {
                error = NodeIdParseError.Unexpected;
                return false;
            }

            // parse the node id.
            if (!NodeId.InternalTryParse(
                text,
                serverIndex != 0 || !string.IsNullOrEmpty(namespaceUri),
                out NodeId innerNodeId,
                out error))
            {
                return false;
            }

            // Create the result using the constructor
            value = new ExpandedNodeId(innerNodeId, namespaceUri, serverIndex);

            return true;
        }

        /// <summary>
        /// Internal try parse method with context that returns error message on failure.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="text">The text to parse.</param>
        /// <param name="options">The options to use when parsing the ExpandedNodeId.</param>
        /// <param name="value">The parsed ExpandedNodeId if successful,
        /// otherwise ExpandedNodeId.Null.</param>
        /// <param name="error">Error message if parsing fails.</param>
        /// <returns>True if parsing was successful, false otherwise.</returns>
        private static bool InternalTryParseWithContext(
            IServiceMessageContext context,
            string text,
            NodeIdParsingOptions options,
            out ExpandedNodeId value,
            out NodeIdParseError error)
        {
            error = NodeIdParseError.None;
            value = Null;

            if (string.IsNullOrEmpty(text))
            {
                value = Null;
                return true;
            }

            int serverIndex = 0;

            if (text.StartsWith("svu=", StringComparison.Ordinal))
            {
                int index = text.IndexOf(';', 4);

                if (index < 0)
                {
                    error = NodeIdParseError.InvalidServerUriFormat;
                    return false;
                }

                string serverUri = CoreUtils.UnescapeUri(text.AsSpan()[4..index]);
                serverIndex =
                    options?.UpdateTables == true
                        ? context.ServerUris.GetIndexOrAppend(serverUri)
                        : context.ServerUris.GetIndex(serverUri);

                if (serverIndex < 0)
                {
                    error = NodeIdParseError.NoServerUriMapping;
                    return false;
                }

                text = text[(index + 1)..];
            }

            if (text.StartsWith("svr=", StringComparison.Ordinal))
            {
                int index = text.IndexOf(';', 4);

                if (index < 0)
                {
                    error = NodeIdParseError.InvalidServerUriFormat;
                    return false;
                }

                if (ushort.TryParse(text[4..index], out ushort ns))
                {
                    serverIndex = ns;

                    if (options.ServerMappings != null && options?.NamespaceMappings.Length < ns)
                    {
                        serverIndex = options.NamespaceMappings[ns];
                    }
                }

                text = text[(index + 1)..];
            }

            int namespaceIndex = 0;
            string namespaceUri = null;

            if (text.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int index = text.IndexOf(';', 4);

                if (index < 0)
                {
                    error = NodeIdParseError.InvalidNamespaceFormat;
                    return false;
                }

                namespaceUri = CoreUtils.UnescapeUri(text[4..index]);
                namespaceIndex =
                    options?.UpdateTables == true
                        ? context.NamespaceUris.GetIndexOrAppend(namespaceUri)
                        : context.NamespaceUris.GetIndex(namespaceUri);

                text = text[(index + 1)..];
            }

            if (!NodeId.InternalTryParseWithContext(context, text, options, out NodeId nodeId, out error))
            {
                return false;
            }

            if (namespaceIndex > 0)
            {
#pragma warning disable CS0618 // Type or member is obsolete
                value = new ExpandedNodeId(
                    nodeId.Identifier,
                    (ushort)namespaceIndex,
                    null,
                    (uint)serverIndex);
#pragma warning restore CS0618 // Type or member is obsolete
            }
            else
            {
                value = new ExpandedNodeId(nodeId, namespaceUri, (uint)serverIndex);
            }

            return true;
        }

        /// <summary>
        /// The set of hexadecimal digits used for decoding escaped URIs.
        /// </summary>
        private const string kHexDigits = "0123456789ABCDEF";

        /// <summary>
        /// Inner data structure to hold the additional data
        /// for an expanded node id.
        /// </summary>
        internal struct Inner
        {
            public object NamespaceUri;

            /// <summary> Padding </summary>
            public uint Reserved;

            public uint ServerIndex;
        }
#pragma warning disable IDE0032 // Use auto property
        private readonly NodeId m_nodeId;
        private readonly Inner m_data;
#pragma warning restore IDE0032 // Use auto property
    }

    /// <summary>
    /// List of expanded node ids
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfExpandedNodeId",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "ExpandedNodeId")]
    public class ExpandedNodeIdCollection : List<ExpandedNodeId>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Creates a new [empty] collection.
        /// </remarks>
        public ExpandedNodeIdCollection()
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        public ExpandedNodeIdCollection(IEnumerable<ExpandedNodeId> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        public ExpandedNodeIdCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">An array of <see cref="ExpandedNodeId"/>
        /// values to return as a collection</param>
        public static implicit operator ExpandedNodeIdCollection(
            ExpandedNodeId[] values)
        {
            return values == null ? [] : [.. values];
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        public new object MemberwiseClone()
        {
            var clone = new ExpandedNodeIdCollection(Count);

            foreach (ExpandedNodeId element in this)
            {
                clone.Add(element);
            }

            return clone;
        }
    }

    /// <summary>
    /// Helper to allow data contract serialization of ExpadedNodeId
    /// </summary>
    [DataContract(
        Name = "ExpandedNodeId",
        Namespace = Namespaces.OpcUaXsd)]
    public class SerializableExpandedNodeId :
        ISurrogateFor<ExpandedNodeId>,
        IEquatable<ExpandedNodeId>,
        IEquatable<SerializableExpandedNodeId>
    {
        /// <inheritdoc/>
        public SerializableExpandedNodeId()
        {
            Value = default;
        }

        /// <inheritdoc/>
        public SerializableExpandedNodeId(ExpandedNodeId value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public ExpandedNodeId Value { get; private set; }

        /// <inheritdoc/>
        public object GetValue()
        {
            return Value;
        }

        /// <summary>
        /// The node identifier formatted as a URI.
        /// </summary>
        [DataMember(Name = "Identifier", Order = 1, IsRequired = true)]
        internal string IdentifierText
        {
            get => Value.Format(CultureInfo.InvariantCulture);
            set => Value = ExpandedNodeId.Parse(value);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj switch
            {
                SerializableExpandedNodeId s => Equals(s),
                ExpandedNodeId n => Equals(n),
                _ => Value.Equals(obj)
            };
        }

        /// <inheritdoc/>
        public bool Equals(ExpandedNodeId obj)
        {
            return Value.Equals(obj);
        }

        /// <inheritdoc/>
        public bool Equals(SerializableExpandedNodeId obj)
        {
            return Value.Equals(obj?.Value ?? default);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(
            SerializableExpandedNodeId left,
            SerializableExpandedNodeId right)
        {
            return left is null ? right is null : left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(
            SerializableExpandedNodeId left,
            SerializableExpandedNodeId right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator ==(
            SerializableExpandedNodeId left,
            ExpandedNodeId right)
        {
            return left is null ? right.IsNull : left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(
            SerializableExpandedNodeId left,
            ExpandedNodeId right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static implicit operator SerializableExpandedNodeId(
            ExpandedNodeId expandedNodeId)
        {
            return new SerializableExpandedNodeId(expandedNodeId);
        }

        /// <inheritdoc/>
        public static implicit operator ExpandedNodeId(
            SerializableExpandedNodeId expandedNodeId)
        {
            return expandedNodeId.Value;
        }

        /// <inheritdoc/>
        public static explicit operator string(
            SerializableExpandedNodeId expandedNodeId)
        {
            return expandedNodeId.IdentifierText;
        }

        /// <inheritdoc/>
        public static explicit operator SerializableExpandedNodeId(
            string expandedNodeId)
        {
            return new SerializableExpandedNodeId
            {
                IdentifierText = expandedNodeId
            };
        }
    }
}
