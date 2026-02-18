/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
                ArrayDimensions = node.ArrayDimensions;
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
            DataType = default;
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

            clone.Value = CoreUtils.Clone(Value);
            clone.DataType = DataType;
            clone.ValueRank = CoreUtils.Clone(ValueRank);
            clone.m_arrayDimensions = CoreUtils.Clone(m_arrayDimensions);
            clone.IsAbstract = CoreUtils.Clone(IsAbstract);

            return clone;
        }

        /// <summary>
        /// The number in each dimension of an array value.
        /// </summary>
        /// <value>The number in each dimension of an array value.</value>
        ArrayOf<uint> IVariableBase.ArrayDimensions
        {
            get => m_arrayDimensions;
            set => m_arrayDimensions = value;
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
                    return !Value.IsNull;
                case Attributes.ValueRank:
                case Attributes.DataType:
                case Attributes.IsAbstract:
                    return true;
                case Attributes.ArrayDimensions:
                    return !m_arrayDimensions.IsEmpty;
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
                    return Value.AsBoxedObject();
                case Attributes.ArrayDimensions:
                    if (m_arrayDimensions.IsEmpty)
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

        private ArrayOf<uint> m_arrayDimensions;
    }
}
