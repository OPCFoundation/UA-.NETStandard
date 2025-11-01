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

// define to enable checks for a null NodeId modification
// some tests are failing with this enabled, only turn on to catch issues
// #define IMMUTABLENULLNODEID

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;

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
    /// <b>Important:</b> Keep in mind that the actual ID's of nodes should be unique such that no two
    /// nodes within an address-space share the same ID's.
    /// </note>
    /// <para>
    /// The NodeId can be assigned to a particular namespace index. This index is merely just a number and does
    /// not represent some index within a collection that this node has any knowledge of. The assumption is
    /// that the host of this object will manage that directly.
    /// <br/></para>
    /// </remarks>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class NodeId : IComparable, IFormattable, IEquatable<NodeId>, ICloneable
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        /// <remarks>
        /// Creates a new instance of the class which will have the default values. The actual
        /// Node Id will need to be defined as this constructor does not specify the id.
        /// </remarks>
        public NodeId()
        {
            Initialize();
        }

        /// <summary>
        /// Creates a deep copy of the value.
        /// </summary>
        /// <remarks>
        /// Creates a new NodeId by copying the properties of the node specified in the parameter.
        /// </remarks>
        /// <param name="value">The NodeId object whose properties will be copied.</param>
        /// <exception cref="ArgumentNullException">Thrown when <i>value</i> is null</exception>
        public NodeId(NodeId value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            NamespaceIndex = value.NamespaceIndex;
            IdType = value.IdType;
            m_identifier = Utils.Clone(value.m_identifier);
        }

        /// <summary>
        /// Initializes a numeric node identifier.
        /// </summary>
        /// <remarks>
        /// Creates a new NodeId that will have a numeric (unsigned-int) id
        /// </remarks>
        /// <param name="value">The numeric value of the id</param>
        public NodeId(uint value)
        {
            NamespaceIndex = 0;
            IdType = IdType.Numeric;
            m_identifier = value;
        }

        /// <summary>
        /// Initializes a numeric node identifier with a namespace index.
        /// </summary>
        /// <remarks>
        /// Creates a new NodeId that will use a numeric (unsigned int) for its Id, but also
        /// specifies which namespace this node should belong to.
        /// </remarks>
        /// <param name="value">The new (numeric) Id for the node being created</param>
        /// <param name="namespaceIndex">The index of the namespace that this node should belong to</param>
        /// <seealso cref="SetNamespaceIndex"/>
        public NodeId(uint value, ushort namespaceIndex)
        {
            NamespaceIndex = namespaceIndex;
            IdType = IdType.Numeric;
            m_identifier = value;
        }

        /// <summary>
        /// Initializes a string node identifier with a namespace index.
        /// </summary>
        /// <remarks>
        /// Creates a new NodeId that will use a string for its Id, but also
        /// specifies if the Id is a URI, and which namespace this node belongs to.
        /// </remarks>
        /// <param name="value">The new (string) Id for the node being created</param>
        /// <param name="namespaceIndex">The index of the namespace that this node belongs to</param>
        public NodeId(string value, ushort namespaceIndex)
        {
            NamespaceIndex = namespaceIndex;
            IdType = IdType.String;
            m_identifier = value;
        }

        /// <summary>
        /// Initializes a guid node identifier.
        /// </summary>
        /// <remarks>
        /// Creates a new node whose Id will be a <see cref="Guid"/>.
        /// </remarks>
        /// <param name="value">The new Guid value of this nodes Id.</param>
        public NodeId(Guid value)
        {
            NamespaceIndex = 0;
            IdType = IdType.Guid;
            m_identifier = value;
        }

        /// <summary>
        /// Initializes a guid node identifier.
        /// </summary>
        /// <remarks>
        /// Creates a new node whose Id will be a <see cref="Guid"/>.
        /// </remarks>
        /// <param name="value">The new Guid value of this nodes Id.</param>
        /// <param name="namespaceIndex">The index of the namespace that this node belongs to</param>
        public NodeId(Guid value, ushort namespaceIndex)
        {
            NamespaceIndex = namespaceIndex;
            IdType = IdType.Guid;
            m_identifier = value;
        }

        /// <summary>
        /// Initializes an opaque node identifier.
        /// </summary>
        /// <remarks>
        /// Creates a new node whose Id will be a series of <see cref="byte"/>.
        /// </remarks>
        /// <param name="value">An array of <see cref="byte"/> that will become this Node's ID</param>
        public NodeId(byte[] value)
        {
            NamespaceIndex = 0;
            IdType = IdType.Opaque;
            m_identifier = null;

            if (value != null)
            {
                byte[] copy = new byte[value.Length];
                Array.Copy(value, copy, value.Length);
                m_identifier = copy;
            }
        }

        /// <summary>
        /// Initializes an opaque node identifier with a namespace index.
        /// </summary>
        /// <remarks>
        /// Creates a new node whose Id will be a series of <see cref="byte"/>, while specifying
        /// the index of the namespace that this node belongs to.
        /// </remarks>
        /// <param name="value">An array of <see cref="byte"/> that will become this Node's ID</param>
        /// <param name="namespaceIndex">The index of the namespace that this node belongs to</param>
        public NodeId(byte[] value, ushort namespaceIndex)
        {
            NamespaceIndex = namespaceIndex;
            IdType = IdType.Opaque;
            m_identifier = null;

            if (value != null)
            {
                byte[] copy = new byte[value.Length];
                Array.Copy(value, copy, value.Length);
                m_identifier = copy;
            }
        }

        /// <summary>
        /// Initializes a node id by parsing a node id string.
        /// </summary>
        /// <remarks>
        /// Creates a new node with a String id.
        /// </remarks>
        /// <param name="text">The string id of this new node</param>
        public NodeId(string text)
        {
            NodeId nodeId = Parse(text);

            NamespaceIndex = nodeId.NamespaceIndex;
            IdType = nodeId.IdType;
            m_identifier = nodeId.Identifier;
        }

        /// <summary>
        /// Initializes a node identifier with a namespace index.
        /// </summary>
        /// <remarks>
        /// Throws an exception if the identifier type is not supported.
        /// </remarks>
        /// <param name="value">The identifier</param>
        /// <param name="namespaceIndex">The index of the namespace that qualifies the node</param>
        public NodeId(object value, ushort namespaceIndex)
        {
            NamespaceIndex = namespaceIndex;

            if (value is uint)
            {
                SetIdentifier(IdType.Numeric, value);
                return;
            }

            if (value is null or string)
            {
                SetIdentifier(IdType.String, value);
                return;
            }

            if (value is Guid)
            {
                SetIdentifier(IdType.Guid, value);
                return;
            }

            if (value is Uuid uuid)
            {
                SetIdentifier(IdType.Guid, (Guid)uuid);
                return;
            }

            if (value is byte[])
            {
                SetIdentifier(IdType.Opaque, value);
                return;
            }

            throw new ArgumentException("Identifier type not supported.", nameof(value));
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        private void Initialize()
        {
            NamespaceIndex = 0;
            IdType = IdType.Numeric;
            m_identifier = null;
        }

        /// <summary>
        /// Parses an NodeId formatted as a string and converts it a NodeId.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="text">The text to parse.</param>
        /// <param name="options">The options to use when parsing a NodeId.</param>
        /// <returns>The NodeId.</returns>
        /// <exception cref="ServiceResultException">Thrown if the namespace URI is not in the namespace table.</exception>
        public static NodeId Parse(
            IServiceMessageContext context,
            string text,
            NodeIdParsingOptions options = null)
        {
            if (!InternalTryParseWithContext(context, text, options, out NodeId value, out string errorMessage))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    errorMessage ?? Utils.Format("Cannot parse node id text: '{0}'", text));
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
        /// <param name="errorMessage">Error message if parsing fails.</param>
        /// <returns>True if parsing was successful, false otherwise.</returns>
        internal static bool InternalTryParseWithContext(
            IServiceMessageContext context,
            string text,
            NodeIdParsingOptions options,
            out NodeId value,
            out string errorMessage)
        {
            errorMessage = null;
            value = Null;

            if (string.IsNullOrEmpty(text))
            {
                value = Null;
                return true;
            }

            string originalText = text;
            int namespaceIndex = 0;

            if (text.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int index = text.IndexOf(';', 4);

                if (index < 0)
                {
                    errorMessage = Utils.Format("Invalid NodeId ({0}).", originalText);
                    return false;
                }

                string namespaceUri = Utils.UnescapeUri(text.AsSpan()[4..index]);
                namespaceIndex =
                    options?.UpdateTables == true
                        ? context.NamespaceUris.GetIndexOrAppend(namespaceUri)
                        : context.NamespaceUris.GetIndex(namespaceUri);

                if (namespaceIndex < 0)
                {
                    errorMessage = Utils.Format("No mapping to NamespaceIndex for NamespaceUri ({0}).", namespaceUri);
                    return false;
                }

                text = text[(index + 1)..];
            }

            if (text.StartsWith("ns=", StringComparison.Ordinal))
            {
                int index = text.IndexOf(';', 3);

                if (index < 0)
                {
                    errorMessage = Utils.Format("Invalid ExpandedNodeId ({0}).", originalText);
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
                        errorMessage = Utils.Format("Unexpected IdType value {0}.", idType);
                        return false;
                }
            }

            errorMessage = Utils.Format("Invalid NodeId Identifier ({0}).", originalText);
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
            if (m_identifier == null)
            {
                return null;
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
                            .Append(Utils.EscapeUri(namespaceUri))
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
                        .Append((uint)m_identifier);
                    break;
                case IdType.Guid:
                    buffer.Append("g=")
                        .Append(((Guid)m_identifier).ToString());
                    break;
                case IdType.Opaque:
                    buffer.Append("b=")
                        .Append(Convert.ToBase64String((byte[])m_identifier));
                    break;
                case IdType.String:
                    buffer.Append("s=")
                        .Append(m_identifier.ToString());
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
        public static NodeId Create(
            object identifier,
            string namespaceUri,
            NamespaceTable namespaceTable)
        {
            int index = -1;

            if (namespaceTable != null)
            {
                index = namespaceTable.GetIndex(namespaceUri);
            }

            if (index < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "NamespaceUri ({0}) is not in the namespace table.",
                    namespaceUri);
            }

            return new NodeId(identifier, (ushort)index);
        }

        /// <summary>
        /// Converts an integer to a numeric node identifier.
        /// </summary>
        /// <remarks>
        /// Converts an integer to a numeric node identifier for comparisons.
        /// </remarks>
        /// <example>
        /// <code lang="C#">
        ///
        /// //create some variables
        /// uint id1 = 100, id2=101;
        /// NodeId node1;
        ///
        /// //create our node
        /// node1 = new NodeId(id1);
        ///
        /// //now to compare the node to the ids using a simple comparison and Equals:
        /// Console.WriteLine("Comparing NodeId to uint");
        /// Console.WriteLine("\tComparing 100 to 100 = [equals] {0}", node1.Equals(id1));
        /// Console.WriteLine("\tComparing 100 to 100 = [ ==   ] {0}", node1 == id1);
        /// Console.WriteLine("\tComparing 100 to 101 = [equals] {0}", node1.Equals(id2));
        /// Console.WriteLine("\tComparing 100 to 101 = [ ==   ] {0}", node1 == id2);
        ///
        /// </code>
        /// <code lang="Visual Basic">
        ///
        /// 'create some variables
        /// Dim id1 As UInt = 100
        /// Dim id2 As UInt = 102
        /// Dim node1 As NodeId
        ///
        /// 'create our node
        /// node1 = new NodeId(id1)
        ///
        /// 'now to compare the node to the ids using a simple comparison and Equals:
        /// Console.WriteLine("Comparing NodeId to uint")
        /// Console.WriteLine("   Comparing 100 to 100 = [equals] {0}", node1.Equals(id1))
        /// Console.WriteLine("   Comparing 100 to 100 = [  =   ] {0}", node1 = id1)
        /// Console.WriteLine("   Comparing 100 to 101 = [equals] {0}", node1.Equals(id2))
        /// Console.WriteLine("   Comparing 100 to 101 = [  =   ] {0}", node1 = id2)
        ///
        /// </code>
        /// <para>
        /// This produces the following output (taken from C# example):
        /// <br/></para>
        /// <para>
        /// Comparing NodeId to uint<br/>
        ///     Comparing 100 to 100 = [equals] True<br/>
        ///     Comparing 100 to 100 = [ ==   ] True<br/>
        ///     Comparing 100 to 101 = [equals] False<br/>
        ///     Comparing 100 to 101 = [ ==   ] False<br/>
        /// <br/></para>
        /// </example>
        /// <param name="value">The <see cref="uint"/> to compare this node to.</param>
        public static implicit operator NodeId(uint value)
        {
            return new NodeId(value);
        }

        /// <summary>
        /// Converts a guid to a guid node identifier.
        /// </summary>
        /// <remarks>
        /// Converts a NodeId into a Guid allowing you to compare a Node against a Guid.
        /// </remarks>
        /// <example>
        /// <code lang="C#">
        ///
        /// //define our 2 GUID ids, and then define our node to use the first id.
        /// Guid id1 = Guid.NewGuid(), id2 = Guid.NewGuid();
        /// NodeId node1 = new NodeId(id1);
        ///
        /// //now to compare the node to the guids
        /// Console.WriteLine("\n\nComparing NodeId to GUID");
        /// Console.WriteLine("\tComparing {0} to {0} = [equals] {2}", id1, id1, node1.Equals(id1));
        /// Console.WriteLine("\tComparing {0} to {0} = [ ==   ] {2}", id1, id1, node1 == id1);
        /// Console.WriteLine("\tComparing {0} to {1} = [equals] {2}", id1, id2, node1.Equals(id2));
        /// Console.WriteLine("\tComparing {0} to {1} = [ ==   ] {2}", id1, id2, node1 == id2);
        ///
        /// </code>
        /// <code lang="Visual Basic">
        ///
        /// 'define our 2 GUID ids, and then define our node to use the first id.
        /// Dim id1 As Guid = Guid.NewGuid()
        /// Dim id2 As Guid = Guid.NewGuid()
        /// Dim node1 As NodeId = new NodeId(id1)
        ///
        /// 'now to compare the node to the guids
        /// Console.WriteLine("Comparing NodeId to GUID")
        /// Console.WriteLine("  Comparing {0} to {0} = [equals] {2}", id1, id1, node1.Equals(id1));
        /// Console.WriteLine("  Comparing {0} to {0} = [  =   ] {2}", id1, id1, node1 = id1);
        /// Console.WriteLine("  Comparing {0} to {0} = [equals] {2}", id1, id2, node1.Equals(id2));
        /// Console.WriteLine("  Comparing {0} to {0} = [  =   ] {2}", id1, id2, node1 = id2);
        ///
        /// </code>
        /// <para>
        /// This produces the following output (taken from C# example):
        /// <br/></para>
        /// <para>
        /// Comparing NodeId to GUID<br/>
        ///     Comparing bbe8b5f2-0f50-4302-877f-346afb07704c to bbe8b5f2-0f50-4302-877f-346afb07704c = [equals] True<br/>
        ///     Comparing bbe8b5f2-0f50-4302-877f-346afb07704c to bbe8b5f2-0f50-4302-877f-346afb07704c = [  =   ] True<br/>
        ///     Comparing bbe8b5f2-0f50-4302-877f-346afb07704c to e707de86-4c11-4fe6-94b2-83638a9427e6 = [equals] False<br/>
        ///     Comparing bbe8b5f2-0f50-4302-877f-346afb07704c to e707de86-4c11-4fe6-94b2-83638a9427e6 = [  =   ] False<br/>
        /// <br/></para>
        /// </example>
        /// <param name="value">The <see cref="Guid"/> to compare this node to.</param>
        public static implicit operator NodeId(Guid value)
        {
            return new NodeId(value);
        }

        /// <summary>
        /// Converts a byte array to an opaque node identifier.
        /// </summary>
        /// <remarks>
        /// This operator allows you to compare a NodeId to an array of Bytes.
        /// </remarks>
        /// <example>
        /// <code lang="C#">
        ///
        /// //define our 2 Byte[] ids, and then define our node to use the first id.
        /// byte[] id1 = new byte[] { 65, 66, 67, 68, 69 };
        /// byte[] id2 = new byte[] { 97, 98, 99, 100, 101 };
        /// NodeId node1 = new NodeId(id1);
        ///
        /// //convert our bytes to string so we can display them
        /// string id1String = System.Text.ASCIIEncoding.ASCII.GetString(id1);
        /// string id2String = System.Text.ASCIIEncoding.ASCII.GetString(id2);
        ///
        /// //now to compare the node to the guids
        /// Console.WriteLine("\n\nComparing NodeId to Byte[]");
        /// Console.WriteLine("\tComparing {0} to {0} = [equals] {2}", id1String, id1String, node1.Equals(id1));
        /// Console.WriteLine("\tComparing {0} to {0} = [  =   ] {2}", id1String, id1String, node1 == id1);
        /// Console.WriteLine("\tComparing {0} to {1} = [equals] {2}", id1String, id2String, node1.Equals(id2));
        /// Console.WriteLine("\tComparing {0} to {1} = [  =   ] {2}", id1String, id2String, node1 == id2);
        ///
        /// </code>
        /// <code lang="Visual Basic">
        ///
        /// 'define our 2 Byte[] ids, and then define our node to use the first id.
        /// Dim id1 As Byte() = New Byte() { 65, 66, 67, 68, 69 }
        /// Dim id2 As Byte() = New Byte() { 97, 98, 99, 100, 101 }
        /// Dim node1 As NodeId = New NodeId(id1)
        ///
        /// 'convert our bytes to string so we can display them
        /// Dim id1String As String = System.Text.ASCIIEncoding.ASCII.GetString(id1)
        /// Dim id2String As String = System.Text.ASCIIEncoding.ASCII.GetString(id2)
        ///
        /// 'now to compare the node to the guids
        /// Console.WriteLine("Comparing NodeId to Byte()")
        /// Console.WriteLine("Comparing {0} to {0} = [equals] {2}", id1String, id1String, node1.Equals(id1))
        /// Console.WriteLine("Comparing {0} to {0} = [  =   ] {2}", id1String, id1String, node1 = id1)
        /// Console.WriteLine("Comparing {0} to {1} = [equals] {2}", id1String, id2String, node1.Equals(id2))
        /// Console.WriteLine("Comparing {0} to {1} = [  =   ] {2}", id1String, id2String, node1 = id2)
        ///
        /// </code>
        /// <para>
        /// This produces the following output (taken from C# example):
        /// <br/></para>
        /// <para>
        /// Comparing NodeId to Byte[]
        ///     Comparing ABCDE to ABCDE = [equals] True
        ///     Comparing ABCDE to ABCDE = [ ==   ] True
        ///     Comparing ABCDE to abcde = [equals] False
        ///     Comparing ABCDE to abcde = [ ==   ] False
        /// <br/></para>
        /// </example>
        /// <param name="value">The <see cref="byte"/>[] array to compare this node to</param>
        public static implicit operator NodeId(byte[] value)
        {
            return new NodeId(value);
        }

        /// <summary>
        /// Parses a node id string and initializes a node id.
        /// </summary>
        /// <remarks>
        /// Compares a Node to a String
        /// </remarks>
        /// <example>
        /// <code lang="C#">
        ///
        /// //define our 2 String ids, and then define our node to use the first id.
        /// String id1 = "Hello", id2 = "World";
        /// NodeId node1 = new NodeId(id1);
        ///
        /// //now to compare the node to the guids
        /// Console.WriteLine("\n\nComparing NodeId to String");
        /// Console.WriteLine("\tComparing {0} to {1} = [equals] {2}", id1, id1, node1.Equals(id1));
        /// Console.WriteLine("\tComparing {0} to {1} = [ ==   ] {2}", id1, id1, node1 == id1);
        /// Console.WriteLine("\tComparing {0} to {1} = [equals] {2}", id1, id2, node1.Equals(id2));
        /// Console.WriteLine("\tComparing {0} to {1} = [ ==   ] {2}", id1, id2, node1 == id2);
        ///
        ///
        /// </code>
        /// <code lang="Visual Basic">
        ///
        /// 'define our 2 String ids, and then define our node to use the first id.
        /// Dim id1 As String = "Hello"
        /// Dim id2 As String = "World"
        /// Dim node1 As NodeId = New NodeId(id1)
        ///
        /// 'now to compare the node to the guids
        /// Console.WriteLine("Comparing NodeId to String");
        /// Console.WriteLine("Comparing {0} to {1} = [equals] {2}", id1, id1, node1.Equals(id1));
        /// Console.WriteLine("Comparing {0} to {1} = [  =   ] {2}", id1, id1, node1 = id1);
        /// Console.WriteLine("Comparing {0} to {1} = [equals] {2}", id1, id2, node1.Equals(id2));
        /// Console.WriteLine("Comparing {0} to {1} = [  =   ] {2}", id1, id2, node1 = id2);
        ///
        /// </code>
        /// </example>
        /// <param name="text">The <see cref="string"/> to compare this node to.</param>
        public static implicit operator NodeId(string text)
        {
            return Parse(text);
        }

        /// <summary>
        /// Checks if the node id represents a 'Null' node id.
        /// </summary>
        /// <remarks>
        /// Returns a true/false value to indicate if the specified NodeId is null.
        /// </remarks>
        /// <param name="nodeId">The NodeId to validate</param>
        public static bool IsNull(NodeId nodeId)
        {
            if (nodeId == null)
            {
                return true;
            }

            return nodeId.IsNullNodeId;
        }

        /// <summary>
        /// Checks if the node id represents a 'Null' node id.
        /// </summary>
        /// <remarks>
        /// Returns a true/false to indicate if the specified <see cref="ExpandedNodeId"/> is null.
        /// </remarks>
        /// <param name="nodeId">The ExpandedNodeId to validate</param>
        public static bool IsNull(ExpandedNodeId nodeId)
        {
            if (nodeId == null)
            {
                return true;
            }

            return nodeId.IsNull;
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
            if (!InternalTryParse(text, false, out NodeId value, out string errorMessage))
            {
                // Check if this should be an ArgumentException based on the error message
                if (errorMessage != null && (errorMessage.Contains("namespace Uri ('nsu=')") || 
                    errorMessage.Contains("Missing valid identifier prefix")))
                {
                    throw new ArgumentException(errorMessage);
                }
                
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid,
                    errorMessage ?? Utils.Format("Cannot parse node id text: '{0}'", text));
            }
            return value;
        }

        /// <summary>
        /// Tries to parse a node id string and returns true if successful.
        /// </summary>
        /// <remarks>
        /// Tries to parse a NodeId String and returns a NodeId object if successful.
        /// Valid NodeId strings are of the form:
        ///     "i=1234", "s=HelloWorld", "g=AF469096-F02A-4563-940B-603958363B81", "b=01020304",
        ///     "ns=2;s=HelloWorld", "ns=2;i=1234", "ns=2;g=AF469096-F02A-4563-940B-603958363B81", "ns=2;b=01020304"
        /// Invalid NodeId strings will return false and set value to NodeId.Null, e.g.
        ///     "HelloWorld", "nsu=http://opcfoundation.org/UA/;i=1234"
        /// </remarks>
        /// <param name="text">The NodeId value as a string.</param>
        /// <param name="value">The parsed NodeId if successful, otherwise NodeId.Null.</param>
        /// <returns>True if the parsing was successful, false otherwise.</returns>
        public static bool TryParse(string text, out NodeId value)
        {
            return InternalTryParse(text, false, out value, out _);
        }

        /// <summary>
        /// Tries to parse a NodeId formatted as a string and converts it to a NodeId.
        /// </summary>
        /// <param name="context">The current context.</param>
        /// <param name="text">The text to parse.</param>
        /// <param name="value">The parsed NodeId if successful, otherwise NodeId.Null.</param>
        /// <param name="options">The options to use when parsing a NodeId.</param>
        /// <returns>True if the parsing was successful, false otherwise.</returns>
        public static bool TryParse(
            IServiceMessageContext context,
            string text,
            out NodeId value,
            NodeIdParsingOptions options = null)
        {
            return InternalTryParseWithContext(context, text, options, out value, out _);
        }

        /// <summary>
        /// Internal parse method.
        /// </summary>
        /// <param name="text">The NodeId value as string.</param>
        /// <param name="namespaceSet">If the namespaceUri was already set.</param>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="ArgumentException"></exception>
        internal static NodeId InternalParse(string text, bool namespaceSet)
        {
            if (!InternalTryParse(text, namespaceSet, out NodeId value, out string errorMessage))
            {
                // Check if this should be an ArgumentException based on the error message
                if (errorMessage != null && (errorMessage.Contains("namespace Uri ('nsu=')") || 
                    errorMessage.Contains("Missing valid identifier prefix")))
                {
                    throw new ArgumentException(errorMessage);
                }
                
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid,
                    errorMessage ?? Utils.Format("Cannot parse node id text: '{0}'", text));
            }
            return value;
        }

        /// <summary>
        /// Internal try parse method that returns error message on failure.
        /// </summary>
        /// <param name="text">The NodeId value as string.</param>
        /// <param name="namespaceSet">If the namespaceUri was already set.</param>
        /// <param name="value">The parsed NodeId if successful, otherwise NodeId.Null.</param>
        /// <param name="errorMessage">Error message if parsing fails.</param>
        /// <returns>True if parsing was successful, false otherwise.</returns>
        internal static bool InternalTryParse(string text, bool namespaceSet, out NodeId value, out string errorMessage)
        {
            errorMessage = null;
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
                        errorMessage = "Invalid namespace index.";
                        return false;
                    }

                    if (!ushort.TryParse(text.Substring(3, index - 3), NumberStyles.None, CultureInfo.InvariantCulture, out namespaceIndex))
                    {
                        errorMessage = "Invalid namespace index format.";
                        return false;
                    }

                    text = text[(index + 1)..];
                }

                // parse numeric node identifier.
                if (text.StartsWith("i=", StringComparison.Ordinal))
                {
                    if (uint.TryParse(text.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out uint numericId))
                    {
                        value = new NodeId(numericId, namespaceIndex);
                        return true;
                    }
                    errorMessage = Utils.Format("Invalid numeric identifier: '{0}'", text);
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
                    if (Guid.TryParse(text.Substring(2), out Guid guidId))
                    {
                        value = new NodeId(guidId, namespaceIndex);
                        return true;
                    }
                    errorMessage = Utils.Format("Invalid GUID identifier: '{0}'", text);
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
                        errorMessage = Utils.Format("Invalid base64 identifier: '{0}'", text);
                        return false;
                    }
                }

                // parse the namespace index if present.
                if (text.StartsWith("nsu=", StringComparison.Ordinal))
                {
                    errorMessage = "Invalid namespace Uri ('nsu=') for a NodeId.";
                    return false;
                }

                // Allow implicit string identifier only if namespace URI was specified (from ExpandedNodeId)
                // Do not allow it if only namespace index (ns=) was specified
                if (namespaceUriSpecified)
                {
                    value = new NodeId(text, namespaceIndex);
                    return true;
                }

                errorMessage = Utils.Format("Invalid NodeId identifier. Missing valid identifier prefix ('i=', 's=', 'g=', 'b='): '{0}'", text);
                return false;
            }
            catch (Exception e)
            {
                errorMessage = Utils.Format("Cannot parse node id text: '{0}': {1}", text, e.Message);
                return false;
            }
        }

        /// <summary>
        /// Returns an instance of a null NodeId.
        /// </summary>
        public static NodeId Null { get; } = new NodeId();

#if IMMUTABLENULLNODEID
#else
#endif

        /// <summary>
        /// Formats a node id as a string.
        /// </summary>
        /// <remarks>
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
        /// </remarks>
        private string Format(IFormatProvider formatProvider)
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
            Format(formatProvider, buffer, m_identifier, IdType, NamespaceIndex);
        }

        /// <summary>
        /// Formats the NodeId as a string and appends it to the buffer.
        /// </summary>
        public static void Format(
            StringBuilder buffer,
            object identifier,
            IdType identifierType,
            ushort namespaceIndex)
        {
            Format(
                CultureInfo.InvariantCulture,
                buffer,
                identifier,
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
            object identifier,
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
            FormatIdentifier(formatProvider, buffer, identifier, identifierType);
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
        /// Converts a node id to an expanded node id using a namespace table.
        /// </summary>
        /// <remarks>
        /// Returns an ExpandedNodeId based on the NodeId requested in the parameters. If the namespaceTable
        /// is specified then the relevant namespace will be returned from the namespaceTable collection which is
        /// also passed in as a parameter.
        /// </remarks>
        /// <returns>null, if the <i>nodeId</i> parameter is null. Otherwise an ExpandedNodeId will be returned for the specified nodeId</returns>
        /// <param name="nodeId">The NodeId to return, wrapped in within the ExpandedNodeId.</param>
        /// <param name="namespaceTable">The namespace tables collection that may be used to retrieve the namespace from that the specified NodeId belongs to</param>
        public static ExpandedNodeId ToExpandedNodeId(NodeId nodeId, NamespaceTable namespaceTable)
        {
            if (nodeId == null)
            {
                return null;
            }

            var expandedId = new ExpandedNodeId(nodeId);

            if (nodeId.NamespaceIndex > 0)
            {
                string uri = namespaceTable.GetString(nodeId.NamespaceIndex);

                if (uri != null)
                {
                    expandedId.SetNamespaceUri(uri);
                }
            }

            return expandedId;
        }

        /// <summary>
        /// Updates the namespace index.
        /// </summary>
        internal void SetNamespaceIndex(ushort value)
        {
            ValidateImmutableNodeIdIsNotModified();
            NamespaceIndex = value;
        }

        /// <summary>
        /// Updates the identifier.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal void SetIdentifier(IdType idType, object value)
        {
            ValidateImmutableNodeIdIsNotModified();
            IdType = idType;

            m_identifier = idType switch
            {
                IdType.Opaque => Utils.Clone(value),
                IdType.Guid => (Guid)value,
                IdType.Numeric or IdType.String => value,
                _ => throw ServiceResultException.Unexpected(
                    $"Unexpected IdType value {idType}.")
            };
        }

        /// <summary>
        /// Updates the identifier.
        /// </summary>
        internal void SetIdentifier(string value, IdType idType)
        {
            ValidateImmutableNodeIdIsNotModified();

            IdType = idType;
            SetIdentifier(IdType.String, value);
        }

        /// <summary>
        /// Compares the current instance to the object.
        /// </summary>
        /// <remarks>
        /// Enables this object type to be compared to other types of object.
        /// </remarks>
        /// <exception cref="ServiceResultException"></exception>
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

            ushort namespaceIndex = NamespaceIndex;
            IdType idType = IdType;
            object id = null;

            // check for expanded node ids.
            var nodeId = obj as NodeId;

            if (nodeId is not null)
            {
                if (IsNullNodeId && nodeId.IsNullNodeId)
                {
                    return 0;
                }

                namespaceIndex = nodeId.NamespaceIndex;
                idType = nodeId.IdType;
                id = nodeId.Identifier;
            }
            else
            {
                uint? uid = obj as uint?;
                int? iid = obj as int?;

                // check for numeric constants.
                if (uid != null || iid != null)
                {
                    if (namespaceIndex != 0 || idType != IdType.Numeric)
                    {
                        return -1;
                    }

                    uint id2;
                    if (iid != null && uid == null)
                    {
                        if (iid.Value < 0)
                        {
                            return +1;
                        }
                        id2 = (uint)iid.Value;
                    }
                    else
                    {
                        id2 = uid.Value;
                    }

                    uint id1 = (m_identifier as uint?) ?? 0U;

                    if (id1 == id2)
                    {
                        return 0;
                    }

                    return id1 < id2 ? -1 : +1;
                }

                var expandedId = obj as ExpandedNodeId;

                if (expandedId is not null)
                {
                    if (expandedId.IsAbsolute)
                    {
                        return -1;
                    }

                    if (IsNullNodeId && expandedId.InnerNodeId?.IsNullNodeId != false)
                    {
                        return 0;
                    }

                    namespaceIndex = expandedId.NamespaceIndex;
                    idType = expandedId.IdType;
                    id = expandedId.Identifier;
                }
                else if (obj != null)
                {
                    var guid2 = obj as Guid?;
                    var uuid2 = obj as Uuid?;
                    if (guid2 != null || uuid2 != null)
                    {
                        if (namespaceIndex != 0 || idType != IdType.Guid)
                        {
                            return -1;
                        }

                        idType = IdType.Guid;
                        id = m_identifier;
                    }
                    else
                    {
                        // can not compare to unknown object type
                        return -1;
                    }
                }
            }

            // check for different namespace.
            if (namespaceIndex != NamespaceIndex)
            {
                return NamespaceIndex < namespaceIndex ? -1 : +1;
            }

            // check for different id type.
            if (idType != IdType)
            {
                return IdType < idType ? -1 : +1;
            }

            // check for two nulls.
            if (m_identifier == null && id == null)
            {
                return 0;
            }

            // check for a single null.
            if (m_identifier == null && id != null)
            {
                switch (idType)
                {
                    case IdType.String:
                        string stringId = id as string;

                        if (stringId.Length == 0)
                        {
                            return 0;
                        }

                        break;
                    case IdType.Opaque:
                        byte[] opaqueId = id as byte[];

                        if (opaqueId.Length == 0)
                        {
                            return 0;
                        }

                        break;
                    case IdType.Numeric:
                        uint? numericId = id as uint?;

                        if (numericId.Value == 0)
                        {
                            return 0;
                        }

                        break;
                    case IdType.Guid:
                        break;
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unexpected IdType value {idType}.");
                }

                return -1;
            }

            // check for a single null.
            if (m_identifier != null && id == null)
            {
                switch (idType)
                {
                    case IdType.String:
                        string stringId = m_identifier as string;

                        if (stringId.Length == 0)
                        {
                            return 0;
                        }

                        break;
                    case IdType.Opaque:
                        byte[] opaqueId = m_identifier as byte[];

                        if (opaqueId.Length == 0)
                        {
                            return 0;
                        }

                        break;
                    case IdType.Numeric:
                        uint? numericId = m_identifier as uint?;

                        if (numericId.Value == 0)
                        {
                            return 0;
                        }

                        break;
                    case IdType.Guid:
                        break;
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unexpected IdType value {idType}.");
                }

                return +1;
            }

            return CompareTo(idType, id);
        }

        /// <summary>
        /// Returns true if a is greater than b.
        /// </summary>
        /// <remarks>
        /// Returns true if a is greater than b.
        /// </remarks>
        public static bool operator >(NodeId value1, NodeId value2)
        {
            if (value1 is not null)
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
        public static bool operator <(NodeId value1, NodeId value2)
        {
            if (value1 is not null)
            {
                return value1.CompareTo(value2) < 0;
            }

            return true;
        }

        /// <summary>
        /// Returns the string representation of a NodeId.
        /// </summary>
        /// <remarks>
        /// Returns the string representation of a NodeId. This is the same as calling
        /// <see cref="Format(IFormatProvider)"/>.
        /// </remarks>
        /// <exception cref="FormatException">Thrown when the format is not null</exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return Format(formatProvider);
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Makes a deep copy of the object.
        /// </summary>
        /// <remarks>
        /// Returns a copy of this object.
        /// </remarks>
        public new object MemberwiseClone()
        {
            // this object cannot be altered after it is created so no new allocation is necessary.
            return this;
        }

        /// <summary>
        /// Determines if the specified object is equal to the NodeId.
        /// </summary>
        /// <remarks>
        /// Returns a true/false if the specified NodeId is the same as this NodeId.
        /// </remarks>
        /// <param name="obj">The object (NodeId or ExpandedNodeId is desired) to compare to</param>
        public override bool Equals(object obj)
        {
            return CompareTo(obj) == 0;
        }

        /// <summary>
        /// Determines if the specified NodeId is equal to the NodeId.
        /// </summary>
        /// <remarks>
        /// Returns a true/false if the specified NodeId is the same as this NodeId.
        /// Null NodeIds are considered equal.
        /// </remarks>
        /// <param name="other">The NodeId to compare to</param>
        public bool Equals(NodeId other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (IsNullNodeId && (other == null || other.IsNullNodeId))
            {
                return true;
            }

            if (other == null)
            {
                return false;
            }

            // check for different namespace.
            if (other.NamespaceIndex != NamespaceIndex)
            {
                return false;
            }

            if (other.IdType != IdType)
            {
                return false;
            }

            return CompareTo(other.IdType, other.Identifier) == 0;
        }

        /// <summary>
        /// Returns a unique hashcode for the NodeId
        /// </summary>
        /// <remarks>
        /// Returns a unique hashcode for the NodeId
        /// </remarks>
        public override int GetHashCode()
        {
            if (m_identifier == null)
            {
                return 0;
            }

            if (IsNullNodeId)
            {
                return 0;
            }

            var hashCode = new HashCode();
            hashCode.Add(NamespaceIndex);
            hashCode.Add(IdType);
            switch (IdType)
            {
                case IdType.Numeric:
                    hashCode.Add((uint)m_identifier);
                    break;
                case IdType.String:
                    hashCode.Add((string)m_identifier);
                    break;
                case IdType.Guid:
                    hashCode.Add((Guid)m_identifier);
                    break;
                case IdType.Opaque:
#if NET6_0_OR_GREATER
                    hashCode.AddBytes((byte[])m_identifier);
#else
                    foreach (byte id in (byte[])m_identifier)
                    {
                        hashCode.Add(id);
                    }
#endif
                    break;
                default:
                    hashCode.Add(m_identifier);
                    break;
            }
            return hashCode.ToHashCode();
        }

        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are equal.
        /// </remarks>
        public static bool operator ==(NodeId value1, object value2)
        {
            if (value1 is null)
            {
                return value2 is null;
            }

            return value1.CompareTo(value2) == 0;
        }

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are not equal.
        /// </remarks>
        public static bool operator !=(NodeId value1, object value2)
        {
            if (value1 is null)
            {
                return value2 is not null;
            }

            return value1.CompareTo(value2) != 0;
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
            get => Format(CultureInfo.InvariantCulture);
            set
            {
                ValidateImmutableNodeIdIsNotModified();

                NodeId nodeId = Parse(value);

                NamespaceIndex = nodeId.NamespaceIndex;
                IdType = nodeId.IdType;
                m_identifier = nodeId.Identifier;
            }
        }

        /// <summary>
        /// The index of the namespace URI in the server's namespace array.
        /// </summary>
        /// <remarks>
        /// The index of the namespace URI in the server's namespace array.
        /// </remarks>
        public ushort NamespaceIndex { get; private set; }

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
        public IdType IdType { get; private set; }

        /// <summary>
        /// The node identifier.
        /// </summary>
        /// <remarks>
        /// Returns the Id in its native format, i.e. UInt, GUID, String etc.
        /// </remarks>
        /// <exception cref="ServiceResultException"></exception>
        public object Identifier
        {
            get
            {
                if (m_identifier == null)
                {
                    switch (IdType)
                    {
                        case IdType.Numeric:
                            return (uint)0;
                        case IdType.Guid:
                            return Guid.Empty;
                        case IdType.String:
                        case IdType.Opaque:
                            break;
                        default:
                            throw ServiceResultException.Unexpected(
                                $"Unexpected IdType value {IdType}.");
                    }
                }

                return m_identifier;
            }
        }

        /// <summary>
        /// Whether the object represents a Null NodeId.
        /// </summary>
        /// <remarks>
        /// Whether the NodeId represents a Null NodeId.
        /// </remarks>
        /// <exception cref="ServiceResultException"></exception>
        public bool IsNullNodeId
        {
            get
            {
                // non-zero namespace means it can't be null.
                if (NamespaceIndex != 0)
                {
                    return false;
                }

                // the definition of a null identifier depends on the identifier type.
                if (m_identifier != null)
                {
                    switch (IdType)
                    {
                        case IdType.Numeric:
                            if (!m_identifier.Equals((uint)0))
                            {
                                return false;
                            }

                            break;
                        case IdType.String:
                            if (!string.IsNullOrEmpty((string)m_identifier))
                            {
                                return false;
                            }

                            break;
                        case IdType.Guid:
                            if (!m_identifier.Equals(Guid.Empty))
                            {
                                return false;
                            }

                            break;
                        case IdType.Opaque:
                            if (m_identifier != null && ((byte[])m_identifier).Length > 0)
                            {
                                return false;
                            }

                            break;
                        default:
                            throw ServiceResultException.Unexpected(
                                $"Unexpected IdType value {IdType}.");
                    }
                }

                // must be null.
                return true;
            }
        }

#if UNUSED
        /// <summary>
        /// Compares two node identifiers.
        /// </summary>
        private static int CompareIdentifiers(IdType idType1, object id1, IdType idType2, object id2)
        {
            if (id1 == null && id2 == null)
            {
                return 0;
            }

            if (idType1 != idType2)
            {
                return idType1.CompareTo(idType2);
            }

            if (id1 == null || id2 == null)
            {
                object nonNull = id1;

                if (id1 == null)
                {
                    nonNull = id2;
                }

                switch (idType1)
                {
                    case IdType.Numeric:
                        if (nonNull is uint integer && integer == 0)
                        {
                            return 0;
                        }

                        break;

                    case IdType.Guid:
                        if (nonNull is Guid guid && guid == Guid.Empty)
                        {
                            return 0;
                        }

                        break;

                    case IdType.String:
                        if (nonNull is string text && text.Length == 0)
                        {
                            return 0;
                        }

                        break;

                    case IdType.Opaque:
                        if (nonNull is byte[] bytes && bytes.Length == 0)
                        {
                            return 0;
                        }

                        break;
                }

                return (id1 == null) ? -1 : +1;
            }

            if (id1 is byte[] bytes1)
            {
                if (id2 is not byte[] bytes2)
                {
                    return +1;
                }

                if (bytes1.Length != bytes2.Length)
                {
                    return bytes1.Length.CompareTo(bytes2.Length);
                }

                for (int ii = 0; ii < bytes1.Length; ii++)
                {
                    int result = bytes1[ii].CompareTo(bytes2[ii]);

                    if (result != 0)
                    {
                        return result;
                    }
                }

                // both arrays are equal.
                return 0;
            }

            if (id1 is IComparable comparable1)
            {
                return comparable1.CompareTo(id2);
            }

            return string.CompareOrdinal(id1.ToString(), id2.ToString());
        }
#endif

        /// <summary>
        /// Helper to determine if the identifier of specified type is greater/less.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private int CompareTo(IdType idType, object id)
        {
            Debug.Assert(IdType == idType);

            // compare ids.
            switch (idType)
            {
                case IdType.Numeric:
                {
                    uint id1 = (uint)m_identifier;
                    uint id2 = (uint)id;

                    if (id1 == id2)
                    {
                        return 0;
                    }

                    return id1 < id2 ? -1 : +1;
                }
                case IdType.String:
                {
                    string id1 = (string)m_identifier;
                    string id2 = (string)id;
                    return string.CompareOrdinal(id1, id2);
                }
                case IdType.Guid:
                {
                    if (m_identifier is Guid id2)
                    {
                        if (id is Uuid uuid2)
                        {
                            return id2.CompareTo(uuid2);
                        }
                        return id2.CompareTo((Guid)id);
                    }

                    if (m_identifier is Uuid id1)
                    {
                        if (id is Uuid uuid1)
                        {
                            return id1.CompareTo(uuid1);
                        }
                        return id1.CompareTo((Guid)id);
                    }

                    return -1;
                }
                case IdType.Opaque:
                {
                    byte[] id1 = (byte[])m_identifier;
                    byte[] id2 = (byte[])id;

                    if (Utils.IsEqual(id1, id2))
                    {
                        return 0;
                    }

                    if (id1.Length == id2.Length)
                    {
                        for (int ii = 0; ii < id1.Length; ii++)
                        {
                            if (id1[ii] != id2[ii])
                            {
                                return id1[ii] < id2[ii] ? -1 : +1;
                            }
                        }

                        return 0;
                    }

                    return id1.Length < id2.Length ? -1 : +1;
                }
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected IdType value {IdType}.");
            }
        }

        /// <summary>
        /// Formats a node id as a string.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static void FormatIdentifier(
            IFormatProvider formatProvider,
            StringBuilder buffer,
            object identifier,
            IdType identifierType)
        {
            switch (identifierType)
            {
                case IdType.Numeric:
                    if (identifier == null)
                    {
                        buffer.Append('0');
                        break;
                    }

                    buffer.AppendFormat(formatProvider, "{0}", identifier);
                    break;
                case IdType.String:
                    buffer.AppendFormat(formatProvider, "{0}", identifier);
                    break;
                case IdType.Guid:
                    if (identifier == null)
                    {
                        buffer.Append(Guid.Empty.ToString());
                        break;
                    }

                    buffer.AppendFormat(formatProvider, "{0}", identifier);
                    break;
                case IdType.Opaque:
                    if (identifier != null)
                    {
                        buffer.AppendFormat(
                            formatProvider,
                            "{0}",
                            Convert.ToBase64String((byte[])identifier));
                    }
                    break;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected IdType value {identifierType}.");
            }
        }

        /// <summary>
        /// Validate that an immutable NodeId is not overwritten.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        [Conditional("IMMUTABLENULLNODEID")]
        private static void ValidateImmutableNodeIdIsNotModified()
        {
#if IMMUTABLENULLNODEID
            if (this is ImmutableNodeId)
            {
                throw new InvalidOperationException("Cannot modify the immutable NodeId.Null.");
            }
#endif
        }

        private object m_identifier;
    }

#if IMMUTABLENULLNODEID

    /// <summary>
    /// A NodeId class as helper to catch if the immutable NodeId.Null is being modified.
    /// </summary>
    internal class ImmutableNodeId : NodeId
    {
        internal ImmutableNodeId() { }
    }
#endif

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
        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public NodeIdCollection()
        {
        }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Creates a new collection based on the referenced collection.
        /// </remarks>
        /// <param name="collection">The existing collection to use as the basis of creating this collection</param>
        public NodeIdCollection(IEnumerable<NodeId> collection)
            : base(collection)
        {
        }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Creates a new collection while specifying the max size of the collection.
        /// </remarks>
        /// <param name="capacity">The max. capacity of the collection</param>
        public NodeIdCollection(int capacity)
            : base(capacity)
        {
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        /// <remarks>
        /// A quick-use method that will take an array of <see cref="NodeId"/> objects and will
        /// return them within a <see cref="NodeIdCollection"/>.
        /// </remarks>
        /// <param name="values">An array of <see cref="NodeId"/> to add to the collection</param>
        /// <returns>A <see cref="NodeIdCollection"/> containing the <see cref="NodeId"/>'s added via the parameters</returns>
        public static NodeIdCollection ToNodeIdCollection(NodeId[] values)
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
        /// <remarks>
        /// Converts an array to a collection.
        /// </remarks>
        /// <param name="values">An array of <see cref="NodeId"/> objects to compare</param>
        public static implicit operator NodeIdCollection(NodeId[] values)
        {
            return ToNodeIdCollection(values);
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return MemberwiseClone();
        }

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public new object MemberwiseClone()
        {
            var clone = new NodeIdCollection(Count);

            foreach (NodeId element in this)
            {
                clone.Add(Utils.Clone(element));
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
    /// Helper which implements a NodeId IEqualityComparer for Linq queries.
    /// </summary>
    public class NodeIdComparer : IEqualityComparer<NodeId>
    {
        /// <inheritdoc/>
        public bool Equals(NodeId x, NodeId y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return x == y;
        }

        /// <inheritdoc/>
        public int GetHashCode(NodeId nodeId)
        {
            if (nodeId is null)
            {
                return 0;
            }

            return nodeId.GetHashCode();
        }
    }
}
