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

using System;
using System.Collections.Generic;
using System.Linq;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// The base class for all variable type nodes.
    /// </summary>
    public abstract class BaseVariableTypeState : BaseTypeState
    {
        /// <summary>
        /// Initializes the instance with its default attribute values.
        /// </summary>
        protected BaseVariableTypeState()
            : base(NodeClass.VariableType)
        {
            m_valueRank = ValueRanks.Any;
        }

        /// <summary>
        /// Initializes the instance from another instance.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            if (source is BaseVariableTypeState type)
            {
                m_value = CoreUtils.Clone(type.m_value);
                m_dataType = type.m_dataType;
                m_valueRank = type.m_valueRank;
                m_arrayDimensions = null;

                if (type.m_arrayDimensions != null)
                {
                    m_arrayDimensions = new ReadOnlyList<uint>(type.m_arrayDimensions, true);
                }
            }

            base.Initialize(context, source);
        }

        /// <summary>
        /// Initialized data type and value rank
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="context"></param>
        protected virtual void Initialize<T>(ISystemContext context)
        {
            DataType = TypeInfo.GetDataTypeId(typeof(T), context.NamespaceUris);
            ValueRank = TypeInfo.GetValueRank(typeof(T));
        }

        /// <summary>
        /// Sets the value to its default value if it is not valid.
        /// </summary>
        protected virtual object ExtractValueFromVariant(
            ISystemContext context,
            object value,
            bool throwOnError)
        {
            return value;
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            var clone = (BaseVariableTypeState)Activator.CreateInstance(GetType());
            CopyTo(clone);
            return clone;
        }

        /// <inheritdoc/>
        public override bool DeepEquals(NodeState node)
        {
            if (node is not BaseVariableTypeState state)
            {
                return false;
            }
            return
                base.DeepEquals(state) &&
                EqualityComparer<object>.Default.Equals(state.Value, Value) &&
                state.DataType == DataType &&
                state.ValueRank == ValueRank &&
                ArrayEqualityComparer<uint>.Default.Equals(
                    state.ArrayDimensions?.ToArray(), ArrayDimensions?.ToArray())
                ;
        }

        /// <inheritdoc/>
        public override int DeepGetHashCode()
        {
            var hash = new HashCode();
            hash.Add(base.DeepGetHashCode());
            hash.Add(Value);
            hash.Add(DataType);
            hash.Add(ValueRank);
            hash.Add(ArrayEqualityComparer<uint>.Default.GetHashCode(
                ArrayDimensions?.ToArray()));
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        protected override void CopyTo(NodeState target)
        {
            if (target is BaseVariableTypeState state)
            {
                state.Value = Value;
                state.DataType = DataType;
                state.ValueRank = ValueRank;
                state.ArrayDimensions = ArrayDimensions;
            }
            base.CopyTo(target);
        }

        /// <summary>
        /// The value of the variable.
        /// </summary>
        public Variant Value
        {
            get => m_value;
            set
            {
                if (m_value != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.Value;
                }

                m_value = value;
            }
        }

        /// <summary>
        /// The value of the variable as a Variant.
        /// </summary>
        public Variant WrappedValue
        {
            get => Value;
            set => Value = value;
        }

        /// <summary>
        /// The data type for the variable value.
        /// </summary>
        public NodeId DataType
        {
            get => m_dataType;
            set
            {
                if (m_dataType != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_dataType = value;
            }
        }

        /// <summary>
        /// The number of array dimensions permitted for the variable value.
        /// </summary>
        public int ValueRank
        {
            get => m_valueRank;
            set
            {
                if (m_valueRank != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_valueRank = value;
            }
        }

        /// <summary>
        /// The number of dimensions for an array values with one or more fixed dimensions.
        /// </summary>
        public ReadOnlyList<uint> ArrayDimensions
        {
            get => m_arrayDimensions;
            set
            {
                if (!ReferenceEquals(m_arrayDimensions, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_arrayDimensions = value;
            }
        }

        /// <summary>
        /// Raised when the Value attribute is read.
        /// </summary>
        public NodeValueSimpleEventHandler OnSimpleReadValue;

        /// <summary>
        /// Raised when the Value attribute is written.
        /// </summary>
        public NodeValueSimpleEventHandler OnSimpleWriteValue;

        /// <summary>
        /// Raised when the DataType attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<NodeId> OnReadDataType;

        /// <summary>
        /// Raised when the DataType attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<NodeId> OnWriteDataType;

        /// <summary>
        /// Raised when the ValueRank attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<int> OnReadValueRank;

        /// <summary>
        /// Raised when the ValueRank attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<int> OnWriteValueRank;

        /// <summary>
        /// Raised when the ArrayDimensions attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<uint[]> OnReadArrayDimensions;

        /// <summary>
        /// Raised when the ArrayDimensions attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<uint[]> OnWriteArrayDimensions;

        /// <summary>
        /// Exports a copy of the node to a node table.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        protected override void Export(ISystemContext context, Node node)
        {
            base.Export(context, node);

            if (node is VariableTypeNode variableTypeNode)
            {
                variableTypeNode.Value = new Variant(CoreUtils.Clone(Value));
                variableTypeNode.DataType = DataType;
                variableTypeNode.ValueRank = ValueRank;
                variableTypeNode.ArrayDimensions = null;

                if (ArrayDimensions != null)
                {
                    variableTypeNode.ArrayDimensions = [.. ArrayDimensions];
                }
            }
        }

        /// <summary>
        /// Saves the attributes from the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="encoder">The encoder wrapping the stream to write.</param>
        public override void Save(ISystemContext context, XmlEncoder encoder)
        {
            base.Save(context, encoder);

            encoder.PushNamespace(Namespaces.OpcUaXsd);

            if (!Value.IsNull)
            {
                encoder.WriteVariant("Value", Value);
            }

            if (!DataType.IsNull)
            {
                encoder.WriteNodeId("DataType", DataType);
            }

            if (ValueRank != ValueRanks.Any)
            {
                encoder.WriteInt32("ValueRank", ValueRank);
            }

            if (ArrayDimensions != null)
            {
                encoder.WriteString(
                    "ArrayDimensions",
                    BaseVariableState.ArrayDimensionsToXml(ArrayDimensions));
            }

            encoder.PopNamespace();
        }

        /// <summary>
        /// Updates the attributes from the stream.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="decoder">The decoder wrapping the stream to read.</param>
        public override void Update(ISystemContext context, XmlDecoder decoder)
        {
            base.Update(context, decoder);

            decoder.PushNamespace(Namespaces.OpcUaXsd);

            if (decoder.Peek("Value"))
            {
                Value = decoder.ReadVariant("Value");
            }

            if (decoder.Peek("DataType"))
            {
                DataType = decoder.ReadNodeId("DataType");
            }

            if (decoder.Peek("ValueRank"))
            {
                ValueRank = decoder.ReadInt32("ValueRank");
            }

            if (decoder.Peek("ArrayDimensions"))
            {
                ArrayDimensions = BaseVariableState.ArrayDimensionsFromXml(
                    decoder.ReadString("ArrayDimensions"));
            }

            decoder.PopNamespace();
        }

        /// <summary>
        /// Returns a mask which indicates which attributes have non-default value.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <returns>A mask the specifies the available attributes.</returns>
        public override AttributesToSave GetAttributesToSave(ISystemContext context)
        {
            AttributesToSave attributesToSave = base.GetAttributesToSave(context);

            if (!Value.IsNull)
            {
                attributesToSave |= AttributesToSave.Value;
            }

            if (!m_dataType.IsNull)
            {
                attributesToSave |= AttributesToSave.DataType;
            }

            if (m_valueRank != ValueRanks.Any)
            {
                attributesToSave |= AttributesToSave.ValueRank;
            }

            if (m_arrayDimensions != null)
            {
                attributesToSave |= AttributesToSave.ArrayDimensions;
            }

            return attributesToSave;
        }

        /// <summary>
        /// Saves object in an binary stream.
        /// </summary>
        /// <param name="context">The context user.</param>
        /// <param name="encoder">The encoder to write to.</param>
        /// <param name="attributesToSave">The masks indicating what attributes to write.</param>
        public override void Save(
            ISystemContext context,
            BinaryEncoder encoder,
            AttributesToSave attributesToSave)
        {
            base.Save(context, encoder, attributesToSave);

            if ((attributesToSave & AttributesToSave.Value) != 0)
            {
                encoder.WriteVariant(null, Value);
            }

            if ((attributesToSave & AttributesToSave.DataType) != 0)
            {
                encoder.WriteNodeId(null, m_dataType);
            }

            if ((attributesToSave & AttributesToSave.ValueRank) != 0)
            {
                encoder.WriteInt32(null, m_valueRank);
            }

            if ((attributesToSave & AttributesToSave.ArrayDimensions) != 0)
            {
                encoder.WriteUInt32Array(null, m_arrayDimensions);
            }
        }

        /// <summary>
        /// Updates the specified context.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="decoder">The decoder.</param>
        /// <param name="attributesToLoad">The attributes to load.</param>
        public override void Update(
            ISystemContext context,
            BinaryDecoder decoder,
            AttributesToSave attributesToLoad)
        {
            base.Update(context, decoder, attributesToLoad);

            if ((attributesToLoad & AttributesToSave.Value) != 0)
            {
                Value = decoder.ReadVariant(null);
            }

            if ((attributesToLoad & AttributesToSave.DataType) != 0)
            {
                m_dataType = decoder.ReadNodeId(null);
            }

            if ((attributesToLoad & AttributesToSave.ValueRank) != 0)
            {
                m_valueRank = decoder.ReadInt32(null);
            }

            if ((attributesToLoad & AttributesToSave.ArrayDimensions) != 0)
            {
                UInt32Collection arrayDimensions = decoder.ReadUInt32Array(null);

                if (arrayDimensions != null && arrayDimensions.Count > 0)
                {
                    m_arrayDimensions = new ReadOnlyList<uint>(arrayDimensions);
                }
                else
                {
                    m_arrayDimensions = null;
                }
            }
        }

        /// <summary>
        /// Reads the value for any non-value attribute.
        /// </summary>
        protected override ServiceResult ReadNonValueAttribute(
            ISystemContext context,
            uint attributeId,
            ref Variant value)
        {
            ServiceResult result = null;

            switch (attributeId)
            {
                case Attributes.DataType:
                    NodeId dataType = m_dataType;

                    NodeAttributeEventHandler<NodeId> onReadDataType = OnReadDataType;

                    if (onReadDataType != null)
                    {
                        result = onReadDataType(context, this, ref dataType);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = dataType;
                    }

                    return result;
                case Attributes.ValueRank:
                    int valueRank = m_valueRank;

                    NodeAttributeEventHandler<int> onReadValueRank = OnReadValueRank;

                    if (onReadValueRank != null)
                    {
                        result = onReadValueRank(context, this, ref valueRank);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = valueRank;
                    }

                    return result;
                case Attributes.ArrayDimensions:
                    uint[] arrayDimensions = m_arrayDimensions?.ToArray();

                    NodeAttributeEventHandler<uint[]> onReadArrayDimensions = OnReadArrayDimensions;

                    if (onReadArrayDimensions != null)
                    {
                        result = onReadArrayDimensions(context, this, ref arrayDimensions);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = arrayDimensions;
                    }

                    return result;
                default:
                    return base.ReadNonValueAttribute(context, attributeId, ref value);
            }
        }

        /// <summary>
        /// Reads the value for the value attribute.
        /// </summary>
        protected override ServiceResult ReadValueAttribute(
            ISystemContext context,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref DateTime sourceTimestamp)
        {
            value = m_value;

            ServiceResult result = ServiceResult.Good;

            VariableCopyPolicy copyPolicy = VariableCopyPolicy.CopyOnRead;

            // use default behavior.
            if (OnSimpleReadValue != null)
            {
                result = OnSimpleReadValue(context, this, ref value);

                if (ServiceResult.IsBad(result))
                {
                    return result;
                }

                copyPolicy = VariableCopyPolicy.Never;
            }
            else
            {
                // check if a valid value exists.
                if (value.IsNull)
                {
                    return StatusCodes.BadAttributeIdInvalid;
                }
            }

            // apply the index range and encoding.
            result = BaseVariableState.ApplyIndexRangeAndDataEncoding(
                context,
                indexRange,
                dataEncoding,
                ref value);

            if (ServiceResult.IsBad(result))
            {
                return result;
            }

            // copy returned value.
            if (copyPolicy == VariableCopyPolicy.CopyOnRead)
            {
                value = CoreUtils.Clone(value);
            }

            return result;
        }

        /// <summary>
        /// Write the value for any non-value attribute.
        /// </summary>
        protected override ServiceResult WriteNonValueAttribute(
            ISystemContext context,
            uint attributeId,
            Variant value)
        {
            ServiceResult result = null;

            switch (attributeId)
            {
                case Attributes.DataType:
                    if (!value.TryGet(out NodeId dataType))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.DataType) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<NodeId> onWriteDataType = OnWriteDataType;

                    if (onWriteDataType != null)
                    {
                        result = onWriteDataType(context, this, ref dataType);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        DataType = dataType;
                    }

                    return result;
                case Attributes.ValueRank:
                    if (!value.TryGet(out int valueRank))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.ValueRank) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<int> onWriteValueRank = OnWriteValueRank;

                    if (onWriteValueRank != null)
                    {
                        result = onWriteValueRank(context, this, ref valueRank);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        ValueRank = valueRank;
                    }

                    return result;
                case Attributes.ArrayDimensions:
                    if (!value.TryGet(out uint[] arrayDimensions))
                    {
                        if (!value.IsNull)
                        {
                            return StatusCodes.BadTypeMismatch;
                        }
                        arrayDimensions = [];
                    }

                    if ((WriteMask & AttributeWriteMask.ArrayDimensions) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<uint[]> onWriteArrayDimensions
                        = OnWriteArrayDimensions;

                    if (onWriteArrayDimensions != null)
                    {
                        result = onWriteArrayDimensions(context, this, ref arrayDimensions);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        if (arrayDimensions != null)
                        {
                            m_arrayDimensions = new ReadOnlyList<uint>(arrayDimensions);
                        }
                        else
                        {
                            ArrayDimensions = null;
                        }
                    }

                    return result;
                default:
                    return base.WriteNonValueAttribute(context, attributeId, value);
            }
        }

        /// <summary>
        /// Write the value for the value attribute.
        /// </summary>
        protected override ServiceResult WriteValueAttribute(
            ISystemContext context,
            NumericRange indexRange,
            Variant value,
            StatusCode statusCode,
            DateTime sourceTimestamp)
        {
            ServiceResult result = null;

            if ((WriteMask & AttributeWriteMask.ValueForVariableType) == 0)
            {
                return StatusCodes.BadNotWritable;
            }

            // ensure the source timestamp has a valid value.
            if (sourceTimestamp == DateTime.MinValue)
            {
                sourceTimestamp = DateTime.UtcNow;
            }

            // index range writes not supported.
            if (indexRange != NumericRange.Empty)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            // verify data type.
            var typeInfo = TypeInfo.IsInstanceOfDataType(
                value,
                m_dataType,
                m_valueRank,
                context.NamespaceUris,
                context.TypeTable);

            if (typeInfo.IsUnknown)
            {
                return StatusCodes.BadTypeMismatch;
            }

            // check for simple write value handler.
            if (OnSimpleWriteValue != null)
            {
                result = OnSimpleWriteValue(context, this, ref value);

                if (ServiceResult.IsBad(result))
                {
                    return result;
                }
            }

            // update cached values.
            Value = value;

            return ServiceResult.Good;
        }

        private Variant m_value;
        private NodeId m_dataType;
        private int m_valueRank;
        private ReadOnlyList<uint> m_arrayDimensions;
    }

    /// <summary>
    /// A base class for all data variable type nodes.
    /// </summary>
    public class BaseDataVariableTypeState : BaseVariableTypeState
    {
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public BaseDataVariableTypeState()
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public static NodeState Construct(NodeState parent)
        {
            return new BaseDataVariableTypeState();
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            SuperTypeId = NodeId.Create(
                VariableTypes.BaseVariableType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            NodeId = NodeId.Create(
                VariableTypes.BaseDataVariableType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            BrowseName = QualifiedName.Create(
                BrowseNames.BaseDataVariableType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            DisplayName = new LocalizedText(
                BrowseNames.BaseDataVariableType,
                string.Empty,
                BrowseNames.BaseDataVariableType);
            Description = default;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            IsAbstract = false;
            Value = Variant.Null;
            DataType = NodeId.Create(
                DataTypes.BaseDataType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            ValueRank = ValueRanks.Any;
            ArrayDimensions = null;
        }
    }

    /// <summary>
    /// A typed base class for all data variable type nodes.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class BaseDataVariableTypeState<T> : BaseDataVariableTypeState
    {
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public BaseDataVariableTypeState()
        {
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            base.Initialize<T>(context);
        }

        /// <summary>
        /// The value of the variable.
        /// </summary>
        public new T Value
        {
            get => BaseVariableState.CheckTypeBeforeCast<T>(base.Value, true);
            set => base.Value = new Variant(value);
        }
    }

    /// <summary>
    /// A base class for all property variable type nodes.
    /// </summary>
    public class PropertyTypeState : BaseVariableTypeState
    {
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public PropertyTypeState()
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public static NodeState Construct(NodeState parent)
        {
            return new PropertyTypeState();
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            SuperTypeId = NodeId.Create(
                VariableTypes.BaseVariableType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            NodeId = NodeId.Create(
                VariableTypes.PropertyType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            BrowseName = QualifiedName.Create(
                BrowseNames.PropertyType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            DisplayName = new LocalizedText(
                BrowseNames.PropertyType,
                string.Empty,
                BrowseNames.PropertyType);
            Description = default;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            IsAbstract = false;
            Value = Variant.Null;
            DataType = NodeId.Create(
                DataTypes.BaseDataType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            ValueRank = ValueRanks.Any;
            ArrayDimensions = null;
        }
    }

    /// <summary>
    /// A typed base class for all property variable type nodes.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class PropertyTypeState<T> : PropertyTypeState
    {
        /// <summary>
        /// Initializes the type with its default attribute values.
        /// </summary>
        public PropertyTypeState()
        {
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);
            base.Initialize<T>(context);
        }

        /// <summary>
        /// The value of the variable.
        /// </summary>
        public new T Value
        {
            get => BaseVariableState.CheckTypeBeforeCast<T>(base.Value, true);
            set => base.Value = new Variant(value);
        }
    }
}
