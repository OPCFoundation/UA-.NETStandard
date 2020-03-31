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
using System.Collections.Generic;
using Opc.Ua;
using Opc.Ua.Server;

namespace AggregationServer
{
    /// <summary>
    /// Stores the type information provided by the AE server.
    /// </summary>
    public class NamespaceMapper
    {
        #region Public Methods
        /// <summary>
        /// Gets the local namespace indexes.
        /// </summary>
        public int[] LocalNamespaceIndexes
        {
            get { return m_localNamespaceIndexes; }
        }

        /// <summary>
        /// Gets or sets the Uris for the Node Managers it supports e.g. "http://samples.org/UA/memorybuffer".
        /// </summary>
        public string[] TypeSystemNamespaceUris { get; set; }

        /// <summary>
        /// Initializes the mapper.
        /// </summary>
        /// <param name="localNamespaceUris"></param>
        /// <param name="remoteNamespaceUris"></param>
        public void Initialize(StringTable localNamespaceUris, StringTable remoteNamespaceUris, string applicationUri)
        {
            m_localNamespaceIndexes = new int[remoteNamespaceUris.Count];

            for (int ii = 1; ii < remoteNamespaceUris.Count; ii++)
            {
                string namespaceUri = remoteNamespaceUris.GetString((uint)ii);

                bool isTypeSystemUri = false;

                if (TypeSystemNamespaceUris != null)
                {
                    for (int jj = 0; jj < TypeSystemNamespaceUris.Length; jj++)
                    {
                        if (TypeSystemNamespaceUris[jj] == namespaceUri)
                        {
                            isTypeSystemUri = true;
                            break;
                        }
                    }
                }

                if (!isTypeSystemUri)
                {
                    namespaceUri = applicationUri + ":" + namespaceUri;
                }

                m_localNamespaceIndexes[ii] = localNamespaceUris.GetIndexOrAppend(namespaceUri);
            }

            m_remoteNamespaceIndexes = new int[localNamespaceUris.Count];

            for (int ii = 0; ii < m_localNamespaceIndexes.Length; ii++)
            {
                if (m_remoteNamespaceIndexes.Length > m_localNamespaceIndexes[ii])
                {
                    m_remoteNamespaceIndexes[m_localNamespaceIndexes[ii]] = ii;
                }
            }
        }

        /// <summary>
        /// Converts a remote NodeId to a local NodeId.
        /// </summary>
        public NodeId ToLocalId(NodeId value)
        {
            return ToId(value, m_localNamespaceIndexes);
        }

        /// <summary>
        /// Converts a local NodeId to a remote NodeId.
        /// </summary>
        public NodeId ToRemoteId(NodeId value)
        {
            return ToId(value, m_remoteNamespaceIndexes);
        }

        /// <summary>
        /// Converts a remote ExpandedNodeId to a local ExpandedNodeId.
        /// </summary>
        public ExpandedNodeId ToLocalId(ExpandedNodeId value)
        {
            return ToId(value, m_localNamespaceIndexes);
        }

        /// <summary>
        /// Converts a local ExpandedNodeId to a remote ExpandedNodeId.
        /// </summary>
        public ExpandedNodeId ToRemoteId(ExpandedNodeId value)
        {
            return ToId(value, m_remoteNamespaceIndexes);
        }

        /// <summary>
        /// Converts a remote QualifiedName to a local QualifiedName.
        /// </summary>
        public QualifiedName ToLocalName(QualifiedName value)
        {
            return ToName(value, m_localNamespaceIndexes);
        }

        /// <summary>
        /// Converts a local QualifiedName to a remote QualifiedName.
        /// </summary>
        public QualifiedName ToRemoteName(QualifiedName value)
        {
            return ToName(value, m_remoteNamespaceIndexes);
        }

        /// <summary>
        /// Converts a remote ExtensionObject to a local ExtensionObject.
        /// </summary>
        public ExtensionObject ToLocalExtensionObject(ExtensionObject value)
        {
            return ToExtensionObject(value, m_localNamespaceIndexes);
        }

        /// <summary>
        /// Converts a local ExtensionObject to a remote ExtensionObject.
        /// </summary>
        public ExtensionObject ToRemoteExtensionObject(ExtensionObject value)
        {
            return ToExtensionObject(value, m_remoteNamespaceIndexes);
        }

        /// <summary>
        /// Converts a remote ExtensionObject to a local ExtensionObject.
        /// </summary>
        public Variant ToLocalVariant(Variant value)
        {
            return ToVariant(value, m_localNamespaceIndexes);
        }

        /// <summary>
        /// Converts a local ExtensionObject to a remote ExtensionObject.
        /// </summary>
        public Variant ToRemoteVariant(Variant value)
        {
            return ToVariant(value, m_remoteNamespaceIndexes);
        }

        /// <summary>
        /// Converts a remote ExtensionObject to a local ExtensionObject.
        /// </summary>
        public object ToLocalValue(object value)
        {
            if (value is Variant)
            {
                return ToVariant((Variant)value, m_localNamespaceIndexes);
            }
            else
            {
                return ToVariant(new Variant(value), m_localNamespaceIndexes).Value;
            }
        }

        /// <summary>
        /// Converts a local ExtensionObject to a remote ExtensionObject.
        /// </summary>
        public object ToRemoteValue(object value)
        {
            if (value is Variant)
            {
                return ToVariant((Variant)value, m_remoteNamespaceIndexes);
            }
            else
            {
                return ToVariant(new Variant(value), m_remoteNamespaceIndexes).Value;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Converts a remote NodeId to a local NodeId.
        /// </summary>
        private NodeId ToId(NodeId nodeId, int[] namespaceIndexes)
        {
            if (NodeId.IsNull(nodeId))
            {
                return NodeId.Null;
            }

            if (nodeId.NamespaceIndex == 0)
            {
                return nodeId;
            }

            if (namespaceIndexes.Length <= nodeId.NamespaceIndex)
            {
                return NodeId.Null;
            }

            return new NodeId(nodeId.Identifier, (ushort)namespaceIndexes[nodeId.NamespaceIndex]);
        }

        /// <summary>
        /// Converts a remote ExpandedNodeId to a local ExpandedNodeId.
        /// </summary>
        private ExpandedNodeId ToId(ExpandedNodeId nodeId, int[] namespaceIndexes)
        {
            if (NodeId.IsNull(nodeId))
            {
                return NodeId.Null;
            }

            if (nodeId.NamespaceIndex == 0)
            {
                return nodeId;
            }

            if (namespaceIndexes.Length <= nodeId.NamespaceIndex)
            {
                return NodeId.Null;
            }

            return new ExpandedNodeId(nodeId.Identifier, (ushort)namespaceIndexes[nodeId.NamespaceIndex], nodeId.NamespaceUri, nodeId.ServerIndex);
        }

        /// <summary>
        /// Converts a remote QualifiedName to a local QualifiedName.
        /// </summary>
        private QualifiedName ToName(QualifiedName name, int[] namespaceIndexes)
        {
            if (QualifiedName.IsNull(name))
            {
                return QualifiedName.Null;
            }

            if (name.NamespaceIndex == 0)
            {
                return name;
            }

            if (namespaceIndexes.Length <= name.NamespaceIndex)
            {
                return QualifiedName.Null;
            }

            return new QualifiedName(name.Name, (ushort)namespaceIndexes[name.NamespaceIndex]);
        }

        /// <summary>
        /// Converts a remote ExtensionObject to a local ExtensionObject.
        /// </summary>
        private ExtensionObject ToExtensionObject(ExtensionObject extension, int[] namespaceIndexes)
        {
            if (ExtensionObject.IsNull(extension))
            {
                return extension;
            }

            Argument argument = extension.Body as Argument;

            if (argument != null)
            {
                Argument argument2 = (Argument)argument.MemberwiseClone();
                argument2.DataType = ToId(argument.DataType, namespaceIndexes);
                return new ExtensionObject(null, argument2);
            }

            return extension;
        }

        /// <summary>
        /// Converts a remote Variant to a local Variant.
        /// </summary>
        private Variant ToVariant(Variant value, int[] namespaceIndexes)
        {
            if (Variant.Null == value)
            {
                return Variant.Null;
            }

            TypeInfo type = value.TypeInfo;

            if (type == null)
            {
                type = TypeInfo.Construct(value.Value);
            }

            if (type == null)
            {
                return Variant.Null;
            }

            if (type.ValueRank == ValueRanks.Scalar)
            {
                switch (type.BuiltInType)
                {
                    case BuiltInType.NodeId:
                    {
                        return new Variant(ToId((NodeId)value.Value, namespaceIndexes));
                    }

                    case BuiltInType.ExpandedNodeId:
                    {
                        return new Variant(ToId((ExpandedNodeId)value.Value, namespaceIndexes));
                    }

                    case BuiltInType.QualifiedName:
                    {
                        return new Variant(ToName((QualifiedName)value.Value, namespaceIndexes));
                    }

                    case BuiltInType.ExtensionObject:
                    {
                        return new Variant(ToExtensionObject((ExtensionObject)value.Value, namespaceIndexes));
                    }
                }
            }
            else
            {
                switch (type.BuiltInType)
                {
                    case BuiltInType.NodeId:
                    case BuiltInType.ExpandedNodeId:
                    case BuiltInType.QualifiedName:
                    case BuiltInType.ExtensionObject:
                    case BuiltInType.Variant:
                    {
                        Array array = null;

                        if (Object.ReferenceEquals(m_localNamespaceIndexes, namespaceIndexes))
                        {
                            array = TypeInfo.CastArray((Array)value.Value, type.BuiltInType, type.BuiltInType, CastArrayToLocal);
                        }
                        else
                        {
                            array = TypeInfo.CastArray((Array)value.Value, type.BuiltInType, type.BuiltInType, CastArrayToRemote);
                        }

                        return new Variant(array, type);
                    }
                }
            }

            return value;
        }

        /// <summary>
        /// Casts an array value to a local value.
        /// </summary>
        private object CastArrayToLocal(object source, BuiltInType srcType, BuiltInType dstType)
        {
            if (source is Variant)
            {
                Variant result = ToVariant((Variant)source, m_localNamespaceIndexes);
                return result;
            }
            else
            {
                Variant result = ToVariant(new Variant(source), m_localNamespaceIndexes);
                return result.Value;
            }
        }

        /// <summary>
        /// Casts an array value to a remote value.
        /// </summary>
        private object CastArrayToRemote(object source, BuiltInType srcType, BuiltInType dstType)
        {
            if (source is Variant)
            {
                Variant result = ToVariant((Variant)source, m_remoteNamespaceIndexes);
                return result;
            }
            else
            {
                Variant result = ToVariant(new Variant(source), m_remoteNamespaceIndexes);
                return result.Value;
            }
        }
        #endregion

        #region Private Fields
        private int[] m_localNamespaceIndexes;
        private int[] m_remoteNamespaceIndexes;
        #endregion
    }
}
