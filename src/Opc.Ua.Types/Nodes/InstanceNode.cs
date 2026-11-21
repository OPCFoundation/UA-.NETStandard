// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Instance node
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class InstanceNode : Node
    {
        /// <inheritdoc/>
        public InstanceNode()
        {
        }

        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public InstanceNode(ILocalNode source)
            : base(source)
        {
        }

        /// <inheritdoc/>
        public override ExpandedNodeId TypeId => DataTypeIds.InstanceNode;

        /// <inheritdoc/>
        public override ExpandedNodeId BinaryEncodingId => ObjectIds.InstanceNode_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public override ExpandedNodeId XmlEncodingId => ObjectIds.InstanceNode_Encoding_DefaultXml;
    }
}
