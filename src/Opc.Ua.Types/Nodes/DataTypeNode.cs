/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Data type node
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class DataTypeNode : TypeNode, IDataType
    {
        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public DataTypeNode(ILocalNode source)
            : base(source)
        {
            NodeClass = NodeClass.DataType;

            if (source is IDataType node)
            {
                IsAbstract = node.IsAbstract;
            }
        }

        /// <inheritdoc/>
        public DataTypeNode()
        {
            Initialize();
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        private void Initialize()
        {
            IsAbstract = true;
            DataTypeDefinition = default;
        }

        /// <summary>
        /// Is abstract data type
        /// </summary>
        [DataMember(Name = "IsAbstract", IsRequired = false, Order = 1)]
        public bool IsAbstract { get; set; }

        /// <summary>
        /// Data type definition
        /// </summary>
        [DataMember(Name = "DataTypeDefinition", IsRequired = false, Order = 2)]
        public ExtensionObject DataTypeDefinition { get; set; }

        /// <inheritdoc/>
        public override ExpandedNodeId TypeId => DataTypeIds.DataTypeNode;

        /// <inheritdoc/>
        public override ExpandedNodeId BinaryEncodingId => ObjectIds.DataTypeNode_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public override ExpandedNodeId XmlEncodingId => ObjectIds.DataTypeNode_Encoding_DefaultXml;

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            base.Encode(encoder);

            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteBoolean("IsAbstract", IsAbstract);
            encoder.WriteExtensionObject("DataTypeDefinition", DataTypeDefinition);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
            base.Decode(decoder);

            decoder.PushNamespace(Namespaces.OpcUaXsd);

            IsAbstract = decoder.ReadBoolean("IsAbstract");
            DataTypeDefinition = decoder.ReadExtensionObject("DataTypeDefinition");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override bool IsEqual(IEncodeable? encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not DataTypeNode value)
            {
                return false;
            }

            if (IsAbstract != value.IsAbstract)
            {
                return false;
            }

            if (DataTypeDefinition != value.DataTypeDefinition)
            {
                return false;
            }

            return base.IsEqual(encodeable);
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            return (DataTypeNode)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (DataTypeNode)base.MemberwiseClone();

            clone.IsAbstract = CoreUtils.Clone(IsAbstract);
            clone.DataTypeDefinition = CoreUtils.Clone(DataTypeDefinition);

            return clone;
        }

        /// <summary>
        /// Whether the node supports the specified attribute.
        /// </summary>
        /// <param name="attributeId">The attribute identifier.</param>
        /// <returns>True if the node supports the specified attribute.</returns>
        public override bool SupportsAttribute(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.IsAbstract:
                case Attributes.DataTypeDefinition:
                    return true;
                default:
                    return base.SupportsAttribute(attributeId);
            }
        }

        /// <summary>
        /// Reads the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>The value of an attribute.</returns>
        protected override Variant Read(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.IsAbstract:
                    return IsAbstract;
                case Attributes.DataTypeDefinition:
                    return DataTypeDefinition;
                default:
                    return base.Read(attributeId);
            }
        }

        /// <summary>
        /// Writes the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="value">The value.</param>
        /// <returns>The result of write operation.</returns>
        protected override ServiceResult Write(uint attributeId, Variant value)
        {
            switch (attributeId)
            {
                case Attributes.IsAbstract:
                    IsAbstract = (bool)value;
                    return ServiceResult.Good;
                case Attributes.DataTypeDefinition:
                    DataTypeDefinition = (ExtensionObject)value;
                    return ServiceResult.Good;
                default:
                    return base.Write(attributeId, value);
            }
        }
    }
}
