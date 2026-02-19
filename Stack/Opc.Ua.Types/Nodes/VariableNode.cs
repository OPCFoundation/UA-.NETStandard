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
    /// Variable node
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class VariableNode : InstanceNode, IVariable
    {
        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public VariableNode(ILocalNode source)
            : base(source)
        {
            NodeClass = NodeClass.Variable;

            if (source is IVariable variable)
            {
                DataType = variable.DataType;
                ValueRank = variable.ValueRank;
                AccessLevel = variable.AccessLevel;
                UserAccessLevel = variable.UserAccessLevel;
                MinimumSamplingInterval = variable.MinimumSamplingInterval;
                Historizing = variable.Historizing;

                Variant value = variable.Value;
                if (value.IsNull)
                {
                    value = TypeInfo.GetDefaultVariantValue(
                        variable.DataType,
                        variable.ValueRank);
                }
                Value = value;
                ArrayDimensions = variable.ArrayDimensions;
            }
        }

        /// <inheritdoc/>
        public VariableNode()
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
            AccessLevel = 0;
            UserAccessLevel = 0;
            MinimumSamplingInterval = 0;
            Historizing = true;
            AccessLevelEx = 0;
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
        /// Access level
        /// </summary>
        [DataMember(Name = "AccessLevel", IsRequired = false, Order = 5)]
        public byte AccessLevel { get; set; }

        /// <summary>
        /// User access level
        /// </summary>
        [DataMember(Name = "UserAccessLevel", IsRequired = false, Order = 6)]
        public byte UserAccessLevel { get; set; }

        /// <summary>
        /// Min sampling interval
        /// </summary>
        [DataMember(Name = "MinimumSamplingInterval", IsRequired = false, Order = 7)]
        public double MinimumSamplingInterval { get; set; }

        /// <summary>
        /// Historizing
        /// </summary>
        [DataMember(Name = "Historizing", IsRequired = false, Order = 8)]
        public bool Historizing { get; set; }

        /// <summary>
        /// Access level ex
        /// </summary>
        [DataMember(Name = "AccessLevelEx", IsRequired = false, Order = 9)]
        public uint AccessLevelEx { get; set; }

        /// <inheritdoc/>
        public override ExpandedNodeId TypeId => DataTypeIds.VariableNode;

        /// <inheritdoc/>
        public override ExpandedNodeId BinaryEncodingId => ObjectIds.VariableNode_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public override ExpandedNodeId XmlEncodingId => ObjectIds.VariableNode_Encoding_DefaultXml;

        /// <inheritdoc/>
        public override ExpandedNodeId JsonEncodingId => ObjectIds.VariableNode_Encoding_DefaultJson;

        /// <inheritdoc/>
        public override void Encode(IEncoder encoder)
        {
            base.Encode(encoder);

            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteVariant("Value", Value);
            encoder.WriteNodeId("DataType", DataType);
            encoder.WriteInt32("ValueRank", ValueRank);
            encoder.WriteUInt32Array("ArrayDimensions", ArrayDimensions);
            encoder.WriteByte("AccessLevel", AccessLevel);
            encoder.WriteByte("UserAccessLevel", UserAccessLevel);
            encoder.WriteDouble("MinimumSamplingInterval", MinimumSamplingInterval);
            encoder.WriteBoolean("Historizing", Historizing);
            encoder.WriteUInt32("AccessLevelEx", AccessLevelEx);

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
            AccessLevel = decoder.ReadByte("AccessLevel");
            UserAccessLevel = decoder.ReadByte("UserAccessLevel");
            MinimumSamplingInterval = decoder.ReadDouble("MinimumSamplingInterval");
            Historizing = decoder.ReadBoolean("Historizing");
            AccessLevelEx = decoder.ReadUInt32("AccessLevelEx");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public override bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not VariableNode value)
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

            if (!CoreUtils.IsEqual(AccessLevel, value.AccessLevel))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(UserAccessLevel, value.UserAccessLevel))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(MinimumSamplingInterval, value.MinimumSamplingInterval))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(Historizing, value.Historizing))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(AccessLevelEx, value.AccessLevelEx))
            {
                return false;
            }

            return base.IsEqual(encodeable);
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            return (VariableNode)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (VariableNode)base.MemberwiseClone();

            clone.Value = CoreUtils.Clone(Value);
            clone.DataType = DataType;
            clone.ValueRank = CoreUtils.Clone(ValueRank);
            clone.m_arrayDimensions = CoreUtils.Clone(m_arrayDimensions);
            clone.AccessLevel = CoreUtils.Clone(AccessLevel);
            clone.UserAccessLevel = CoreUtils.Clone(UserAccessLevel);
            clone.MinimumSamplingInterval = (double)CoreUtils.Clone(MinimumSamplingInterval);
            clone.Historizing = CoreUtils.Clone(Historizing);
            clone.AccessLevelEx = CoreUtils.Clone(AccessLevelEx);

            return clone;
        }

        /// <summary>
        /// The number in each dimension of an array value.
        /// </summary>
        /// <value>The array dimensions.</value>
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
                case Attributes.DataType:
                case Attributes.ValueRank:
                case Attributes.AccessLevel:
                case Attributes.AccessLevelEx:
                case Attributes.UserAccessLevel:
                case Attributes.MinimumSamplingInterval:
                case Attributes.Historizing:
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
        protected override Variant Read(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.DataType:
                    return DataType;
                case Attributes.ValueRank:
                    return ValueRank;
                case Attributes.AccessLevel:
                    return AccessLevel;
                case Attributes.UserAccessLevel:
                    return UserAccessLevel;
                case Attributes.MinimumSamplingInterval:
                    return MinimumSamplingInterval;
                case Attributes.Historizing:
                    return Historizing;
                case Attributes.AccessLevelEx:
                    return AccessLevelEx;
                // values are copied when the are written so then can be safely returned.
                case Attributes.Value:
                    return Value;
                // array dimensions attribute is not support if it is empty.
                case Attributes.ArrayDimensions:
                    if (m_arrayDimensions.IsEmpty)
                    {
                        return StatusCodes.BadAttributeIdInvalid;
                    }

                    return m_arrayDimensions;
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
                case Attributes.AccessLevel:
                    AccessLevel = (byte)value;
                    return ServiceResult.Good;
                case Attributes.UserAccessLevel:
                    UserAccessLevel = (byte)value;
                    return ServiceResult.Good;
                case Attributes.AccessLevelEx:
                    AccessLevelEx = (uint)value;
                    return ServiceResult.Good;
                case Attributes.MinimumSamplingInterval:
                    MinimumSamplingInterval = (int)value;
                    return ServiceResult.Good;
                case Attributes.Historizing:
                    Historizing = (bool)value;
                    return ServiceResult.Good;
                // values are copied when the are written so then can be safely returned on read.
                case Attributes.Value:
                    Value = new Variant(CoreUtils.Clone(value));
                    return ServiceResult.Good;
                case Attributes.DataType:
                    var dataType = (NodeId)value;

                    // must ensure the value is of the correct datatype.
                    if (dataType != DataType)
                    {
                        Value = TypeInfo.GetDefaultVariantValue(dataType, ValueRank);
                    }

                    DataType = dataType;
                    return ServiceResult.Good;
                case Attributes.ValueRank:
                    int valueRank = (int)value;

                    if (valueRank != ValueRank)
                    {
                        Value = TypeInfo.GetDefaultVariantValue(DataType, valueRank);
                    }

                    ValueRank = valueRank;

                    return ServiceResult.Good;
                case Attributes.ArrayDimensions:
                    m_arrayDimensions = value.GetUInt32Array();

                    // ensure number of dimensions is correct.
                    if (m_arrayDimensions.Count > 0 && m_arrayDimensions.Count != ValueRank)
                    {
                        ValueRank = m_arrayDimensions.Count;
                        Value = TypeInfo.GetDefaultVariantValue(DataType, ValueRank);
                    }

                    return ServiceResult.Good;
                default:
                    return base.Write(attributeId, value);
            }
        }

        private ArrayOf<uint> m_arrayDimensions;
    }
}
