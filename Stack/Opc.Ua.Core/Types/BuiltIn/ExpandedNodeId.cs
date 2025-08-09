/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// Extends a node id by adding a complete namespace URI.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public sealed class ExpandedNodeId : ICloneable, IComparable, IEquatable<ExpandedNodeId>, IFormattable
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the object, accepting the default values.
        /// </remarks>
        internal ExpandedNodeId()
        {
            Initialize();
        }

        /// <summary>
        /// Creates a deep copy of the value.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the object, while copying the properties of the specified object.
        /// </remarks>
        /// <param name="value">The ExpandedNodeId to copy</param>
        /// <exception cref="ArgumentNullException">Thrown when the parameter is null</exception>
        public ExpandedNodeId(ExpandedNodeId value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            m_namespaceUri = value.m_namespaceUri;

            if (value.InnerNodeId != null)
            {
                InnerNodeId = new NodeId(value.InnerNodeId);
            }
        }

        /// <summary>
        /// Initializes an expanded node identifier with a node id.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the object, while wrapping the specified <see cref="NodeId"/>.
        /// </remarks>
        /// <param name="nodeId">The <see cref="NodeId"/> to wrap</param>
        public ExpandedNodeId(NodeId nodeId)
        {
            Initialize();

            if (nodeId != null)
            {
                InnerNodeId = new NodeId(nodeId);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExpandedNodeId"/> class.
        /// </summary>
        /// <param name="identifier">The identifier.</param>
        /// <param name="namespaceIndex">The namespace index.</param>
        /// <param name="namespaceUri">The namespace URI.</param>
        /// <param name="serverIndex">The server index.</param>
        public ExpandedNodeId(object identifier, ushort namespaceIndex, string namespaceUri, uint serverIndex)
        {
            InnerNodeId = new NodeId(identifier, namespaceIndex);
            m_namespaceUri = namespaceUri;
            m_serverIndex = serverIndex;
        }

        /// <summary>
        /// Initializes an expanded node identifier with a node id and a namespace URI.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the object while allowing you to specify both the
        /// <see cref="NodeId"/> and the Namespace URI that applies to the NodeID.
        /// </remarks>
        /// <param name="nodeId">The <see cref="NodeId"/> to wrap.</param>
        /// <param name="namespaceUri">The namespace that this node belongs to</param>
        public ExpandedNodeId(NodeId nodeId, string namespaceUri)
        {
            Initialize();

            if (nodeId != null)
            {
                InnerNodeId = new NodeId(nodeId);
            }

            if (!string.IsNullOrEmpty(namespaceUri))
            {
                SetNamespaceUri(namespaceUri);
            }
        }

        /// <summary>
        /// Initializes an expanded node identifier with a node id and a namespace URI.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the object while allowing you to specify both the
        /// <see cref="NodeId"/> and the Namespace URI that applies to the NodeID.
        /// </remarks>
        /// <param name="nodeId">The <see cref="NodeId"/> to wrap.</param>
        /// <param name="namespaceUri">The namespace that this node belongs to</param>
        /// <param name="serverIndex">The server that the node belongs to</param>
        public ExpandedNodeId(NodeId nodeId, string namespaceUri, uint serverIndex)
        {
            Initialize();

            if (nodeId != null)
            {
                InnerNodeId = new NodeId(nodeId);
            }

            if (!string.IsNullOrEmpty(namespaceUri))
            {
                SetNamespaceUri(namespaceUri);
            }

            m_serverIndex = serverIndex;
        }

        /// <summary>
        /// Initializes a numeric node identifier.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the object while accepting the numeric id/value of
        /// the NodeID we are wrapping.
        /// </remarks>
        /// <param name="value">The numeric id of a node to wrap</param>
        public ExpandedNodeId(uint value)
        {
            Initialize();
            InnerNodeId = new NodeId(value);
        }

        /// <summary>
        /// Initializes a numeric node identifier with a namespace index.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the class while accepting both the id and namespace
        /// of the node we are wrapping.
        /// </remarks>
        /// <param name="value">The numeric id of the node we are wrapping</param>
        /// <param name="namespaceIndex">The namespace index that this node belongs to</param>
        public ExpandedNodeId(uint value, ushort namespaceIndex)
        {
            Initialize();
            InnerNodeId = new NodeId(value, namespaceIndex);
        }

        /// <summary>
        /// Initializes a numeric node identifier with a namespace URI.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the class while accepting both the numeric id of the
        /// node, along with the actual namespace that this node belongs to.
        /// </remarks>
        /// <param name="value">The numeric id of the node we are wrapping</param>
        /// <param name="namespaceUri">The namespace that this node belongs to</param>
        public ExpandedNodeId(uint value, string namespaceUri)
        {
            Initialize();
            InnerNodeId = new NodeId(value);
            SetNamespaceUri(namespaceUri);
        }

        /// <summary>
        /// Initializes a string node identifier with a namespace index.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the class while allowing you to specify both the
        /// node and the namespace.
        /// </remarks>
        /// <param name="value">The string id/value of the node we are wrapping</param>
        /// <param name="namespaceIndex">The numeric index of the namespace within the table, that this node belongs to</param>
        public ExpandedNodeId(string value, ushort namespaceIndex)
        {
            Initialize();
            InnerNodeId = new NodeId(value, namespaceIndex);
        }

        /// <summary>
        /// Initializes a string node identifier with a namespace URI.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the class while allowing you to specify both the node and namespace
        /// </remarks>
        /// <param name="value">The string value/id of the node we are wrapping</param>
        /// <param name="namespaceUri">The actual namespace URI that this node belongs to</param>
        public ExpandedNodeId(string value, string namespaceUri)
        {
            Initialize();
            InnerNodeId = new NodeId(value, 0);
            SetNamespaceUri(namespaceUri);
        }

        /// <summary>
        /// Initializes a guid node identifier.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the class while specifying the <see cref="Guid"/> value
        /// of the node we are wrapping.
        /// </remarks>
        /// <param name="value">The Guid value of the node we are wrapping</param>
        public ExpandedNodeId(Guid value)
        {
            Initialize();
            InnerNodeId = new NodeId(value);
        }

        /// <summary>
        /// Initializes a guid node identifier.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the class while specifying the <see cref="Guid"/> value
        /// of the node and the namespaceIndex we are wrapping.
        /// </remarks>
        /// <param name="value">The Guid value of the node we are wrapping</param>
        /// <param name="namespaceIndex">The index of the namespace that this node should belong to</param>
        public ExpandedNodeId(Guid value, ushort namespaceIndex)
        {
            Initialize();
            InnerNodeId = new NodeId(value, namespaceIndex);
        }

        /// <summary>
        /// Initializes a guid node identifier.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the class while specifying the <see cref="Guid"/> value
        /// of the node and the namespaceUri we are wrapping.
        /// </remarks>
        /// <param name="value">The Guid value of the node we are wrapping</param>
        /// <param name="namespaceUri">The namespace that this node belongs to</param>
        public ExpandedNodeId(Guid value, string namespaceUri)
        {
            Initialize();
            InnerNodeId = new NodeId(value);
            SetNamespaceUri(namespaceUri);
        }

        /// <summary>
        /// Initializes a opaque node identifier.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the class while allowing you to specify the byte[] id
        /// of the node.
        /// </remarks>
        /// <param name="value">The id of the node we are wrapping</param>
        public ExpandedNodeId(byte[] value)
        {
            Initialize();
            InnerNodeId = new NodeId(value);
        }

        /// <summary>
        /// Initializes an opaque node identifier with a namespace index.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the class while allowing you to specify the node
        /// and namespace index.
        /// </remarks>
        /// <param name="value">The id of the node we are wrapping</param>
        /// <param name="namespaceIndex">The index of the namespace that this node should belong to</param>
        public ExpandedNodeId(byte[] value, ushort namespaceIndex)
        {
            Initialize();
            InnerNodeId = new NodeId(value, namespaceIndex);
        }

        /// <summary>
        /// Initializes an opaque node identifier with a namespace index.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the class while allowing you to specify the node and namespace.
        /// </remarks>
        /// <param name="value">The node we are wrapping</param>
        /// <param name="namespaceUri">The namespace that this node belongs to</param>
        public ExpandedNodeId(byte[] value, string namespaceUri)
        {
            Initialize();
            InnerNodeId = new NodeId(value);
            SetNamespaceUri(namespaceUri);
        }

        /// <summary>
        /// Initializes a node id by parsing a node id string.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the class while allowing you to specify the id of the node.
        /// </remarks>
        /// <param name="text">The textual id of the node being wrapped</param>
        public ExpandedNodeId(string text)
        {
            Initialize();
            InternalParse(text);
        }

        /// <summary>
        /// Sets the private members to default values.
        /// </summary>
        private void Initialize()
        {
            InnerNodeId = null;
            m_namespaceUri = null;
            m_serverIndex = 0;
        }

        /// <summary>
        /// The index of the namespace URI in the server's namespace array.
        /// </summary>
        public ushort NamespaceIndex
        {
            get
            {
                if (InnerNodeId != null)
                {
                    return InnerNodeId.NamespaceIndex;
                }

                return 0;
            }
        }

        /// <summary>
        /// The type of node identifier used.
        /// </summary>
        public IdType IdType
        {
            get
            {
                if (InnerNodeId != null)
                {
                    return InnerNodeId.IdType;
                }

                return IdType.Numeric;
            }
        }

        /// <summary>
        /// The node identifier.
        /// </summary>
        /// <remarks>
        /// Returns the node id in whatever form, i.e.
        /// string, Guid, byte[] or uint.
        /// </remarks>
        public object Identifier
        {
            get
            {
                if (InnerNodeId != null)
                {
                    return InnerNodeId.Identifier;
                }

                return null;
            }
        }

        /// <summary>
        /// The namespace that qualifies the node identifier.
        /// </summary>
        /// <remarks>
        /// Returns the namespace that the node belongs to
        /// </remarks>
        public string NamespaceUri => m_namespaceUri;

        /// <summary>
        /// The index of the server where the node exists.
        /// </summary>
        /// <remarks>
        /// Returns the index of the server where the node resides
        /// </remarks>
        public uint ServerIndex => m_serverIndex;

        /// <summary>
        /// Whether the object represents a Null NodeId.
        /// </summary>
        /// <remarks>
        /// Returns whether or not the <see cref="NodeId"/> is null
        /// </remarks>
        public bool IsNull
        {
            get
            {
                if (!string.IsNullOrEmpty(m_namespaceUri))
                {
                    return false;
                }

                if (m_serverIndex > 0)
                {
                    return false;
                }

                return NodeId.IsNull(InnerNodeId);
            }
        }

        /// <summary>
        /// Returns true if the expanded node id is an absolute identifier that contains a namespace URI instead of a server dependent index.
        /// </summary>
        public bool IsAbsolute => !string.IsNullOrEmpty(m_namespaceUri) || m_serverIndex > 0;

        /// <summary>
        /// Returns the inner node id.
        /// </summary>
        internal NodeId InnerNodeId { get; set; }

        /// <summary>
        /// The node identifier formatted as a URI.
        /// </summary>
        [DataMember(Name = "Identifier", Order = 1, IsRequired = true)]
        internal string IdentifierText
        {
            get => Format(CultureInfo.InvariantCulture);
            set
            {
                ExpandedNodeId nodeId = Parse(value);

                InnerNodeId = nodeId.InnerNodeId;
                m_namespaceUri = nodeId.m_namespaceUri;
                m_serverIndex = nodeId.m_serverIndex;
            }
        }

        /// <summary>
        /// Formats a expanded node id as a string.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Formats a ExpandedNodeId as a string.
        /// <br/></para>
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
        /// Note: Only information already included in the ExpandedNodeId-Instance will be included in the result
        /// </para>
        /// </remarks>
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
            if (InnerNodeId != null)
            {
                Format(formatProvider, buffer, InnerNodeId.Identifier, InnerNodeId.IdType, InnerNodeId.NamespaceIndex, m_namespaceUri, m_serverIndex);
            }
            else
            {
                Format(formatProvider, buffer, null, IdType.Numeric, 0, m_namespaceUri, m_serverIndex);
            }
        }

        /// <summary>
        /// Formats the node ids as string and adds it to the buffer.
        /// </summary>
        public static void Format(
            StringBuilder buffer,
            object identifier,
            IdType identifierType,
            ushort namespaceIndex,
            string namespaceUri,
            uint serverIndex)
        {
            Format(CultureInfo.InvariantCulture, buffer, identifier, identifierType, namespaceIndex, namespaceUri, serverIndex);
        }

        /// <summary>
        /// Formats the node ids as string and adds it to the buffer.
        /// </summary>
        public static void Format(
            IFormatProvider formatProvider,
            StringBuilder buffer,
            object identifier,
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
                buffer.Append("nsu=");
                buffer.Append(Utils.EscapeUri(namespaceUri));
                buffer.Append(';');
            }

            NodeId.Format(formatProvider, buffer, identifier, identifierType, namespaceIndex);
        }

        /// <summary>
        /// Parses a expanded node id string, translated any namespace indexes and returns the result.
        /// </summary>
        public static ExpandedNodeId Parse(string text, NamespaceTable currentNamespaces, NamespaceTable targetNamespaces)
        {
            // parse the string.
            ExpandedNodeId nodeId = Parse(text);

            // lookup the namespace uri.
            string uri = nodeId.m_namespaceUri;

            if (nodeId.InnerNodeId.NamespaceIndex != 0)
            {
                uri = currentNamespaces.GetString(nodeId.InnerNodeId.NamespaceIndex);
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
            if (nodeId.ServerIndex != 0)
            {
                nodeId.InnerNodeId = new NodeId(nodeId.InnerNodeId.Identifier, 0);
                nodeId.m_namespaceUri = uri;
                return nodeId;
            }

            // local node id.
            nodeId.InnerNodeId = new NodeId(nodeId.InnerNodeId.Identifier, namespaceIndex);
            nodeId.m_namespaceUri = null;

            return nodeId;
        }

        /// <summary>
        /// Parses a expanded node id string and returns a node id object.
        /// </summary>
        /// <remarks>
        /// Parses a ExpandedNodeId String and returns a NodeId object
        /// </remarks>
        /// <param name="text">The ExpandedNodeId value as a string.</param>
        /// <exception cref="ServiceResultException">Thrown under a variety of circumstances, each time with a specific message.</exception>
        public static ExpandedNodeId Parse(string text)
        {
            try
            {
                // check for null.
                if (string.IsNullOrEmpty(text))
                {
                    return Null;
                }

                return new ExpandedNodeId(text);
            }
            catch (Exception e)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid,
                    Utils.Format("Cannot parse expanded node id text: '{0}'", text),
                    e);
            }
        }

        /// <summary>
        /// Unescapes any reserved characters in the uri.
        /// </summary>
        internal static void UnescapeUri(string text, int start, int index, StringBuilder buffer)
        {
            for (int ii = start; ii < index; ii++)
            {
                char ch = text[ii];

                switch (ch)
                {
                    case '%':
                        if (ii + 2 >= index)
                        {
                            throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "Invalid escaped character in namespace uri.");
                        }

                        ushort value = 0;

                        int digit = kHexDigits.IndexOf(char.ToUpperInvariant(text[++ii]), StringComparison.Ordinal);

                        if (digit == -1)
                        {
                            throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "Invalid escaped character in namespace uri.");
                        }

                        value += (ushort)digit;
                        value <<= 4;

                        digit = kHexDigits.IndexOf(char.ToUpperInvariant(text[++ii]), StringComparison.Ordinal);

                        if (digit == -1)
                        {
                            throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "Invalid escaped character in namespace uri.");
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
        /// The set of hexadecimal digits used for decoding escaped URIs.
        /// </summary>
        private const string kHexDigits = "0123456789ABCDEF";

        /// <summary>
        /// Compares the current instance to the object.
        /// </summary>
        public int CompareTo(object obj)
        {
            // check for null.
            if (ReferenceEquals(obj, null))
            {
                return -1;
            }

            // check for reference comparisons.
            if (ReferenceEquals(this, obj))
            {
                return 0;
            }

            // just compare node ids.
            if (!IsAbsolute && InnerNodeId != null)
            {
                return InnerNodeId.CompareTo(obj);
            }

            var nodeId = obj as NodeId;

            // check for expanded node ids.
            var expandedId = obj as ExpandedNodeId;

            if (expandedId != null)
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

                nodeId = expandedId.InnerNodeId;
            }

            // check for null.
            if (InnerNodeId != null)
            {
                return InnerNodeId.CompareTo(nodeId);
            }

            // compare node ids.
            return (nodeId == null) ? 0 : -1;
        }

        /// <summary>
        /// Returns true if a is greater than b.
        /// </summary>
        public static bool operator >(ExpandedNodeId value1, object value2)
        {
            if (!ReferenceEquals(value1, null))
            {
                return value1.CompareTo(value2) > 0;
            }

            return false;
        }

        /// <summary>
        /// Returns true if a is less than b.
        /// </summary>
        public static bool operator <(ExpandedNodeId value1, object value2)
        {
            if (!ReferenceEquals(value1, null))
            {
                return value1.CompareTo(value2) < 0;
            }

            return true;
        }

        /// <summary>
        /// Determines if the specified object is equal to the ExpandedNodeId.
        /// </summary>
        public override bool Equals(object obj)
        {
            return CompareTo(obj) == 0;
        }

        /// <summary>
        /// Returns a unique hashcode for the ExpandedNodeId
        /// </summary>
        public override int GetHashCode()
        {
            if (InnerNodeId == null || InnerNodeId.IsNullNodeId)
            {
                return 0;
            }

            // just compare node ids.
            if (!IsAbsolute)
            {
                return InnerNodeId.GetHashCode();
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

            hash.Add(InnerNodeId);

            return hash.ToHashCode();
        }

        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        public static bool operator ==(ExpandedNodeId value1, object value2)
        {
            if (ReferenceEquals(value1, null))
            {
                return ReferenceEquals(value2, null);
            }

            return value1.CompareTo(value2) == 0;
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        public static bool operator !=(ExpandedNodeId value1, object value2)
        {
            if (ReferenceEquals(value1, null))
            {
                return !ReferenceEquals(value2, null);
            }

            return value1.CompareTo(value2) != 0;
        }

        /// <summary>
        /// Implements <see cref="IEquatable{T}"/>.Equals(T)"/>
        /// </summary>
        /// <param name="other">The other ExpandedNodeId.</param>
        public bool Equals(ExpandedNodeId other)
        {
            return CompareTo(other) == 0;
        }

        /// <summary>
        /// Returns the string representation of an ExpandedNodeId.
        /// </summary>
        /// <returns>The <see cref="ExpandedNodeId"/> as a formatted string</returns>
        /// <param name="format">(Unused) The format string.</param>
        /// <param name="formatProvider">(Unused) The format-provider.</param>
        /// <exception cref="FormatException">Thrown when the 'format' parameter is NOT null. So leave that parameter null.</exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return Format(formatProvider);
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Makes a deep copy of the object.
        /// </summary>
        /// <remarks>
        /// Returns a reference to *this* object. This means that no copy is being made of this object.
        /// </remarks>
        public new object MemberwiseClone()
        {
            // this object cannot be altered after it is created so no new allocation is necessary.
            return this;
        }

        /// <summary>
        /// Returns the string representation of am ExpandedNodeId.
        /// </summary>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Converts an expanded node id to a node id using a namespace table.
        /// </summary>
        /// <remarks>
        /// Converts an <see cref="ExpandedNodeId"/> to a <see cref="NodeId"/> using a namespace table.
        /// </remarks>
        /// <param name="nodeId">The ExpandedNodeId to convert to a NodeId</param>
        /// <param name="namespaceTable">The namespace table that contains all the namespaces needed to resolve the namespace index as encoded within this object.</param>
        public static NodeId ToNodeId(ExpandedNodeId nodeId, NamespaceTable namespaceTable)
        {
            // check for null.
            if (nodeId == null)
            {
                return null;
            }

            // return a reference to the internal node id object.
            if (string.IsNullOrEmpty(nodeId.m_namespaceUri) && nodeId.m_serverIndex == 0)
            {
                return nodeId.InnerNodeId;
            }

            // create copy.
            var localId = new NodeId(nodeId.InnerNodeId);

            int index = -1;

            if (namespaceTable != null)
            {
                index = namespaceTable.GetIndex(nodeId.NamespaceUri);
            }

            if (index < 0)
            {
                return null;
            }

            localId.SetNamespaceIndex((ushort)index);

            return localId;
        }

        /// <summary>
        /// Updates the namespace index.
        /// </summary>
        internal void SetNamespaceIndex(ushort namespaceIndex)
        {
            InnerNodeId.SetNamespaceIndex(namespaceIndex);
            m_namespaceUri = null;
        }

        /// <summary>
        /// Updates the namespace uri.
        /// </summary>
        internal void SetNamespaceUri(string uri)
        {
            InnerNodeId.SetNamespaceIndex(0);
            m_namespaceUri = uri;
        }

        /// <summary>
        /// Updates the server index.
        /// </summary>
        internal void SetServerIndex(uint serverIndex)
        {
            m_serverIndex = serverIndex;
        }

        /// <summary>
        /// Parses an ExpandedNodeId formatted as a string and converts it a local NodeId.
        /// </summary>
        /// <param name="context">The current context,</param>
        /// <param name="text">The text to parse.</param>
        /// <param name="options">The options to use when parsing the ExpandedNodeId.</param>
        /// <returns>The local identifier.</returns>
        /// <exception cref="ServiceResultException">Thrown if the namespace URI is not in the namespace table.</exception>
        public static ExpandedNodeId Parse(IServiceMessageContext context, string text, NodeIdParsingOptions options = null)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Null;
            }

            string originalText = text;
            int serverIndex = 0;

            if (text.StartsWith("svu=", StringComparison.Ordinal))
            {
                int index = text.IndexOf(';', 4);

                if (index < 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, $"Invalid ExpandedNodeId ({originalText}).");
                }

                string serverUri = Utils.UnescapeUri(text.AsSpan()[4..index]);
                serverIndex = (options?.UpdateTables == true) ? context.ServerUris.GetIndexOrAppend(serverUri) : context.ServerUris.GetIndex(serverUri);

                if (serverIndex < 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, $"No mapping to ServerIndex for ServerUri ({serverUri}).");
                }

                text = text[(index + 1)..];
            }

            if (text.StartsWith("svr=", StringComparison.Ordinal))
            {
                int index = text.IndexOf(';', 4);

                if (index < 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, $"Invalid ExpandedNodeId ({originalText}).");
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
                    throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, $"Invalid ExpandedNodeId ({originalText}).");
                }

                namespaceUri = Utils.UnescapeUri(text[4..index]);
                namespaceIndex = (options?.UpdateTables == true) ? context.NamespaceUris.GetIndexOrAppend(namespaceUri) : context.NamespaceUris.GetIndex(namespaceUri);

                text = text[(index + 1)..];
            }

            var nodeId = NodeId.Parse(context, text, options);

            if (namespaceIndex > 0)
            {
                return new ExpandedNodeId(
                    nodeId.Identifier,
                    (ushort)namespaceIndex,
                    null,
                    (uint)serverIndex);
            }

            return new ExpandedNodeId(nodeId, namespaceUri, (uint)serverIndex);
        }

        /// <summary>
        /// Formats a NodeId as a string.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="useUris">The NamespaceUri and/or ServerUri is used instead of the indexes.</param>
        /// <returns>The formatted identifier.</returns>
        public string Format(IServiceMessageContext context, bool useUris = false)
        {
            if (NodeId.IsNull(InnerNodeId))
            {
                return null;
            }

            var buffer = new StringBuilder();

            if (m_serverIndex > 0)
            {
                if (useUris)
                {
                    string serverUri = context.ServerUris.GetString(m_serverIndex);

                    if (!string.IsNullOrEmpty(serverUri))
                    {
                        buffer.Append("svu=");
                        buffer.Append(Utils.EscapeUri(serverUri));
                        buffer.Append(';');
                    }
                    else
                    {
                        buffer.Append("svr=");
                        buffer.Append(m_serverIndex);
                        buffer.Append(';');
                    }
                }
                else
                {
                    buffer.Append("svr=");
                    buffer.Append(m_serverIndex);
                    buffer.Append(';');
                }
            }

            if (!string.IsNullOrEmpty(m_namespaceUri))
            {
                buffer.Append("nsu=");
                buffer.Append(Utils.EscapeUri(m_namespaceUri));
                buffer.Append(';');
            }

            string id = InnerNodeId.Format(context, useUris);
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
            ExpandedNodeId nodeId = Parse(text);

            if (!nodeId.IsAbsolute)
            {
                return nodeId.InnerNodeId;
            }

            NodeId localId = ToNodeId(nodeId, namespaceUris);

            if (localId == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadNodeIdInvalid, "NamespaceUri ({0}) is not in the namespace table.", nodeId.NamespaceUri);
            }

            return localId;
        }

        /// <summary>
        /// Converts an ExpandedNodeId to a NodeId.
        /// </summary>
        /// <exception cref="InvalidCastException">Thrown if the ExpandedNodeId is an absolute node identifier.</exception>
        public static explicit operator NodeId(ExpandedNodeId value)
        {
            if (value == null)
            {
                return null;
            }

            if (value.IsAbsolute)
            {
                throw new InvalidCastException("Cannot cast an absolute ExpandedNodeId to a NodeId. Use ExpandedNodeId.ToNodeId instead.");
            }

            return value.InnerNodeId;
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
        public static implicit operator ExpandedNodeId(byte[] value)
        {
            return new ExpandedNodeId(value);
        }

        /// <summary>
        /// Parses a node id string and initializes a node id.
        /// </summary>
        public static implicit operator ExpandedNodeId(string text)
        {
            return new ExpandedNodeId(text);
        }

        /// <summary>
        /// Converts a NodeId to an ExpandedNodeId
        /// </summary>
        public static implicit operator ExpandedNodeId(NodeId nodeId)
        {
            return new ExpandedNodeId(nodeId);
        }

        /// <summary>
        /// Returns an instance of a null ExpandedNodeId.
        /// </summary>
        public static ExpandedNodeId Null { get; } = new ExpandedNodeId();

        /// <summary>
        /// Parses a expanded node id string and sets the properties.
        /// </summary>
        /// <param name="text">The ExpandedNodeId value as a string.</param>
        private void InternalParse(string text)
        {
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
                        throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "Invalid server index.");
                    }

                    serverIndex = Convert.ToUInt32(text[4..index], CultureInfo.InvariantCulture);

                    text = text[(index + 1)..];
                }

                // parse the namespace uri if present.
                if (text.StartsWith("nsu=", StringComparison.Ordinal))
                {
                    int index = text.IndexOf(';', StringComparison.Ordinal);

                    if (index == -1)
                    {
                        throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "Invalid namespace uri.");
                    }

                    var buffer = new StringBuilder();

                    UnescapeUri(text, 4, index, buffer);
                    namespaceUri = buffer.ToString();
                    text = text[(index + 1)..];
                }
            }
            catch (Exception e)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid,
                    Utils.Format("Cannot parse expanded node id text: '{0}'", text),
                    e);
            }

            // parse the node id.

            // set the properties.
            InnerNodeId = NodeId.InternalParse(text, serverIndex != 0 || !string.IsNullOrEmpty(namespaceUri));
            m_namespaceUri = namespaceUri;
            m_serverIndex = serverIndex;
        }

        private string m_namespaceUri;
        private uint m_serverIndex;
    }

    /// <summary>
    /// A collection of ExpandedNodeId objects.
    /// </summary>
    [CollectionDataContract(Name = "ListOfExpandedNodeId", Namespace = Namespaces.OpcUaXsd, ItemName = "ExpandedNodeId")]
    public class ExpandedNodeIdCollection : List<ExpandedNodeId>, ICloneable
    {
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Creates a new [empty] collection.
        /// </remarks>
        public ExpandedNodeIdCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Initializes the collection from another collection.
        /// </remarks>
        public ExpandedNodeIdCollection(IEnumerable<ExpandedNodeId> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Initializes the collection with the specified capacity.
        /// </remarks>
        public ExpandedNodeIdCollection(int capacity) : base(capacity) { }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// This static method converts an array of <see cref="ExpandedNodeId"/> objects to
        /// an <see cref="ExpandedNodeIdCollection"/>.
        /// </remarks>
        /// <param name="values">An array of <see cref="ExpandedNodeId"/> values to return as a collection</param>
        public static ExpandedNodeIdCollection ToExpandedNodeIdCollection(ExpandedNodeId[] values)
        {
            if (values != null)
            {
                return [.. values];
            }

            return [];
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <param name="values">An array of <see cref="ExpandedNodeId"/> values to return as a collection</param>
        public static implicit operator ExpandedNodeIdCollection(ExpandedNodeId[] values)
        {
            return ToExpandedNodeIdCollection(values);
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
                clone.Add(Utils.Clone(element));
            }

            return clone;
        }
    }//class
}//namespace
