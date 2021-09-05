/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;
using System.Text;

namespace Opc.Ua
{
    /// <summary> 
    /// The base class for all variable type nodes.
    /// </summary>
    public abstract class BaseVariableTypeState : BaseTypeState
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with its defalt attribute values.
        /// </summary>
        protected BaseVariableTypeState() : base(NodeClass.VariableType)
        {
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the instance from another instance.
        /// </summary>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            BaseVariableTypeState type = source as BaseVariableTypeState;

            if (type != null)
            {
                m_value = Utils.Clone(type.m_value);
                m_dataType = type.m_dataType;
                m_valueRank = type.m_valueRank;
                m_arrayDimensions = null;

                if (type.m_arrayDimensions != null)
                {
                    m_arrayDimensions = new ReadOnlyList<uint>(type.m_arrayDimensions, true);
                }
            }

            m_value = ExtractValueFromVariant(context, m_value, false);

            base.Initialize(context, source);
        }

        /// <summary>
        /// Sets the value to its default value if it is not valid.
        /// </summary>
        protected virtual object ExtractValueFromVariant(ISystemContext context, object value, bool throwOnError)
        {
            return value;
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The value of the variable.
        /// </summary>
        public object Value
        {
            get
            {
                return m_value;
            }

            set
            {
                if (!Object.ReferenceEquals(m_value, value))
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
            get 
            { 
                return new Variant(m_value); 
            }
            
            set
            {
                Value = ExtractValueFromVariant(null, value.Value, false);
            }
        }

        /// <summary>
        /// The data type for the variable value.
        /// </summary>
        public NodeId DataType
        {
            get
            {
                return m_dataType;
            }

            set
            {
                if (!Object.ReferenceEquals(m_dataType, value))
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
            get
            {
                return m_valueRank;
            }

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
            get
            {
                return m_arrayDimensions;
            }

            set
            {
                if (!Object.ReferenceEquals(m_arrayDimensions, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_arrayDimensions = value;
            }
        }
        #endregion

        #region Event Callbacks
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
        public NodeAttributeEventHandler<IList<uint>> OnReadArrayDimensions;

        /// <summary>
        /// Raised when the ArrayDimensions attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<IList<uint>> OnWriteArrayDimensions;
        #endregion

        #region Serialization Functions
        /// <summary>
        /// Exports a copy of the node to a node table.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        protected override void Export(ISystemContext context, Node node)
        {
            base.Export(context, node);

            VariableTypeNode variableTypeNode = node as VariableTypeNode;

            if (variableTypeNode != null)
            {
                variableTypeNode.Value = new Variant(Utils.Clone(this.Value));
                variableTypeNode.DataType = this.DataType;
                variableTypeNode.ValueRank = this.ValueRank;
                variableTypeNode.ArrayDimensions = null;

                if (this.ArrayDimensions != null)
                {
                    variableTypeNode.ArrayDimensions = new UInt32Collection(this.ArrayDimensions);
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

            if (m_value != null)
            {
                encoder.WriteVariant("Value", WrappedValue);
            }

            if (!NodeId.IsNull(DataType))
            {
                encoder.WriteNodeId("DataType", DataType);
            }

            if (ValueRank != ValueRanks.Any)
            {
                encoder.WriteInt32("ValueRank", ValueRank);
            }

            if (ArrayDimensions != null)
            {
                encoder.WriteString("ArrayDimensions", BaseVariableState.ArrayDimensionsToXml(ArrayDimensions));
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
                WrappedValue = decoder.ReadVariant("Value");
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
                ArrayDimensions = BaseVariableState.ArrayDimensionsFromXml(decoder.ReadString("ArrayDimensions"));
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

            if (WrappedValue != Variant.Null)
            {
                attributesToSave |= AttributesToSave.Value;
            }

            if (!NodeId.IsNull(m_dataType))
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
        public override void Save(ISystemContext context, BinaryEncoder encoder, AttributesToSave attributesToSave)
        {
            base.Save(context, encoder, attributesToSave);

            if ((attributesToSave & AttributesToSave.Value) != 0)
            {
                encoder.WriteVariant(null, WrappedValue);
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
        /// <param name="attibutesToLoad">The attributes to load.</param>
        public override void Update(ISystemContext context, BinaryDecoder decoder, AttributesToSave attibutesToLoad)
        {
            base.Update(context, decoder, attibutesToLoad);

            if ((attibutesToLoad & AttributesToSave.Value) != 0)
            {
                WrappedValue = decoder.ReadVariant(null);
            }

            if ((attibutesToLoad & AttributesToSave.DataType) != 0)
            {
                m_dataType = decoder.ReadNodeId(null);
            }

            if ((attibutesToLoad & AttributesToSave.ValueRank) != 0)
            {
                m_valueRank = decoder.ReadInt32(null);
            }

            if ((attibutesToLoad & AttributesToSave.ArrayDimensions) != 0)
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
        #endregion

        #region Read Support Functions
        /// <summary>
        /// Reads the value for any non-value attribute.
        /// </summary>
        protected override ServiceResult ReadNonValueAttribute(
            ISystemContext context,
            uint attributeId,
            ref object value)
        {
            ServiceResult result = null;

            switch (attributeId)
            {
                case Attributes.DataType:
                {
                    NodeId dataType = m_dataType;

                    if (OnReadDataType != null)
                    {
                        result = OnReadDataType(context, this, ref dataType);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = dataType;
                    }

                    return result;
                }

                case Attributes.ValueRank:
                {
                    int valueRank = m_valueRank;

                    if (OnReadValueRank != null)
                    {
                        result = OnReadValueRank(context, this, ref valueRank);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = valueRank;
                    }

                    return result;
                }

                case Attributes.ArrayDimensions:
                {
                    IList<uint> arrayDimensions = m_arrayDimensions;

                    if (OnReadArrayDimensions != null)
                    {
                        result = OnReadArrayDimensions(context, this, ref arrayDimensions);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = arrayDimensions;
                    }

                    return result;
                }
            }

            return base.ReadNonValueAttribute(context, attributeId, ref value);
        }

        /// <summary>
        /// Reads the value for the value attribute.
        /// </summary>
        protected override ServiceResult ReadValueAttribute(
            ISystemContext context,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref DateTime sourceTimestamp)
        {  
            value = m_value;

            ServiceResult result = ServiceResult.Good;

            VariableCopyPolicy copyPolicy = VariableCopyPolicy.CopyOnRead;

            // use default behavior.
            if (OnSimpleReadValue != null)
            {
                result = OnSimpleReadValue(
                    context,
                    this,
                    ref value);

                if (ServiceResult.IsBad(result))
                {
                    return result;
                }

                copyPolicy = VariableCopyPolicy.Never;
            }
            else
            {
                // check if a valid value exists.
                if (value == null)
                {
                    return StatusCodes.BadAttributeIdInvalid;
                }
            }
            
            // apply the index range and encoding.
            result = BaseVariableState.ApplyIndexRangeAndDataEncoding(context, indexRange, dataEncoding, ref value);

            if (ServiceResult.IsBad(result))
            {
                return result;
            }

            // copy returned value.
            if (copyPolicy == VariableCopyPolicy.CopyOnRead)
            {
                value = Utils.Clone(value);
            }

            return result;
        }
        #endregion

        #region Write Support Functions
        /// <summary>
        /// Write the value for any non-value attribute.
        /// </summary>
        protected override ServiceResult WriteNonValueAttribute(
            ISystemContext context,
            uint attributeId,
            object value)
        {
            ServiceResult result = null;

            switch (attributeId)
            {
                case Attributes.DataType:
                {
                    NodeId dataType = value as NodeId;

                    if (dataType == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.DataType) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    if (OnWriteDataType != null)
                    {
                        result = OnWriteDataType(context, this, ref dataType);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        DataType = dataType;
                    }

                    return result;
                }

                case Attributes.ValueRank:
                {
                    int? valueRankRef = value as int?;

                    if (valueRankRef == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.ValueRank) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    int valueRank = valueRankRef.Value;

                    if (OnWriteValueRank != null)
                    {
                        result = OnWriteValueRank(context, this, ref valueRank);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                       ValueRank = valueRank;
                    }

                    return result;
                }

                case Attributes.ArrayDimensions:
                {
                    IList<uint> arrayDimensions = value as IList<uint>;

                    if ((WriteMask & AttributeWriteMask.ArrayDimensions) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    if (OnWriteArrayDimensions != null)
                    {
                        result = OnWriteArrayDimensions(context, this, ref arrayDimensions);
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
                }
            }

            return base.WriteNonValueAttribute(context, attributeId, value);
        }

        /// <summary>
        /// Write the value for the value attribute.
        /// </summary>
        protected override ServiceResult WriteValueAttribute(
            ISystemContext context,
            NumericRange indexRange,
            object value,
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
            TypeInfo typeInfo = TypeInfo.IsInstanceOfDataType(
                value,
                m_dataType,
                m_valueRank,
                context.NamespaceUris,
                context.TypeTable);

            if (typeInfo == null || typeInfo == TypeInfo.Unknown)
            {
                return StatusCodes.BadTypeMismatch;
            }

            // check for simple write value handler.
            if (OnSimpleWriteValue != null)
            {
                result = OnSimpleWriteValue(
                    context,
                    this,
                    ref value);

                if (ServiceResult.IsBad(result))
                {
                    return result;
                }
            }

            // update cached values.
            Value = value;

            return ServiceResult.Good;
        }
        #endregion
        
        #region Private Fields
        private object m_value;
        private NodeId m_dataType;
        private int m_valueRank;
        private ReadOnlyList<uint> m_arrayDimensions;
        #endregion
    }

    /// <summary> 
    /// A base class for all data variable type nodes.
    /// </summary>
    public class BaseDataVariableTypeState : BaseVariableTypeState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its defalt attribute values.
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
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            SuperTypeId = Opc.Ua.NodeId.Create(Opc.Ua.VariableTypes.BaseVariableType, Opc.Ua.Namespaces.OpcUa, context.NamespaceUris);
            NodeId = Opc.Ua.NodeId.Create(Opc.Ua.VariableTypes.BaseDataVariableType, Opc.Ua.Namespaces.OpcUa, context.NamespaceUris);
            BrowseName = Opc.Ua.QualifiedName.Create(Opc.Ua.BrowseNames.BaseDataVariableType, Opc.Ua.Namespaces.OpcUa, context.NamespaceUris);
            DisplayName = new LocalizedText(Opc.Ua.BrowseNames.BaseDataVariableType, String.Empty, Opc.Ua.BrowseNames.BaseDataVariableType);
            Description = null;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            IsAbstract = false;
            Value = null;
            DataType = Opc.Ua.NodeId.Create(Opc.Ua.DataTypes.BaseDataType, Opc.Ua.Namespaces.OpcUa, context.NamespaceUris);
            ValueRank = ValueRanks.Any;
            ArrayDimensions = null;
        }
        #endregion
    }

    /// <summary> 
    /// A typed base class for all data variable type nodes.
    /// </summary>
    public class BaseDataVariableTypeState<T> : BaseDataVariableTypeState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its defalt attribute values.
        /// </summary>
        public BaseDataVariableTypeState()
        {
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);

            Value = default(T);
            DataType = TypeInfo.GetDataTypeId(typeof(T));
            ValueRank = TypeInfo.GetValueRank(typeof(T));
        }

        /// <summary>
        /// Sets the value to its default value if it is not valid.
        /// </summary>
        protected override object ExtractValueFromVariant(ISystemContext context, object value, bool throwOnError)
        {
            return BaseVariableState.ExtractValueFromVariant<T>(context, value, throwOnError);
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The value of the variable.
        /// </summary>
        public new T Value
        {
            get
            {
                return BaseVariableState.CheckTypeBeforeCast<T>(base.Value, true);
            }

            set
            {
                base.Value = value;
            }
        }
        #endregion
    }

    /// <summary> 
    /// A base class for all property variable type nodes.
    /// </summary>
    public class PropertyTypeState : BaseVariableTypeState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its defalt attribute values.
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
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            SuperTypeId = Opc.Ua.NodeId.Create(Opc.Ua.VariableTypes.BaseVariableType, Opc.Ua.Namespaces.OpcUa, context.NamespaceUris);
            NodeId = Opc.Ua.NodeId.Create(Opc.Ua.VariableTypes.PropertyType, Opc.Ua.Namespaces.OpcUa, context.NamespaceUris);
            BrowseName = Opc.Ua.QualifiedName.Create(Opc.Ua.BrowseNames.PropertyType, Opc.Ua.Namespaces.OpcUa, context.NamespaceUris);
            DisplayName = new LocalizedText(Opc.Ua.BrowseNames.PropertyType, String.Empty, Opc.Ua.BrowseNames.PropertyType);
            Description = null;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            IsAbstract = false;
            Value = null;
            DataType = Opc.Ua.NodeId.Create(Opc.Ua.DataTypes.BaseDataType, Opc.Ua.Namespaces.OpcUa, context.NamespaceUris);
            ValueRank = ValueRanks.Any;
            ArrayDimensions = null;
        }
        #endregion
    }

    /// <summary> 
    /// A typed base class for all property variable type nodes.
    /// </summary>
    public class PropertyTypeState<T> : PropertyTypeState
    {
        #region Constructors
        /// <summary>
        /// Initializes the type with its defalt attribute values.
        /// </summary>
        public PropertyTypeState()
        {
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);

            Value = default(T);
            DataType = TypeInfo.GetDataTypeId(typeof(T));
            ValueRank = TypeInfo.GetValueRank(typeof(T));
        }

        /// <summary>
        /// Sets the value to its default value if it is not valid.
        /// </summary>
        protected override object ExtractValueFromVariant(ISystemContext context, object value, bool throwOnError)
        {
            return BaseVariableState.ExtractValueFromVariant<T>(context, value, throwOnError);
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The value of the variable.
        /// </summary>
        public new T Value
        {
            get
            {
                return BaseVariableState.CheckTypeBeforeCast<T>(base.Value, true);
            }

            set
            {
                base.Value = value;
            }
        }
        #endregion
    }
}
