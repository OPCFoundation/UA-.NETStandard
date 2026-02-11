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
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Stores an identifier for a node in a server's address space.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Please refer to OPC Specifications</b>:
    /// <list type="bullet">
    /// <item><b>Address Space Model</b> section <b>7.2</b></item>
    /// <item><b>Address Space Model</b> section <b>5.2.2</b></item>
    /// </list>
    /// </para>
    /// <para>
    /// Stores the id of a Node, which resides within the server's address space.
    /// <br/></para>
    /// <para>
    /// The NodeId can be either:
    /// <list type="bullet">
    /// <item><see cref="uint"/></item>
    /// <item><see cref="Guid"/></item>
    /// <item><see cref="string"/></item>
    /// <item><see cref="byte"/>[]</item>
    /// </list>
    /// <br/></para>
    /// <note>
    /// <b>Important:</b> Keep in mind that the actual ID's of nodes should be
    /// unique such that no two nodes within an address-space share the same ID's.
    /// </note>
    /// <para>
    /// The NodeId can be assigned to a particular namespace index. This index is
    /// merely just a number and does not represent some index within a collection
    /// that this node has any knowledge of. The assumption is that the host of
    /// this object will manage that directly.
    /// <br/></para>
    /// <para>
    /// <b>The Node id is not data contract serializable.
    /// Use <see cref="SerializableNodeId"/> as part of your contracts</b>
    /// </para>
    /// </remarks>
    public readonly struct NodeId :
        IEquatable<NodeId>, IComparable<NodeId>,
        IEquatable<ExpandedNodeId>, IComparable<ExpandedNodeId>,
        IEquatable<uint>, IComparable<uint>,
        IEquatable<string>, IComparable<string>,
        IEquatable<Guid>, IComparable<Guid>,
        IEquatable<byte[]>, IComparable<byte[]>,
        IComparable,
        IFormattable
    {
        /// <summary>
        /// Returns an instance of a null NodeId.
        /// </summary>
        public static readonly NodeId Null;

        /// <summary>
        /// The index of the namespace URI in the server's namespace array.
        /// </summary>
        public ushort NamespaceIndex => m_inner.NamespaceIdx;

        /// <summary>
        /// The type of node identifier used.
        /// </summary>
        /// <remarks>
        /// Returns the type of Id, whether it is:
        /// <list type="bullet">
        /// <item><see cref="uint"/></item>
        /// <item><see cref="Guid"/></item>
        /// <item><see cref="string"/></item>
        /// <item><see cref="byte"/>[]</item>
        /// </list>
        /// </remarks>
        /// <seealso cref="IdType"/>
        public IdType IdType => (IdType)m_inner.Type;

        /// <summary>
        /// Create null node id.
        /// </summary>
        private NodeId(IdType idType = IdType.Numeric)
        {
            m_inner.NamespaceIdx = 0;
            m_inner.Type = (byte)idType;
            m_inner.Numeric = 0;
            m_identifier = null;
        }

        /// <summary>
        /// Creates a new NodeId that will have a numeric (unsigned-int) id
        /// </summary>
        /// <param name="value">The numeric value of the id</param>
        public NodeId(uint value)
        {
            m_inner.NamespaceIdx = 0;
            m_inner.Type = (byte)IdType.Numeric;
            m_inner.Numeric = value;
            m_identifier = null;
        }

        /// <summary>
        /// Creates a new NodeId that will use a numeric (unsigned int) for its Id,
        /// but also specifies which namespace this node should belong to.
        /// </summary>
        /// <param name="value">The new (numeric) Id for the node being created</param>
        /// <param name="namespaceIndex">The index of the namespace that this
        /// node should belong to</param>
        /// <seealso cref="WithNamespaceIndex"/>
        public NodeId(uint value, ushort namespaceIndex)
        {
            m_inner.NamespaceIdx = namespaceIndex;
            m_inner.Type = (byte)IdType.Numeric;
            m_inner.Numeric = value;
            m_identifier = null;
        }

        /// <summary>
        /// Creates a new NodeId that will use a string for its Id, but also
        /// specifies if the Id is a URI, and which namespace this node belongs to.
        /// </summary>
        /// <param name="value">The new (string) Id for the node being created</param>
        /// <param name="namespaceIndex">The index of the namespace that this
        /// node belongs to</param>
        public NodeId(string value, ushort namespaceIndex)
        {
            m_inner.NamespaceIdx = namespaceIndex;
            m_inner.Type = (byte)IdType.String;
            m_inner.Numeric = string.IsNullOrEmpty(value) ? 0u :
                (uint)value.GetHashCode(StringComparison.Ordinal);
            m_identifier = value;
        }

        /// <summary>
        /// Creates a new node whose Id will be a <see cref="Guid"/>.
        /// </summary>
        /// <param name="value">The new Guid value of this nodes Id.</param>
        public NodeId(Guid value)
        {
            m_inner.NamespaceIdx = 0;
            m_inner.Type = (byte)IdType.Guid;
            m_inner.Numeric =
                value == Guid.Empty ? 0u : (uint)value.GetHashCode();
            m_identifier = value;
        }

        /// <summary>
        /// Creates a new node whose Id will be a <see cref="Guid"/>.
        /// </summary>
        /// <param name="value">The new Guid value of this nodes Id.</param>
        /// <param name="namespaceIndex">The index of the namespace that this
        /// node belongs to</param>
        public NodeId(Guid value, ushort namespaceIndex)
        {
            m_inner.NamespaceIdx = namespaceIndex;
            m_inner.Type = (byte)IdType.Guid;
            m_inner.Numeric =
                value == Guid.Empty ? 0u : (uint)value.GetHashCode();
            m_identifier = value;
        }

        /// <summary>
        /// Creates a new node whose Id will be a series of <see cref="byte"/>.
        /// </summary>
        /// <param name="value">An array of <see cref="byte"/> that will become
        /// this Node's ID</param>
        public NodeId(byte[] value)
        {
            m_inner.NamespaceIdx = 0;
            m_inner.Type = (byte)IdType.Opaque;
            if (value?.Length > 0)
            {
                byte[] copy = new byte[value.Length];
                Array.Copy(value, copy, value.Length);
                m_inner.Numeric =
                    (uint)ByteStringEqualityComparer.Default.GetHashCode(copy);
                m_identifier = copy;
            }
            else
            {
                m_inner.Numeric = 0;
                m_identifier = value;
            }
        }

        /// <summary>
        /// Creates a new node whose Id will be a series of <see cref="byte"/>,
        /// while specifying the index of the namespace that this node belongs to.
        /// </summary>
        /// <param name="value">An array of <see cref="byte"/> that will become
        /// this Node's ID</param>
        /// <param name="namespaceIndex">The index of the namespace that this
        /// node belongs to</param>
        public NodeId(byte[] value, ushort namespaceIndex)
        {
            m_inner.NamespaceIdx = namespaceIndex;
            m_inner.Type = (byte)IdType.Opaque;
            if (value?.Length > 0)
            {
                byte[] copy = new byte[value.Length];
                Array.Copy(value, copy, value.Length);
                m_inner.Numeric =
                    (uint)ByteStringEqualityComparer.Default.GetHashCode(copy);
                m_identifier = copy;
            }
            else
            {
                m_inner.Numeric = 0;
                m_identifier = value;
            }
        }

        /// <summary>
        /// Creates a new node with a String id.
        /// </summary>
        /// <param name="text">The string id of this new node</param>
        [Obsolete("Use NodeId.Parse instead. This will be removed soon.")]
        public NodeId(string text)
        {
            this = Parse(text);
        }

        /// <summary>
        /// Initializes a node identifier with a namespace index.
        /// Throws an exception if the identifier type is not supported.
        /// </summary>
        /// <param name="value">The identifier</param>
        /// <param name="namespaceIndex">The index of the namespace that
        /// qualifies the node</param>
        [Obsolete("Use concrete constructor with typed identifier values instead.")]
        [JsonConstructor]
        public NodeId(object value, ushort namespaceIndex)
        {
            switch (value)
            {
                case uint:
                    this = SetIdentifier(IdType.Numeric, value);
                    break;
                case null or string:
                    this = SetIdentifier(IdType.String, value);
                    break;
                case Guid:
                    this = SetIdentifier(IdType.Guid, value);
                    break;
                case byte[]:
                    this = SetIdentifier(IdType.Opaque, value);
                    break;
                default:
                    throw new ArgumentException(
                        "Identifier type not supported.",
                        nameof(value));
            }
            m_inner.NamespaceIdx = namespaceIndex;
        }

        /// <summary>
        /// Parses an NodeId formatted as a string and converts it a NodeId.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="text">The text to parse.</param>
        /// <param name="options">The options to use when parsing a NodeId.</param>
        /// <returns>The NodeId.</returns>
        /// <exception cref="ServiceResultException">Thrown if the namespace
        /// URI is not in the namespace table.</exception>
        public static NodeId Parse(
            IServiceMessageContext context,
            string text,
            NodeIdParsingOptions options = null)
        {
            if (!InternalTryParseWithContext(
                context,
                text,
                options,
                out NodeId value,
                out NodeIdParseError error))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "Cannot parse node id text: '{0}' Error: {1}",
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
        /// <param name="options">The options to use when parsing a NodeId.</param>
        /// <param name="value">The parsed NodeId if successful, otherwise NodeId.Null.</param>
        /// <param name="error">Error message if parsing fails.</param>
        /// <returns>True if parsing was successful, false otherwise.</returns>
        internal static bool InternalTryParseWithContext(
            IServiceMessageContext context,
            string text,
            NodeIdParsingOptions options,
            out NodeId value,
            out NodeIdParseError error)
        {
            error = NodeIdParseError.None;
            value = Null;

            if (string.IsNullOrEmpty(text))
            {
                value = Null;
                return true;
            }

            int namespaceIndex = 0;

            if (text.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int index = text.IndexOf(';', 4);

                if (index < 0)
                {
                    error = NodeIdParseError.InvalidNamespaceFormat;
                    return false;
                }

                string namespaceUri = CoreUtils.UnescapeUri(text.AsSpan()[4..index]);
                namespaceIndex =
                    options?.UpdateTables == true
                        ? context.NamespaceUris.GetIndexOrAppend(namespaceUri)
                        : context.NamespaceUris.GetIndex(namespaceUri);

                if (namespaceIndex < 0)
                {
                    error = NodeIdParseError.NoNamespaceMapping;
                    return false;
                }

                text = text[(index + 1)..];
            }

            if (text.StartsWith("ns=", StringComparison.Ordinal))
            {
                int index = text.IndexOf(';', 3);

                if (index < 0)
                {
                    error = NodeIdParseError.InvalidNamespaceFormat;
                    return false;
                }

                if (ushort.TryParse(text[3..index], out ushort ns))
                {
                    namespaceIndex = ns;

                    if (options?.NamespaceMappings != null &&
                        options?.NamespaceMappings.Length < ns)
                    {
                        namespaceIndex = options.NamespaceMappings[ns];
                    }
                }

                text = text[(index + 1)..];
            }

            if (text.Length >= 2)
            {
                char idType = text[0];
                text = text[2..];

                switch (idType)
                {
                    case 'i':
                        if (uint.TryParse(text, out uint number))
                        {
                            value = new NodeId(number, (ushort)namespaceIndex);
                            return true;
                        }

                        break;
                    case 's':
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            value = new NodeId(text, (ushort)namespaceIndex);
                            return true;
                        }

                        break;
                    case 'b':
                        try
                        {
                            byte[] bytes = Convert.FromBase64String(text);
                            value = new NodeId(bytes, (ushort)namespaceIndex);
                            return true;
                        }
                        catch
                        {
                            // error handled after the switch statement.
                        }

                        break;
                    case 'g':
                        if (Guid.TryParse(text, out Guid guid))
                        {
                            value = new NodeId(guid, (ushort)namespaceIndex);
                            return true;
                        }

                        break;
                    default:
                        error = NodeIdParseError.InvalidIdentifierType;
                        return false;
                }
            }

            error = NodeIdParseError.InvalidIdentifier;
            return false;
        }

        /// <summary>
        /// Formats a NodeId as a string.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="useNamespaceUri">The NamespaceUri is used instead of the NamespaceIndex.</param>
        /// <returns>The formatted identifier.</returns>
        /// <exception cref="ServiceResultException"></exception>
        public string Format(IServiceMessageContext context, bool useNamespaceUri = false)
        {
            if (IsNull)
            {
                return string.Empty;
            }

            var buffer = new StringBuilder();

            if (NamespaceIndex > 0)
            {
                if (useNamespaceUri)
                {
                    string namespaceUri = context.NamespaceUris.GetString(NamespaceIndex);

                    if (!string.IsNullOrEmpty(namespaceUri))
                    {
                        buffer.Append("nsu=")
                            .Append(CoreUtils.EscapeUri(namespaceUri))
                            .Append(';');
                    }
                    else
                    {
                        buffer.Append("ns=")
                            .Append(NamespaceIndex)
                            .Append(';');
                    }
                }
                else
                {
                    buffer.Append("ns=")
                        .Append(NamespaceIndex)
                        .Append(';');
                }
            }

            switch (IdType)
            {
                case IdType.Numeric:
                    buffer.Append("i=")
                        .Append(NumericIdentifier);
                    break;
                case IdType.Guid:
                    buffer.Append("g=")
                        .Append(GuidIdentifier);
                    break;
                case IdType.Opaque:
                    buffer.Append("b=")
                        .Append(Convert.ToBase64String(OpaqueIdentifer));
                    break;
                case IdType.String:
                    buffer.Append("s=")
                        .Append(StringIdentifier);
                    break;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected IdType value {IdType}.");
            }

            return buffer.ToString();
        }

        /// <summary>
        /// Converts an identifier and a namespaceUri to a local NodeId using the namespaceTable.
        /// </summary>
        /// <param name="identifier">The identifier for the node.</param>
        /// <param name="namespaceUri">The URI to look up.</param>
        /// <param name="namespaceTable">The table to use for the URI lookup.</param>
        /// <returns>A local NodeId</returns>
        /// <exception cref="ServiceResultException">Thrown when the namespace cannot be found</exception>
        [Obsolete("Use typed NodeId.Create() method instead.")]
        public static NodeId Create(
            object identifier,
            string namespaceUri,
            NamespaceTable namespaceTable)
        {
            int index = GetNamespaceIndex(namespaceUri, namespaceTable);
            return new NodeId(identifier, (ushort)index);
        }

        /// <summary>
        /// Converts a string identifier and a namespaceUri to a local NodeId using the namespaceTable.
        /// </summary>
        /// <param name="identifier">The identifier for the node.</param>
        /// <param name="namespaceUri">The URI to look up.</param>
        /// <param name="namespaceTable">The table to use for the URI lookup.</param>
        /// <returns>A local NodeId</returns>
        /// <exception cref="ServiceResultException">Thrown when the namespace cannot be found</exception>
        public static NodeId Create(
            string identifier,
            string namespaceUri,
            NamespaceTable namespaceTable)
        {
            int index = GetNamespaceIndex(namespaceUri, namespaceTable);
            return new NodeId(identifier, (ushort)index);
        }

        /// <summary>
        /// Converts a numeric identifier and a namespaceUri to a local NodeId using the namespaceTable.
        /// </summary>
        /// <param name="identifier">The identifier for the node.</param>
        /// <param name="namespaceUri">The URI to look up.</param>
        /// <param name="namespaceTable">The table to use for the URI lookup.</param>
        /// <returns>A local NodeId</returns>
        /// <exception cref="ServiceResultException">Thrown when the namespace cannot be found</exception>
        public static NodeId Create(
            uint identifier,
            string namespaceUri,
            NamespaceTable namespaceTable)
        {
            int index = GetNamespaceIndex(namespaceUri, namespaceTable);
            return new NodeId(identifier, (ushort)index);
        }

        /// <summary>
        /// Converts a byte array identifier and a namespaceUri to a local NodeId using the namespaceTable.
        /// </summary>
        /// <param name="identifier">The identifier for the node.</param>
        /// <param name="namespaceUri">The URI to look up.</param>
        /// <param name="namespaceTable">The table to use for the URI lookup.</param>
        /// <returns>A local NodeId</returns>
        /// <exception cref="ServiceResultException">Thrown when the namespace cannot be found</exception>
        public static NodeId Create(
            byte[] identifier,
            string namespaceUri,
            NamespaceTable namespaceTable)
        {
            int index = GetNamespaceIndex(namespaceUri, namespaceTable);
            return new NodeId(identifier, (ushort)index);
        }

        /// <summary>
        /// Converts a Guid identifier and a namespaceUri to a local NodeId using the namespaceTable.
        /// </summary>
        /// <param name="identifier">The identifier for the node.</param>
        /// <param name="namespaceUri">The URI to look up.</param>
        /// <param name="namespaceTable">The table to use for the URI lookup.</param>
        /// <returns>A local NodeId</returns>
        /// <exception cref="ServiceResultException">Thrown when the namespace cannot be found</exception>
        public static NodeId Create(
            Guid identifier,
            string namespaceUri,
            NamespaceTable namespaceTable)
        {
            int index = GetNamespaceIndex(namespaceUri, namespaceTable);
            return new NodeId(identifier, (ushort)index);
        }

        /// <summary>
        /// Converts an integer to a numeric node identifier.
        /// </summary>
        public static implicit operator NodeId(uint value)
        {
            return new NodeId(value);
        }

        /// <summary>
        /// Converts a guid to a guid node identifier.
        /// </summary>
        public static implicit operator NodeId(Guid value)
        {
            return new NodeId(value);
        }

        /// <summary>
        /// Converts a byte array to an opaque node identifier.
        /// </summary>
        public static explicit operator NodeId(byte[] value)
        {
            return new NodeId(value);
        }

        /// <summary>
        /// Parses a node id string and returns a node id object.
        /// </summary>
        /// <remarks>
        /// Parses a NodeId String and returns a NodeId object.
        /// Valid NodeId strings are of the form:
        ///     "i=1234", "s=HelloWorld", "g=AF469096-F02A-4563-940B-603958363B81", "b=01020304",
        ///     "ns=2;s=HelloWorld", "ns=2;i=1234", "ns=2;g=AF469096-F02A-4563-940B-603958363B81", "ns=2;b=01020304"
        /// Invalid NodeId strings will throw an exception, e.g.
        ///     "HelloWorld", "nsu=http://opcfoundation.org/UA/;i=1234"
        /// </remarks>
        /// <param name="text">The NodeId value as a string.</param>
        /// <exception cref="ServiceResultException">Thrown under a variety of circumstances, each time with a specific message.</exception>
        /// <exception cref="ArgumentException">Thrown due to invalid text, each time with a specific message.</exception>
        public static NodeId Parse(string text)
        {
            if (!InternalTryParse(text, false, out NodeId value, out NodeIdParseError error))
            {
                // Check if this should be an ArgumentException based on the error message
                if (error is NodeIdParseError.InvalidNamespaceUri or NodeIdParseError.IdentifierMissing)
                {
                    throw new ArgumentException(error.ToString());
                }

                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "Cannot parse node id text: '{0}' Error: {1}", text, error);
            }
            return value;
        }

        /// <summary>
        /// <para>
        /// Tries to parse a NodeId String and returns a NodeId object if successful.
        /// </para>
        /// <para>
        /// Valid NodeId strings are of the form:
        /// "i=1234",
        /// "s=HelloWorld",
        /// "g=AF469096-F02A-4563-940B-603958363B81",
        /// "b=01020304",
        /// "ns=2;s=HelloWorld",
        /// "ns=2;i=1234",
        /// "ns=2;g=AF469096-F02A-4563-940B-603958363B81",
        /// "ns=2;b=01020304"
        /// </para>
        /// <para>
        /// Invalid NodeId strings will return false and set value to NodeId.Null, e.g.
        /// "HelloWorld",
        /// "nsu=http://opcfoundation.org/UA/;i=1234"
        /// </para>
        /// </summary>
        /// <param name="text">The NodeId value as a string.</param>
        /// <param name="value">The parsed NodeId if successful, otherwise NodeId.Null.</param>
        /// <returns>True if the parsing was successful, false otherwise.</returns>
        public static bool TryParse(string text, out NodeId value)
        {
            return InternalTryParse(text, false, out value, out _);
        }

        /// <summary>
        /// Tries to parse a NodeId String and returns a NodeId object if successful.
        /// </summary>
        /// <param name="text">The NodeId value as a string.</param>
        /// <param name="value">The parsed NodeId if successful, otherwise NodeId.Null.</param>
        /// <param name="error">Error that occurred</param>
        /// <returns></returns>
        public static bool TryParse(string text, out NodeId value, out NodeIdParseError error)
        {
            return InternalTryParse(text, false, out value, out error);
        }

        /// <summary>
        /// Tries to parse a NodeId formatted as a string and converts it to a NodeId.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="text">The text to parse.</param>
        /// <param name="value">The parsed NodeId if successful, otherwise NodeId.Null.</param>
        /// <returns>True if the parsing was successful, false otherwise.</returns>
        public static bool TryParse(
            IServiceMessageContext context,
            string text,
            out NodeId value)
        {
            return InternalTryParseWithContext(context, text, null, out value, out _);
        }

        /// <summary>
        /// Tries to parse a NodeId formatted as a string and converts it to a NodeId.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="text">The text to parse.</param>
        /// <param name="options">The options to use when parsing a NodeId.</param>
        /// <param name="value">The parsed NodeId if successful, otherwise NodeId.Null.</param>
        /// <returns>True if the parsing was successful, false otherwise.</returns>
        public static bool TryParse(
            IServiceMessageContext context,
            string text,
            NodeIdParsingOptions options,
            out NodeId value)
        {
            return InternalTryParseWithContext(context, text, options, out value, out _);
        }

        /// <summary>
        /// Tries to parse a NodeId formatted as a string and converts it to a NodeId.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="text">The text to parse.</param>
        /// <param name="options">The options to use when parsing a NodeId.</param>
        /// <param name="value">The parsed NodeId if successful, otherwise NodeId.Null.</param>
        /// <param name="error">Error information</param>
        /// <returns>True if the parsing was successful, false otherwise.</returns>
        public static bool TryParse(
            IServiceMessageContext context,
            string text,
            NodeIdParsingOptions options,
            out NodeId value,
            out NodeIdParseError error)
        {
            return InternalTryParseWithContext(context, text, options, out value, out error);
        }

        /// <summary>
        /// Internal try parse method that returns error message on failure.
        /// </summary>
        /// <param name="text">The NodeId value as string.</param>
        /// <param name="namespaceSet">If the namespaceUri was already set.</param>
        /// <param name="value">The parsed NodeId if successful, otherwise NodeId.Null.</param>
        /// <param name="error">Error message if parsing fails.</param>
        /// <returns>True if parsing was successful, false otherwise.</returns>
        internal static bool InternalTryParse(
            string text,
            bool namespaceSet,
            out NodeId value,
            out NodeIdParseError error)
        {
            error = NodeIdParseError.None;
            value = Null;

            try
            {
                if (string.IsNullOrEmpty(text))
                {
                    value = Null;
                    return true;
                }

                ushort namespaceIndex = 0;
                bool namespaceUriSpecified = namespaceSet; // Track if nsu= was used (from ExpandedNodeId)

                // parse the namespace index if present.
                if (text.StartsWith("ns=", StringComparison.Ordinal))
                {
                    int index = text.IndexOf(';', StringComparison.Ordinal);

                    if (index == -1)
                    {
                        error = NodeIdParseError.InvalidNamespaceIndex;
                        return false;
                    }

                    if (!ushort.TryParse(
                        text[3..index],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out namespaceIndex))
                    {
                        error = NodeIdParseError.InvalidNamespaceIndex;
                        return false;
                    }

                    text = text[(index + 1)..];
                }

                // parse numeric node identifier.
                if (text.StartsWith("i=", StringComparison.Ordinal))
                {
                    if (uint.TryParse(
                        text[2..],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out uint numericId))
                    {
                        value = new NodeId(numericId, namespaceIndex);
                        return true;
                    }
                    error = NodeIdParseError.InvalidIdentifier;
                    return false;
                }

                // parse string node identifier.
                if (text.StartsWith("s=", StringComparison.Ordinal))
                {
                    value = new NodeId(text[2..], namespaceIndex);
                    return true;
                }

                // parse guid node identifier.
                if (text.StartsWith("g=", StringComparison.Ordinal))
                {
                    if (Guid.TryParse(text[2..], out Guid guidId))
                    {
                        value = new NodeId(guidId, namespaceIndex);
                        return true;
                    }
                    error = NodeIdParseError.InvalidIdentifier;
                    return false;
                }

                // parse opaque node identifier.
                if (text.StartsWith("b=", StringComparison.Ordinal))
                {
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(text[2..]);
                        value = new NodeId(bytes, namespaceIndex);
                        return true;
                    }
                    catch
                    {
                        error = NodeIdParseError.InvalidIdentifier;
                        return false;
                    }
                }

                // parse the namespace index if present.
                if (text.StartsWith("nsu=", StringComparison.Ordinal))
                {
                    error = NodeIdParseError.InvalidNamespaceUri;
                    return false;
                }

                // Allow implicit string identifier only if namespace URI was
                // specified (from ExpandedNodeId)
                // Do not allow it if only namespace index (ns=) was specified
                if (namespaceUriSpecified)
                {
                    value = new NodeId(text, namespaceIndex);
                    return true;
                }

                error = NodeIdParseError.IdentifierMissing;
                return false;
            }
            catch
            {
                error = NodeIdParseError.Unexpected;
                return false;
            }
        }

        /// <summary>
        /// <para>
        /// Formats a NodeId as a string.
        /// <br/></para>
        /// <para>
        /// An example of this would be:
        /// <br/></para>
        /// <para>
        /// NodeId = "hello123"<br/>
        /// NamespaceId = 1;<br/>
        /// <br/> This would translate into:<br/>
        /// ns=1;s=hello123
        /// <br/></para>
        /// </summary>
        internal string Format(IFormatProvider formatProvider)
        {
            var buffer = new StringBuilder();
            Format(formatProvider, buffer);
            return buffer.ToString();
        }

        /// <summary>
        /// Formats the NodeId as a string and appends it to the buffer.
        /// </summary>
        private void Format(IFormatProvider formatProvider, StringBuilder buffer)
        {
            Format(formatProvider, buffer, IdentifierAsString, IdType, NamespaceIndex);
        }

        /// <summary>
        /// Formats the NodeId as a string and appends it to the buffer.
        /// </summary>
        public static void Format(StringBuilder buffer, NodeId nodeId)
        {
            Format(
                CultureInfo.InvariantCulture,
                buffer,
                nodeId);
        }

        /// <summary>
        /// Formats the NodeId as a string and appends it to the buffer.
        /// </summary>
        public static void Format(
            IFormatProvider formatProvider,
            StringBuilder buffer,
            NodeId nodeId)
        {
            Format(
                formatProvider,
                buffer,
                nodeId.IdentifierAsString,
                nodeId.IdType,
                nodeId.NamespaceIndex);
        }

        /// <summary>
        /// Formats the NodeId as a string and appends it to the buffer.
        /// </summary>
        public static void Format(
            StringBuilder buffer,
            string identifierAsString,
            IdType identifierType,
            ushort namespaceIndex)
        {
            Format(
                CultureInfo.InvariantCulture,
                buffer,
                identifierAsString,
                identifierType,
                namespaceIndex);
        }

        /// <summary>
        /// Formats the NodeId as a string and appends it to the buffer.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static void Format(
            IFormatProvider formatProvider,
            StringBuilder buffer,
            string identifierAsString,
            IdType identifierType,
            ushort namespaceIndex)
        {
            if (namespaceIndex != 0)
            {
                buffer.AppendFormat(formatProvider, "ns={0};", namespaceIndex);
            }

            // add identifier type prefix.
            switch (identifierType)
            {
                case IdType.Numeric:
                    buffer.Append("i=");
                    break;
                case IdType.String:
                    buffer.Append("s=");
                    break;
                case IdType.Guid:
                    buffer.Append("g=");
                    break;
                case IdType.Opaque:
                    buffer.Append("b=");
                    break;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected IdType value {identifierType}.");
            }

            // add identifier.
            buffer.Append(identifierAsString);
        }

        /// <summary>
        /// Returns the string representation of a NodeId.
        /// </summary>
        /// <remarks>
        /// Returns the Node represented as a String. This is the same as calling
        /// <see cref="Format(IFormatProvider)"/>.
        /// </remarks>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// <para>Converts a node id to an expanded node id using a namespace table.
        /// </para>
        /// <para>
        /// Returns an ExpandedNodeId based on the NodeId requested in the parameters.
        /// If the namespaceTable is specified then the relevant namespace will be
        /// returned from the namespaceTable collection which is also passed in as
        /// a parameter.
        /// </para>
        /// </summary>
        /// <returns>null, if the <i>nodeId</i> parameter is null. Otherwise an
        /// ExpandedNodeId will be returned for the specified nodeId</returns>
        /// <param name="nodeId">The NodeId to return, wrapped in within the ExpandedNodeId.</param>
        /// <param name="namespaceTable">The namespace tables collection that may be used
        /// to retrieve the namespace from that the specified NodeId belongs to</param>
        public static ExpandedNodeId ToExpandedNodeId(NodeId nodeId, NamespaceTable namespaceTable)
        {
            if (nodeId.IsNull)
            {
                return default;
            }

            var expandedId = new ExpandedNodeId(nodeId);

            if (nodeId.NamespaceIndex > 0)
            {
                string uri = namespaceTable.GetString(nodeId.NamespaceIndex);

                if (uri != null)
                {
                    return expandedId.WithNamespaceUri(uri);
                }
            }

            return expandedId;
        }

        /// <summary>
        /// Updates the namespace index.
        /// </summary>
        [Pure]
        public NodeId WithNamespaceIndex(ushort value)
        {
            if (IsNull)
            {
                // Makes no sense to update a null node with namespace index
                return this;
            }
            return IdType switch
            {
                // TODO: avoid recalculation of hashcode
                IdType.String => new NodeId(StringIdentifier, value),
                IdType.Guid => new NodeId(GuidIdentifier, value),
                IdType.Opaque => new NodeId(OpaqueIdentifer, value),
                _ => new NodeId(NumericIdentifier, value)
            };
        }

        /// <summary>
        /// Updates the namespace index.
        /// </summary>
        [Obsolete("NodeId is a readonly struct You must store the returned value. " +
            "Use WithNamespaceIndex to use clearer semantics.")]
        public NodeId SetNamespaceIndex(ushort value)
        {
            return WithNamespaceIndex(value);
        }

        /// <summary>
        /// Updates the identifier.
        /// </summary>
        [Pure]
        public NodeId WithIdentifier(uint value)
        {
            return new NodeId(value, NamespaceIndex);
        }

        /// <summary>
        /// Updates the identifier.
        /// </summary>
        [Pure]
        public NodeId WithIdentifier(string value)
        {
            return new NodeId(value, NamespaceIndex);
        }

        /// <summary>
        /// Updates the identifier.
        /// </summary>
        [Pure]
        public NodeId WithIdentifier(byte[] value)
        {
            return new NodeId(value, NamespaceIndex);
        }

        /// <summary>
        /// Updates the identifier.
        /// </summary>
        [Pure]
        public NodeId WithIdentifier(Guid value)
        {
            return new NodeId(value, NamespaceIndex);
        }

        /// <summary>
        /// Updates the identifier.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        [Obsolete("NodeId is a readonly struct You must store the returned value. " +
            "Better create NodeId with typed identifier values instead.")]
        internal NodeId SetIdentifier(IdType idType, object value)
        {
            return value switch
            {
                uint numeric => WithIdentifier(numeric),
                byte[] bytes => WithIdentifier(bytes),
                string str => WithIdentifier(str),
                Guid guid => WithIdentifier(guid),
                null => new NodeId(idType),
                _ => throw ServiceResultException.Unexpected(
                    $"Unexpected value type {value.GetType()}.")
            };
        }

        /// <inheritdoc/>
        public int CompareTo(NodeId nodeId)
        {
            if (IsNull)
            {
                return nodeId.IsNull ? 0 : 1; // nodeId is greater than null
            }

            // check for different namespace.
            if (NamespaceIndex != nodeId.NamespaceIndex)
            {
                return NamespaceIndex > nodeId.NamespaceIndex ? -1 : +1;
            }

            // check for different id type.
            if (IdType != nodeId.IdType)
            {
                return IdType > nodeId.IdType ? -1 : +1;
            }

            return IdType switch
            {
                IdType.String =>
                    string.CompareOrdinal(StringIdentifier, nodeId.StringIdentifier),
                IdType.Numeric =>
                    NumericIdentifier.CompareTo(nodeId.NumericIdentifier),
                IdType.Guid =>
                    GuidIdentifier.CompareTo(nodeId.GuidIdentifier),
                IdType.Opaque =>
                    OpaqueIdentifer.SequenceCompareTo(nodeId.OpaqueIdentifer),
                _ => -1
            };
        }

        /// <inheritdoc/>
        public int CompareTo(ExpandedNodeId nodeId)
        {
            if (nodeId.IsAbsolute)
            {
                return -1;
            }
            return CompareTo(nodeId.InnerNodeId);
        }

        /// <inheritdoc/>
        public int CompareTo(string obj)
        {
            if (IsNull)
            {
                return string.IsNullOrEmpty(obj) ? 0 : 1;
            }
            if (NamespaceIndex != 0 || IdType != IdType.String)
            {
                return -1;
            }
            return string.CompareOrdinal(StringIdentifier, obj);
        }

        /// <inheritdoc/>
        public int CompareTo(uint obj)
        {
            if (IsNull)
            {
                return obj == 0 ? 0 : 1;
            }
            if (NamespaceIndex != 0 || IdType != IdType.Numeric)
            {
                return -1;
            }
            return NumericIdentifier.CompareTo(obj);
        }

        /// <inheritdoc/>
        public int CompareTo(Guid obj)
        {
            if (IsNull)
            {
                return obj == Guid.Empty ? 0 : 1;
            }
            if (NamespaceIndex != 0 || IdType != IdType.Guid)
            {
                return -1;
            }
            return GuidIdentifier.CompareTo(obj);
        }

        /// <inheritdoc/>
        public int CompareTo(byte[] obj)
        {
            if (IsNull)
            {
                return obj == null || obj.Length == 0 ? 0 : 1;
            }
            if (NamespaceIndex != 0 || IdType != IdType.Opaque)
            {
                return -1;
            }
            return OpaqueIdentifer.SequenceCompareTo(obj ?? []);
        }

        /// <inheritdoc/>
        public int CompareTo(object obj)
        {
            // Needed for filter operators - do not remove
            return obj switch
            {
                null => IsNull ? 0 : -1,
                int n => n < 0 ? -1 : CompareTo((uint)n),
                uint n => CompareTo(n),
                Guid g => CompareTo(g),
                Uuid g => CompareTo(g.Guid),
                string s => CompareTo(s),
                byte[] b => CompareTo(b),
                ExpandedNodeId e => CompareTo(e),
                NodeId nodeId => CompareTo(nodeId),
                SerializableNodeId s => CompareTo(s.Value),
                SerializableExpandedNodeId se => CompareTo(se.Value),
                _ => -1
            };
        }

        /// <inheritdoc/>
        public static bool operator >(NodeId value1, NodeId value2)
        {
            return value1.CompareTo(value2) > 0;
        }

        /// <inheritdoc/>
        public static bool operator <(NodeId value1, NodeId value2)
        {
            return value1.CompareTo(value2) < 0;
        }

        /// <inheritdoc/>
        public static bool operator >=(NodeId left, NodeId right)
        {
            return right.CompareTo(left) <= 0;
        }

        /// <inheritdoc/>
        public static bool operator <=(NodeId left, NodeId right)
        {
            return left.CompareTo(right) <= 0;
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
        public override bool Equals(object obj)
        {
            return obj switch
            {
                null => IsNull,
                int n => n >= 0 && Equals((uint)n),
                uint n => Equals(n),
                Guid g => Equals(g),
                byte[] b => Equals(b),
                string s => Equals(s),
                ExpandedNodeId expandedNodeId => Equals(expandedNodeId),
                NodeId n => Equals(n),
                SerializableNodeId s => Equals(s.Value),
                SerializableExpandedNodeId se => Equals(se.Value),
                _ => base.Equals(obj)
            };
        }

        /// <inheritdoc/>
        public bool Equals(NodeId other)
        {
            if (other.NamespaceIndex != NamespaceIndex)
            {
                return false;
            }
            bool isNull1 = IsNull;
            bool isNull2 = other.IsNull;
            if (isNull1 || isNull2)
            {
                return isNull1 == isNull2;
            }
            if (other.IdType != IdType)
            {
                return false;
            }
            switch (IdType)
            {
                case IdType.Numeric:
                    return NumericIdentifier == other.NumericIdentifier;
                case IdType.String:
                    return string.Equals(
                        StringIdentifier,
                        other.StringIdentifier,
                        StringComparison.Ordinal);
                case IdType.Guid:
                    return GuidIdentifier == other.GuidIdentifier;
                case IdType.Opaque:
                    return ByteStringEqualityComparer.Default.Equals(
                        OpaqueIdentifer,
                        other.OpaqueIdentifer);
            }
            return false;
        }

        /// <inheritdoc/>
        public bool Equals(ExpandedNodeId other)
        {
            if (other.IsAbsolute)
            {
                return false;
            }
            return Equals(other.InnerNodeId);
        }

        /// <inheritdoc/>
        public bool Equals(uint other)
        {
            if (NamespaceIndex != 0 || IdType != IdType.Numeric)
            {
                return false;
            }
            return NumericIdentifier == other;
        }

        /// <inheritdoc/>
        public bool Equals(Guid other)
        {
            if (NamespaceIndex != 0 || IdType != IdType.Guid)
            {
                return false;
            }
            return GuidIdentifier == other;
        }

        /// <inheritdoc/>
        public bool Equals(byte[] other)
        {
            if (NamespaceIndex != 0 || IdType != IdType.Opaque)
            {
                return false;
            }
            if (other == null || other.Length == 0)
            {
                return IsNull;
            }
            return ByteStringEqualityComparer.Default.Equals(
                OpaqueIdentifer,
                other);
        }

        /// <inheritdoc/>
        public bool Equals(string other)
        {
            if (NamespaceIndex != 0 || IdType != IdType.String)
            {
                return false;
            }
            if (string.IsNullOrEmpty(other))
            {
                return IsNull;
            }
            return string.Equals(
                StringIdentifier,
                other,
                StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
#if NO_HASH_CACHE
            if (IsNull)
            {
                return 0;
            }

            var hashCode = new HashCode();
            hashCode.Add(NamespaceIndex);
            hashCode.Add(IdType);
            switch (IdType)
            {
                case IdType.String:
                    hashCode.Add(StringIdentifier);
                    break;
                case IdType.Guid:
                    hashCode.Add(GuidIdentifier);
                    break;
                case IdType.Opaque:
#if NET6_0_OR_GREATER
                    hashCode.AddBytes(OpaqueIdentifer);
#else
                    foreach (byte id in OpaqueIdentifer)
                    {
                        hashCode.Add(id);
                    }
#endif
                    break;
                default:
                    hashCode.Add(NumericIdentifier);
                    break;
            }
            return hashCode.ToHashCode();
#else
            return (int)m_inner.Numeric ^ (m_inner.NamespaceIdx >> 16);
#endif
        }

        /// <inheritdoc/>
        public static bool operator ==(NodeId value1, object value2)
        {
            return value1.CompareTo(value2) == 0;
        }

        /// <inheritdoc/>
        public static bool operator !=(NodeId value1, object value2)
        {
            return value1.CompareTo(value2) != 0;
        }

        /// <inheritdoc/>
        public static bool operator ==(NodeId value1, NodeId value2)
        {
            return value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator !=(NodeId value1, NodeId value2)
        {
            return !value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator ==(NodeId value1, ExpandedNodeId value2)
        {
            return value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator !=(NodeId value1, ExpandedNodeId value2)
        {
            return !value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator ==(NodeId value1, uint value2)
        {
            return value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator !=(NodeId value1, uint value2)
        {
            return !value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator ==(NodeId value1, byte[] value2)
        {
            return value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator !=(NodeId value1, byte[] value2)
        {
            return !value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator ==(NodeId value1, Guid value2)
        {
            return value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator !=(NodeId value1, Guid value2)
        {
            return !value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator ==(NodeId value1, string value2)
        {
            return value1.Equals(value2);
        }

        /// <inheritdoc/>
        public static bool operator !=(NodeId value1, string value2)
        {
            return !value1.Equals(value2);
        }

        /// <summary>
        /// Identifier as bytes
        /// </summary>
        internal byte[] OpaqueIdentifer =>
            (byte[])m_identifier ?? [];

        /// <summary>
        /// Identifier as string
        /// </summary>
        internal string StringIdentifier =>
            (string)m_identifier ?? string.Empty;

        /// <summary>
        /// Identifier as numberic
        /// </summary>
        internal uint NumericIdentifier =>
            m_identifier == null ? m_inner.Numeric : 0;

        /// <summary>
        /// Identifier as Guid
        /// </summary>
        internal Guid GuidIdentifier =>
            m_identifier == null ? Guid.Empty : (Guid)m_identifier;

        /// <summary>
        /// The node identifier.
        /// </summary>
        /// <remarks>
        /// Returns the Id in its native format, i.e. UInt, GUID, String etc.
        /// </remarks>
        /// <exception cref="ServiceResultException"></exception>
        [Obsolete("Use TryGetIdentifier<T> to get strongly typed identifier values or " +
            "consider using IdentifierAsString if you want to stringify the identifier.")]
        public object Identifier => IdType switch
        {
            IdType.Numeric => NumericIdentifier,
            IdType.String => StringIdentifier,
            IdType.Guid => GuidIdentifier,
            IdType.Opaque => OpaqueIdentifer,
            _ => throw ServiceResultException.Unexpected(
                $"Unexpected IdType value {IdType}.")
        };

        /// <summary>
        /// Returns a string version of just the identifier
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public string IdentifierAsString => IdType switch
        {
            IdType.Numeric => NumericIdentifier.ToString(CultureInfo.InvariantCulture),
            IdType.String => StringIdentifier,
            IdType.Guid => GuidIdentifier.ToString(),
            IdType.Opaque => Convert.ToBase64String(OpaqueIdentifer),
            _ => throw ServiceResultException.Unexpected(
                $"Unexpected IdType value {IdType}.")
        };

        /// <summary>
        /// Try get the numeric node identifier.
        /// </summary>
        public bool TryGetIdentifier(out uint identifier)
        {
            if (IdType == IdType.Numeric)
            {
                identifier = NumericIdentifier;
                return true;
            }
            identifier = default;
            return false;
        }

        /// <summary>
        /// Try get the opque node identifier.
        /// </summary>
        public bool TryGetIdentifier(out byte[] identifier)
        {
            if (IdType == IdType.Opaque)
            {
                identifier = OpaqueIdentifer;
                return true;
            }
            identifier = default;
            return false;
        }

        /// <summary>
        /// Try get the string node identifier.
        /// </summary>
        public bool TryGetIdentifier(out string identifier)
        {
            if (IdType == IdType.String)
            {
                identifier = StringIdentifier;
                return true;
            }
            identifier = default;
            return false;
        }

        /// <summary>
        /// Try get the Guid node identifier.
        /// </summary>
        public bool TryGetIdentifier(out Guid identifier)
        {
            if (IdType == IdType.Guid)
            {
                identifier = GuidIdentifier;
                return true;
            }
            identifier = default;
            return false;
        }

        /// <summary>
        /// Whether the object represents a Null NodeId.
        /// see https://reference.opcfoundation.org/Core/Part3/v105/docs/8.2.4
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public bool IsNull => IdType switch
        {
            _ when NamespaceIndex != 0 => false, // null identifiers allowed in ns != 0
            IdType.Numeric => NumericIdentifier == 0,
            IdType.String => string.IsNullOrEmpty(StringIdentifier),
            IdType.Guid => GuidIdentifier == Guid.Empty,
            IdType.Opaque => OpaqueIdentifer.Length == 0,
            _ => false
        };

        /// <summary>
        /// Get namespace index for id or throw if not found.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static int GetNamespaceIndex(string namespaceUri, NamespaceTable namespaceTable)
        {
            int index = namespaceTable == null ? -1 : namespaceTable.GetIndex(namespaceUri);
            if (index < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "NamespaceUri ({0}) is not in the namespace table.",
                    namespaceUri);
            }
            return index;
        }

        /// <summary>
        /// Trick the runtime to layout the struct (which is forced
        /// to auto layout due to types used) in the order we want.
        /// </summary>
        internal struct Inner
        {
            public uint Numeric;
            public ushort NamespaceIdx;
            public byte Type;

            /// <summary> Implicit padding </summary>
            public byte Reserved;
        }

#pragma warning disable IDE0032 // Use auto property
        private readonly object m_identifier;
        private readonly Inner m_inner;
#pragma warning restore IDE0032 // Use auto property
    }

    /// <summary>
    /// A collection of NodeIds.
    /// </summary>
    /// <remarks>
    /// Provides a strongly-typed collection of <see cref="NodeId"/>.
    /// </remarks>
    [CollectionDataContract(
        Name = "ListOfNodeId",
        Namespace = Namespaces.OpcUaXsd,
        ItemName = "NodeId")]
    public class NodeIdCollection : List<NodeId>, ICloneable
    {
        /// <inheritdoc/>
        public NodeIdCollection()
        {
        }

        /// <inheritdoc/>
        public NodeIdCollection(IEnumerable<NodeId> collection)
            : base(collection)
        {
        }

        /// <inheritdoc/>
        public NodeIdCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        public static NodeIdCollection ToNodeIdCollection(NodeId[] values)
        {
            return values != null ? [.. values] : [];
        }

        /// <inheritdoc/>
        public static implicit operator NodeIdCollection(NodeId[] values)
        {
            return ToNodeIdCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = new NodeIdCollection(Count);

            foreach (NodeId element in this)
            {
                clone.Add(element);
            }

            return clone;
        }
    }

    /// <summary>
    /// Options that affect how a NodeId string is parsed.
    /// </summary>
    public class NodeIdParsingOptions
    {
        /// <summary>
        /// If TRUE, the parser adds unknown URIs to the namespace or server table.
        /// </summary>
        public bool UpdateTables { get; set; }

        /// <summary>
        /// The mapping from serialized namespace indexes to the indexes used in the context.
        /// </summary>
        public ushort[] NamespaceMappings { get; set; }

        /// <summary>
        /// The mapping from serialized server indexes to the indexes used in the context.
        /// </summary>
        public ushort[] ServerMappings { get; set; }
    }

    /// <summary>
    /// Helper to allow data contract serialization of NodeId
    /// </summary>
    [DataContract(
        Name = "NodeId",
        Namespace = Namespaces.OpcUaXsd)]
    public class SerializableNodeId : ISurrogateFor<NodeId>
    {
        /// <inheritdoc/>
        public SerializableNodeId()
        {
            Value = default;
        }

        /// <inheritdoc/>
        public SerializableNodeId(NodeId value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public NodeId Value { get; private set; }

        /// <inheritdoc/>
        public object GetValue()
        {
            return Value;
        }

        /// <summary>
        /// The node identifier formatted as a URI.
        /// </summary>
        [DataMember(Name = "Identifier", Order = 1)]
        internal string IdentifierText
        {
            get => Value.Format(CultureInfo.InvariantCulture);
            set => Value = NodeId.Parse(value);
        }
    }

    /// <summary>
    /// Node id parse errors
    /// </summary>
    public enum NodeIdParseError
    {
        /// <summary>
        /// No error
        /// </summary>
        None,

        /// <summary>
        /// Unexpected error during parsing
        /// </summary>
        Unexpected,

        /// <summary>
        /// Invalid server index
        /// </summary>
        InvalidServerIndex,

        /// <summary>
        /// Invalid server format
        /// </summary>
        InvalidServerUriFormat,

        /// <summary>
        /// No server uri mapping
        /// </summary>
        NoServerUriMapping,

        /// <summary>
        /// Invalid namespace uri
        /// </summary>
        InvalidNamespaceUri,

        /// <summary>
        /// Invalid namespace format
        /// </summary>
        InvalidNamespaceFormat,

        /// <summary>
        /// Namespace mapping missing
        /// </summary>
        NoNamespaceMapping,

        /// <summary>
        /// Invalid identifier
        /// </summary>
        InvalidIdentifier,

        /// <summary>
        /// Identifier missing
        /// </summary>
        IdentifierMissing,

        /// <summary>
        /// Invalid identifier type
        /// </summary>
        InvalidIdentifierType,

        /// <summary>
        /// Invalid namespace index
        /// </summary>
        InvalidNamespaceIndex
    }
}
