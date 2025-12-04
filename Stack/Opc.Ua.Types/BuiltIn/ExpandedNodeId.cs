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
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Extends a node id by adding a complete namespace URI.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public sealed class ExpandedNodeId :
        ICloneable,
        IComparable,
        IEquatable<ExpandedNodeId>,
        IFormattable
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

            NamespaceUri = value.NamespaceUri;

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
        public ExpandedNodeId(
            object identifier,
            ushort namespaceIndex,
            string namespaceUri,
            uint serverIndex)
        {
            InnerNodeId = new NodeId(identifier, namespaceIndex);
            NamespaceUri = namespaceUri;
            ServerIndex = serverIndex;
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

            ServerIndex = serverIndex;
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
            NamespaceUri = null;
            ServerIndex = 0;
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
        public string NamespaceUri { get; private set; }

        /// <summary>
        /// The index of the server where the node exists.
        /// </summary>
        /// <remarks>
        /// Returns the index of the server where the node resides
        /// </remarks>
        public uint ServerIndex { get; private set; }

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
                if (!string.IsNullOrEmpty(NamespaceUri))
                {
                    return false;
                }

                if (ServerIndex > 0)
                {
                    return false;
                }

                return NodeId.IsNull(InnerNodeId);
            }
        }

        /// <summary>
        /// Returns true if the expanded node id is an absolute identifier that contains a namespace URI instead of a server dependent index.
        /// </summary>
        public bool IsAbsolute => !string.IsNullOrEmpty(NamespaceUri) || ServerIndex > 0;

        /// <summary>
        /// Returns the inner node id.
        /// </summary>
        public NodeId InnerNodeId { get; set; }

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
                NamespaceUri = nodeId.NamespaceUri;
                ServerIndex = nodeId.ServerIndex;
            }
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
            if (InnerNodeId != null)
            {
                Format(
                    formatProvider,
                    buffer,
                    InnerNodeId.Identifier,
                    InnerNodeId.IdType,
                    InnerNodeId.NamespaceIndex,
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
            object identifier,
            IdType identifierType,
            ushort namespaceIndex,
            string namespaceUri,
            uint serverIndex)
        {
            Format(
                CultureInfo.InvariantCulture,
                buffer,
                identifier,
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
                buffer.Append("nsu=")
                    .Append(CoreUtils.EscapeUri(namespaceUri))
                    .Append(';');
            }

            NodeId.Format(formatProvider, buffer, identifier, identifierType, namespaceIndex);
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
            ExpandedNodeId nodeId = Parse(text);

            // lookup the namespace uri.
            string uri = nodeId.NamespaceUri;

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
                nodeId.NamespaceUri = uri;
                return nodeId;
            }

            // local node id.
            nodeId.InnerNodeId = new NodeId(nodeId.InnerNodeId.Identifier, namespaceIndex);
            nodeId.NamespaceUri = null;

            return nodeId;
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
                value = new ExpandedNodeId(
                    nodeId.Identifier,
                    (ushort)namespaceIndex,
                    null,
                    (uint)serverIndex);
            }
            else
            {
                value = new ExpandedNodeId(nodeId, namespaceUri, (uint)serverIndex);
            }

            return true;
        }

        /// <summary>
        /// Unescapes any reserved characters in the uri.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
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
        /// The set of hexadecimal digits used for decoding escaped URIs.
        /// </summary>
        private const string kHexDigits = "0123456789ABCDEF";

        /// <inheritdoc/>
        public int CompareTo(object obj)
        {
            // check for null.
            if (obj is null)
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
            return nodeId == null ? 0 : -1;
        }

        /// <inheritdoc/>
        public static bool operator >(ExpandedNodeId value1, object value2)
        {
            if (value1 is not null)
            {
                return value1.CompareTo(value2) > 0;
            }

            return false;
        }

        /// <inheritdoc/>
        public static bool operator <(ExpandedNodeId value1, object value2)
        {
            if (value1 is not null)
            {
                return value1.CompareTo(value2) < 0;
            }
            return true;
        }

        /// <inheritdoc/>
        public static bool operator >=(ExpandedNodeId left, ExpandedNodeId right)
        {
            return right is null || right.CompareTo(left) <= 0;
        }

        /// <inheritdoc/>
        public static bool operator <=(ExpandedNodeId left, ExpandedNodeId right)
        {
            return left is null || left.CompareTo(right) <= 0;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return CompareTo(obj) == 0;
        }
        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public static bool operator ==(ExpandedNodeId value1, object value2)
        {
            if (value1 is null)
            {
                return value2 is null;
            }

            return value1.CompareTo(value2) == 0;
        }

        /// <inheritdoc/>
        public static bool operator !=(ExpandedNodeId value1, object value2)
        {
            if (value1 is null)
            {
                return value2 is not null;
            }

            return value1.CompareTo(value2) != 0;
        }

        /// <inheritdoc/>
        public bool Equals(ExpandedNodeId other)
        {
            return CompareTo(other) == 0;
        }

        /// <inheritdoc/>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return Format(formatProvider);
            }

            throw new FormatException(CoreUtils.Format("Invalid format string: '{0}'.", format));
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

        /// <inheritdoc/>
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
            if (string.IsNullOrEmpty(nodeId.NamespaceUri) && nodeId.ServerIndex == 0)
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
            NamespaceUri = null;
        }

        /// <summary>
        /// Updates the namespace uri.
        /// </summary>
        internal void SetNamespaceUri(string uri)
        {
            InnerNodeId.SetNamespaceIndex(0);
            NamespaceUri = uri;
        }

        /// <summary>
        /// Updates the server index.
        /// </summary>
        internal void SetServerIndex(uint serverIndex)
        {
            ServerIndex = serverIndex;
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

            return ToNodeId(nodeId, namespaceUris)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "NamespaceUri ({0}) is not in the namespace table.",
                    nodeId.NamespaceUri);
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
                throw new InvalidCastException(
                    "Cannot cast an absolute ExpandedNodeId to a NodeId. Use ExpandedNodeId.ToNodeId instead.");
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
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="ArgumentException"></exception>
        private void InternalParse(string text)
        {
            if (!InternalTryParseAndSet(text, out NodeIdParseError error))
            {
                // Check if this should be an ArgumentException based on the error message
                if (error is NodeIdParseError.InvalidNamespaceUri or NodeIdParseError.IdentifierMissing)
                {
                    throw new ArgumentException(error.ToString());
                }

                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "Cannot parse expanded node id text: '{0}' Error: {1}", text, error);
            }
        }

        /// <summary>
        /// Tries to parse a expanded node id string and sets the properties on this instance.
        /// </summary>
        /// <param name="text">The ExpandedNodeId value as a string.</param>
        /// <param name="error">Error message if parsing fails.</param>
        /// <returns>True if parsing was successful, false otherwise.</returns>
        private bool InternalTryParseAndSet(string text, out NodeIdParseError error)
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
                        error = NodeIdParseError.InvalidServerIndex;
                        return false;
                    }

                    if (!uint.TryParse(text[4..index], NumberStyles.None, CultureInfo.InvariantCulture, out serverIndex))
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

            // set the properties.
            InnerNodeId = innerNodeId;
            NamespaceUri = namespaceUri;
            SetServerIndex(serverIndex);

            return true;
        }
    }

    /// <summary>
    /// A collection of ExpandedNodeId objects.
    /// </summary>
    [CollectionDataContract(
        Name = "ListOfExpandedNodeId",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "ExpandedNodeId"
    )]
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
                clone.Add(CoreUtils.Clone(element));
            }

            return clone;
        }
    }
}
