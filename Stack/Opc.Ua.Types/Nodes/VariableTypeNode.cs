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

using System.Collections.Generic;
using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Variable type node
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class VariableTypeNode : TypeNode, IVariableType
    {
        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public VariableTypeNode(ILocalNode source)
            : base(source)
        {
            NodeClass = NodeClass.VariableType;

            if (source is IVariableType node)
            {
                IsAbstract = node.IsAbstract;
                Value = new Variant(node.Value);
                DataType = node.DataType;
                ValueRank = node.ValueRank;

                if (node.ArrayDimensions != null)
                {
                    ArrayDimensions = [.. node.ArrayDimensions];
                }
            }
        }

        /// <inheritdoc/>
        public VariableTypeNode()
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
            Value = Variant.Null;
            DataType = null;
            ValueRank = 0;
            m_arrayDimensions = [];
            IsAbstract = true;
        }

        /// <summary>
        /// Value
        /// </summary>
        [DataMember(Name = "Value", IsRequired = false, Order = 1)]
        public Variant Value { get; set; } = Variant.Null;

        /// <summary>
        /// Data type
        /// </summary>
        [DataMember(Name = "DataType", IsRequired = false, Order = 2)]
        public NodeId DataType { get; set; }

        /// <summary>
        /// Value rank
        /// </summary>
        [DataMember(Name = "ValueRank", IsRequired = false, Order = 3)]
        public int ValueRank { get; set; }

        /// <summary>
        /// Array dimensions
        /// </summary>
        [DataMember(Name = "ArrayDimensions", IsRequired = false, Order = 4)]
        public UInt32Collection ArrayDimensions
        {
            get => m_arrayDimensions;

            set
            {
                m_arrayDimensions = value;

                if (value == null)
                {
                    m_arrayDimensions = [];
                }
            }
        }

        /// <summary>
        /// Is abstract node
        /// </summary>
        [DataMember(Name = "IsAbstract", IsRequired = false, Order = 5)]
        public bool IsAbstract { get; set; }

        /// <inheritdoc/>
        public override ExpandedNodeId TypeId => DataTypeIds.VariableTypeNode;

        /// <inheritdoc/>
        public override ExpandedNodeId BinaryEncodingId => ObjectIds.VariableTypeNode_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public override ExpandedNodeId XmlEncodingId => ObjectIds.VariableTypeNode_Encoding_DefaultXml;

        /// <inheritdoc/>
        public override ExpandedNodeId JsonEncodingId => ObjectIds.VariableTypeNode_Encoding_DefaultJson;

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            base.Encode(encoder);

            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteVariant("Value", Value);
            encoder.WriteNodeId("DataType", DataType);
            encoder.WriteInt32("ValueRank", ValueRank);
            encoder.WriteUInt32Array("ArrayDimensions", ArrayDimensions);
            encoder.WriteBoolean("IsAbstract", IsAbstract);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override void Decode(IDecoder decoder)
        {
            base.Decode(decoder);

            decoder.PushNamespace(Namespaces.OpcUaXsd);

            Value = decoder.ReadVariant("Value");
            DataType = decoder.ReadNodeId("DataType");
            ValueRank = decoder.ReadInt32("ValueRank");
            ArrayDimensions = decoder.ReadUInt32Array("ArrayDimensions");
            IsAbstract = decoder.ReadBoolean("IsAbstract");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not VariableTypeNode value)
            {
                return false;
            }

            if (!CoreUtils.IsEqual(Value, value.Value))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(DataType, value.DataType))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(ValueRank, value.ValueRank))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(m_arrayDimensions, value.m_arrayDimensions))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(IsAbstract, value.IsAbstract))
            {
                return false;
            }

            return base.IsEqual(encodeable);
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            return (VariableTypeNode)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (VariableTypeNode)base.MemberwiseClone();

            clone.Value = (Variant)CoreUtils.Clone(Value);
            clone.DataType = CoreUtils.Clone(DataType);
            clone.ValueRank = (int)CoreUtils.Clone(ValueRank);
            clone.m_arrayDimensions = CoreUtils.Clone(m_arrayDimensions);
            clone.IsAbstract = (bool)CoreUtils.Clone(IsAbstract);

            return clone;
        }

        /// <summary>
        /// The value attribute.
        /// </summary>
        /// <value>The value.</value>
        object IVariableBase.Value
        {
            get => Value.Value;
            set => Value = new Variant(value);
        }

        /// <summary>
        /// The number in each dimension of an array value.
        /// </summary>
        /// <value>The number in each dimension of an array value.</value>
        IList<uint> IVariableBase.ArrayDimensions
        {
            get => m_arrayDimensions;
            set
            {
                if (value == null)
                {
                    m_arrayDimensions = [];
                }
                else
                {
                    m_arrayDimensions = [.. value];
                }
            }
        }

        /// <summary>
        /// Whether the node supports the specified attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>True if the node supports the specified attribute.</returns>
        public override bool SupportsAttribute(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.Value:
                    return Value.Value != null;
                case Attributes.ValueRank:
                case Attributes.DataType:
                case Attributes.IsAbstract:
                    return true;
                case Attributes.ArrayDimensions:
                    return m_arrayDimensions != null && m_arrayDimensions.Count != 0;
                default:
                    return base.SupportsAttribute(attributeId);
            }
        }

        /// <summary>
        /// Reads the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>The value of an attribute.</returns>
        protected override object Read(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.DataType:
                    return DataType;
                case Attributes.ValueRank:
                    return ValueRank;
                // values are copied when the are written so then can be safely returned.
                case Attributes.Value:
                    return Value.Value;
                case Attributes.ArrayDimensions:
                    if (m_arrayDimensions == null || m_arrayDimensions.Count == 0)
                    {
                        return StatusCodes.BadAttributeIdInvalid;
                    }

                    return m_arrayDimensions.ToArray();
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
        protected override ServiceResult Write(uint attributeId, object value)
        {
            switch (attributeId)
            {
                // values are copied when the are written so then can be safely returned on read.
                case Attributes.Value:
                    Value = new Variant(CoreUtils.Clone(value));
                    return ServiceResult.Good;
                case Attributes.DataType:
                    var dataType = (NodeId)value;

                    // must ensure the value is of the correct datatype.
                    if (dataType != DataType)
                    {
                        Value = Variant.Null;
                    }

                    DataType = dataType;
                    return ServiceResult.Good;
                case Attributes.ValueRank:
                    int valueRank = (int)value;

                    if (valueRank != ValueRank)
                    {
                        Value = Variant.Null;
                    }

                    ValueRank = valueRank;

                    return ServiceResult.Good;
                case Attributes.ArrayDimensions:
                    m_arrayDimensions = [.. (uint[])value];

                    // ensure number of dimensions is correct.
                    if (m_arrayDimensions.Count > 0 && ValueRank != m_arrayDimensions.Count)
                    {
                        ValueRank = m_arrayDimensions.Count;
                        Value = Variant.Null;
                    }

                    return ServiceResult.Good;
                default:
                    return base.Write(attributeId, value);
            }
        }

        private UInt32Collection m_arrayDimensions;
    }
}
