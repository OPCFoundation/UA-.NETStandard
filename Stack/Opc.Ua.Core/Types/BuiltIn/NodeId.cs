/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections;
using System.Collections.Generic;
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
    /// <item><b>Address Space Model</b> setion <b>7.2</b></item>
    /// <item><b>Address Space Model</b> setion <b>5.2.2</b></item>
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
    public class NodeId : IComparable, IFormattable
    {
        #region Constructors
        #region public NodeId()

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

        #endregion
        #region public NodeId(NodeId value)
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
            if (value == null) throw new ArgumentNullException(nameof(value));

            m_namespaceIndex = value.m_namespaceIndex;
            m_identifierType = value.m_identifierType;
            m_identifier = Utils.Clone(value.m_identifier);
        }
        #endregion
        #region public NodeId(uint value)
        /// <summary>
        /// Initializes a numeric node identifier.
        /// </summary>
        /// <remarks>
        /// Creates a new NodeId that will have a numeric (unsigned-int) id
        /// </remarks>
        /// <param name="value">The numeric value of the id</param>
        public NodeId(uint value)
        {
            m_namespaceIndex = 0;
            m_identifierType = IdType.Numeric;
            m_identifier = value;
        }
        #endregion
        #region public NodeId(uint value, ushort namespaceIndex)
        /// <summary>
        /// Initializes a guid node identifier with a namespace index.
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
            m_namespaceIndex = namespaceIndex;
            m_identifierType = IdType.Numeric;
            m_identifier = value;
        }

        #endregion

        #region public NodeId(string value, ushort namespaceIndex)
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
            m_namespaceIndex = namespaceIndex;
            m_identifierType = IdType.String;
            m_identifier = value;
        }

        #endregion
        #region public NodeId(Guid value)
        /// <summary>
        /// Initializes a guid node identifier.
        /// </summary>
        /// <remarks>
        /// Creates a new node whose Id will be a <see cref="Guid"/>.
        /// </remarks>
        /// <param name="value">The new Guid value of this nodes Id.</param>
        public NodeId(Guid value)
        {
            m_namespaceIndex = 0;
            m_identifierType = IdType.Guid;
            m_identifier = value;
        }

        #endregion        

        #region public NodeId(Guid value, ushort namespaceIndex)
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
            m_namespaceIndex = namespaceIndex;
            m_identifierType = IdType.Guid;
            m_identifier = value;
        }
        #endregion    

        #region public NodeId(byte[] value)
        /// <summary>
        /// Initializes a guid node identifier.
        /// </summary>
        /// <remarks>
        /// Creates a new node whose Id will be a series of <see cref="Byte"/>.
        /// </remarks>
        /// <param name="value">An array of <see cref="Byte"/> that will become this Node's ID</param>
        public NodeId(byte[] value)
        {
            m_namespaceIndex = 0;
            m_identifierType = IdType.Opaque;
            m_identifier = null;

            if (value != null)
            {
                byte[] copy = new byte[value.Length];
                Array.Copy(value, copy, value.Length);
                m_identifier = copy;
            }
        }

        #endregion        
        #region public NodeId(byte[] value, ushort namespaceIndex)
        /// <summary>
        /// Initializes an opaque node identifier with a namespace index.
        /// </summary>
        /// <remarks>
        /// Creates a new node whose Id will be a series of <see cref="Byte"/>, while specifying
        /// the index of the namespace that this node belongs to.
        /// </remarks>
        /// <param name="value">An array of <see cref="Byte"/> that will become this Node's ID</param>
        /// <param name="namespaceIndex">The index of the namespace that this node belongs to</param>
        public NodeId(byte[] value, ushort namespaceIndex)
        {
            m_namespaceIndex = namespaceIndex;
            m_identifierType = IdType.Opaque;
            m_identifier = null;

            if (value != null)
            {
                byte[] copy = new byte[value.Length];
                Array.Copy(value, copy, value.Length);
                m_identifier = copy;
            }
        }

        #endregion
        #region public NodeId(string text)
        /// <summary>
        /// Initializes a node id by parsing a node id string.
        /// </summary>
        /// <remarks>
        /// Creates a new node with a String id.
        /// </remarks>
        /// <param name="text">The string id of this new node</param>
        public NodeId(string text)
        {
            NodeId nodeId = NodeId.Parse(text);

            m_namespaceIndex = nodeId.NamespaceIndex;
            m_identifierType = nodeId.IdType;
            m_identifier = nodeId.Identifier;
        }

        #endregion

        #region public NodeId(object value, ushort namespaceIndex)
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
            m_namespaceIndex = namespaceIndex;

            if (value is uint)
            {
                SetIdentifier(IdType.Numeric, value);
                return;
            }

            if (value == null || value is string)
            {
                SetIdentifier(IdType.String, value);
                return;
            }

            if (value is Guid)
            {
                SetIdentifier(IdType.Guid, value);
                return;
            }

            if (value is Uuid)
            {
                SetIdentifier(IdType.Guid, value);
                return;
            }

            if (value is byte[])
            {
                SetIdentifier(IdType.Opaque, value);
                return;
            }

            throw new ArgumentException("Identifier type not supported.", nameof(value));
        }
        #endregion

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        [OnDeserializing()]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        /// <summary>
        /// Initializes the object during deserialization.
        /// </summary>
        private void Initialize()
        {
            m_namespaceIndex = 0;
            m_identifierType = IdType.Numeric;
            m_identifier = null;
        }
        #endregion

        #region Static Members
        /// <summary>
        /// Converts an identifier and a namespaceUri to a local NodeId using the namespaceTable.
        /// </summary>
        /// <param name="identifier">The identifier for the node.</param>
        /// <param name="namespaceUri">The URI to look up.</param>
        /// <param name="namespaceTable">The table to use for the URI lookup.</param>
        /// <returns>A local NodeId</returns>
        /// <exception cref="ServiceResultException">Thrown when the namespace cannot be found</exception>
        public static NodeId Create(object identifier, string namespaceUri, NamespaceTable namespaceTable)
        {
            int index = -1;

            if (namespaceTable != null)
            {
                index = namespaceTable.GetIndex(namespaceUri);
            }

            if (index < 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadNodeIdInvalid, "NamespaceUri ({0}) is not in the namespace table.", namespaceUri);
            }

            return new NodeId(identifier, (ushort)index);
        }

        #region public static implicit operator NodeId(uint value)
        /// <summary>
        /// Converts an integer to a numeric node identifier.
        /// </summary>
        /// <remarks>
        /// Converts an integer to a numeric node identifier for comparissons.
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
        /// //now to compare the node to the ids using a simple comparisson and Equals:
        /// Utils.Trace("Comparing NodeId to uint");
        /// Utils.Trace("\tComparing 100 to 100 = [equals] {0}", node1.Equals(id1));
        /// Utils.Trace("\tComparing 100 to 100 = [ ==   ] {0}", node1 == id1);
        /// Utils.Trace("\tComparing 100 to 101 = [equals] {0}", node1.Equals(id2));
        /// Utils.Trace("\tComparing 100 to 101 = [ ==   ] {0}", node1 == id2);
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
        /// 'now to compare the node to the ids using a simple comparisson and Equals:
        /// Utils.Trace("Comparing NodeId to uint")
        /// Utils.Trace( String.Format("   Comparing 100 to 100 = [equals] {0}", node1.Equals(id1)) )
        /// Utils.Trace( String.Format("   Comparing 100 to 100 = [  =   ] {0}", node1 = id1) )
        /// Utils.Trace( String.Format("   Comparing 100 to 101 = [equals] {0}", node1.Equals(id2)) )
        /// Utils.Trace( String.Format("   Comparing 100 to 101 = [  =   ] {0}", node1 = id2) )
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

        #endregion
        #region public static implicit operator NodeId(Guid value)
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
        /// Utils.Trace("\n\nComparing NodeId to GUID");
        /// Utils.Trace("\tComparing {0} to {0} = [equals] {2}", id1, id1, node1.Equals(id1));
        /// Utils.Trace("\tComparing {0} to {0} = [ ==   ] {2}", id1, id1, node1 == id1);
        /// Utils.Trace("\tComparing {0} to {1} = [equals] {2}", id1, id2, node1.Equals(id2));
        /// Utils.Trace("\tComparing {0} to {1} = [ ==   ] {2}", id1, id2, node1 == id2);
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
        /// Utils.Trace("Comparing NodeId to GUID")
        /// Utils.Trace( String.Format( "  Comparing {0} to {0} = [equals] {2}", id1, id1, node1.Equals(id1)) );
        /// Utils.Trace( String.Format( "  Comparing {0} to {0} = [  =   ] {2}", id1, id1, node1 = id1) );
        /// Utils.Trace( String.Format( "  Comparing {0} to {0} = [equals] {2}", id1, id2, node1.Equals(id2)) );
        /// Utils.Trace( String.Format( "  Comparing {0} to {0} = [  =   ] {2}", id1, id2, node1 = id2) );
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

        #endregion
        #region public static implicit operator NodeId(byte[] value)
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
        /// Utils.Trace("\n\nComparing NodeId to Byte[]");
        /// Utils.Trace("\tComparing {0} to {0} = [equals] {2}", id1String, id1String, node1.Equals(id1));
        /// Utils.Trace("\tComparing {0} to {0} = [  =   ] {2}", id1String, id1String, node1 == id1);
        /// Utils.Trace("\tComparing {0} to {1} = [equals] {2}", id1String, id2String, node1.Equals(id2));
        /// Utils.Trace("\tComparing {0} to {1} = [  =   ] {2}", id1String, id2String, node1 == id2);
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
        /// Utils.Trace("Comparing NodeId to Byte()")
        /// Utils.Trace( String.Format("Comparing {0} to {0} = [equals] {2}", id1String, id1String, node1.Equals(id1)) )
        /// Utils.Trace( String.Format("Comparing {0} to {0} = [  =   ] {2}", id1String, id1String, node1 = id1) )
        /// Utils.Trace( String.Format("Comparing {0} to {1} = [equals] {2}", id1String, id2String, node1.Equals(id2)) )
        /// Utils.Trace( String.Format("Comparing {0} to {1} = [  =   ] {2}", id1String, id2String, node1 = id2) )
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
        /// <param name="value">The <see cref="Byte"/>[] array to compare this node to</param>
        public static implicit operator NodeId(byte[] value)
        {
            return new NodeId(value);
        }

        #endregion
        #region public static implicit operator NodeId(string text)
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
        /// Utils.Trace("\n\nComparing NodeId to String");
        /// Utils.Trace("\tComparing {0} to {0} = [equals] {2}", id1, id1, node1.Equals(id1));
        /// Utils.Trace("\tComparing {0} to {0} = [ ==   ] {2}", id1, id1, node1 == id1);
        /// Utils.Trace("\tComparing {0} to {1} = [equals] {2}", id1, id2, node1.Equals(id2));
        /// Utils.Trace("\tComparing {0} to {1} = [ ==   ] {2}", id1, id2, node1 == id2);
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
        /// Utils.Trace("Comparing NodeId to String");
        /// Utils.Trace(String.Format("Comparing {0} to {0} = [equals] {2}", id1, id1, node1.Equals(id1)));
        /// Utils.Trace(String.Format("Comparing {0} to {0} = [  =   ] {2}", id1, id1, node1 = id1));
        /// Utils.Trace(String.Format("Comparing {0} to {1} = [equals] {2}", id1, id2, node1.Equals(id2)));
        /// Utils.Trace(String.Format("Comparing {0} to {1} = [  =   ] {2}", id1, id2, node1 = id2));
        /// 
        /// </code>
        /// </example>
        /// <param name="text">The <see cref="String"/> to compare this node to.</param>
        public static implicit operator NodeId(string text)
        {
            return NodeId.Parse(text);
        }

        #endregion
        #region public static bool IsNull(NodeId nodeId)
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

        #endregion
        #region public static bool IsNull(ExpandedNodeId nodeId)
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
        #endregion
        #region public static NodeId Parse(string text)
        /// <summary>
        /// Parses a node id string and returns a node id object.
        /// </summary>
        /// <remarks>
        /// Parses a NodeId String and returns a NodeId object
        /// </remarks>
        /// <param name="text">The NodeId value as a string.</param>
        /// <exception cref="ServiceResultException">Thrown under a variety of circumstances, each time with a specific message.</exception>
        public static NodeId Parse(string text)
        {
            try
            {
                if (String.IsNullOrEmpty(text))
                {
                    return NodeId.Null;
                }

                ushort namespaceIndex = 0;

                // parse the namespace index if present.
                if (text.StartsWith("ns=", StringComparison.Ordinal))
                {
                    int index = text.IndexOf(';');

                    if (index == -1)
                    {
                        throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "Invalid namespace index.");
                    }

                    namespaceIndex = Convert.ToUInt16(text.Substring(3, index - 3), CultureInfo.InvariantCulture);

                    text = text.Substring(index + 1);
                }

                // parse numeric node identifier.
                if (text.StartsWith("i=", StringComparison.Ordinal))
                {
                    return new NodeId(Convert.ToUInt32(text.Substring(2), CultureInfo.InvariantCulture), namespaceIndex);
                }

                // parse string node identifier.
                if (text.StartsWith("s=", StringComparison.Ordinal))
                {
                    return new NodeId(text.Substring(2), namespaceIndex);
                }

                // parse guid node identifier.
                if (text.StartsWith("g=", StringComparison.Ordinal))
                {
                    return new NodeId(new Guid(text.Substring(2)), namespaceIndex);
                }

                // parse opaque node identifier.
                if (text.StartsWith("b=", StringComparison.Ordinal))
                {
                    return new NodeId(Convert.FromBase64String(text.Substring(2)), namespaceIndex);
                }

                // treat as a string identifier if a namespace was specified.
                if (namespaceIndex != 0)
                {
                    return new NodeId(text, namespaceIndex);
                }

                // treat as URI identifier.
                return new NodeId(text, 0);
            }
            catch (Exception e)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid,
                    Utils.Format("Cannot parse node id text: '{0}'", text),
                    e);
            }
        }
        #endregion

        /// <summary>
        /// Returns an instance of a null NodeId.
        /// </summary>
        public static NodeId Null => s_Null;

        private static readonly NodeId s_Null = new NodeId();
        #endregion

        #region Public Methods (and some Internals)

        #region public string Format()
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
        public string Format()
        {
            StringBuilder buffer = new StringBuilder();
            Format(buffer);
            return buffer.ToString();
        }

        /// <summary>
        /// Formats the NodeId as a string and appends it to the buffer.
        /// </summary>
        public void Format(StringBuilder buffer)
        {
            Format(buffer, m_identifier, m_identifierType, m_namespaceIndex);
        }

        /// <summary>
        /// Formats the NodeId as a string and appends it to the buffer.
        /// </summary>
        public static void Format(StringBuilder buffer, object identifier, IdType identifierType, ushort namespaceIndex)
        {
            if (namespaceIndex != 0)
            {
                buffer.AppendFormat(CultureInfo.InvariantCulture, "ns={0};", namespaceIndex);
            }

            // add identifier type prefix.
            switch (identifierType)
            {
                case IdType.Numeric:
                {
                    buffer.Append("i=");
                    break;
                }

                case IdType.String:
                {
                    buffer.Append("s=");
                    break;
                }

                case IdType.Guid:
                {
                    buffer.Append("g=");
                    break;
                }

                case IdType.Opaque:
                {
                    buffer.Append("b=");
                    break;
                }
            }

            // add identifier.
            FormatIdentifier(buffer, identifier, identifierType);
        }
        #endregion

        #region public override string ToString()

        /// <summary>
        /// Returns the string representation of a NodeId.
        /// </summary>
        /// <remarks>
        /// Returns the Node represented as a String. This is the same as calling
        /// <see cref="Format()"/>.
        /// </remarks>
        public override string ToString()
        {
            return ToString(null, null);
        }

        #endregion
        #region public static ExpandedNodeId ToExpandedNodeId(NodeId nodeId, NamespaceTable namespaceTable)
        /// <summary>
        /// Converts an node id to an expanded node id using a namespace table.
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

            ExpandedNodeId expandedId = new ExpandedNodeId(nodeId);

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

        #endregion
        /// <summary>
        /// Updates the namespace index.
        /// </summary>
        internal void SetNamespaceIndex(ushort value)
        {
            m_namespaceIndex = value;
        }

        /// <summary>
        /// Updates the identifier.
        /// </summary>
        internal void SetIdentifier(IdType idType, object value)
        {
            m_identifierType = idType;

            switch (idType)
            {
                case IdType.Opaque:
                {
                    m_identifier = Utils.Clone(value);
                    break;
                }

                default:
                {
                    m_identifier = value;
                    break;
                }
            }
        }

        /// <summary>
        /// Updates the identifier.
        /// </summary>
        internal void SetIdentifier(string value, IdType idType)
        {
            m_identifierType = idType;
            SetIdentifier(IdType.String, value);
        }

        #endregion

        #region IComparable Members

        #region public int CompareTo(object obj)
        /// <summary>
        /// Compares the current instance to the object.
        /// </summary>
        /// <remarks>
        /// Enables this object type to be compared to other types of object.
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
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

            ushort namespaceIndex = this.m_namespaceIndex;
            IdType idType = this.m_identifierType;
            object id = null;

            // check for expanded node ids.
            NodeId nodeId = obj as NodeId;

            if (!Object.ReferenceEquals(nodeId, null))
            {
                namespaceIndex = nodeId.NamespaceIndex;
                idType = nodeId.IdType;
                id = nodeId.Identifier;
            }
            else
            {
                UInt32? uid = obj as UInt32?;

                // check for numeric contants.
                if (uid != null)
                {
                    if (namespaceIndex != 0 || idType != IdType.Numeric)
                    {
                        return -1;
                    }

                    uint id1 = (uint)m_identifier;
                    uint id2 = uid.Value;

                    if (id1 == id2)
                    {
                        return 0;
                    }

                    return (id1 < id2) ? -1 : +1;
                }

                ExpandedNodeId expandedId = obj as ExpandedNodeId;

                if (!Object.ReferenceEquals(expandedId, null))
                {
                    if (expandedId.IsAbsolute)
                    {
                        return -1;
                    }

                    namespaceIndex = expandedId.NamespaceIndex;
                    idType = expandedId.IdType;
                    id = expandedId.Identifier;
                }
            }

            // check for different namespace.
            if (namespaceIndex != m_namespaceIndex)
            {
                return (m_namespaceIndex < namespaceIndex) ? -1 : +1;
            }

            // check for different id type.
            if (idType != m_identifierType)
            {
                return (m_identifierType < idType) ? -1 : +1;
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
                    {
                        string stringId = id as string;

                        if (stringId.Length == 0)
                        {
                            return 0;
                        }

                        break;
                    }

                    case IdType.Opaque:
                    {
                        byte[] opaqueId = id as byte[];

                        if (opaqueId.Length == 0)
                        {
                            return 0;
                        }

                        break;
                    }

                    case IdType.Numeric:
                    {
                        uint? numericId = id as uint?;

                        if (numericId.Value == 0)
                        {
                            return 0;
                        }

                        break;
                    }
                }

                return -1;
            }

            // check for a single null.
            if (m_identifier != null && id == null)
            {
                switch (idType)
                {
                    case IdType.String:
                    {
                        string stringId = m_identifier as string;

                        if (stringId.Length == 0)
                        {
                            return 0;
                        }

                        break;
                    }

                    case IdType.Opaque:
                    {
                        byte[] opaqueId = m_identifier as byte[];

                        if (opaqueId.Length == 0)
                        {
                            return 0;
                        }

                        break;
                    }

                    case IdType.Numeric:
                    {
                        uint? numericId = m_identifier as uint?;

                        if (numericId.Value == 0)
                        {
                            return 0;
                        }

                        break;
                    }
                }

                return +1;
            }

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

                    return (id1 < id2) ? -1 : +1;
                }

                case IdType.String:
                {
                    string id1 = (string)m_identifier;
                    string id2 = (string)id;
                    return String.CompareOrdinal(id1, id2);
                }

                case IdType.Guid:
                {
                    Guid id1 = (Guid)m_identifier;
                    if (id is Uuid)
                    {
                        return id1.CompareTo((Uuid)id);
                    }
                    return id1.CompareTo((Guid)id);
                }

                case IdType.Opaque:
                {
                    byte[] id1 = (byte[])m_identifier;
                    byte[] id2 = (byte[])id;

                    if (id1.Length == id2.Length)
                    {
                        for (int ii = 0; ii < id1.Length; ii++)
                        {
                            if (id1[ii] != id2[ii])
                            {
                                return (id1[ii] < id2[ii]) ? -1 : +1;
                            }
                        }

                        return 0;
                    }

                    return (id1.Length < id2.Length) ? -1 : +1;
                }
            }

            // invalid id type - should never get here.
            return +1;
        }

        #endregion
        #region public static bool operator>(NodeId value1, NodeId value2)
        /// <summary>
        /// Returns true if a is greater than b.
        /// </summary>
        /// <remarks>
        /// Returns true if a is greater than b.
        /// </remarks>
        public static bool operator >(NodeId value1, NodeId value2)
        {
            if (!Object.ReferenceEquals(value1, null))
            {
                return value1.CompareTo(value2) > 0;
            }

            return false;
        }

        #endregion        
        #region public static bool operator<(NodeId value1, NodeId value2)
        /// <summary>
        /// Returns true if a is less than b.
        /// </summary>
        /// <remarks>
        /// Returns true if a is less than b.
        /// </remarks>
        public static bool operator <(NodeId value1, NodeId value2)
        {
            if (!Object.ReferenceEquals(value1, null))
            {
                return value1.CompareTo(value2) < 0;
            }

            return true;
        }
        #endregion

        #endregion

        #region IFormattable Members

        #region public string ToString(string format, IFormatProvider formatProvider)
        /// <summary>
        /// Returns the string representation of a NodeId.
        /// </summary>
        /// <remarks>
        /// Returns the string representation of a NodeId. This is the same as calling
        /// <see cref="Format()"/>.
        /// </remarks>
        /// <exception cref="FormatException">Thrown when the format is not null</exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return String.Format(formatProvider, "{0}", Format());
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }

        #endregion

        #endregion

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

        #region Comparison Functions

        #region public override bool Equals(object obj)
        /// <summary>
        /// Determines if the specified object is equal to the NodeId.
        /// </summary>
        /// <remarks>
        /// Returns a true/false if the specified NodeId is the same as this NodeId.
        /// </remarks>
        /// <param name="obj">The object (NodeId or ExpandedNodeId is desired) to compare to</param>
        public override bool Equals(object obj)
        {
            return (CompareTo(obj) == 0);
        }

        #endregion
        #region public override int GetHashCode()
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

            if (m_identifierType == IdType.Opaque)
            {
                byte[] id = (byte[])m_identifier;

                int hash = id.Length;

                for (int ii = 0; ii < 16 && ii < id.Length; ii++)
                {
                    hash <<= 1;
                    hash += id[ii];

                    if (id.Length - ii >= 1)
                    {
                        hash += (id[id.Length - ii - 1] << 16);
                    }
                }

                return hash;
            }

            return m_identifier.GetHashCode();
        }

        #endregion
        #region public static bool operator==(NodeId a, object b) 
        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are equal.
        /// </remarks>
        public static bool operator ==(NodeId value1, object value2)
        {
            if (Object.ReferenceEquals(value1, null))
            {
                return Object.ReferenceEquals(value2, null);
            }

            return (value1.CompareTo(value2) == 0);
        }

        #endregion
        #region public static bool operator!=(NodeId value1, object value2) 
        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <remarks>
        /// Returns true if the objects are not equal.
        /// </remarks>
        public static bool operator !=(NodeId value1, object value2)
        {
            if (Object.ReferenceEquals(value1, null))
            {
                return !Object.ReferenceEquals(value2, null);
            }

            return (value1.CompareTo(value2) != 0);
        }


        #endregion

        #endregion

        #region Public Properties

        #region internal string IdentifierText
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
                NodeId nodeId = NodeId.Parse(value);

                m_namespaceIndex = nodeId.NamespaceIndex;
                m_identifierType = nodeId.IdType;
                m_identifier = nodeId.Identifier;
            }
        }

        #endregion                
        #region public ushort NamespaceIndex
        /// <summary>
        /// The index of the namespace URI in the server's namespace array.
        /// </summary>
        /// <remarks>
        /// The index of the namespace URI in the server's namespace array.
        /// </remarks>
        public ushort NamespaceIndex => m_namespaceIndex;

        #endregion
        #region public IdType IdType
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
        public IdType IdType => m_identifierType;

        #endregion
        #region public object Identifier
        /// <summary>
        /// The node identifier.
        /// </summary>
        /// <remarks>
        /// Returns the Id in its native format, i.e. UInt, GUID, String etc.
        /// </remarks>
        public object Identifier
        {
            get
            {
                if (m_identifier == null)
                {
                    switch (m_identifierType)
                    {
                        case IdType.Numeric: { return (uint)0; }
                        case IdType.Guid: { return Guid.Empty; }
                    }
                }

                return m_identifier;
            }
        }

        #endregion             
        #region public bool IsNull
        /// <summary>
        /// Whether the object represents a Null NodeId.
        /// </summary>
        /// <remarks>
        /// Whether the NodeId represents a Null NodeId.
        /// </remarks>
        public bool IsNullNodeId
        {
            get
            {
                // non-zero namespace means it can't be null.
                if (m_namespaceIndex != 0)
                {
                    return false;
                }

                // the definition of a null identifier depends on the identifier type.
                if (m_identifier != null)
                {
                    switch (m_identifierType)
                    {
                        case IdType.Numeric:
                        {
                            if (!m_identifier.Equals((uint)0))
                            {
                                return false;
                            }

                            break;
                        }

                        case IdType.String:
                        {
                            if (!String.IsNullOrEmpty((string)m_identifier))
                            {
                                return false;
                            }

                            break;
                        }

                        case IdType.Guid:
                        {
                            if (!m_identifier.Equals(Guid.Empty))
                            {
                                return false;
                            }

                            break;
                        }

                        case IdType.Opaque:
                        {
                            if (m_identifier != null && ((byte[])m_identifier).Length > 0)
                            {
                                return false;
                            }

                            break;
                        }
                    }
                }

                // must be null.
                return true;
            }
        }

        #endregion

        #endregion

        #region Private Methods
        /// <summary>
        /// Compares two node identifiers.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
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
                    {
                        if (nonNull is uint && (uint)nonNull == 0)
                        {
                            return 0;
                        }

                        break;
                    }

                    case IdType.Guid:
                    {
                        if (nonNull is Guid && (Guid)nonNull == Guid.Empty)
                        {
                            return 0;
                        }

                        break;
                    }

                    case IdType.String:
                    {
                        string text = nonNull as string;

                        if (text != null && text.Length == 0)
                        {
                            return 0;
                        }

                        break;
                    }

                    case IdType.Opaque:
                    {
                        byte[] bytes = nonNull as byte[];

                        if (bytes != null && bytes.Length == 0)
                        {
                            return 0;
                        }

                        break;
                    }
                }

                return (id1 == null) ? -1 : +1;
            }

            byte[] bytes1 = id1 as byte[];

            if (bytes1 != null)
            {
                byte[] bytes2 = id2 as byte[];

                if (bytes2 == null)
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

            IComparable comparable1 = id1 as IComparable;

            if (comparable1 != null)
            {
                return comparable1.CompareTo(id2);
            }

            return String.CompareOrdinal(id1.ToString(), id2.ToString());
        }

        /// <summary>
        /// Formats a node id as a string.
        /// </summary>
        private static void FormatIdentifier(StringBuilder buffer, object identifier, IdType identifierType)
        {
            switch (identifierType)
            {
                case IdType.Numeric:
                {
                    if (identifier == null)
                    {
                        buffer.Append('0');
                        break;
                    }

                    buffer.AppendFormat(CultureInfo.InvariantCulture, "{0}", identifier);
                    break;
                }

                case IdType.String:
                {
                    buffer.AppendFormat(CultureInfo.InvariantCulture, "{0}", identifier);
                    break;
                }

                case IdType.Guid:
                {
                    if (identifier == null)
                    {
                        buffer.Append(Guid.Empty);
                        break;
                    }

                    buffer.AppendFormat(CultureInfo.InvariantCulture, "{0}", identifier);
                    break;
                }

                case IdType.Opaque:
                {
                    if (identifier != null)
                    {
                        buffer.AppendFormat(CultureInfo.InvariantCulture, "{0}", Convert.ToBase64String((byte[])identifier));
                    }

                    break;
                }
            }
        }

        #endregion

        #region Private Fields
        private ushort m_namespaceIndex;
        private IdType m_identifierType;
        private object m_identifier;
        #endregion
    }

    #region NodeIdCollection Class
    /// <summary>
    /// A collection of NodeIds.
    /// </summary>
    /// <remarks>
    /// Provides a strongly-typed collection of <see cref="NodeId"/>.
    /// </remarks>
    [CollectionDataContract(Name = "ListOfNodeId", Namespace = Namespaces.OpcUaXsd, ItemName = "NodeId")]
    public partial class NodeIdCollection : List<NodeId>
    {

        #region CTORs

        /// <summary>
        /// Initializes an empty collection.
        /// </summary>
        /// <remarks>
        /// Initializes an empty collection.
        /// </remarks>
        public NodeIdCollection() { }

        /// <summary>
        /// Initializes the collection from another collection.
        /// </summary>
        /// <remarks>
        /// Creates a new collection based on the referenced collection.
        /// </remarks>
        /// <param name="collection">The existing collection to use as the basis of creating this collection</param>
        public NodeIdCollection(IEnumerable<NodeId> collection) : base(collection) { }

        /// <summary>
        /// Initializes the collection with the specified capacity.
        /// </summary>
        /// <remarks>
        /// Creates a new collection while specifying the max size of the collection.
        /// </remarks>
        /// <param name="capacity">The max. capacity of the collection</param>
        public NodeIdCollection(int capacity) : base(capacity) { }

        #endregion        

        #region public static NodeIdCollection ToNodeIdCollection(NodeId[] values)
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
                return new NodeIdCollection(values);
            }

            return new NodeIdCollection();
        }

        #endregion
        #region public static implicit operator NodeIdCollection(NodeId[] values)
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

        #endregion

        /// <summary>
        /// Creates a deep copy of the collection.
        /// </summary>
        /// <remarks>
        /// Creates a deep copy of the collection.
        /// </remarks>
        public new object MemberwiseClone()
        {
            NodeIdCollection clone = new NodeIdCollection(this.Count);

            foreach (NodeId element in this)
            {
                clone.Add((NodeId)Utils.Clone(element));
            }

            return clone;
        }
    }//class
    #endregion

    /// <summary>
    /// A dictionary designed to provide efficient lookups for objects identified by a NodeId
    /// </summary>
    public class NodeIdDictionary<T> : IDictionary<NodeId, T>
    {
        #region Constructors
        /// <summary>
        /// Creates an empty dictionary.
        /// </summary>
        public NodeIdDictionary()
        {
            m_version = 0;
            m_numericIds = new SortedDictionary<ulong, T>();
        }
        #endregion

        #region IDictionary<NodeId,T> Members
        /// <summary cref="IDictionary.Add" />
        public void Add(NodeId key, T value)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            m_version++;

            switch (key.IdType)
            {
                case IdType.Numeric:
                {
                    ulong id = ((ulong)key.NamespaceIndex) << 32;
                    id += (uint)key.Identifier;
                    m_numericIds.Add(id, value);
                    return;
                }

                case IdType.String:
                {
                    IDictionary<string, T> dictionary = GetStringDictionary(key.NamespaceIndex, true);
                    dictionary.Add((string)key.Identifier, value);
                    return;
                }

                case IdType.Guid:
                {
                    IDictionary<Guid, T> dictionary = GetGuidDictionary(key.NamespaceIndex, true);
                    dictionary.Add((Guid)key.Identifier, value);
                    return;
                }

                case IdType.Opaque:
                {
                    IDictionary<ByteKey, T> dictionary = GetOpaqueDictionary(key.NamespaceIndex, true);
                    dictionary.Add(new NodeIdDictionary<T>.ByteKey((byte[])key.Identifier), value);
                    return;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(key), "key.IdType");
        }

        /// <summary cref="IDictionary{TKey,TValue}.ContainsKey" />
        public bool ContainsKey(NodeId key)
        {
            if (key == null)
            {
                return false;
            }

            switch (key.IdType)
            {
                case IdType.Numeric:
                {
                    ulong id = ((ulong)key.NamespaceIndex) << 32;
                    id += (uint)key.Identifier;
                    return m_numericIds.ContainsKey(id);
                }

                case IdType.String:
                {
                    IDictionary<string, T> dictionary = GetStringDictionary(key.NamespaceIndex, false);

                    if (dictionary != null)
                    {
                        return dictionary.ContainsKey((string)key.Identifier);
                    }

                    break;
                }

                case IdType.Guid:
                {
                    IDictionary<Guid, T> dictionary = GetGuidDictionary(key.NamespaceIndex, false);

                    if (dictionary != null)
                    {
                        return dictionary.ContainsKey((Guid)key.Identifier);
                    }

                    break;
                }

                case IdType.Opaque:
                {
                    IDictionary<ByteKey, T> dictionary = GetOpaqueDictionary(key.NamespaceIndex, false);

                    if (dictionary != null)
                    {
                        return dictionary.ContainsKey(new ByteKey((byte[])key.Identifier));
                    }

                    break;
                }
            }

            return false;
        }

        /// <summary cref="IDictionary{TKey,TValue}.Keys" />
        public ICollection<NodeId> Keys
        {
            get
            {
                List<NodeId> keys = new List<NodeId>();

                foreach (ulong id in m_numericIds.Keys)
                {
                    keys.Add(new NodeId((uint)(id & 0xFFFFFFFF), (ushort)((id >> 32) & 0xFFFF)));
                }

                if (m_dictionarySets == null)
                {
                    return keys;
                }

                for (ushort ii = 0; ii < (ushort)m_dictionarySets.Length; ii++)
                {
                    DictionarySet dictionarySet = m_dictionarySets[ii];

                    if (dictionarySet == null)
                    {
                        continue;
                    }

                    if (dictionarySet.String != null)
                    {
                        foreach (string id in dictionarySet.String.Keys)
                        {
                            keys.Add(new NodeId(id, ii));
                        }
                    }

                    if (dictionarySet.Guid != null)
                    {
                        foreach (Guid id in dictionarySet.Guid.Keys)
                        {
                            keys.Add(new NodeId(id, ii));
                        }
                    }

                    if (dictionarySet.Opaque != null)
                    {
                        foreach (ByteKey id in dictionarySet.Opaque.Keys)
                        {
                            keys.Add(new NodeId(id.Bytes, ii));
                        }
                    }
                }

                return keys;
            }
        }

        /// <summary cref="IDictionary.Remove" />
        public bool Remove(NodeId key)
        {
            if (key == null)
            {
                return false;
            }

            m_version++;

            switch (key.IdType)
            {
                case IdType.Numeric:
                {
                    ulong id = ((ulong)key.NamespaceIndex) << 32;
                    id += (uint)key.Identifier;
                    return m_numericIds.Remove(id);
                }

                case IdType.String:
                {
                    IDictionary<string, T> dictionary = GetStringDictionary(key.NamespaceIndex, false);

                    if (dictionary != null)
                    {
                        return dictionary.Remove((string)key.Identifier);
                    }

                    break;
                }

                case IdType.Guid:
                {
                    IDictionary<Guid, T> dictionary = GetGuidDictionary(key.NamespaceIndex, false);

                    if (dictionary != null)
                    {
                        return dictionary.Remove((Guid)key.Identifier);
                    }

                    break;
                }

                case IdType.Opaque:
                {
                    IDictionary<ByteKey, T> dictionary = GetOpaqueDictionary(key.NamespaceIndex, false);

                    if (dictionary != null)
                    {
                        return dictionary.Remove(new ByteKey((byte[])key.Identifier));
                    }

                    break;
                }
            }

            return false;
        }

        /// <summary cref="IDictionary{TKey,TValue}.TryGetValue" />
        public bool TryGetValue(NodeId key, out T value)
        {
            value = default(T);

            if (key == null)
            {
                return false;
            }

            switch (key.IdType)
            {
                case IdType.Numeric:
                {
                    ulong id = ((ulong)key.NamespaceIndex) << 32;
                    id += (uint)key.Identifier;
                    return m_numericIds.TryGetValue(id, out value);
                }

                case IdType.String:
                {
                    IDictionary<string, T> dictionary = GetStringDictionary(key.NamespaceIndex, false);

                    if (dictionary != null)
                    {
                        return dictionary.TryGetValue((string)key.Identifier, out value);
                    }

                    break;
                }

                case IdType.Guid:
                {
                    IDictionary<Guid, T> dictionary = GetGuidDictionary(key.NamespaceIndex, false);

                    if (dictionary != null)
                    {
                        return dictionary.TryGetValue((Guid)key.Identifier, out value);
                    }

                    break;
                }

                case IdType.Opaque:
                {
                    IDictionary<ByteKey, T> dictionary = GetOpaqueDictionary(key.NamespaceIndex, false);

                    if (dictionary != null)
                    {
                        return dictionary.TryGetValue(new ByteKey((byte[])key.Identifier), out value);
                    }

                    break;
                }
            }

            return false;
        }

        /// <summary cref="IDictionary{TKey,TValue}.Values" />
        public ICollection<T> Values
        {
            get
            {
                List<T> values = new List<T>();
                values.AddRange(m_numericIds.Values);

                if (m_dictionarySets == null)
                {
                    return values;
                }

                for (int ii = 0; ii < m_dictionarySets.Length; ii++)
                {
                    DictionarySet dictionarySet = m_dictionarySets[ii];

                    if (dictionarySet == null)
                    {
                        continue;
                    }

                    if (dictionarySet.String != null)
                    {
                        values.AddRange(dictionarySet.String.Values);
                    }

                    if (dictionarySet.Guid != null)
                    {
                        values.AddRange(dictionarySet.Guid.Values);
                    }

                    if (dictionarySet.Opaque != null)
                    {
                        values.AddRange(dictionarySet.Opaque.Values);
                    }
                }

                return values;
            }
        }

        /// <summary>
        /// Gets or sets the value with the specified NodeId.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1065:DoNotRaiseExceptionsInUnexpectedLocations")]
        public T this[NodeId key]
        {
            get
            {
                if (key == null)
                {
                    throw new ArgumentNullException(nameof(key));
                }

                switch (key.IdType)
                {
                    case IdType.Numeric:
                    {
                        ulong id = ((ulong)key.NamespaceIndex) << 32;
                        id += (uint)key.Identifier;
                        return m_numericIds[id];
                    }

                    case IdType.String:
                    {
                        IDictionary<string, T> dictionary = GetStringDictionary(key.NamespaceIndex, false);

                        if (dictionary != null)
                        {
                            return dictionary[(string)key.Identifier];
                        }

                        break;
                    }

                    case IdType.Guid:
                    {
                        IDictionary<Guid, T> dictionary = GetGuidDictionary(key.NamespaceIndex, false);

                        if (dictionary != null)
                        {
                            return dictionary[(Guid)key.Identifier];
                        }

                        break;
                    }

                    case IdType.Opaque:
                    {
                        IDictionary<ByteKey, T> dictionary = GetOpaqueDictionary(key.NamespaceIndex, false);

                        if (dictionary != null)
                        {
                            return dictionary[new ByteKey((byte[])key.Identifier)];
                        }

                        break;
                    }
                }

                throw new KeyNotFoundException();
            }

            set
            {
                if (key == null)
                {
                    throw new ArgumentNullException(nameof(key));
                }

                m_version++;

                switch (key.IdType)
                {
                    case IdType.Numeric:
                    {
                        ulong id = ((ulong)key.NamespaceIndex) << 32;
                        id += (uint)key.Identifier;
                        m_numericIds[id] = value;
                        return;
                    }

                    case IdType.String:
                    {
                        IDictionary<string, T> dictionary = GetStringDictionary(key.NamespaceIndex, true);
                        dictionary[(string)key.Identifier] = value;
                        return;
                    }

                    case IdType.Guid:
                    {
                        IDictionary<Guid, T> dictionary = GetGuidDictionary(key.NamespaceIndex, true);
                        dictionary[(Guid)key.Identifier] = value;
                        return;
                    }

                    case IdType.Opaque:
                    {
                        IDictionary<ByteKey, T> dictionary = GetOpaqueDictionary(key.NamespaceIndex, true);
                        dictionary[new ByteKey((byte[])key.Identifier)] = value;
                        return;
                    }
                }

                throw new ArgumentOutOfRangeException(nameof(key), "key.IdType");
            }
        }
        #endregion

        #region ICollection<KeyValuePair<NodeId,T>> Members
        /// <summary cref="ICollection{T}.Add" />
        public void Add(KeyValuePair<NodeId, T> item)
        {
            Add(item.Key, item.Value);
        }

        /// <summary cref="ICollection{T}.Clear" />
        public void Clear()
        {
            m_version++;
            m_numericIds.Clear();
            m_dictionarySets = null;
        }

        /// <summary cref="ICollection{T}.Contains" />
        public bool Contains(KeyValuePair<NodeId, T> item)
        {
            T value;

            if (!TryGetValue(item.Key, out value))
            {
                return false;
            }

            return Object.Equals(value, item.Value);
        }

        /// <summary cref="ICollection{T}.CopyTo" />
        public void CopyTo(KeyValuePair<NodeId, T>[] array, int arrayIndex)
        {
            if (array == null)
            {
                throw new ArgumentNullException(nameof(array));
            }

            if (arrayIndex < 0 || array.Length <= arrayIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(arrayIndex), "arrayIndex < 0 || array.Length <= arrayIndex");
            }

            foreach (KeyValuePair<ulong, T> entry in m_numericIds)
            {
                CheckCopyTo(array, arrayIndex);

                array[arrayIndex++] = new KeyValuePair<NodeId, T>(
                    new NodeId((uint)(entry.Key & 0xFFFFFFFF), (ushort)((entry.Key >> 32) & 0xFFFF)),
                    entry.Value);
            }

            if (m_dictionarySets == null)
            {
                return;
            }

            for (int ii = 0; ii < m_dictionarySets.Length; ii++)
            {
                DictionarySet dictionarySet = m_dictionarySets[ii];

                if (dictionarySet == null)
                {
                    continue;
                }

                if (dictionarySet.String != null)
                {
                    foreach (KeyValuePair<string, T> entry in dictionarySet.String)
                    {
                        CheckCopyTo(array, arrayIndex);
                        array[arrayIndex++] = new KeyValuePair<NodeId, T>(new NodeId(entry.Key, (ushort)ii), entry.Value);
                    }
                }

                if (dictionarySet.Guid != null)
                {
                    foreach (KeyValuePair<Guid, T> entry in dictionarySet.Guid)
                    {
                        CheckCopyTo(array, arrayIndex);
                        array[arrayIndex++] = new KeyValuePair<NodeId, T>(new NodeId(entry.Key, (ushort)ii), entry.Value);
                    }
                }

                if (dictionarySet.Opaque != null)
                {
                    foreach (KeyValuePair<ByteKey, T> entry in dictionarySet.Opaque)
                    {
                        CheckCopyTo(array, arrayIndex);
                        array[arrayIndex++] = new KeyValuePair<NodeId, T>(new NodeId(entry.Key.Bytes, (ushort)ii), entry.Value);
                    }
                }
            }
        }

        /// <summary>
        /// Checks that there is enough space in the array.
        /// </summary>
        private static void CheckCopyTo(KeyValuePair<NodeId, T>[] array, int arrayIndex)
        {
            if (arrayIndex >= array.Length)
            {
                throw new ArgumentException("Not enough space in array.", nameof(array));
            }
        }

        /// <summary cref="ICollection{T}.Count" />
        public int Count
        {
            get
            {
                int count = m_numericIds.Count;

                if (m_dictionarySets == null)
                {
                    return count;
                }

                for (int ii = 0; ii < m_dictionarySets.Length; ii++)
                {
                    DictionarySet dictionarySet = m_dictionarySets[ii];

                    if (dictionarySet == null)
                    {
                        continue;
                    }

                    if (dictionarySet.String != null)
                    {
                        count += dictionarySet.String.Count;
                    }

                    if (dictionarySet.Guid != null)
                    {
                        count += dictionarySet.Guid.Count;
                    }

                    if (dictionarySet.Opaque != null)
                    {
                        count += dictionarySet.Opaque.Count;
                    }
                }

                return count;
            }
        }

        /// <summary cref="ICollection{T}.IsReadOnly" />
        public bool IsReadOnly => false;

        /// <summary cref="ICollection{T}.Remove" />
        public bool Remove(KeyValuePair<NodeId, T> item)
        {
            return Remove(item.Key);
        }
        #endregion

        #region IEnumerable<KeyValuePair<NodeId,T>> Members
        /// <summary cref="System.Collections.IEnumerable.GetEnumerator()" />
        public IEnumerator<KeyValuePair<NodeId, T>> GetEnumerator()
        {
            return new Enumerator(this);
        }
        #endregion

        #region IEnumerable Members
        /// <summary cref="System.Collections.IEnumerable.GetEnumerator()" />
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Returns the dictionary set for the specified namespace.
        /// </summary>
        private DictionarySet GetDictionarySet(ushort namespaceIndex, bool create)
        {
            if (m_dictionarySets == null || m_dictionarySets.Length <= namespaceIndex)
            {
                if (!create)
                {
                    return null;
                }

                DictionarySet[] dictionarySets = new NodeIdDictionary<T>.DictionarySet[namespaceIndex + 1];

                if (m_dictionarySets != null)
                {
                    Array.Copy(m_dictionarySets, dictionarySets, m_dictionarySets.Length);
                }

                m_dictionarySets = dictionarySets;
            }

            DictionarySet dictionarySet = m_dictionarySets[namespaceIndex];

            if (dictionarySet == null)
            {
                if (!create)
                {
                    return null;
                }

                m_dictionarySets[namespaceIndex] = dictionarySet = new NodeIdDictionary<T>.DictionarySet();
            }

            return dictionarySet;
        }

        /// <summary>
        /// Returns the dictionary set for String identifiers in the specified namespace.
        /// </summary>
        private IDictionary<string, T> GetStringDictionary(ushort namespaceIndex, bool create)
        {
            DictionarySet dictionarySet = GetDictionarySet(namespaceIndex, create);

            if (dictionarySet == null)
            {
                return null;
            }

            IDictionary<string, T> dictionary = dictionarySet.String;

            if (dictionary == null)
            {
                if (!create)
                {
                    return null;
                }

                dictionary = dictionarySet.String = new SortedDictionary<string, T>();
            }

            return dictionary;
        }

        /// <summary>
        /// Returns the dictionary set for Guid identifiers in the specified namespace.
        /// </summary>
        private IDictionary<Guid, T> GetGuidDictionary(ushort namespaceIndex, bool create)
        {
            DictionarySet dictionarySet = GetDictionarySet(namespaceIndex, create);

            if (dictionarySet == null)
            {
                return null;
            }

            IDictionary<Guid, T> dictionary = dictionarySet.Guid;

            if (dictionary == null)
            {
                if (!create)
                {
                    return null;
                }

                dictionary = dictionarySet.Guid = new SortedDictionary<Guid, T>();
            }

            return dictionary;
        }

        /// <summary>
        /// Returns the dictionary set for Opaque identifiers in the specified namespace.
        /// </summary>
        private IDictionary<ByteKey, T> GetOpaqueDictionary(ushort namespaceIndex, bool create)
        {
            DictionarySet dictionarySet = GetDictionarySet(namespaceIndex, create);

            if (dictionarySet == null)
            {
                return null;
            }

            IDictionary<ByteKey, T> dictionary = dictionarySet.Opaque;

            if (dictionary == null)
            {
                if (!create)
                {
                    return null;
                }

                dictionary = dictionarySet.Opaque = new SortedDictionary<ByteKey, T>();
            }

            return dictionary;
        }
        #endregion

        #region DictionarySet Class
        /// <summary>
        /// Stores the dictionaries for a single namespace index.
        /// </summary>
        private class DictionarySet
        {
            public SortedDictionary<string, T> String;
            public SortedDictionary<Guid, T> Guid;
            public SortedDictionary<ByteKey, T> Opaque;
        }
        #endregion

        #region ByteKey Class
        /// <summary>
        /// Wraps a byte array for use as a key in a dictionary.
        /// </summary>
        private struct ByteKey : IEquatable<ByteKey>, IComparable<ByteKey>
        {
            #region Public Interface
            /// <summary>
            /// Initializes the key with an array of bytes.
            /// </summary>
            public ByteKey(byte[] bytes)
            {
                Bytes = bytes;
            }

            /// <summary>
            /// The array of bytes.
            /// </summary>
            public byte[] Bytes;
            #endregion

            #region IEquatable<ByteKey> Members
            /// <summary cref="IEquatable{T}"></summary>
            public bool Equals(ByteKey other)
            {
                if (other.Bytes == null || Bytes == null)
                {
                    return (other.Bytes == null && Bytes == null);
                }

                if (other.Bytes.Length != Bytes.Length)
                {
                    return false;
                }

                for (int ii = 0; ii < other.Bytes.Length; ii++)
                {
                    if (other.Bytes[ii] != Bytes[ii])
                    {
                        return false;
                    }
                }

                return false;
            }
            #endregion

            #region IComparable<ByteKey> Members
            /// <summary cref="IComparable{T}.CompareTo"></summary>
            public int CompareTo(ByteKey other)
            {
                if (other.Bytes == null || Bytes == null)
                {
                    return (other.Bytes == null) ? +1 : -1;
                }

                if (other.Bytes.Length != Bytes.Length)
                {
                    return (other.Bytes.Length < Bytes.Length) ? +1 : -1;
                }

                for (int ii = 0; ii < other.Bytes.Length; ii++)
                {
                    if (other.Bytes[ii] != Bytes[ii])
                    {
                        return (other.Bytes[ii] < Bytes[ii]) ? +1 : -1;
                    }
                }

                return 0;
            }
            #endregion
        }
        #endregion        

        #region Enumerator Class
        /// <summary>
        /// The enumerator for the node dictionary.
        /// </summary>
        private class Enumerator : IEnumerator<KeyValuePair<NodeId, T>>
        {
            #region Constructors
            /// <summary>
            /// Constructs the enumerator for the specified dictionary.
            /// </summary>
            public Enumerator(NodeIdDictionary<T> dictionary)
            {
                m_dictionary = dictionary;
                m_version = dictionary.m_version;
                m_idType = 0;
                m_namespaceIndex = 0;
            }
            #endregion

            #region IEnumerator<KeyValuePair<NodeId,T>> Members
            /// <summary cref="IEnumerator{T}.Current" />
            public KeyValuePair<NodeId, T> Current
            {
                get
                {
                    CheckVersion();

                    if (m_enumerator == null)
                    {
                        throw new InvalidOperationException("The enumerator is positioned before the first element of the collection or after the last element.");
                    }

                    NodeId id = null;

                    switch (m_idType)
                    {
                        case IdType.Numeric:
                        {
                            ulong key = (ulong)m_enumerator.Key;
                            id = new NodeId((uint)(key & 0xFFFFFFFF), (ushort)((key >> 32) & 0xFFFF));
                            break;
                        }

                        case IdType.String:
                        {
                            id = new NodeId((string)m_enumerator.Key, m_namespaceIndex);
                            break;
                        }

                        case IdType.Guid:
                        {
                            id = new NodeId((Guid)m_enumerator.Key, m_namespaceIndex);
                            break;
                        }

                        case IdType.Opaque:
                        {
                            id = new NodeId(((ByteKey)m_enumerator.Key).Bytes, m_namespaceIndex);
                            break;
                        }
                    }

                    return new KeyValuePair<NodeId, T>(id, (T)m_enumerator.Value);
                }
            }
            #endregion

            #region IDisposable Members
            /// <summary>
            /// Frees any unmanaged resources.
            /// </summary>
            public void Dispose()
            {
                Dispose(true);
            }

            /// <summary>
            /// An overrideable version of the Dispose.
            /// </summary>
            protected virtual void Dispose(bool disposing)
            {
                if (disposing)
                {
                    // do to nothing.
                }
            }
            #endregion

            #region IEnumerator Members
            /// <summary cref="IEnumerator.Current" />
            object System.Collections.IEnumerator.Current => this.Current;

            /// <summary cref="IEnumerator.MoveNext" />
            public bool MoveNext()
            {
                CheckVersion();

                if (m_enumerator == null)
                {
                    m_enumerator = m_dictionary.m_numericIds.GetEnumerator();
                    m_idType = IdType.Numeric;
                    m_namespaceIndex = 0;
                }

                bool result = m_enumerator.MoveNext();

                if (result)
                {
                    return true;
                }

                while (m_dictionary.m_dictionarySets != null && m_namespaceIndex < m_dictionary.m_dictionarySets.Length)
                {
                    if (m_idType == IdType.Numeric)
                    {
                        m_idType = IdType.String;

                        IDictionary<string, T> dictionary = m_dictionary.GetStringDictionary(m_namespaceIndex, false);

                        if (dictionary != null)
                        {
                            ReleaseEnumerator();
                            m_enumerator = (IDictionaryEnumerator)dictionary.GetEnumerator();

                            if (m_enumerator.MoveNext())
                            {
                                return true;
                            }
                        }
                    }

                    if (m_idType == IdType.String)
                    {
                        m_idType = IdType.Guid;

                        IDictionary<Guid, T> dictionary = m_dictionary.GetGuidDictionary(m_namespaceIndex, false);

                        if (dictionary != null)
                        {
                            ReleaseEnumerator();
                            m_enumerator = (IDictionaryEnumerator)dictionary.GetEnumerator();

                            if (m_enumerator.MoveNext())
                            {
                                return true;
                            }
                        }
                    }

                    if (m_idType == IdType.Guid)
                    {
                        m_idType = IdType.Opaque;

                        IDictionary<ByteKey, T> dictionary = m_dictionary.GetOpaqueDictionary(m_namespaceIndex, false);

                        if (dictionary != null)
                        {
                            ReleaseEnumerator();
                            m_enumerator = (IDictionaryEnumerator)dictionary.GetEnumerator();

                            if (m_enumerator.MoveNext())
                            {
                                return true;
                            }
                        }
                    }

                    m_idType = IdType.Numeric;
                    m_namespaceIndex++;
                }

                ReleaseEnumerator();
                return false;
            }

            /// <summary cref="IEnumerator.Reset" />
            public void Reset()
            {
                CheckVersion();
                ReleaseEnumerator();
                m_idType = 0;
                m_namespaceIndex = 0;
            }
            #endregion

            #region Private Methods
            /// <summary>
            /// Releases and disposes the current enumerator.
            /// </summary>
            private void ReleaseEnumerator()
            {
                if (m_enumerator != null)
                {
                    IDisposable diposeable = m_enumerator as IDisposable;

                    if (diposeable != null)
                    {
                        diposeable.Dispose();
                    }

                    m_enumerator = null;
                }
            }

            /// <summary>
            /// Checks if the dictionary has changed.
            /// </summary>
            private void CheckVersion()
            {
                if (m_version != m_dictionary.m_version)
                {
                    throw new InvalidOperationException("The dictionary was modified after the enumerator was created.");
                }
            }
            #endregion

            #region Private Fields
            private NodeIdDictionary<T> m_dictionary;
            private ushort m_namespaceIndex;
            private IdType m_idType;
            private IDictionaryEnumerator m_enumerator;
            private ulong m_version;
            #endregion
        }
        #endregion

        #region Private Fields
        private DictionarySet[] m_dictionarySets;
        private SortedDictionary<ulong, T> m_numericIds;
        private ulong m_version;
        #endregion
    }
}
