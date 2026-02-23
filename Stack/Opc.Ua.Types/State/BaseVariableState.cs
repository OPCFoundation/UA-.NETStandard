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
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// The base class for all variable nodes.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public abstract class BaseVariableState : BaseInstanceState
    {
        /// <summary>
        /// Initializes the instance with its default attribute values.
        /// </summary>
        /// <param name="parent">The parent node.</param>
        protected BaseVariableState(NodeState parent)
            : base(NodeClass.Variable, parent)
        {
            m_value = Variant.Null;
            m_statusCode = StatusCodes.BadWaitingForInitialData;
            m_timestamp = DateTime.MinValue;
            m_dataType = DataTypeIds.BaseDataType;
            m_valueRank = ValueRanks.Any;
            m_arrayDimensions = default;
            m_accessLevel = m_userAccessLevel = AccessLevels.CurrentRead;
            m_minimumSamplingInterval = MinimumSamplingIntervals.Continuous;
            m_historizing = false;
            CopyPolicy = VariableCopyPolicy.CopyOnRead;
            m_valueTouched = false;
        }

        /// <summary>
        /// Consume the telemetry context
        /// </summary>
        protected override void Initialize(ITelemetryContext telemetry)
        {
            m_logger = telemetry.CreateLogger<BaseVariableState>();
            base.Initialize(telemetry);
        }

        /// <summary>
        /// Initializes the instance from another instance.
        /// </summary>
        /// <param name="context">The description how access the system containing the data.</param>
        /// <param name="source">A source node to be copied to this instance.</param>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            if (source is BaseVariableState instance)
            {
                m_value = instance.m_value;
                m_timestamp = instance.m_timestamp;
                m_dataType = instance.m_dataType;
                m_valueRank = instance.m_valueRank;
                m_arrayDimensions = instance.m_arrayDimensions;
                m_accessLevel = instance.m_accessLevel;
                m_userAccessLevel = instance.m_userAccessLevel;
                m_minimumSamplingInterval = instance.m_minimumSamplingInterval;
                m_historizing = instance.m_historizing;
                m_valueTouched = instance.m_valueTouched;
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
        /// If overridden returns the id of the default type definition node for the instance.
        /// </summary>
        /// <param name="namespaceUris">The namespace uris.</param>
        /// <returns>Returns the id of the default type definition or
        /// <see cref="VariableTypes.BaseVariableType"/></returns> if not overridden
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return VariableTypeIds.BaseVariableType;
        }

        /// <summary>
        /// If overridden returns the id of the default data type node for the instance.
        /// </summary>
        /// <param name="namespaceUris">The namespace uris.</param>
        /// <returns>
        /// The id <see cref="NodeId"/> of the default data type node for the instance
        /// or <see cref="DataTypes.BaseDataType"/> if not overridden.
        /// </returns>
        protected virtual NodeId GetDefaultDataTypeId(NamespaceTable namespaceUris)
        {
            return DataTypeIds.BaseDataType;
        }

        /// <summary>
        /// If overridden returns the id of the default value rank for the instance.
        /// </summary>
        /// <returns>
        /// The id of the default value rank or <see cref="ValueRanks.Any"/> if not overridden.
        /// </returns>
        protected virtual int GetDefaultValueRank()
        {
            return ValueRanks.Any;
        }

        /// <summary>
        /// Decodes the contents of an extension object.
        /// </summary>
        /// <param name="context">The context (uses MessageContextExtension.Current.MessageContext if null).</param>
        /// <param name="targetType">The type that the ExtensionObject must be converted to.</param>
        /// <param name="extension">The ExtensionObject to convert.</param>
        /// <param name="throwOnError">Whether to throw an exception on error.</param>
        /// <returns>The decoded instance. Null on error.</returns>
        /// <exception cref="ServiceResultException"></exception>
        public static object DecodeExtensionObject(
            ISystemContext context,
            Type targetType,
            ExtensionObject extension,
            bool throwOnError)
        {
            if (targetType.IsInstanceOfType(extension.Body))
            {
                return extension.Body;
            }

            if (Activator.CreateInstance(targetType) is IEncodeable instance)
            {
                IDecoder decoder = null;
                try
                {
                    IServiceMessageContext messageContext;
                    if (context != null)
                    {
                        messageContext = context.AsMessageContext();
                    }
                    else
                    {
                        messageContext = AmbientMessageContext.CurrentContext;
                    }

                    if (extension.Encoding == ExtensionObjectEncoding.Binary)
                    {
                        decoder = new BinaryDecoder(
                            extension.Body is ByteString b ? b.ToArray() : [],
                            messageContext);
                    }
                    else if (extension.Encoding == ExtensionObjectEncoding.Xml)
                    {
                        decoder = new XmlDecoder(
                            extension.Body is XmlElement xe ? xe : default,
                            messageContext);
                    }

                    if (decoder != null)
                    {
                        try
                        {
                            instance.Decode(decoder);
                            return instance;
                        }
                        catch (Exception e)
                        {
                            if (throwOnError)
                            {
                                throw ServiceResultException.Create(
                                    StatusCodes.BadTypeMismatch,
                                    "Cannot convert ExtensionObject to {0}. Error = {1}",
                                    targetType.Name,
                                    e.Message);
                            }
                        }
                    }
                }
                finally
                {
                    CoreUtils.SilentDispose(decoder);
                }
            }

            if (throwOnError)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Cannot convert ExtensionObject to {0}.",
                    targetType.Name);
            }

            return null;
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            var clone = (BaseInstanceState)Activator.CreateInstance(GetType(), Parent);
            CopyTo(clone);
            return clone;
        }

        /// <inheritdoc/>
        public override bool DeepEquals(NodeState node)
        {
            if (node is not BaseVariableState state)
            {
                return false;
            }
            return
                base.DeepEquals(state) &&
                state.Timestamp == Timestamp &&
                state.StatusCode == StatusCode &&
                EqualityComparer<object>.Default.Equals(state.Value, Value) &&
                state.DataType == DataType &&
                state.ValueRank == ValueRank &&
                state.ArrayDimensions == ArrayDimensions &&
                state.AccessLevel == AccessLevel &&
                state.UserAccessLevel == UserAccessLevel &&
                state.MinimumSamplingInterval == MinimumSamplingInterval &&
                state.Historizing == Historizing
                ;
        }

        /// <inheritdoc/>
        public override int DeepGetHashCode()
        {
            var hash = new HashCode();
            hash.Add(base.DeepGetHashCode());
            hash.Add(Timestamp);
            hash.Add(StatusCode);
            hash.Add(Value);
            hash.Add(DataType);
            hash.Add(ValueRank);
            hash.Add(ArrayDimensions);
            hash.Add(AccessLevel);
            hash.Add(UserAccessLevel);
            hash.Add(MinimumSamplingInterval);
            hash.Add(Historizing);
            return hash.ToHashCode();
        }

        /// <inheritdoc/>
        protected override void CopyTo(NodeState target)
        {
            if (target is BaseVariableState state)
            {
                state.Value = Value;
                state.Timestamp = Timestamp;
                state.StatusCode = StatusCode;
                state.m_valueTouched = m_valueTouched;
                state.DataType = DataType;
                state.ValueRank = ValueRank;
                state.ArrayDimensions = ArrayDimensions;
                state.AccessLevel = AccessLevel;
                state.UserAccessLevel = UserAccessLevel;
                state.MinimumSamplingInterval = MinimumSamplingInterval;
                state.Historizing = Historizing;
            }
            base.CopyTo(target);
        }

        /// <summary>
        /// The value of the variable as a Variant.
        /// </summary>
        /// <value>The wrapped value as a Variant.</value>
        [DataMember(Name = "Value", Order = 0, IsRequired = false, EmitDefaultValue = false)]
        public Variant Value
        {
            get => m_value;
            set
            {
                if (value != m_value)
                {
                    ChangeMasks |= NodeStateChangeMasks.Value;
                }

                if (!m_valueTouched)
                {
                    StatusCode = StatusCodes.Good;
                }

                m_value = value;

                m_valueTouched = true;
            }
        }

        /// <summary>
        /// The value of the variable as a Variant.
        /// </summary>
        /// <value>The wrapped value as a Variant.</value>
        public Variant WrappedValue
        {
            get => Value;
            set => Value = value;
        }

        /// <summary>
        /// The timestamp associated with the variable value.
        /// </summary>
        /// <value>The timestamp.</value>
        public DateTime Timestamp
        {
            get => m_timestamp;
            set
            {
                if (m_timestamp != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.Value;
                }

                m_timestamp = value;
            }
        }

        /// <summary>
        /// The status code associated with the variable value.
        /// </summary>
        /// <value>The status code.</value>
        public StatusCode StatusCode
        {
            get => m_statusCode;
            set
            {
                if (m_statusCode != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.Value;
                }

                m_statusCode = value;
            }
        }

        /// <summary>
        /// The behavior to use when reading or writing all or part of the object.
        /// </summary>
        /// <value>The copy policy that specifies the policies to use when handling reads and write to value.</value>
        /// <remarks>
        /// This value is ignored if the OnReadValue or OnWriteValue event handlers are provided.
        /// </remarks>
        public VariableCopyPolicy CopyPolicy { get; set; }

        /// <summary>
        /// The data type for the variable value.
        /// </summary>
        /// <value>The type of the data <see cref="NodeId"/>.</value>
        [DataMember(Name = "DataType", Order = 1, IsRequired = false, EmitDefaultValue = false)]
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
        /// <value>The value rank. </value>
        /// <remarks>Indicates whether the DataType is an array and how many dimensions the array has.</remarks>
        [DataMember(Name = "ValueRank", Order = 2, IsRequired = false, EmitDefaultValue = false)]
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
        /// <value>The array dimensions.</value>
        /// <remarks>
        /// If the Value Rank does not identify an array of a specific dimension (i.e. ValueRank &lt;= 0)
        /// the Array Dimensions attribute can either be set to null or the attribute is missing. This behaviour is vendor-specific.
        /// If the Value Rank attribute specifies an array of a specific dimension (i.e. ValueRank &gt; 0) then the Array Dimensions
        /// attribute shall be specified in the table defining the Variable.
        /// </remarks>
        public ArrayOf<uint> ArrayDimensions
        {
            get => m_arrayDimensions;
            set
            {
                if (m_arrayDimensions != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_arrayDimensions = value;
            }
        }

        /// <summary>
        /// The type of access available for the variable.
        /// </summary>
        /// <value>The access level.</value>
        [DataMember(Name = "AccessLevel", Order = 4, IsRequired = false, EmitDefaultValue = false)]
        public byte AccessLevel
        {
            get => (byte)(m_accessLevel & 0xFF);
            set
            {
                if (AccessLevel != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                // set first 8 bits of AccessLevelEx
                m_accessLevel = (m_accessLevel & 0xFFFFFF00) | value;
            }
        }

        /// <summary>
        /// The type of access granted to the current user.
        /// </summary>
        /// <value>The user access level.</value>
        [DataMember(
            Name = "UserAccessLevel",
            Order = 5,
            IsRequired = false,
            EmitDefaultValue = false)]
        public byte UserAccessLevel
        {
            get => m_userAccessLevel;
            set
            {
                if (m_userAccessLevel != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_userAccessLevel = value;
            }
        }

        /// <summary>
        /// The minimum sampling interval supported by the variable.
        /// </summary>
        /// <value>The minimum sampling interval.</value>
        [DataMember(
            Name = "MinimumSamplingInterval",
            Order = 6,
            IsRequired = false,
            EmitDefaultValue = false)]
        public double MinimumSamplingInterval
        {
            get => m_minimumSamplingInterval;
            set
            {
                if (m_minimumSamplingInterval != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_minimumSamplingInterval = value;
            }
        }

        /// <summary>
        /// Whether the server is archiving the value of the variable.
        /// </summary>
        /// <value><c>true</c> if historizing; otherwise, <c>false</c>.</value>
        [DataMember(Name = "Historizing", Order = 7, IsRequired = false, EmitDefaultValue = false)]
        public bool Historizing
        {
            get => m_historizing;
            set
            {
                if (m_historizing != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_historizing = value;
            }
        }

        /// <summary>
        /// A bit mask specifying how the value may be accessed.
        /// </summary>
        /// <value>The extended access level.</value>
        [DataMember(
            Name = "AccessLevelEx",
            Order = 8,
            IsRequired = false,
            EmitDefaultValue = false)]
        public uint AccessLevelEx
        {
            get => m_accessLevel;
            set
            {
                if (m_accessLevel != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_accessLevel = value;
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
        /// Raised when the Value attribute is read.
        /// </summary>
        public NodeValueEventHandler OnReadValue;

        /// <summary>
        /// Raised when the Value attribute is written.
        /// </summary>
        public NodeValueEventHandler OnWriteValue;

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
        public NodeAttributeEventHandler<ArrayOf<uint>> OnReadArrayDimensions;

        /// <summary>
        /// Raised when the ArrayDimensions attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<ArrayOf<uint>> OnWriteArrayDimensions;

        /// <summary>
        /// Raised when the AccessLevel attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<byte> OnReadAccessLevel;

        /// <summary>
        /// Raised when the AccessLevel attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<byte> OnWriteAccessLevel;

        /// <summary>
        /// Raised when the UserAccessLevel attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<byte> OnReadUserAccessLevel;

        /// <summary>
        /// Raised when the UserAccessLevel attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<byte> OnWriteUserAccessLevel;

        /// <summary>
        /// Raised when the MinimumSamplingInterval attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<double> OnReadMinimumSamplingInterval;

        /// <summary>
        /// Raised when the MinimumSamplingInterval attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<double> OnWriteMinimumSamplingInterval;

        /// <summary>
        /// Raised when the Historizing attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<bool> OnReadHistorizing;

        /// <summary>
        /// Raised when the Historizing attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<bool> OnWriteHistorizing;

        /// <summary>
        /// Raised when the AccessLevelEx attribute is read.
        /// </summary>
        public NodeAttributeEventHandler<uint> OnReadAccessLevelEx;

        /// <summary>
        /// Raised when the AccessLevelEx attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<uint> OnWriteAccessLevelEx;

        /// <summary>
        /// Exports a copy of the node to a <paramref name="node"/> node provided the
        /// <paramref name="node"/> type is compatible with <see cref="VariableNode"/>.
        /// </summary>
        /// <param name="context">The context that describes how access the system
        /// containing the data.</param>
        /// <param name="node">The node to be a copy of this instance.</param>
        protected override void Export(ISystemContext context, Node node)
        {
            base.Export(context, node);

            if (node is VariableNode variableNode)
            {
                try
                {
                    variableNode.Value = CoreUtils.Clone(Value); // TODO: Clone correctly

                    variableNode.DataType = DataType;
                    variableNode.ValueRank = ValueRank;
                    variableNode.ArrayDimensions = null;
                    variableNode.ArrayDimensions = ArrayDimensions;
                    variableNode.AccessLevel = AccessLevel;
                    variableNode.UserAccessLevel = UserAccessLevel;
                    variableNode.MinimumSamplingInterval = MinimumSamplingInterval;
                    variableNode.Historizing = Historizing;
                }
                catch (Exception e)
                {
                    m_logger.LogError(e, "Unexpected error exporting node");
                }
            }
        }

        /// <summary>
        /// Saves the attributes from this instance to the <paramref name="encoder"/>.
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

            if (StatusCode != StatusCodes.Good)
            {
                encoder.WriteStatusCode("StatusCode", StatusCode);
            }

            if (!DataType.IsNull)
            {
                encoder.WriteNodeId("DataType", DataType);
            }

            if (ValueRank != ValueRanks.Any)
            {
                encoder.WriteInt32("ValueRank", ValueRank);
            }

            if (!ArrayDimensions.IsEmpty)
            {
                encoder.WriteString("ArrayDimensions", ArrayDimensionsToXml(ArrayDimensions));
            }

            if (AccessLevel != 0)
            {
                encoder.WriteByte("AccessLevel", AccessLevel);
            }

            if (UserAccessLevel != 0)
            {
                encoder.WriteByte("UserAccessLevel", UserAccessLevel);
            }

            if (MinimumSamplingInterval != 0)
            {
                encoder.WriteDouble("MinimumSamplingInterval", MinimumSamplingInterval);
            }

            if (Historizing)
            {
                encoder.WriteBoolean("Historizing", Historizing);
            }

            encoder.PopNamespace();
        }

        /// <summary>
        /// Updates the attributes from the <paramref name="decoder"/>.
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

            if (decoder.Peek("Timestamp"))
            {
                Timestamp = decoder.ReadDateTime("Timestamp");
            }

            if (decoder.Peek("StatusCode"))
            {
                StatusCode = decoder.ReadStatusCode("StatusCode");
            }
            else
            {
                StatusCode = StatusCodes.Good;
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
                ArrayDimensions = ArrayDimensionsFromXml(decoder.ReadString("ArrayDimensions"));
            }

            if (decoder.Peek("AccessLevel"))
            {
                AccessLevel = decoder.ReadByte("AccessLevel");
            }

            if (decoder.Peek("UserAccessLevel"))
            {
                UserAccessLevel = decoder.ReadByte("UserAccessLevel");
            }

            if (decoder.Peek("MinimumSamplingInterval"))
            {
                MinimumSamplingInterval = decoder.ReadDouble("MinimumSamplingInterval");
            }

            if (decoder.Peek("Historizing"))
            {
                Historizing = decoder.ReadBoolean("Historizing");
            }

            decoder.PopNamespace();
        }

        /// <summary>
        /// Returns a mask which indicates which attributes have non-default value.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <returns>
        /// A mask that specifies the available attributes.
        /// </returns>
        public override AttributesToSave GetAttributesToSave(ISystemContext context)
        {
            AttributesToSave attributesToSave = base.GetAttributesToSave(context);

            if (!Value.IsNull)
            {
                attributesToSave |= AttributesToSave.Value;
            }

            if (m_statusCode != StatusCodes.Good)
            {
                attributesToSave |= AttributesToSave.StatusCode;
            }

            if (!m_dataType.IsNull)
            {
                attributesToSave |= AttributesToSave.DataType;
            }

            if (m_valueRank != ValueRanks.Any)
            {
                attributesToSave |= AttributesToSave.ValueRank;
            }

            if (!m_arrayDimensions.IsEmpty)
            {
                attributesToSave |= AttributesToSave.ArrayDimensions;
            }

            if (m_accessLevel != 0)
            {
                attributesToSave |= AttributesToSave.AccessLevel;
            }

            if (m_userAccessLevel != 0)
            {
                attributesToSave |= AttributesToSave.UserAccessLevel;
            }

            if (m_minimumSamplingInterval != 0)
            {
                attributesToSave |= AttributesToSave.MinimumSamplingInterval;
            }

            if (m_historizing)
            {
                attributesToSave |= AttributesToSave.Historizing;
            }

            return attributesToSave;
        }

        /// <summary>
        /// Saves object in an binary stream.
        /// </summary>
        /// <param name="context">The context that describes how access the system containing the data..</param>
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

            if ((attributesToSave & AttributesToSave.StatusCode) != 0)
            {
                encoder.WriteStatusCode(null, m_statusCode);
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
                encoder.WriteUInt32Array(null, m_arrayDimensions.ToArray());
            }

            if ((attributesToSave & AttributesToSave.AccessLevel) != 0)
            {
                encoder.WriteByte(null, AccessLevel);
            }

            if ((attributesToSave & AttributesToSave.UserAccessLevel) != 0)
            {
                encoder.WriteByte(null, m_userAccessLevel);
            }

            if ((attributesToSave & AttributesToSave.MinimumSamplingInterval) != 0)
            {
                encoder.WriteDouble(null, m_minimumSamplingInterval);
            }

            if ((attributesToSave & AttributesToSave.Historizing) != 0)
            {
                encoder.WriteBoolean(null, m_historizing);
            }
        }

        /// <summary>
        /// Updates the attributes of this instance from the <paramref name="decoder"/>.
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

            if ((attributesToLoad & AttributesToSave.StatusCode) != 0)
            {
                m_statusCode = decoder.ReadStatusCode(null);
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
                m_arrayDimensions = decoder.ReadUInt32Array(null);
            }

            if ((attributesToLoad & AttributesToSave.AccessLevel) != 0)
            {
                AccessLevel = decoder.ReadByte(null);
            }

            if ((attributesToLoad & AttributesToSave.UserAccessLevel) != 0)
            {
                m_userAccessLevel = decoder.ReadByte(null);
            }

            if ((attributesToLoad & AttributesToSave.MinimumSamplingInterval) != 0)
            {
                m_minimumSamplingInterval = decoder.ReadDouble(null);
            }

            if ((attributesToLoad & AttributesToSave.Historizing) != 0)
            {
                m_historizing = decoder.ReadBoolean(null);
            }
        }

        /// <summary>
        /// Converts the array dimensions to an XML string.
        /// </summary>
        /// <param name="arrayDimensions">The array dimensions.</param>
        /// <returns>The XML string value.</returns>
        public static string ArrayDimensionsToXml(ArrayOf<uint> arrayDimensions)
        {
            if (arrayDimensions.IsEmpty)
            {
                return null;
            }

            var buffer = new StringBuilder();

            for (int ii = 0; ii < arrayDimensions.Count; ii++)
            {
                if (buffer.Length > 0)
                {
                    buffer.Append(',');
                }

                buffer.Append(arrayDimensions[ii]);
            }

            return buffer.ToString();
        }

        /// <summary>
        /// Returns a list of the array dimensions.
        /// </summary>
        /// <param name="value">The XML string value.</param>
        /// <returns>The array dimensions list.</returns>
        public static ArrayOf<uint> ArrayDimensionsFromXml(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return default;
            }

            string[] fields = value.Split(s_commaSeparator, StringSplitOptions.RemoveEmptyEntries);

            if (fields == null || fields.Length == 0)
            {
                return default;
            }

            uint[] arrayDimensions = new uint[fields.Length];

            for (int ii = 0; ii < arrayDimensions.Length; ii++)
            {
                try
                {
                    arrayDimensions[ii] = Convert.ToUInt32(
                        fields[ii],
                        CultureInfo.InvariantCulture);
                }
                catch
                {
                    arrayDimensions[ii] = 0;
                }
            }

            return arrayDimensions.ToArrayOf();
        }

        /// <summary>
        /// Recursively sets the status code and timestamp for the node and all child variables.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="statusCode">The status code.</param>
        /// <param name="timestamp">The timestamp. Not updated if set to DateTime.Min</param>
        public override void SetStatusCode(
            ISystemContext context,
            StatusCode statusCode,
            DateTime timestamp)
        {
            base.SetStatusCode(context, statusCode, timestamp);

            StatusCode = statusCode;

            if (timestamp != DateTime.MinValue)
            {
                Timestamp = timestamp;
            }
        }

        /// <summary>
        /// Reads the value for any non-value attribute.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="attributeId">The attribute identifier <see cref="Attributes"/>.</param>
        /// <param name="value">The returned value.</param>
        /// <returns>
        /// An instance of the <see cref="ServiceResult"/> containing the status code and diagnostic info for the operation.
        /// ServiceResult.Good if successful. Detailed error information otherwise.
        /// </returns>
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
                    ArrayOf<uint> arrayDimensions = m_arrayDimensions;

                    NodeAttributeEventHandler<ArrayOf<uint>> onReadArrayDimensions
                        = OnReadArrayDimensions;

                    if (onReadArrayDimensions != null)
                    {
                        result = onReadArrayDimensions(context, this, ref arrayDimensions);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = arrayDimensions;
                    }

                    return result;
                case Attributes.AccessLevel:
                    byte accessLevel = AccessLevel;

                    NodeAttributeEventHandler<byte> onReadAccessLevel = OnReadAccessLevel;

                    if (onReadAccessLevel != null)
                    {
                        result = onReadAccessLevel(context, this, ref accessLevel);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = accessLevel;
                    }

                    return result;
                case Attributes.AccessLevelEx:
                    uint accessLevelEx = m_accessLevel;

                    NodeAttributeEventHandler<uint> onReadAccessLevelEx = OnReadAccessLevelEx;

                    if (onReadAccessLevelEx != null)
                    {
                        result = onReadAccessLevelEx(context, this, ref accessLevelEx);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = accessLevelEx;
                    }

                    return result;
                case Attributes.UserAccessLevel:
                    byte userAccessLevel = m_userAccessLevel;

                    NodeAttributeEventHandler<byte> onReadUserAccessLevel = OnReadUserAccessLevel;

                    if (onReadUserAccessLevel != null)
                    {
                        result = onReadUserAccessLevel(context, this, ref userAccessLevel);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = userAccessLevel;
                    }

                    return result;
                case Attributes.MinimumSamplingInterval:
                    double minimumSamplingInterval = m_minimumSamplingInterval;

                    NodeAttributeEventHandler<double> onReadMinimumSamplingInterval
                        = OnReadMinimumSamplingInterval;

                    if (onReadMinimumSamplingInterval != null)
                    {
                        result = onReadMinimumSamplingInterval(
                            context,
                            this,
                            ref minimumSamplingInterval);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = minimumSamplingInterval;
                    }

                    return result;
                case Attributes.Historizing:
                    bool historizing = m_historizing;

                    NodeAttributeEventHandler<bool> onReadHistorizing = OnReadHistorizing;

                    if (onReadHistorizing != null)
                    {
                        result = onReadHistorizing(context, this, ref historizing);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = historizing;
                    }

                    return result;
                default:
                    return base.ReadNonValueAttribute(context, attributeId, ref value);
            }
        }

        /// <summary>
        /// Reads the value for the value attribute.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="indexRange">The index range.</param>
        /// <param name="dataEncoding">The data encoding.</param>
        /// <param name="value">The value to be returned.</param>
        /// <param name="sourceTimestamp">The source timestamp.</param>
        /// <returns>
        /// An instance of the <see cref="ServiceResult"/> containing the status code and diagnostic info for the operation.
        /// ServiceResult.Good if successful. Detailed error information otherwise.
        /// </returns>
        protected override ServiceResult ReadValueAttribute(
            ISystemContext context,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref DateTime sourceTimestamp)
        {
            // check the access level for the variable.
            if ((m_accessLevel & AccessLevels.CurrentRead) == 0)
            {
                return StatusCodes.BadNotReadable;
            }

            // check the user access level for the variable.
            byte userAccessLevel = m_userAccessLevel;
            OnReadUserAccessLevel?.Invoke(context, this, ref userAccessLevel);

            if ((userAccessLevel & AccessLevels.CurrentRead) == 0)
            {
                return StatusCodes.BadUserAccessDenied;
            }

            // ensure a value timestamp exists.
            if (m_timestamp == DateTime.MinValue)
            {
                sourceTimestamp = DateTime.UtcNow;
            }
            else
            {
                sourceTimestamp = m_timestamp;
            }

            value = m_value;
            StatusCode statusCode = m_statusCode;

            ServiceResult result = null;

            NodeValueEventHandler onReadValue = OnReadValue;

            // check if the read behavior has been overridden.
            if (onReadValue != null)
            {
                result = onReadValue(
                    context,
                    this,
                    indexRange,
                    dataEncoding,
                    ref value,
                    ref statusCode,
                    ref sourceTimestamp);

                if (ServiceResult.IsBad(result))
                {
                    return result;
                }

                // return the correct status code if no errors.
                if (ServiceResult.IsGood(result) && statusCode != StatusCodes.Good)
                {
                    result = statusCode;
                }

                return result;
            }

            NodeValueSimpleEventHandler onSimpleReadValue = OnSimpleReadValue;

            // use default behavior.
            if (onSimpleReadValue != null)
            {
                result = onSimpleReadValue(context, this, ref value);

                if (ServiceResult.IsBad(result))
                {
                    return result;
                }
            }

            // apply the index range and encoding.
            result = ApplyIndexRangeAndDataEncoding(context, indexRange, dataEncoding, ref value);

            if (ServiceResult.IsBad(result))
            {
                return result;
            }

            // copy returned value.
            if (CopyPolicy is VariableCopyPolicy.CopyOnRead or VariableCopyPolicy.Always)
            {
                value = CoreUtils.Clone(value);
            }

            // return the correct status code if no errors.
            if (ServiceResult.IsGood(result) && statusCode != StatusCodes.Good)
            {
                result = statusCode;
            }

            return result;
        }

        /// <summary>
        /// Applies the index range and the data encoding to the value.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="indexRange">The index range.</param>
        /// <param name="dataEncoding">The data encoding.</param>
        /// <param name="value">The value.</param>
        public static ServiceResult ApplyIndexRangeAndDataEncoding(
            ISystemContext context,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value)
        {
            ServiceResult result;

            // apply index range.
            if (indexRange != NumericRange.Empty)
            {
                result = indexRange.ApplyRange(ref value);

                if (ServiceResult.IsBad(result))
                {
                    return result;
                }
            }

            // apply data encoding.
            if (!dataEncoding.IsNull)
            {
                IServiceMessageContext messageContext = context.AsMessageContext();

                result = EncodeableObject.ApplyDataEncoding(
                    messageContext,
                    dataEncoding,
                    ref value);

                if (ServiceResult.IsBad(result))
                {
                    return result;
                }
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Write the value for any non-value attribute.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="value">The value.</param>
        /// <returns>
        /// An instance of the <see cref="ServiceResult"/> containing the status code and diagnostic info for the operation.
        /// ServiceResult.Good if successful. Detailed error information otherwise.
        /// </returns>
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
                    if (!value.TryGet(out ArrayOf<uint> arrayDimensions))
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

                    NodeAttributeEventHandler<ArrayOf<uint>> onWriteArrayDimensions
                        = OnWriteArrayDimensions;

                    if (onWriteArrayDimensions != null)
                    {
                        result = onWriteArrayDimensions(context, this, ref arrayDimensions);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        ArrayDimensions = arrayDimensions;
                    }

                    return result;
                case Attributes.AccessLevel:
                    if (!value.TryGet(out byte accessLevel))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.AccessLevel) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<byte> onWriteAccessLevel = OnWriteAccessLevel;

                    if (onWriteAccessLevel != null)
                    {
                        result = onWriteAccessLevel(context, this, ref accessLevel);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        AccessLevel = accessLevel;
                    }

                    return result;
                case Attributes.UserAccessLevel:
                    if (!value.TryGet(out byte userAccessLevel))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.UserAccessLevel) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<byte> onWriteUserAccessLevel = OnWriteUserAccessLevel;

                    if (onWriteUserAccessLevel != null)
                    {
                        result = onWriteUserAccessLevel(context, this, ref userAccessLevel);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        UserAccessLevel = userAccessLevel;
                    }

                    return result;
                case Attributes.MinimumSamplingInterval:
                    if (!value.TryGet(out double minimumSamplingInterval))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.MinimumSamplingInterval) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<double> onWriteMinimumSamplingInterval
                        = OnWriteMinimumSamplingInterval;

                    if (onWriteMinimumSamplingInterval != null)
                    {
                        result = onWriteMinimumSamplingInterval(
                            context,
                            this,
                            ref minimumSamplingInterval);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        MinimumSamplingInterval = minimumSamplingInterval;
                    }

                    return result;
                case Attributes.Historizing:
                    if (!value.TryGet(out bool historizing))
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.Historizing) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    NodeAttributeEventHandler<bool> onWriteHistorizing = OnWriteHistorizing;

                    if (onWriteHistorizing != null)
                    {
                        result = onWriteHistorizing(context, this, ref historizing);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        Historizing = historizing;
                    }

                    return result;
                default:
                    return base.WriteNonValueAttribute(context, attributeId, value);
            }
        }

        /// <summary>
        /// Write the value for the value attribute.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="indexRange">The index range.</param>
        /// <param name="value">The value.</param>
        /// <param name="statusCode">The status code.</param>
        /// <param name="sourceTimestamp">The source timestamp.</param>
        /// <returns>
        /// An instance of the <see cref="ServiceResult"/> containing the status code and diagnostic info for the operation.
        /// ServiceResult.Good if successful. Detailed error information otherwise.
        /// </returns>
        protected override ServiceResult WriteValueAttribute(
            ISystemContext context,
            NumericRange indexRange,
            Variant value,
            StatusCode statusCode,
            DateTime sourceTimestamp)
        {
            ServiceResult result = null;

            // check the access level for the variable.
            if ((m_accessLevel & AccessLevels.CurrentWrite) == 0)
            {
                return StatusCodes.BadNotWritable;
            }

            // check the user access level for the variable.
            byte userAccessLevel = m_userAccessLevel;
            OnReadUserAccessLevel?.Invoke(context, this, ref userAccessLevel);

            if ((userAccessLevel & AccessLevels.CurrentWrite) == 0)
            {
                return StatusCodes.BadUserAccessDenied;
            }

            NodeValueEventHandler onWriteValue = OnWriteValue;

            // check if the write behavior has been overridden.
            if (onWriteValue != null)
            {
                result = onWriteValue(
                    context,
                    this,
                    indexRange,
                    default,
                    ref value,
                    ref statusCode,
                    ref sourceTimestamp);

                if (ServiceResult.IsBad(result))
                {
                    return result;
                }

                m_value = value;
                m_statusCode = statusCode;
                m_timestamp = sourceTimestamp;

                // update timestamp if not set by function.
                if (sourceTimestamp == DateTime.MinValue)
                {
                    m_timestamp = DateTime.UtcNow;
                }

                ChangeMasks |= NodeStateChangeMasks.Value;

                return result;
            }

            // ensure the source timestamp has a valid value.
            if (sourceTimestamp == DateTime.MinValue)
            {
                sourceTimestamp = DateTime.UtcNow;
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
                //if xml element data decoding error appeared : a value of type status code is received with the error code
                if (DataTypeIds.XmlElement == m_dataType)
                {
                    var statusCodeTypeInfo = TypeInfo.IsInstanceOfDataType(
                        value,
                        DataTypeIds.UInt32,
                        -1,
                        context.NamespaceUris,
                        context.TypeTable);
                    if (!statusCodeTypeInfo.IsUnknown)
                    {
                        //the error code
                        return (StatusCode)(uint)value;
                    }
                }
                // test for special case Null type
                if (!(m_dataType.IsNull && value.IsNull))
                {
                    return StatusCodes.BadTypeMismatch;
                }
            }

            // copy passed in value.
            if (CopyPolicy is VariableCopyPolicy.CopyOnWrite or VariableCopyPolicy.Always)
            {
                value = CoreUtils.Clone(value);
            }

            NodeValueSimpleEventHandler onSimpleWriteValue = OnSimpleWriteValue;

            // check for simple write value handler.
            if (onSimpleWriteValue != null)
            {
                // index range writes not supported.
                if (indexRange != NumericRange.Empty)
                {
                    return StatusCodes.BadIndexRangeInvalid;
                }

                result = onSimpleWriteValue(context, this, ref value);

                if (ServiceResult.IsBad(result))
                {
                    return result;
                }
            }
            else
            {
                // apply the index range.
                if (indexRange != NumericRange.Empty)
                {
                    object target = m_value;
                    result = indexRange.UpdateRange(ref target, value);

                    if (ServiceResult.IsBad(result))
                    {
                        return result;
                    }

                    value = VariantHelper.CastFrom(target);
                }
            }

            // update cached values.
            m_value = value;
            m_statusCode = statusCode;
            m_timestamp = sourceTimestamp;

            ChangeMasks |= NodeStateChangeMasks.Value;

            return ServiceResult.Good;
        }

        private Variant m_value;
        private DateTime m_timestamp;
        private bool m_valueTouched;
        private StatusCode m_statusCode;
        private NodeId m_dataType;
        private int m_valueRank;
        private ArrayOf<uint> m_arrayDimensions;
        private uint m_accessLevel;
        private byte m_userAccessLevel;
        private double m_minimumSamplingInterval;
        private bool m_historizing;
        private ILogger m_logger = LoggerUtils.Null.Logger;
        private static readonly char[] s_commaSeparator = [','];
    }

    /// <summary>
    /// A thread safe object that can be used to access the value of a structure variable.
    /// </summary>
    public class BaseVariableValue
    {
        /// <summary>
        /// Initializes the instance with a synchronization object.
        /// </summary>
        public BaseVariableValue(object dataLock)
        {
            Lock = dataLock;
            CopyPolicy = VariableCopyPolicy.CopyOnRead;

            Lock ??= new object();
        }

        /// <summary>
        /// An object used to synchronize access to the value.
        /// </summary>
        public object Lock { get; }

        /// <summary>
        /// The behavior to use when reading or writing all or part of the object.
        /// </summary>
        public VariableCopyPolicy CopyPolicy { get; set; }

        /// <summary>
        /// Gets or sets the current error state.
        /// </summary>
        public ServiceResult Error { get; set; }

        /// <summary>
        /// Gets or sets the timestamp associated with the value.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Clears the change masks for all nodes in the update list.
        /// </summary>
        public void ChangesComplete(ISystemContext context)
        {
            lock (Lock)
            {
                if (m_updateList != null)
                {
                    for (int ii = 0; ii < m_updateList.Length; ii++)
                    {
                        BaseInstanceState instance = m_updateList[ii];

                        if (instance != null)
                        {
                            instance.UpdateChangeMasks(NodeStateChangeMasks.Value);
                            instance.ClearChangeMasks(context, false);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Raised before the value is read.
        /// </summary>
        public VariableValueEventHandler OnBeforeRead;

        /// <summary>
        /// Raised after the value is written.
        /// </summary>
        public VariableValueEventHandler OnAfterWrite;

        /// <summary>
        /// Does any processing before a read operation takes place.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        protected void DoBeforeReadProcessing(ISystemContext context, NodeState node)
        {
            OnBeforeRead?.Invoke(context, this, node);
        }

        /// <summary>
        /// Reads the value or a component of the value.
        /// </summary>
        protected ServiceResult Read(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (Lock)
            {
                // ensure a value timestamp exists.
                if (Timestamp == DateTime.MinValue)
                {
                    Timestamp = DateTime.UtcNow;
                }

                timestamp = Timestamp;

                // check for errors.
                if (ServiceResult.IsBad(Error))
                {
                    value = Variant.Null;
                    statusCode = Error.StatusCode;
                    return Error;
                }

                // apply the index range and encoding.
                ServiceResult result = BaseVariableState.ApplyIndexRangeAndDataEncoding(
                    context,
                    indexRange,
                    dataEncoding,
                    ref value);

                if (ServiceResult.IsBad(result))
                {
                    statusCode = result.StatusCode;
                    return result;
                }

                // apply copy policy
                if ((CopyPolicy & VariableCopyPolicy.CopyOnRead) != 0)
                {
                    value = CoreUtils.Clone(value);
                }

                statusCode = StatusCodes.Good;

                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Reads the current value.
        /// </summary>
        protected ServiceResult Read(object currentValue, ref object valueToRead)
        {
            lock (Lock)
            {
                if (ServiceResult.IsBad(Error))
                {
                    valueToRead = null;
                    return Error;
                }

                if ((CopyPolicy & VariableCopyPolicy.CopyOnRead) != 0)
                {
                    valueToRead = CoreUtils.Clone(currentValue);
                }
                else
                {
                    valueToRead = currentValue;
                }

                return ServiceResult.Good;
            }
        }

        /// <summary>
        /// Writes the current value.
        /// </summary>
        protected object Write(object valueToWrite)
        {
            lock (Lock)
            {
                if ((CopyPolicy & VariableCopyPolicy.CopyOnWrite) != 0)
                {
                    return CoreUtils.Clone(valueToWrite);
                }

                return valueToWrite;
            }
        }

        /// <summary>
        /// Sets the list of nodes which are updated when ClearChangeMasks is called.
        /// </summary>
        protected void SetUpdateList(IList<BaseInstanceState> updateList)
        {
            lock (Lock)
            {
                m_updateList = null;

                if (updateList != null && updateList.Count > 0)
                {
                    m_updateList = new BaseInstanceState[updateList.Count];

                    for (int ii = 0; ii < m_updateList.Length; ii++)
                    {
                        m_updateList[ii] = updateList[ii];

                        // the copy copy is enforced by the value wrapper.

                        if (m_updateList[ii] is BaseVariableState variable)
                        {
                            variable.CopyPolicy = VariableCopyPolicy.Never;
                        }
                    }
                }
            }
        }

        private BaseInstanceState[] m_updateList;
    }

    /// <summary>
    /// Used to receive notifications when the value attribute is read or written.
    /// </summary>
    public delegate void VariableValueEventHandler(
        ISystemContext context,
        BaseVariableValue variable,
        NodeState component);

    /// <summary>
    /// Specifies the policies to use when handling reads and write to value.
    /// </summary>
    [Flags]
    public enum VariableCopyPolicy
    {
        /// <summary>
        /// The value is never copied (only useful for value types that do not contain reference types).
        /// </summary>
        Never = 0x0,

        /// <summary>
        /// The value is copied when is read.
        /// </summary>
        CopyOnRead = 0x1,

        /// <summary>
        /// The value is copied before it is written.
        /// </summary>
        CopyOnWrite = 0x2,

        /// <summary>
        /// Data is copied when it is written and when it is read.
        /// </summary>
        Always = CopyOnWrite | CopyOnRead
    }
}
