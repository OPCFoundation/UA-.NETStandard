// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Type node
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class TypeNode : Node
    {
        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public TypeNode(ILocalNode source)
            : base(source)
        {
        }

        /// <inheritdoc/>
        public TypeNode()
        {
        }

        /// <inheritdoc/>
        public override ExpandedNodeId TypeId => DataTypeIds.TypeNode;

        /// <inheritdoc/>
        public override ExpandedNodeId BinaryEncodingId => ObjectIds.TypeNode_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public override ExpandedNodeId XmlEncodingId => ObjectIds.TypeNode_Encoding_DefaultXml;
    }
}
