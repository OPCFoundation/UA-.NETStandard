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
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using Opc.Ua.Client;

namespace Opc.Ua.Com.Server
{   
    /// <summary>
    /// A class that manages the mapping between the local and remote namespace indexes.
    /// </summary>
    public class ComNamespaceMapper
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComNamespaceMapper"/> class.
        /// </summary>
        public ComNamespaceMapper()
		{
        }
        #endregion

        #region Generic Mapping Methods
        /// <summary>
        /// Initializes the namespace mappings and updates the proxy configuration.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="configuration">The configuration.</param>
        public virtual void Initialize(Session session, ComProxyConfiguration configuration)
        {
            lock (m_lock)
            {
                m_typeTable = session.TypeTree;

                // index namspace uris.
                if (configuration.NamespaceUris == null)
                {
                    configuration.NamespaceUris = new StringCollection();
                }

                m_namespaceUris = new StringTableMapping();
                m_namespaceUris.Initialize(configuration.NamespaceUris, session.NamespaceUris);

                // index server uris.
                if (configuration.ServerUris == null)
                {
                    configuration.ServerUris = new StringCollection();
                }

                m_serverUris = new StringTableMapping();
                m_serverUris.Initialize(configuration.ServerUris, session.ServerUris);

                // create message context.
                ServiceMessageContext context = new ServiceMessageContext();

                context.MaxArrayLength = session.MessageContext.MaxArrayLength;
                context.MaxByteStringLength = session.MessageContext.MaxByteStringLength;
                context.MaxMessageSize = session.MessageContext.MaxMessageSize;
                context.MaxStringLength = session.MessageContext.MaxStringLength;
                context.Factory = session.MessageContext.Factory;
                context.NamespaceUris = new NamespaceTable();
                context.ServerUris = new StringTable();

                // copy namespace uris.
                context.NamespaceUris.Append(session.NamespaceUris.GetString(1));

                for (int ii = 0; ii < configuration.NamespaceUris.Count; ii++)
                {
                    context.NamespaceUris.Append(configuration.NamespaceUris[ii]);
                }

                // copy server uris.
                context.ServerUris.Append(session.ServerUris.GetString(0));

                for (int ii = 0; ii < configuration.ServerUris.Count; ii++)
                {
                    context.ServerUris.Append(configuration.ServerUris[ii]);
                }

                m_localMessageContext = context;

                // index mapping sets by the name assigned.
                if (configuration.MappingSets != null)
                {
                    m_mappingSets = new Dictionary<string, NodeIdMappingSet>();

                    for (int ii = 0; ii < configuration.MappingSets.Count; ii++)
                    {
                        UpdateMappingSet(configuration.MappingSets[ii]);
                    }
                }
            }
        }

        /// <summary>
        /// Updates an integer id mapping set.
        /// </summary>
        public void UpdateMappingSet(NodeIdMappingSet mappingSet)
        {
            lock (m_lock)
            {
                if (m_mappingSets == null)
                {
                    m_mappingSets = new Dictionary<string, NodeIdMappingSet>();
                }

                string mappingType = mappingSet.MappingType;

                if (mappingType == null)
                {
                    mappingType = String.Empty;
                }

                m_mappingSets[mappingType] = mappingSet;
            }
        }

        /// <summary>
        /// Gets the remote node id associated with an integer id.
        /// </summary>
        /// <param name="mappingType">Type of the mapping.</param>
        /// <param name="integerId">The integer id.</param>
        /// <returns>The mapping; null if not found.</returns>
        public NodeId GetRemoteIntegerIdMapping(string mappingType, uint integerId)
        {
            lock (m_lock)
            {
                // check if no mappings defined.
                if (m_mappingSets == null)
                {
                    return null;
                }

                if (mappingType == null)
                {
                    mappingType = String.Empty;
                }

                // check for an existing mapping.
                NodeIdMappingSet mappingSet = null;

                if (!m_mappingSets.TryGetValue(mappingType, out mappingSet))
                {
                    return null;
                }

                // search for a existing integer id.
                if (mappingSet.Mappings != null)
                {
                    for (int ii = 0; ii < mappingSet.Mappings.Count; ii++)
                    {
                        NodeIdMapping mapping = mappingSet.Mappings[ii];

                        if (integerId == mapping.IntegerId)
                        {
                            return GetRemoteNodeId(mapping.NodeId);
                        }
                    }
                }

                // not found.
                return null;
            }
        }

        /// <summary>
        /// Gets the integer mapping for a remote node id.
        /// </summary>
        /// <param name="mappingType">Type of the mapping.</param>
        /// <param name="remoteId">The remote node id.</param>
        /// <returns>The mapping; 0 if not found.</returns>
        public uint GetLocalIntegerIdMapping(string mappingType, NodeId remoteId)
        {
            lock (m_lock)
            {
                // check if no mappings defined.
                if (m_mappingSets == null)
                {
                    return 0;
                }

                if (mappingType == null)
                {
                    mappingType = String.Empty;
                }

                // check for an existing mapping.
                NodeIdMappingSet mappingSet = null;

                if (!m_mappingSets.TryGetValue(mappingType, out mappingSet))
                {
                    return 0;
                }

                // search for a existing integer id.
                NodeId localId = GetLocalNodeId(remoteId);

                if (mappingSet.Mappings != null)
                {
                    for (int ii = 0; ii < mappingSet.Mappings.Count; ii++)
                    {
                        NodeIdMapping mapping = mappingSet.Mappings[ii];

                        if (localId == mapping.NodeId)
                        {
                            return mapping.IntegerId;
                        }
                    }
                }

                // not found.
                return 0;
            }
        }

        /// <summary>
        /// Gets the remote index for the local namespace index.
        /// </summary>
        /// <param name="localIndex">The local namespace index.</param>
        /// <returns>The remote namespace index; UInt16.MaxValue if no mapping exists.</returns>
        public ushort GetRemoteNamespaceIndex(ushort localIndex)
        {
            if (localIndex < 2)
            {
                return localIndex;
            }

            lock (m_lock)
            {
                int remoteIndex = m_namespaceUris.LocalToRemote[localIndex];

                if (remoteIndex < 0)
                {
                    return UInt16.MaxValue;
                }

                return (ushort)remoteIndex;
            }
        }

        /// <summary>
        /// Gets the remote index for the local server index.
        /// </summary>
        /// <param name="localIndex">The local server index.</param>
        /// <returns>The remote server index; UInt32.MaxValue if no mapping exists.</returns>
        public uint GetRemoteServerIndex(uint localIndex)
        {
            if (localIndex < 2)
            {
                return localIndex;
            }

            lock (m_lock)
            {
                int remoteIndex = m_serverUris.LocalToRemote[localIndex];

                if (remoteIndex < 0)
                {
                    return UInt32.MaxValue;
                }

                return (uint)remoteIndex;
            }
        }
        
        /// <summary>
        /// Gets the local index for the remote namespace index.
        /// </summary>
        /// <param name="remoteIndex">The remote namespace index.</param>
        /// <returns>The remote namespace index; UInt16.MaxValue if no mapping exists.</returns>
        public ushort GetLocalNamespaceIndex(ushort remoteIndex)
        {
            if (remoteIndex < 2)
            {
                return remoteIndex;
            }

            lock (m_lock)
            {
                int localIndex = m_namespaceUris.RemoteToLocal[remoteIndex];

                if (localIndex < 0)
                {
                    return UInt16.MaxValue;
                }

                return (ushort)localIndex;
            }
        }

        /// <summary>
        /// Gets the local index for the remote server index.
        /// </summary>
        /// <param name="remoteIndex">The remote server index.</param>
        /// <returns>The remote namespace index; UInt16.MaxValue if no mapping exists.</returns>
        public uint GetLocalServerIndex(ushort remoteIndex)
        {
            if (remoteIndex < 1)
            {
                return remoteIndex;
            }

            lock (m_lock)
            {
                int localIndex = m_serverUris.RemoteToLocal[remoteIndex];

                if (localIndex < 0)
                {
                    return UInt32.MaxValue;
                }

                return (uint)localIndex;
            }
        }

        /// <summary>
        /// Gets the remote node id.
        /// </summary>
        /// <param name="localId">The local id.</param>
        /// <returns>The remote node id.</returns>
        public NodeId GetRemoteNodeId(NodeId localId)
        {
            if (localId == null || localId.NamespaceIndex == 0)
            {
                return localId;
            }

            ushort remoteIndex = GetRemoteNamespaceIndex(localId.NamespaceIndex);

            if (remoteIndex == localId.NamespaceIndex)
            {
                return localId;
            }

            return new NodeId(localId.Identifier, remoteIndex);
        }

        /// <summary>
        /// Gets the local node id.
        /// </summary>
        /// <param name="remoteId">The remote id.</param>
        /// <returns>The local node id.</returns>
        public NodeId GetLocalNodeId(NodeId remoteId)
        {
            if (remoteId == null || remoteId.NamespaceIndex == 0)
            {
                return remoteId;
            }

            ushort localIndex = GetLocalNamespaceIndex(remoteId.NamespaceIndex);

            if (localIndex == remoteId.NamespaceIndex)
            {
                return remoteId;
            }

            return new NodeId(remoteId.Identifier, localIndex);
        }

        /// <summary>
        /// Gets the remote node id.
        /// </summary>
        /// <param name="localId">The local id.</param>
        /// <returns>The remote node id.</returns>
        public ExpandedNodeId GetRemoteExpandedNodeId(ExpandedNodeId localId)
        {
            if (localId == null)
            {
                return localId;
            }

            ushort localNamespaceIndex = localId.NamespaceIndex;
            uint localServerIndex = localId.ServerIndex;

            ushort remoteNamespaceIndex = GetRemoteNamespaceIndex(localNamespaceIndex);
            uint remoteServerIndex = GetRemoteServerIndex(localServerIndex);

            if (localNamespaceIndex == remoteNamespaceIndex && localServerIndex == remoteServerIndex)
            {
                return localId;
            }

            return new ExpandedNodeId(localId.Identifier, remoteNamespaceIndex, localId.NamespaceUri, remoteServerIndex);
        }

        /// <summary>
        /// Gets the local node id.
        /// </summary>
        /// <param name="remoteId">The remote id.</param>
        /// <returns>The local node id.</returns>
        public ExpandedNodeId GetLocaleExpandedNodeId(ExpandedNodeId remoteId)
        {
            if (remoteId == null)
            {
                return remoteId;
            }

            ushort remoteNamespaceIndex = remoteId.NamespaceIndex;
            uint remoteServerIndex = remoteId.ServerIndex;

            ushort localNamespaceIndex = GetRemoteNamespaceIndex(remoteNamespaceIndex);
            uint localServerIndex = GetRemoteServerIndex(remoteServerIndex);

            if (localNamespaceIndex == remoteNamespaceIndex && localServerIndex == remoteServerIndex)
            {
                return remoteId;
            }

            return new ExpandedNodeId(remoteId.Identifier, localNamespaceIndex, remoteId.NamespaceUri, localServerIndex);
        }

        /// <summary>
        /// Gets the remote qualified name.
        /// </summary>
        /// <param name="localName">The local qualified name.</param>
        /// <returns>The remote qualified name.</returns>
        public QualifiedName GetRemoteQualifiedName(QualifiedName localName)
        {
            if (localName == null || localName.NamespaceIndex == 0)
            {
                return localName;
            }

            ushort remoteIndex = GetRemoteNamespaceIndex(localName.NamespaceIndex);

            if (remoteIndex == localName.NamespaceIndex)
            {
                return localName;
            }

            return new QualifiedName(localName.Name, remoteIndex);
        }

        /// <summary>
        /// Gets the c qualified name.
        /// </summary>
        /// <param name="localName">Name of the local.</param>
        /// <returns>The remote qualified name.</returns>
        public QualifiedName GetLocalQualifiedName(QualifiedName localName)
        {
            if (localName == null || localName.NamespaceIndex == 0)
            {
                return localName;
            }

            ushort remoteIndex = GetRemoteNamespaceIndex(localName.NamespaceIndex);

            if (remoteIndex == localName.NamespaceIndex)
            {
                return localName;
            }

            return new QualifiedName(localName.Name, remoteIndex);
        }

        /// <summary>
        /// Returns a list of remote browse paths for a list of local relative paths.
        /// </summary>
        public BrowsePathCollection GetRemoteBrowsePaths(NodeId localNodeId, params string[] relativePaths)
        {
            BrowsePathCollection browsePaths = new BrowsePathCollection();

            if (relativePaths != null)
            {
                for (int ii = 0; ii < relativePaths.Length; ii++)
                {
                    BrowsePath browsePath = new BrowsePath();

                    browsePath.RelativePath = RelativePath.Parse(relativePaths[ii], this.m_typeTable);
                    browsePath.StartingNode = GetRemoteNodeId(localNodeId);

                    for (int jj = 0; jj < browsePath.RelativePath.Elements.Count; jj++)
                    {
                        QualifiedName targetName = browsePath.RelativePath.Elements[jj].TargetName;
                        targetName = GetRemoteQualifiedName(targetName);
                        browsePath.RelativePath.Elements[jj].TargetName = targetName;

                        NodeId referenceTypeId = browsePath.RelativePath.Elements[jj].ReferenceTypeId;
                        referenceTypeId = GetRemoteNodeId(referenceTypeId);
                        browsePath.RelativePath.Elements[jj].ReferenceTypeId = referenceTypeId;
                    }

                    browsePaths.Add(browsePath);
                }
            }

            return browsePaths;
        }
        #endregion

        #region COM DA Specific Mapping Methods
        /// <summary>
        /// Gets the local data value.
        /// </summary>
        /// <param name="remoteValue">The remote value.</param>
        /// <returns>The local data value.</returns>
        public DaValue GetLocalDataValue(DataValue remoteValue)
        {
            DaValue localValue = new DaValue();

            localValue.Error = ComDaProxy.MapReadStatusToErrorCode(remoteValue.StatusCode);

            if (localValue.Error >= 0)
            {
                localValue.HdaQuality = ComUtils.GetHdaQualityCode(remoteValue.StatusCode);
                localValue.Timestamp = remoteValue.SourceTimestamp;
                localValue.Error = ResultIds.S_OK;

                if (localValue.Timestamp == DateTime.MinValue)
                {
                    localValue.Timestamp = remoteValue.ServerTimestamp;
                }

                try
                {
                    localValue.Value = GetLocalValue(remoteValue.WrappedValue);
                }
                catch
                {
                    localValue.Error = ResultIds.E_BADTYPE;
                }
            }

            return localValue;
        }

        /// <summary>
        /// Gets the remote data value.
        /// </summary>
        /// <param name="localValue">The local value.</param>
        /// <param name="remoteType">The remote data type.</param>
        /// <returns>The remote data value.</returns>
        /// <exception cref="COMException">Thrown if a conversion error occurs.</exception>
        public DataValue GetRemoteDataValue(DaValue localValue, TypeInfo remoteType)
        {
            DataValue remoteValue = new DataValue();
            remoteValue.SourceTimestamp = localValue.Timestamp;

            if (localValue.Error < 0)
            {
                throw ComUtils.CreateComException(localValue.Error);
            }

            remoteValue.StatusCode = ComUtils.GetHdaQualityCode(localValue.HdaQuality);

            try
            {
                remoteValue.WrappedValue = GetRemoteValue(new Variant(localValue.Value), remoteType);
            }
            catch (Exception e)
            {
                throw ComUtils.CreateComException(e, ResultIds.E_BADTYPE);
            }
                        
            return remoteValue;
        }
        
        /// <summary>
        /// Converts a remote variant value to a local variant value.
        /// </summary>
        /// <param name="remoteValue">The remote value.</param>
        /// <returns>The local value.</returns>
        public object GetLocalValue(Variant remoteValue)
        {
            TypeInfo remoteType = remoteValue.TypeInfo;

            if (remoteType == null)
            {
                remoteType = TypeInfo.Construct(remoteValue.Value);
            }

            TypeInfo localType = GetLocalTypeInfo(remoteType);

            // check for array conversions.
            Array remoteArray = remoteValue.Value as Array;

            if (remoteArray != null && remoteType.ValueRank != ValueRanks.Scalar)
            {
                Array localArray = null;

                // convert byte[] arrays to object[] arrays.
                if (remoteType.BuiltInType == BuiltInType.ByteString || remoteType.BuiltInType == BuiltInType.ExtensionObject)
                {
                    localArray = TypeInfo.CastArray(remoteArray, remoteType.BuiltInType, BuiltInType.Null, null);
                    return localArray;
                }

                // convert Variant[] arrays to object[] arrays.
                if (localType.BuiltInType == BuiltInType.Variant)
                {
                    localArray = TypeInfo.CastArray(remoteArray, remoteType.BuiltInType, BuiltInType.Null, ConvertRemoteToLocal);
                    return localArray;
                }

                // convert all other types of arrays.
                localArray = TypeInfo.CastArray(remoteArray, remoteType.BuiltInType, localType.BuiltInType, ConvertRemoteToLocal);
                return localArray;
            }

            object localValue = ConvertRemoteToLocal(remoteValue.Value, remoteType.BuiltInType, localType.BuiltInType);
            return localValue;
        }

        /// <summary>
        /// Converts a local variant value to a remote variant value.
        /// </summary>
        /// <param name="localeValue">The local value.</param>
        /// <param name="remoteType">The expected type for the remote value.</param>
        /// <returns>The remote value.</returns>
        public Variant GetRemoteValue(Variant localeValue, TypeInfo remoteType)
        {
            TypeInfo localType = localeValue.TypeInfo;

            if (localType == null)
            {
                localType = TypeInfo.Construct(localeValue.Value);
            }

            if (localType.BuiltInType == remoteType.BuiltInType || remoteType.BuiltInType == BuiltInType.Variant)
            {
                return localeValue;
            }

            if (localType.BuiltInType == BuiltInType.ByteString && (remoteType.BuiltInType == BuiltInType.Byte && remoteType.ValueRank != ValueRanks.Scalar))
            {
                return localeValue;
            }

            Array localArray = localeValue.Value as Array;
            
            if (localArray != null && !typeof(byte[]).IsInstanceOfType(localArray)) 
            {
                Array remoteArray = TypeInfo.CastArray(localArray, localType.BuiltInType, remoteType.BuiltInType, ConvertLocalToRemote);
                return new Variant(remoteArray, remoteType);
            }

            object remoteValue = ConvertLocalToRemote(localeValue.Value, localType.BuiltInType, remoteType.BuiltInType);
            return new Variant(remoteValue, remoteType);
        }

        private static readonly char[] s_SpecialChars = new char[] { '/', '%' };
        
        /// <summary>
        /// Gets the local name for the qualified name,
        /// </summary>
        /// <param name="browseName">The remote qualified name.</param>
        /// <returns></returns>
        public string GetLocalBrowseName(QualifiedName browseName)
        {
            if (QualifiedName.IsNull(browseName))
            {
                return String.Empty;
            }

            ushort namespaceIndex = GetLocalNamespaceIndex(browseName.NamespaceIndex);
            string name = browseName.Name;

            int index = name.IndexOfAny(s_BrowseNameEscapedChars);

            if (index != -1)
            {
                StringBuilder buffer = new StringBuilder(name.Length);
                buffer.Append(namespaceIndex);
                buffer.Append(':');
                
                for (int ii = 0; ii < name.Length; ii++)
                {
                    if (IsEscapedChar(name[ii]))
                    {
                        buffer.AppendFormat("{0:X2}", (int)name[ii]);
                    }
                    else
                    {
                        buffer.Append(name[ii]);
                    }
                }

                return buffer.ToString();
            }

            // check if the namespace index changed.
            if (browseName.NamespaceIndex != namespaceIndex)
            {
                browseName = new QualifiedName(browseName.Name, namespaceIndex);
            }

            return browseName.ToString();
        }

        private static readonly char[] s_BrowseNameEscapedChars = "/*?#[%".ToCharArray();

        /// <summary>
        /// Returns true if char is one of the special chars that must be escaped.
        /// </summary>
        private static bool IsEscapedChar(char ch)
        {
            for (int ii = 0; ii < s_BrowseNameEscapedChars.Length; ii++)
            {
                if (Char.IsControl(ch) || s_BrowseNameEscapedChars[ii] == ch)
                {
                    return true;
                }
            }

            return false;
        }

        private static readonly char[] s_HexChars = "0123456789ABCDFEF".ToCharArray();

        /// <summary>
        /// Converts to char to a hex number. Returns -1 if char is invalid.
        /// </summary>
        private static int ToHexDigit(char ch)
        {
            ch = Char.ToUpperInvariant(ch);

            for (int ii = 0; ii < s_HexChars.Length; ii++)
            {
                if (s_HexChars[ii] == ch)
                {
                    return ii;
                }
            }

            return -1;
        }

        /// <summary>
        /// Gets the remote qualified name for the local name.
        /// </summary>
        /// <param name="browseName">The local qualified name.</param>
        /// <returns>The remote qualified name.</returns>
        public QualifiedName GetRemoteBrowseName(string browseName)
        {
            // check for null.
            if (String.IsNullOrEmpty(browseName))
            {
                return QualifiedName.Null;
            }

            // parse the browse name.
            QualifiedName qname = QualifiedName.Parse(browseName);

            // construct the qualified name with the remote index.
            ushort namespaceIndex = GetRemoteNamespaceIndex(qname.NamespaceIndex);

            // unescape any special characters in name.
            string name = qname.Name;
            int index = name.IndexOf('%');

            if (index != -1)
            {
                StringBuilder buffer = new StringBuilder(name.Length);

                for (int ii = 0; ii < name.Length; ii++)
                {
                    if (name[ii] == '%')
                    {
                        int code = 0;

                        if (ii < name.Length-2)
                        {
                            int ch = ToHexDigit(name[ii+1]);

                            if (ch > 0)
                            {
                                code = ch * 16;
                                ch = ToHexDigit(name[ii+2]);

                                if (ch > 0)
                                {
                                    code += ch;
                                    buffer.Append((char)code);
                                    continue;
                                }
                            }
                        }
                    }

                    buffer.Append(name[ii]);
                }

                return new QualifiedName(buffer.ToString(), namespaceIndex);
            }

            // check if no translation required.
            if (qname.NamespaceIndex == namespaceIndex)
            {
                return qname;
            }

            // return a new name.
            return new QualifiedName(qname.Name, namespaceIndex);
        }
        
        /// <summary>
        /// Constructs a node id from an item id.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <returns>The node id, null if an error occurred.</returns>
        public NodeId GetRemoteNodeId(string itemId)
        {
            try
            {
                // root is the objects folder.
                if (String.IsNullOrEmpty(itemId))
                {
                    return ObjectIds.ObjectsFolder;
                }

                // must remove ';' from item id since the CTT does not like them.
                #if !NO_CTT_HACK
                StringBuilder buffer = new StringBuilder(itemId.Length+2);

                for (int ii = 0; ii < itemId.Length; ii++)
                {
                    char ch = itemId[ii];

                    if (ch == '*')
                    {
                        if (ii < itemId.Length-1 && itemId[ii+1] == '*')
                        {
                            buffer.Append('*');
                            ii++;
                            continue;
                        }

                        buffer.Append('=');
                        continue;
                    }

                    buffer.Append(ch);
                }

                itemId =  buffer.ToString();
                #endif


                // parse the item id.
                NodeId nodeId = NodeId.Parse(itemId);

                // get the remote index.
                ushort namespaceIndex = GetRemoteNamespaceIndex(nodeId.NamespaceIndex);

                // nothing more to do if they are the same.
                if (namespaceIndex == nodeId.NamespaceIndex)
                {
                    return nodeId;
                }

                // create a new node id.
                if (namespaceIndex != UInt16.MaxValue)
                {
                    return new NodeId(nodeId.Identifier, namespaceIndex);
                }

                return null;
            }
            catch (Exception)
            {
                // oops. could not parse the item id.
                return null;
            }
        }

        /// <summary>
        /// Constructs an item id from a node id.
        /// </summary>
        /// <param name="nodeId">The node id.</param>
        /// <returns>The item id, null if an error occurred.</returns>
        public string GetLocalItemId(NodeId nodeId)
        {
            try
            {
                // check for null.
                if (NodeId.IsNull(nodeId))
                {
                    return null;
                }

                // check for the objects folder (use knowledge of the target to avoid calling the less efficient Equals() method).
                if (nodeId.IdType == IdType.Numeric && nodeId.NamespaceIndex == 0)
                {
                    if ((uint)nodeId.Identifier == Objects.ObjectsFolder)
                    {
                        return String.Empty;
                    }
                }

                // get the lcoal index.
                ushort namespaceIndex = GetLocalNamespaceIndex(nodeId.NamespaceIndex);

                // check if a mapping is required.
                if (namespaceIndex != nodeId.NamespaceIndex)
                {
                    // could happen if the server updates it namespace table.
                    if (namespaceIndex == UInt16.MaxValue)
                    {
                        return null;
                    }

                    nodeId = new NodeId(nodeId.Identifier, namespaceIndex);
                }

                // convert the node id to a string.
                string itemId = nodeId.ToString();

                // must remove ';' from item id since the CTT does not like them.
                #if !NO_CTT_HACK
                StringBuilder buffer = new StringBuilder(itemId.Length+2);

                for (int ii = 0; ii < itemId.Length; ii++)
                {
                    char ch = itemId[ii];

                    if (ch == '=')
                    {
                        buffer.Append('*');
                        continue;
                    }

                    if (ch == '*')
                    {
                        buffer.Append('*');
                        continue;
                    }

                    buffer.Append(ch);
                }

                itemId =  buffer.ToString();
                #endif

                return itemId;
            }
            catch (Exception)
            {
                // oops. probably a programming error.
                return null;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Gets the local type info.
        /// </summary>
        /// <param name="remoteType">The remote type info.</param>
        /// <returns>The local type info.</returns>
        private TypeInfo GetLocalTypeInfo(TypeInfo remoteType)
        {
            BuiltInType localBuiltInType = BuiltInType.Null;

            switch (remoteType.BuiltInType)
            {
                case BuiltInType.Guid:
                case BuiltInType.XmlElement:
                case BuiltInType.NodeId:
                case BuiltInType.ExpandedNodeId:
                case BuiltInType.QualifiedName:
                case BuiltInType.LocalizedText:
                {
                    localBuiltInType = BuiltInType.String;
                    break;
                }

                case BuiltInType.StatusCode:
                {
                    localBuiltInType = BuiltInType.UInt32;
                    break;
                }

                case BuiltInType.ExtensionObject:
                {
                    localBuiltInType = BuiltInType.ByteString;
                    break;
                }

                default:
                {
                    localBuiltInType = remoteType.BuiltInType;
                    break;
                }
            }

            return new TypeInfo(localBuiltInType, remoteType.ValueRank);
        }

        /// <summary>
        /// Converts a remote value to a local value.
        /// </summary>
        /// <param name="srcValue">The remote value.</param>
        /// <param name="srcType">The data type of the remote value.</param>
        /// <param name="dstType">The data type of the local value.</param>
        /// <returns>The local value.</returns>
        private object ConvertRemoteToLocal(object srcValue, BuiltInType srcType, BuiltInType dstType)
        {
            // check for null.
            if (srcValue == null)
            {
                return null;
            }

            // must determine the type from the source if the containing array is a variant.
            if (srcType == BuiltInType.Variant)
            {
                TypeInfo typeInfo = TypeInfo.Construct(srcValue);
                srcType = typeInfo.BuiltInType;

                if (typeInfo.ValueRank != ValueRanks.Scalar)
                {
                    return TypeInfo.CastArray((Array)srcValue, srcType, BuiltInType.Null, ConvertRemoteToLocal);
                }
            }

            // no conversion by default.
            object dstValue = srcValue;

            // apply different conversions depending on the data type.
            switch (srcType)
            {
                case BuiltInType.Guid:
                {
                    dstValue = ((Uuid)srcValue).ToString();
                    break;
                }

                case BuiltInType.XmlElement:
                {
                    dstValue = ((XmlElement)srcValue).OuterXml;
                    break;
                }

                case BuiltInType.NodeId:
                {
                    dstValue = GetLocalItemId((NodeId)srcValue);
                    break;
                }

                case BuiltInType.ExpandedNodeId:
                {
                    ExpandedNodeId nodeId = GetLocaleExpandedNodeId((ExpandedNodeId)srcValue);
                    dstValue = nodeId.ToString();
                    break;
                }

                case BuiltInType.QualifiedName:
                {
                    dstValue = GetLocalBrowseName((QualifiedName)srcValue);
                    break;
                }

                case BuiltInType.LocalizedText:
                {
                    dstValue = ((LocalizedText)srcValue).Text;
                    break;
                }

                case BuiltInType.StatusCode:
                {
                    dstValue = ((StatusCode)srcValue).Code;
                    break;
                }

                case BuiltInType.ExtensionObject:
                {
                    BinaryEncoder encoder = new BinaryEncoder(m_localMessageContext);
                    encoder.WriteExtensionObject(null, (ExtensionObject)srcValue);
                    dstValue = encoder.CloseAndReturnBuffer();
                    break;
                }

                case BuiltInType.Variant:
                {
                    dstValue = ((Variant)srcValue).Value;
                    break;
                }

                default:
                {
                    if (dstType != srcType && dstType != BuiltInType.Variant && dstType != BuiltInType.Null)
                    {
                        throw ComUtils.CreateComException(ResultIds.E_BADTYPE);
                    }

                    break;
                }
            }

            // all done.
            return dstValue;
        }

        /// <summary>
        /// Converts a local value to a remote value.
        /// </summary>
        /// <param name="srcValue">The local value.</param>
        /// <param name="srcType">The data type of the local value.</param>
        /// <param name="dstType">The data type of the remote value.</param>
        /// <returns>The remote value.</returns>
        private object ConvertLocalToRemote(object srcValue, BuiltInType srcType, BuiltInType dstType)
        {
            // must determine the type from the source if the containing array is a variant.
            if (srcType == BuiltInType.Variant)
            {
                TypeInfo typeInfo = TypeInfo.Construct(srcValue);
                srcType = typeInfo.BuiltInType;
            }

            // no conversion by default.
            object dstValue = srcValue;

            // apply different conversions depending on the data type.
            switch (dstType)
            {
                case BuiltInType.Guid:
                {
                    dstValue = new Uuid((string)srcValue);
                    break;
                }

                case BuiltInType.XmlElement:
                {
                    XmlDocument document = new XmlDocument();
                    document.InnerXml = (string)srcValue;
                    dstValue = document.DocumentElement;
                    break;
                }

                case BuiltInType.NodeId:
                {
                    dstValue = GetRemoteNodeId((string)srcValue);
                    break;
                }

                case BuiltInType.ExpandedNodeId:
                {
                    ExpandedNodeId nodeId = ExpandedNodeId.Parse((string)srcValue);
                    dstValue = GetRemoteExpandedNodeId(nodeId);
                    break;
                }

                case BuiltInType.QualifiedName:
                {
                    dstValue = GetRemoteBrowseName((string)srcValue);
                    break;
                }

                case BuiltInType.LocalizedText:
                {
                    dstValue = new LocalizedText((string)srcValue);
                    break;
                }

                case BuiltInType.StatusCode:
                {
                    dstValue = new StatusCode((uint)srcValue);
                    break;
                }

                case BuiltInType.ExtensionObject:
                {
                    BinaryDecoder decoder = new BinaryDecoder((byte[])srcValue, m_localMessageContext);
                    dstValue = decoder.ReadExtensionObject(null);
                    decoder.Close();
                    break;
                }

                default:
                {
                    if (dstType != srcType && dstType != BuiltInType.Variant && dstType != BuiltInType.Null)
                    {
                        throw ComUtils.CreateComException(ResultIds.E_BADTYPE);
                    }

                    break;
                }
            }

            // all done.
            return dstValue;
        }
        #endregion

        #region StringTableMapping Class
        /// <summary>
        /// Stores the mapping between two string tables.
        /// </summary>
        private class StringTableMapping
        {
            #region Public Members
            /// <summary>
            /// Gets the mapping from a local index to a remote index.
            /// </summary>
            /// <value>The local to remote mapping.</value>
            public int[] LocalToRemote
            {
                get { return m_localToRemote; }
            }

            /// <summary>
            /// Gets the mapping from a remote index to a local index.
            /// </summary>
            /// <value>The remote to local mapping.</value>
            public int[] RemoteToLocal
            {
                get { return m_remoteToLocal; }
            }

            /// <summary>
            /// Initializes the mapping between the tables.
            /// </summary>
            /// <param name="localTable">The local table.</param>
            /// <param name="remoteTable">The remote table.</param>
            /// <remarks>The local table is updates with missing URIs from the remote table.</remarks>
            public void Initialize(List<string> localTable, StringTable remoteTable)
            {
                m_remoteToLocal = InitializeRemoteToLocalMapping(localTable, remoteTable);
                m_localToRemote = InitializeLocalToRemoteMapping(localTable, remoteTable);
            }
            #endregion
            
            #region Private Methods
            /// <summary>
            /// Initializes the local to remote mapping for a pair of string tables.
            /// </summary>
            /// <param name="localTable">The local table.</param>
            /// <param name="remoteTable">The remote table.</param>
            /// <returns>The mapping.</returns>
            private int[] InitializeLocalToRemoteMapping(List<string> localTable, StringTable remoteTable)
            {
                List<int> indexes = new List<int>();
                indexes.Add(0);

                if (remoteTable is NamespaceTable)
                {
                    indexes.Add(1);
                }

                for (int ii = 0; ii < localTable.Count; ii++)
                {
                    int index = remoteTable.GetIndex(localTable[ii]);
                    indexes.Add(index);
                }

                return indexes.ToArray();
            }

            /// <summary>
            /// Initializes the remote to local mapping for a pair of string tables.
            /// </summary>
            /// <param name="localTable">The local table.</param>
            /// <param name="remoteTable">The remote table.</param>
            /// <returns>The mapping.</returns>
            private int[] InitializeRemoteToLocalMapping(List<string> localTable, StringTable remoteTable)
            {
                List<int> indexes = new List<int>();
                indexes.Add(0);

                if (remoteTable is NamespaceTable)
                {
                    indexes.Add(1);
                }

                int start = indexes.Count;

                for (int ii = start; ii < remoteTable.Count; ii++)
                {
                    string uri = remoteTable.GetString((uint)ii);

                    // look for the matching local index.
                    bool found = false;

                    for (int jj = 0; jj < localTable.Count; jj++)
                    {
                        if (localTable[jj] == uri)
                        {
                            found = true;
                            indexes.Add(jj+start);
                            break;
                        }
                    }

                    // not found.
                    if (!found)
                    {
                        localTable.Add(uri);
                        indexes.Add(localTable.Count-1+start);
                    }
                }

                // return the indexes.
                return indexes.ToArray();
            }
            #endregion

            #region Private Fields
            private int[] m_remoteToLocal;
            private int[] m_localToRemote;
            #endregion
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private ITypeTable m_typeTable;
        private ServiceMessageContext m_localMessageContext;
        private StringTableMapping m_namespaceUris;
        private StringTableMapping m_serverUris;
        private Dictionary<string,NodeIdMappingSet> m_mappingSets;
        #endregion
	}
}
