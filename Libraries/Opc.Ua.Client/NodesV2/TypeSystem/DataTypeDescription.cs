#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Nodes.TypeSystem
{
    using Opc.Ua;
    using System.Xml;

    /// <summary>
    /// Base data type description
    /// </summary>
    public abstract class DataTypeDescription
    {
        /// <summary>
        /// Type id
        /// </summary>
        public ExpandedNodeId TypeId { get; }

        /// <summary>
        /// Binary encoding id
        /// </summary>
        public ExpandedNodeId BinaryEncodingId { get; }

        /// <summary>
        /// Xml encoding id
        /// </summary>
        public ExpandedNodeId XmlEncodingId { get; }

        /// <summary>
        /// Json encoding id
        /// </summary>
        public ExpandedNodeId JsonEncodingId { get; }

        /// <summary>
        /// Xml name
        /// </summary>
        public XmlQualifiedName XmlName { get; }

        /// <summary>
        /// Is abstract
        /// </summary>
        public bool IsAbstract { get; }

        /// <summary>
        /// Create data type description
        /// </summary>
        /// <param name="typeId"></param>
        /// <param name="xmlName"></param>
        /// <param name="binaryEncodingId"></param>
        /// <param name="xmlEncodingId"></param>
        /// <param name="jsonEncodingId"></param>
        /// <param name="isAbstract"></param>
        protected DataTypeDescription(ExpandedNodeId typeId,
            XmlQualifiedName xmlName, ExpandedNodeId binaryEncodingId,
            ExpandedNodeId xmlEncodingId, ExpandedNodeId jsonEncodingId,
            bool isAbstract = false)
        {
            TypeId = typeId;
            BinaryEncodingId = binaryEncodingId;
            XmlEncodingId = xmlEncodingId;
            JsonEncodingId = jsonEncodingId;
            XmlName = xmlName;
            IsAbstract = isAbstract;
        }

        /// <summary>
        /// Create data type description
        /// </summary>
        protected DataTypeDescription()
        {
            TypeId = ExpandedNodeId.Null;
            BinaryEncodingId = ExpandedNodeId.Null;
            XmlEncodingId = ExpandedNodeId.Null;
            JsonEncodingId = ExpandedNodeId.Null;
            XmlName = new XmlQualifiedName();
        }
    }
}
#endif
