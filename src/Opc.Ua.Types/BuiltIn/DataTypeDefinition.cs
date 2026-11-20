/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Data type definition
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public abstract class DataTypeDefinition : IEncodeable
    {
        /// <inheritdoc/>
        public virtual ExpandedNodeId TypeId => DataTypeIds.DataTypeDefinition;

        /// <inheritdoc/>
        public virtual ExpandedNodeId BinaryEncodingId => ObjectIds.DataTypeDefinition_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public virtual ExpandedNodeId XmlEncodingId => ObjectIds.DataTypeDefinition_Encoding_DefaultXml;

        /// <inheritdoc/>
        public abstract void Encode(IEncoder encoder);

        /// <inheritdoc/>
        public abstract void Decode(IDecoder decoder);

        /// <inheritdoc/>
        public abstract bool IsEqual(IEncodeable? encodeable);

        /// <inheritdoc/>
        public abstract object Clone();
    }
}
