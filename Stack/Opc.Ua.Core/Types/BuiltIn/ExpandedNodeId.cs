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
    /// <remarks>
    /// Extends a node id by adding a complete namespace URI.
    /// </remarks>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class ExpandedNodeId : IComparable, IFormattable
    {
        #region Constructors
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
            if (value == null) throw new ArgumentNullException(nameof(value));

            m_namespaceUri = value.m_namespaceUri;

            if (value.m_nodeId != null)
            {
                m_nodeId = new NodeId(value.m_nodeId);
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
                m_nodeId = new NodeId(nodeId);
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
            m_nodeId = new NodeId(identifier, namespaceIndex);
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
                m_nodeId = new NodeId(nodeId);
            }

            if (!String.IsNullOrEmpty(namespaceUri))
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
                m_nodeId = new NodeId(nodeId);
            }

            if (!String.IsNullOrEmpty(namespaceUri))
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
            m_nodeId = new NodeId(value);
        }

        /// <summary>
        /// Initializes a numeric node identifier with a namespace index.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the class while accepting both the id and namespace
        /// of the node we are wrapping.
        /// </remarks>
        /// <param name="value">The numeric id of the node we are wrapping</param>
        /// <param name="namespaceIndex">The namspace index that this node belongs to</param>
        public ExpandedNodeId(uint value, ushort namespaceIndex)
        {
            Initialize();
            m_nodeId = new NodeId(value, namespaceIndex);
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
            m_nodeId = new NodeId(value);
            SetNamespaceUri(namespaceUri);
        }

        /// <summary>
        /// Initializes a string node identifier with a namespace index.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the class while allowing you to specify both the
        /// node and the namespace.
        /// </remarks>
        /// <param name="namespaceIndex">The numeric index of the namespace within the table, that this node belongs to</param>
        /// <param name="value">The string id/value of the node we are wrapping</param>
        public ExpandedNodeId(string value, ushort namespaceIndex)
        {
            Initialize();
            m_nodeId = new NodeId(value, namespaceIndex);
        }

        /// <summary>
        /// Initializes a string node identifier with a namespace URI.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the class while allowing you to specify both the node and namespace
        /// </remarks>
        /// <param name="namespaceUri">The actual namespace URI that this node belongs to</param>
        /// <param name="value">The string value/id of the node we are wrapping</param>
        public ExpandedNodeId(string value, string namespaceUri)
        {
            Initialize();
            m_nodeId = new NodeId(value, 0);
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
            m_nodeId = new NodeId(value);
        }

        /// <summary>
        /// Initializes a guid node identifier.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the class while specifying the <see cref="Guid"/> value
        /// of the node and the namesapceIndex we are wrapping.
        /// </remarks>
        /// <param name="value">The Guid value of the node we are wrapping</param>
        /// <param name="namespaceIndex">The index of the namespace that this node should belong to</param>
        public ExpandedNodeId(Guid value, ushort namespaceIndex)
        {
            Initialize();
            m_nodeId = new NodeId(value, namespaceIndex);
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
            m_nodeId = new NodeId(value);
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
            m_nodeId = new NodeId(value);
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
            m_nodeId = new NodeId(value, namespaceIndex);
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
            m_nodeId = new NodeId(value);
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
            m_nodeId = new NodeId(text);
        }

        /// <summary>
        /// Sets the private members to default values.
        /// </summary>
        /// <remarks>
        /// Sets the private members to default values.
        /// </remarks>
        private void Initialize()
        {
            m_nodeId = null;
            m_namespaceUri = null;
            m_serverIndex = 0;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The index of the namespace URI in the server's namespace array.
        /// </summary>
        /// <remarks>
        /// The index of the namespace URI in the server's namespace array.
        /// </remarks>
        public virtual ushort NamespaceIndex
        {
            get
            {
                if (m_nodeId != null)
                {
                    return m_nodeId.NamespaceIndex;
                }

                return 0;
            }
        }

        /// <summary>
        /// The type of node identifier used.
        /// </summary>
        /// <remarks>
        /// The type of node identifier used.
        /// </remarks>
        public IdType IdType
        {
            get
            {
                if (m_nodeId != null)
                {
                    return m_nodeId.IdType;
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
                if (m_nodeId != null)
                {
                    return m_nodeId.Identifier;
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
                if (!String.IsNullOrEmpty(m_namespaceUri))
                {
                    return false;
                }

                if (m_serverIndex > 0)
                {
                    return false;
                }

                return NodeId.IsNull(m_nodeId);
            }
        }

        /// <summary>
        /// Returns true if the expanded node id is an absolute identifier that contains a namespace URI instead of a server dependent index.
        /// </summary>
        /// <remarks>
        /// Returns true if the expanded node id is an absolute identifier that contains a namespace URI instead of a server dependent index.
        /// </remarks>
        public bool IsAbsolute
        {
            get
            {
                if (!String.IsNullOrEmpty(m_namespaceUri) || m_serverIndex > 0)
                {
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Returns the inner node id.
        /// </summary>
        /// <remarks>
        /// Returns the inner node id.
        /// </remarks>
        internal NodeId InnerNodeId
        {
            get { return m_nodeId; }
            set { m_nodeId = value; }
        }

        /// <summary>
        /// The node identifier formatted as a URI.
        /// </summary>
        /// <remarks>
        /// The node identifier formatted as a URI.
        /// </remarks>
        [DataMember(Name = "Identifier", Order = 1)]
        internal string IdentifierText
        {
            get
            {
                return Format();
            }
            set
            {
                ExpandedNodeId nodeId = ExpandedNodeId.Parse(value);

                m_nodeId = nodeId.m_nodeId;
                m_namespaceUri = nodeId.m_namespaceUri;
                m_serverIndex = nodeId.m_serverIndex;
            }
        }

        #region public string Format()
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
        public string Format()
        {
            StringBuilder buffer = new StringBuilder();
            Format(buffer);
            return buffer.ToString();
        }

        /// <summary>
        /// Formats the node ids as string and adds it to the buffer.
        /// </summary>
        public void Format(StringBuilder buffer)
        {
            if (m_nodeId != null)
            {
                Format(buffer, m_nodeId.Identifier, m_nodeId.IdType, m_nodeId.NamespaceIndex, m_namespaceUri, m_serverIndex);
            }
            else
            {
                Format(buffer, null, IdType.Numeric, 0, m_namespaceUri, m_serverIndex);
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
            if (serverIndex != 0)
            {
                buffer.AppendFormat(CultureInfo.InvariantCulture, "svr={0};", serverIndex);
            }

            if (!String.IsNullOrEmpty(namespaceUri))
            {
                buffer.Append("nsu=");

                for (int ii = 0; ii < namespaceUri.Length; ii++)
                {
                    char ch = namespaceUri[ii];

                    switch (ch)
                    {
                        case ';':
                        case '%':
                        {
                            buffer.AppendFormat(CultureInfo.InvariantCulture, "%{0:X2}", Convert.ToInt16(ch));
                            break;
                        }

                        default:
                        {
                            buffer.Append(ch);
                            break;
                        }
                    }
                }

                buffer.Append(';');
            }

            NodeId.Format(buffer, identifier, identifierType, namespaceIndex);
        }
        #endregion

        #region public static ExpandedNodeId Parse(string, NamespaceTable, NamespaceTable)
        /// <summary>
        /// Parses a expanded node id string, translated any namespace indexes and returns the result.
        /// </summary>
        public static ExpandedNodeId Parse(string text, NamespaceTable currentNamespaces, NamespaceTable targetNamespaces)
        {
            // parse the string.
            ExpandedNodeId nodeId = Parse(text);

            // lookup the namespace uri.
            string uri = nodeId.m_namespaceUri;

            if (nodeId.m_nodeId.NamespaceIndex != 0)
            {
                uri = currentNamespaces.GetString(nodeId.m_nodeId.NamespaceIndex);
            }

            // translate the namespace uri.
            ushort namespaceIndex = 0;

            if (!String.IsNullOrEmpty(uri))
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
                nodeId.m_nodeId = new NodeId(nodeId.m_nodeId.Identifier, 0);
                nodeId.m_namespaceUri = uri;
                return nodeId;
            }

            // local node id.
            nodeId.m_nodeId = new NodeId(nodeId.m_nodeId.Identifier, namespaceIndex);
            nodeId.m_namespaceUri = null;

            return nodeId;
        }
        #endregion

        #region public static ExpandedNodeId Parse(string text)
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
                if (String.IsNullOrEmpty(text))
                {
                    return ExpandedNodeId.Null;
                }

                uint serverIndex = 0;

                // parse the server index if present.
                if (text.StartsWith("svr=", StringComparison.Ordinal))
                {
                    int index = text.IndexOf(';');

                    if (index == -1)
                    {
                        throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "Invalid server index.");
                    }

                    serverIndex = Convert.ToUInt32(text.Substring(4, index - 4), CultureInfo.InvariantCulture);

                    text = text.Substring(index + 1);
                }

                string namespaceUri = null;

                // parse the namespace uri if present.
                if (text.StartsWith("nsu=", StringComparison.Ordinal))
                {
                    int index = text.IndexOf(';');

                    if (index == -1)
                    {
                        throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "Invalid namespace uri.");
                    }

                    StringBuilder buffer = new StringBuilder();

                    UnescapeUri(text, 4, index, buffer);
                    namespaceUri = buffer.ToString();
                    text = text.Substring(index + 1);
                }

                // parse the node id.
                NodeId nodeId = NodeId.Parse(text);

                // craete the node id.
                return new ExpandedNodeId(nodeId, namespaceUri, serverIndex);
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
                    {
                        if (ii + 2 >= index)
                        {
                            throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "Invalid escaped character in namespace uri.");
                        }

                        ushort value = 0;

                        int digit = kHexDigits.IndexOf(Char.ToUpperInvariant(text[++ii]));

                        if (digit == -1)
                        {
                            throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "Invalid escaped character in namespace uri.");
                        }

                        value += (ushort)digit;
                        value <<= 4;

                        digit = kHexDigits.IndexOf(Char.ToUpperInvariant(text[++ii]));

                        if (digit == -1)
                        {
                            throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "Invalid escaped character in namespace uri.");
                        }

                        value += (ushort)digit;

                        char unencodedChar = Convert.ToChar(value);

                        buffer.Append(unencodedChar);
                        break;
                    }

                    default:
                    {
                        buffer.Append(ch);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// The set of hexadecimal digits used for decoding escaped URIs.
        /// </summary>
        private const string kHexDigits = "0123456789ABCDEF";
        #endregion

        #endregion

        #region IComparable Members
        /// <summary>
        /// Compares the current instance to the object.
        /// </summary>
        /// <remarks>
        /// Compares the current instance to the object.
        /// </remarks>
        public int CompareTo(object obj)
        {
            // check for null.
            if (Object.ReferenceEquals(obj, null))
            {
                return -1;
            }

            // check for reference comparisons.
            if (Object.ReferenceEquals(this, obj))
            {
                return 0;
            }

            // just compare node ids.
            if (!this.IsAbsolute)
            {
                if (this.m_nodeId != null)
                {
                    return this.m_nodeId.CompareTo(obj);
                }
            }

            NodeId nodeId = obj as NodeId;

            // check for expanded node ids.
            ExpandedNodeId expandedId = obj as ExpandedNodeId;

            if (expandedId != null)
            {
                if (this.IsNull && expandedId.IsNull)
                {
                    return 0;
                }

                if (this.ServerIndex != expandedId.ServerIndex)
                {
                    return this.ServerIndex.CompareTo(expandedId.ServerIndex);
                }

                if (this.NamespaceUri != expandedId.NamespaceUri)
                {
                    if (this.NamespaceUri != null)
                    {
                        return String.CompareOrdinal(NamespaceUri, expandedId.NamespaceUri);
                    }

                    return -1;
                }

                nodeId = expandedId.m_nodeId;
            }

            // check for null.
            if (this.m_nodeId != null)
            {
                return this.m_nodeId.CompareTo(nodeId);
            }

            // compare node ids.
            return (nodeId == null) ? 0 : -1;
        }

        /// <summary>
        /// Returns true if a is greater than b.
        /// </summary>
        /// <remarks>
        /// Returns true if a is greater than b.
        /// </remarks>
        public static bool operator >(ExpandedNodeId value1, object value2)
        {
            if (!Object.ReferenceEquals(value1, null))
            {
                return value1.CompareTo(value2) > 0;
            }

            return false;
        }

        /// <summary>
        /// Returns true if a is less than b.
        /// </summary>
        /// <remarks>
        /// Returns true if a is less than b.
        /// </remarks>
        public static bool operator <(ExpandedNodeId value1, object value2)
        {
            if (!Object.ReferenceEquals(value1, null))
            {
                return value1.CompareTo(value2) < 0;
            }

            return true;
        }
        #endregion

        #region Comparison Functions
        /// <summary>
        /// Determines if the specified object is equal to the ExpandedNodeId.
        /// </summary>
        /// <remarks>
        /// Determines if the specified object is equal to the ExpandedNodeId.
        /// </remarks>
        public override bool Equals(object obj)
        {
            return (CompareTo(obj) == 0);
        }

        /// <summary>
        /// Returns a unique hashcode for the ExpandedNodeId
        /// </summary>
        /// <remarks>
        /// Returns a unique hashcode for the ExpandedNodeId
        /// </remarks>
        public override int GetHashCode()
        {
            if (m_nodeId == null || m_nodeId.IsNullNodeId)
            {
                return 0;
            }

            // just compare node ids.
            if (!this.IsAbsolute)
            {
                return m_nodeId.GetHashCode();
            }

            var hash = new HashCode();

            if (this.ServerIndex != 0)
            {
                hash.Add(this.ServerIndex);
            }

            if (this.NamespaceUri != null)
            {
                hash.Add(NamespaceUri);
            }

            hash.Add(this.m_nodeId);

            return hash.ToHashCode();
        }

        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are equal.
        /// </remarks>
        public static bool operator ==(ExpandedNodeId value1, object value2)
        {
            if (Object.ReferenceEquals(value1, null))
            {
                return Object.ReferenceEquals(value2, null);
            }

            return (value1.CompareTo(value2) == 0);
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are not equal.
        /// </remarks>
        public static bool operator !=(ExpandedNodeId value1, object value2)
        {
            if (Object.ReferenceEquals(value1, null))
            {
                return !Object.ReferenceEquals(value2, null);
            }

            return (value1.CompareTo(value2) != 0);
        }
        #endregion

        #region IFormattable Members
        /// <summary>
        /// Returns the string representation of an ExpandedNodeId.
        /// </summary>
        /// <remarks>
        /// Returns the string representation of an ExpandedNodeId.
        /// </remarks>
        /// <returns>The <see cref="ExpandedNodeId"/> as a formatted string</returns>
        /// <param name="format">(Unused) The format string.</param>
        /// <param name="formatProvider">(Unused) The format-provider.</param>
        /// <exception cref="FormatException">Thrown when the 'format' parameter is NOT null. So leave that parameter null.</exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return Format();
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion

        #region ICloneable Members
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
        #endregion

        #region Public Methods
        /// <summary>
        /// Returns the string representation of am ExpandedNodeId.
        /// </summary>
        /// <remarks>
        /// Returns the string representation of am ExpandedNodeId.
        /// </remarks>
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
            if (String.IsNullOrEmpty(nodeId.m_namespaceUri) && nodeId.m_serverIndex == 0)
            {
                return nodeId.m_nodeId;
            }

            // create copy.
            NodeId localId = new NodeId(nodeId.m_nodeId);

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
        /// <remarks>
        /// Updates the namespace index.
        /// </remarks>
        internal void SetNamespaceIndex(ushort namespaceIndex)
        {
            m_nodeId.SetNamespaceIndex(namespaceIndex);
            m_namespaceUri = null;
        }

        /// <summary>
        /// Updates the namespace uri.
        /// </summary>
        internal void SetNamespaceUri(string uri)
        {
            m_nodeId.SetNamespaceIndex(0);
            m_namespaceUri = uri;
        }

        /// <summary>
        /// Updates the server index.
        /// </summary>
        internal void SetServerIndex(uint serverIndex)
        {
            m_serverIndex = serverIndex;
        }
        #endregion

        #region Static Members
        /// <summary>
        /// Parses an absolute NodeId formatted as a string and converts it a local NodeId.
        /// </summary>
        /// <param name="namespaceUris">The current namespace table.</param>
        /// <param name="text">The text to parse.</param>
        /// <returns>The local identifier.</returns>
        /// <exception cref="ServiceResultException">Thrown if the namespace URI is not in the namespace table.</exception>
        public static NodeId Parse(string text, NamespaceTable namespaceUris)
        {
            ExpandedNodeId nodeId = ExpandedNodeId.Parse(text);

            if (!nodeId.IsAbsolute)
            {
                return nodeId.InnerNodeId;
            }

            NodeId localId = ExpandedNodeId.ToNodeId(nodeId, namespaceUris);

            if (localId == null)
            {
                throw ServiceResultException.Create(StatusCodes.BadNodeIdInvalid, "NamespaceUri ({0}) is not in the namespace table.", nodeId.NamespaceUri);
            }

            return localId;
        }

        /// <summary>
        /// Converts an ExpandedNodeId to a NodeId.
        /// </summary>
        /// <remarks>
        /// Converts an ExpandedNodeId to a NodeId.
        /// </remarks>
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
        /// <remarks>
        /// Converts an integer to a numeric node identifier.
        /// </remarks>
        public static implicit operator ExpandedNodeId(uint value)
        {
            return new ExpandedNodeId(value);
        }

        /// <summary>
        /// Converts a guid to a guid node identifier.
        /// </summary>
        /// <remarks>
        /// Converts a guid to a guid node identifier.
        /// </remarks>
        public static implicit operator ExpandedNodeId(Guid value)
        {
            return new ExpandedNodeId(value);
        }

        /// <summary>
        /// Converts a byte array to an opaque node identifier.
        /// </summary>
        /// <remarks>
        /// Converts a byte array to an opaque node identifier.
        /// </remarks>
        public static implicit operator ExpandedNodeId(byte[] value)
        {
            return new ExpandedNodeId(value);
        }

        /// <summary>
        /// Parses a node id string and initializes a node id.
        /// </summary>
        /// <remarks>
        /// Parses a node id string and initializes a node id.
        /// </remarks>
        public static implicit operator ExpandedNodeId(string text)
        {
            return new ExpandedNodeId(text);
        }

        /// <summary>
        /// Converts a NodeId to an ExpandedNodeId
        /// </summary>
        /// <remarks>
        /// Converts a NodeId to an ExpandedNodeId
        /// </remarks>
        public static implicit operator ExpandedNodeId(NodeId nodeId)
        {
            return new ExpandedNodeId(nodeId);
        }

        /// <summary>
        /// Returns an instance of a null ExpandedNodeId.
        /// </summary>
        public static ExpandedNodeId Null => s_Null;

        private static readonly ExpandedNodeId s_Null = new ExpandedNodeId();
        #endregion

        #region Private Fields
        private NodeId m_nodeId;
        private string m_namespaceUri;
        private uint m_serverIndex;
        #endregion
    }

    #region ExpandedNodeIdCollection Class
    /// <summary>
    /// A collection of ExpandedNodeId objects.
    /// </summary>
    [CollectionDataContract(Name = "ListOfExpandedNodeId", Namespace = Namespaces.OpcUaXsd, ItemName = "ExpandedNodeId")]
    public partial class ExpandedNodeIdCollection : List<ExpandedNodeId>
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
                return new ExpandedNodeIdCollection(values);
            }

            return new ExpandedNodeIdCollection();
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="ExpandedNodeId"/> values to return as a collection</param>
        public static implicit operator ExpandedNodeIdCollection(ExpandedNodeId[] values)
        {
            return ToExpandedNodeIdCollection(values);
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public new object MemberwiseClone()
        {
            ExpandedNodeIdCollection clone = new ExpandedNodeIdCollection(this.Count);

            foreach (ExpandedNodeId element in this)
            {
                clone.Add((ExpandedNodeId)Utils.Clone(element));
            }

            return clone;
        }

    }//class
    #endregion

}//namespace
