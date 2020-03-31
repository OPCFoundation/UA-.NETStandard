/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
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
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.IO;
using System.Runtime.Serialization;
using System.Reflection;
using System.Threading;

namespace Opc.Ua
{
    /// <summary> 
    /// The base class for all variable nodes.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public abstract class BaseVariableState : BaseInstanceState
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with its defalt attribute values.
        /// </summary>
        /// <param name="parent">The parent node.</param>
        public BaseVariableState(NodeState parent) : base(NodeClass.Variable, parent)
        {
            m_timestamp = DateTime.MinValue;
            m_accessLevel = m_userAccessLevel = AccessLevels.CurrentRead;
            m_copyPolicy = VariableCopyPolicy.CopyOnRead;
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the instance from another instance.
        /// </summary>
        /// <param name="context">The description how access the system containing the data.</param>
        /// <param name="source">A source node to be copied to this instance.</param>
        protected override void Initialize(ISystemContext context, NodeState source)
        {
            BaseVariableState instance = source as BaseVariableState;

            if (instance != null)
            {
                m_value = ExtractValueFromVariant(context, instance.m_value, false);
                m_timestamp = instance.m_timestamp;
                m_statusCode = instance.m_statusCode;
                m_dataType = instance.m_dataType;
                m_valueRank = instance.m_valueRank;
                m_arrayDimensions = null;
                m_accessLevel = instance.m_accessLevel;
                m_userAccessLevel = instance.m_userAccessLevel;
                m_minimumSamplingInterval = instance.m_minimumSamplingInterval;
                m_historizing = instance.m_historizing;

                if (instance.m_arrayDimensions != null)
                {
                    m_arrayDimensions = new ReadOnlyList<uint>(instance.m_arrayDimensions, true);
                }

                m_value = ExtractValueFromVariant(context, m_value, false);
            }

            base.Initialize(context, source);
        }

        /// <summary>
        /// If overridden returns the id of the default type definition node for the instance.
        /// </summary>
        /// <param name="namespaceUris">The namespace uris.</param>
        /// <returns>Returns the id of the default type definition or <see cref="VariableTypes.BaseVariableType"/></returns> if not overridden
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return VariableTypes.BaseVariableType;
        }

        /// <summary>
        /// If overridden returns the id of the default data type node for the instance.
        /// </summary>
        /// <param name="namespaceUris">The namespace uris.</param>
        /// <returns>
        /// The id <see cref="NodeId"/> of the default data type node for the instance or <see cref="DataTypes.BaseDataType"/> if not overridden.
        /// </returns>
        protected virtual NodeId GetDefaultDataTypeId(NamespaceTable namespaceUris)
        {
            return DataTypes.BaseDataType;
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
        /// Converts a values contained in a variant to the value defined for the variable.
        /// </summary>
        [Obsolete("Should use the version that takes a ISystemContext (pass null if ISystemContext is not available).")]
        protected virtual object ExtractValueFromVariant(object value, bool throwOnError)
        {
            return ExtractValueFromVariant(null, value, throwOnError);
        }

        /// <summary>
        /// Converts a values contained in a variant to the value defined for the variable.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="value">The value.</param>
        /// <param name="throwOnError">if set to <c>true</c> throw an exception on error.</param>
        /// <returns>If not overridden returns <paramref name="value"/>.</returns>
        protected virtual object ExtractValueFromVariant(ISystemContext context, object value, bool throwOnError)
        {
            return value;
        }

        /// <summary>
        /// Returns the value after checking if the variable is null.
        /// </summary>
        /// <typeparam name="T">The framework type of value contained in the <paramref name="variable"/>.</typeparam>
        /// <param name="variable">The variable.</param>
        /// <returns>
        /// The value contained by the <paramref name="variable"/> or the default value for the datatype if the variable is null.
        /// </returns>
        public static T GetValue<T>(BaseDataVariableState<T> variable)
        {
            if (variable == null)
            {
                return default(T);
            }

            return variable.Value;
        }

        /// <summary>
        /// Returns the value after checking if the property is null.
        /// </summary>
        /// <typeparam name="T">The type of value contained in the property.</typeparam>
        /// <param name="property">The property.</param>
        /// <returns>
        /// The value. The default value for the datatype if the property is null.
        /// </returns>
        public static T GetValue<T>(PropertyState<T> property)
        {
            if (property == null)
            {
                return default(T);
            }

            return property.Value;
        }

        /// <summary>
        /// Converts a values contained in a variant to the value defined for the variable.
        /// </summary>
        [Obsolete("Should use the version that takes a ISystemContext (pass null if ISystemContext is not available).")]
        public static object ExtractValueFromVariant<T>(object value, bool throwOnError)
        {
            return ExtractValueFromVariant<T>(null, value, throwOnError);
        }

        /// <summary>
        /// Converts a values contained in a variant to the value defined for the variable.
        /// </summary>
        /// <typeparam name="T">The framework type of value contained in this instance.</typeparam>
        /// <param name="context">The context.</param>
        /// <param name="value">The value.</param>
        /// <param name="throwOnError">if set to <c>true</c> throws the <see cref="ServiceResultException"/> on error, otherwise default value for <typeparamref name="T"/> is returned .</param>
        /// <returns>
        /// The value of the <typeparamref name="T"/> type.
        /// </returns>
        /// <remarks>
        /// If throwOnError is <c>false</c> the default value for the type is returned if the value is not valid.
        /// </remarks>
        /// <exception cref="ServiceResultException">If cannot convert <paramref name="value"/>.</exception>
        public static object ExtractValueFromVariant<T>(ISystemContext context, object value, bool throwOnError)
        {
            if (value == null)
            {
                return default(T);
            }

            if (typeof(T).IsInstanceOfType(value))
            {
                return value;
            }

            ExtensionObject extension = value as ExtensionObject;

            if (extension != null)
            {
                if (typeof(T).IsInstanceOfType(extension.Body))
                {
                    return extension.Body;
                }

                if (typeof(IEncodeable).GetTypeInfo().IsAssignableFrom(typeof(T).GetTypeInfo()))
                {
                    return DecodeExtensionObject(context, typeof(T), extension, throwOnError);
                }

                if (throwOnError)
                {
                    throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "Cannot convert {0} to {1}.", value.GetType().Name, typeof(T).Name);
                }

                return default(T);
            }

            Type elementType = typeof(T).GetElementType();

            if (elementType != null)
            {
                // check for array of extensions.
                IList<ExtensionObject> extensions = value as IList<ExtensionObject>;

                if (extensions != null && typeof(IEncodeable).GetTypeInfo().IsAssignableFrom(elementType.GetTypeInfo()))
                {
                    Array encodeables = Array.CreateInstance(elementType, extensions.Count);

                    for (int ii = 0; ii < extensions.Count; ii++)
                    {
                        if (ExtensionObject.IsNull(extensions[ii]))
                        {
                            encodeables.SetValue(null, ii);
                            continue;
                        }

                        if (elementType.IsInstanceOfType(extensions[ii].Body))
                        {
                            encodeables.SetValue(extensions[ii].Body, ii);
                            continue;
                        }

                        object element = DecodeExtensionObject(context, elementType, extensions[ii], throwOnError);

                        if (element != null)
                        {
                            encodeables.SetValue(element, ii);
                            continue;
                        }

                        if (throwOnError)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "Cannot convert ExtensionObject to {0}. Index = {1}", elementType.Name, ii);
                        }
                    }

                    return encodeables;
                }

                // check for array of variants.
                IList<Variant> variants = value as IList<Variant>;

                if (variants != null)
                {
                    // only support conversions to object[].
                    if (elementType != typeof(object))
                    {
                        if (throwOnError)
                        {
                            throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "Cannot convert {0} to {1}.", value.GetType().Name, typeof(T).Name);
                        }
                    }

                    // allocate and copy.
                    object[] objects = new object[variants.Count];

                    for (int ii = 0; ii < variants.Count; ii++)
                    {
                        objects[ii] = variants[ii].Value;
                    }

                    return objects;
                }

                // check for array of uuids.
                if (typeof(Guid).GetTypeInfo().IsAssignableFrom(elementType.GetTypeInfo()))
                {
                    IList<Uuid> uuids = value as IList<Uuid>;

                    if (uuids != null)
                    {
                        Guid[] guids = new Guid[uuids.Count];

                        for (int ii = 0; ii < uuids.Count; ii++)
                        {
                            guids[ii] = (Guid)uuids[ii];
                        }

                        return guids;
                    }
                }

                // check for array of enumeration.
                if (typeof(Enum).GetTypeInfo().IsAssignableFrom(elementType.GetTypeInfo()))
                {
                    IList<int> values = value as IList<int>;

                    if (values != null)
                    {
                        Array enums = Array.CreateInstance(elementType, values.Count);

                        for (int ii = 0; ii < values.Count; ii++)
                        {
                            enums.SetValue(values[ii], ii);
                        }

                        return enums;
                    }
                }
            }

            if (typeof(Guid).GetTypeInfo().IsAssignableFrom(typeof(T).GetTypeInfo()))
            {
                Uuid? uuid = value as Uuid?;

                if (uuid != null)
                {
                    return (Guid)uuid.Value;
                }
            } 
            
            if (typeof(Enum).GetTypeInfo().IsAssignableFrom(typeof(T).GetTypeInfo()))
            {
                int? number = value as int?;

                if (number != null)
                {
                    return (T)(object)number.Value;
                }
            }

            if (throwOnError)
            {
                throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "Cannot convert {0} to {1}.", value.GetType().Name, typeof(T).Name);
            }

            return default(T);
        }

        /// <summary>
        /// Decodes the contents of an extension object.
        /// </summary>
        /// <param name="context">The context (uses ServiceMessageContext.GlobalContext if null).</param>
        /// <param name="targetType">The type that the ExtensionObject must be converted to.</param>
        /// <param name="extension">The ExtensionObject to convert.</param>
        /// <param name="throwOnError">Whether to throw an exception on error.</param>
        /// <returns>The decoded instance. Null on error.</returns>
        public static object DecodeExtensionObject(ISystemContext context, Type targetType, ExtensionObject extension, bool throwOnError)
        {
            if (targetType.IsInstanceOfType(extension.Body))
            {
                return extension.Body;
            }

            IEncodeable instance = Activator.CreateInstance(targetType) as IEncodeable;

            if (instance != null)
            {
                IDecoder decoder = null;

                ServiceMessageContext messageContext = ServiceMessageContext.GlobalContext;

                if (context != null)
                {
                    messageContext = new ServiceMessageContext();
                    messageContext.NamespaceUris = context.NamespaceUris;
                    messageContext.ServerUris = context.ServerUris;
                    messageContext.Factory = context.EncodeableFactory;
                }

                if (extension.Encoding == ExtensionObjectEncoding.Binary)
                {
                    decoder = new BinaryDecoder(extension.Body as byte[], messageContext);
                }

                else if (extension.Encoding == ExtensionObjectEncoding.Xml)
                {
                    decoder = new XmlDecoder(extension.Body as XmlElement, messageContext);
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
                            throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "Cannot convert ExtensionObject to {0}. Error = {1}", targetType.Name, e.Message);
                        }
                    }
                }
            }

            if (throwOnError)
            {
                throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "Cannot convert ExtensionObject to {0}.", targetType.Name);
            }

            return null;
        }

        /// <summary>
        /// Checks the data type of a value before casting it to the type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The framework type of value contained in the variable.</typeparam>
        /// <param name="value">The value.</param>
        /// <param name="throwOnError">if set to <c>true</c> <see cref="ServiceResultException"/> is thrown on error.</param>
        /// <returns>Returns <paramref name="value"/> or default for <typeparamref name="T"/></returns>
        /// <exception cref="ServiceResultException"> if it is impossible to cast the value or the value is null and <see cref="IsValueType"/> for the type <typeparamref name="T"/> returns true. </exception>
        public static T CheckTypeBeforeCast<T>(object value, bool throwOnError)
        {
            if ((value == null && typeof(T).GetTypeInfo().IsValueType) || (value != null && !typeof(T).IsInstanceOfType(value)))
            {
                if (throwOnError)
                {
                    throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "Cannot convert '{0}' to a {1}.", value, typeof(T).Name);
                }

                return default(T);
            }

            return (T)value;
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
                if (value == null && IsValueType)
                {
                    value = ExtractValueFromVariant(null, value, false);
                }

                if (!Object.ReferenceEquals(m_value, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Value;
                }

                m_value = value;
            }
        }

        /// <summary>
        /// Whether the value can be set to null.
        /// </summary>
        public bool IsValueType
        {
            get { return m_isValueType; }
            set { m_isValueType = value; }
        }

        /// <summary>
        /// The value of the variable as a Variant.
        /// </summary>
        /// <value>The wrapped value as a Variant.</value>
        [DataMember(Name = "Value", Order = 0, IsRequired = false, EmitDefaultValue = false)]
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
        /// The timestamp associated with the variable value.
        /// </summary>
        /// <value>The timestamp.</value>
        public DateTime Timestamp
        {
            get
            {
                return m_timestamp;
            }

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
            get
            {
                return m_statusCode;
            }

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
        public VariableCopyPolicy CopyPolicy
        {
            get { return m_copyPolicy; }
            set { m_copyPolicy = value; }
        }

        /// <summary>
        /// The data type for the variable value.
        /// </summary>
        /// <value>The type of the data <see cref="NodeId"/>.</value>
        [DataMember(Name = "DataType", Order = 1, IsRequired = false, EmitDefaultValue = false)]
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
        /// <value>The value rank. </value>
        /// <remarks>Indicates whether the DataType is an array and how many dimensions the array has.</remarks>
        [DataMember(Name = "ValueRank", Order = 2, IsRequired = false, EmitDefaultValue = false)]
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
        /// <value>The array dimensions.</value>
        /// <remarks>
        /// If the Value Rank does not identify an array of a specific dimension (i.e. ValueRank &lt;= 0)
        /// the Array Dimensions attribute can either be set to null or the attribute is missing. This behaviour is vendor-specific.
        /// If the Value Rank attribute specifies an array of a specific dimension (i.e. ValueRank &gt; 0) then the Array Dimensions
        /// attribute shall be specified in the table defining the Variable.
        /// </remarks>
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

        /// <summary>
        /// The type of access available for the variable.
        /// </summary>
        /// <value>The access level.</value>
        [DataMember(Name = "AccessLevel", Order = 4, IsRequired = false, EmitDefaultValue = false)]
        public byte AccessLevel
        {
            get
            {
                return (byte)(m_accessLevel & 0xFF);
            }

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
        [DataMember(Name = "UserAccessLevel", Order = 5, IsRequired = false, EmitDefaultValue = false)]
        public byte UserAccessLevel
        {
            get
            {
                return m_userAccessLevel;
            }

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
        [DataMember(Name = "MinimumSamplingInterval", Order = 6, IsRequired = false, EmitDefaultValue = false)]
        public double MinimumSamplingInterval
        {
            get
            {
                return m_minimumSamplingInterval;
            }

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
            get
            {
                return m_historizing;
            }

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
        [DataMember(Name = "AccessLevelEx", Order = 8, IsRequired = false, EmitDefaultValue = false)]
        public uint AccessLevelEx
        {
            get
            {
                return m_accessLevel;
            }

            set
            {
                if (m_accessLevel != value)
                {
                    ChangeMasks |= NodeStateChangeMasks.NonValue;
                }

                m_accessLevel = value;
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
        public NodeAttributeEventHandler<IList<uint>> OnReadArrayDimensions;

        /// <summary>
        /// Raised when the ArrayDimensions attribute is written.
        /// </summary>
        public NodeAttributeEventHandler<IList<uint>> OnWriteArrayDimensions;

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
        #endregion

        #region Serialization Functions
        /// <summary>
        /// Exports a copy of the node to a <paramref name="node"/> node provided the <paramref name="node"/> type is compatible with <see cref="VariableNode"/>.
        /// </summary>
        /// <param name="context">The context that describes how access the system containing the data.</param>
        /// <param name="node">The node to be a copy of this instance.</param>
        protected override void Export(ISystemContext context, Node node)
        {
            base.Export(context, node);

            VariableNode variableNode = node as VariableNode;

            if (variableNode != null)
            {
                try
                {
                    variableNode.Value = new Variant(Utils.Clone(this.Value));
                
                    variableNode.DataType = this.DataType;
                    variableNode.ValueRank = this.ValueRank;
                    variableNode.ArrayDimensions = null;

                    if (this.ArrayDimensions != null)
                    {
                        variableNode.ArrayDimensions = new UInt32Collection(this.ArrayDimensions);
                    }

                    variableNode.AccessLevel = this.AccessLevel;
                    variableNode.UserAccessLevel = this.UserAccessLevel;
                    variableNode.MinimumSamplingInterval = this.MinimumSamplingInterval;
                    variableNode.Historizing = this.Historizing;
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error exporting node:" + e.Message);
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

            if (m_value != null)
            {
                encoder.WriteVariant("Value", WrappedValue);
            }

            if (StatusCode != StatusCodes.Good)
            {
                encoder.WriteStatusCode("StatusCode", StatusCode);
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
                WrappedValue = decoder.ReadVariant("Value");
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

            // ensure the value has a suitable default value.
            if (m_value == null && m_valueRank == ValueRanks.Scalar)
            {
                bool isValueType = IsValueType;

                if (!isValueType)
                {
                    BuiltInType builtInType = DataTypes.GetBuiltInType(m_dataType, context.TypeTable);

                    if (TypeInfo.IsValueType(builtInType))
                    {
                        isValueType = true;
                    }
                }

                if (isValueType)
                {
                    m_value = TypeInfo.GetDefaultValue(m_dataType, m_valueRank, context.TypeTable);
                }
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

            if (m_value != null)
            {
                attributesToSave |= AttributesToSave.Value;
            }

            if (m_statusCode != StatusCodes.Good)
            {
                attributesToSave |= AttributesToSave.StatusCode;
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
        public override void Save(ISystemContext context, BinaryEncoder encoder, AttributesToSave attributesToSave)
        {
            base.Save(context, encoder, attributesToSave);

            if ((attributesToSave & AttributesToSave.Value) != 0)
            {
                encoder.WriteVariant(null, WrappedValue);
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
                encoder.WriteUInt32Array(null, m_arrayDimensions);
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
        /// <param name="attibutesToLoad">The attributes to load.</param>
        public override void Update(ISystemContext context, BinaryDecoder decoder, AttributesToSave attibutesToLoad)
        {
            base.Update(context, decoder, attibutesToLoad);

            if ((attibutesToLoad & AttributesToSave.Value) != 0)
            {
                WrappedValue = decoder.ReadVariant(null);
            }

            if ((attibutesToLoad & AttributesToSave.StatusCode) != 0)
            {
                m_statusCode = decoder.ReadStatusCode(null);
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

            if ((attibutesToLoad & AttributesToSave.AccessLevel) != 0)
            {
                AccessLevel = decoder.ReadByte(null);
            }

            if ((attibutesToLoad & AttributesToSave.UserAccessLevel) != 0)
            {
                m_userAccessLevel = decoder.ReadByte(null);
            }

            if ((attibutesToLoad & AttributesToSave.MinimumSamplingInterval) != 0)
            {
                m_minimumSamplingInterval = decoder.ReadDouble(null);
            }

            if ((attibutesToLoad & AttributesToSave.Historizing) != 0)
            {
                m_historizing = decoder.ReadBoolean(null);
            }        
        }

        /// <summary>
        /// Converts the array dimensions to an XML string.
        /// </summary>
        /// <param name="arrayDimensions">The array dimensions.</param>
        /// <returns>The XML string value.</returns>
        internal static string ArrayDimensionsToXml(IList<uint> arrayDimensions)
        {
            if (arrayDimensions == null)
            {
                return null;
            }

            StringBuilder buffer = new StringBuilder();

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
        internal static ReadOnlyList<uint> ArrayDimensionsFromXml(string value)
        {
            if (String.IsNullOrEmpty(value))
            {
                return null;
            }

            string[] fields = value.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (fields == null || fields.Length == 0)
            {
                return null;
            }

            uint[] arrayDimensions = new uint[fields.Length];

            for (int ii = 0; ii < arrayDimensions.Length; ii++)
            {
                try
                {
                    arrayDimensions[ii] = Convert.ToUInt32(fields[ii]);
                }
                catch
                {
                    arrayDimensions[ii] = 0;
                }
            }

            return new ReadOnlyList<uint>(arrayDimensions);
        }
        #endregion
        
        #region Overrridden Methods
        /// <summary>
        /// Recusively sets the status code and timestamp for the node and all child variables.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="statusCode">The status code.</param>
        /// <param name="timestamp">The timestamp. Not updated if set to DateTime.Min</param>
        public override void SetStatusCode(ISystemContext context, StatusCode statusCode, DateTime timestamp)
        {
            base.SetStatusCode(context, statusCode, timestamp);

            StatusCode = statusCode;

            if (timestamp != DateTime.MinValue)
            {
                Timestamp = timestamp;
            }
        }
        #endregion

        #region Read Support Functions

        /// <summary>
        /// Reads the value for any non-value attribute.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="attributeId">The attribute idetifier <see cref="Attributes"/>.</param>
        /// <param name="value">The returned value.</param>
        /// <returns>
        /// An instance of the <see cref="ServiceResult"/> containing the status code and diagnostic info for the operation.
        /// ServiceResult.Good if successful. Detailed error information otherwise.
        /// </returns>
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

                case Attributes.AccessLevel:
                {
                    byte accessLevel = AccessLevel;

                    if (OnReadAccessLevel != null)
                    {
                        result = OnReadAccessLevel(context, this, ref accessLevel);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = accessLevel;
                    }

                    return result;
                }

                case Attributes.AccessLevelEx:
                {
                    uint accessLevelEx = m_accessLevel;

                    if (OnReadAccessLevelEx != null)
                    {
                        result = OnReadAccessLevelEx(context, this, ref accessLevelEx);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = accessLevelEx;
                    }

                    return result;
                }

                case Attributes.UserAccessLevel:
                {
                    byte userAccessLevel = m_userAccessLevel;

                    if (OnReadUserAccessLevel != null)
                    {
                        result = OnReadUserAccessLevel(context, this, ref userAccessLevel);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = userAccessLevel;
                    }

                    return result;
                }

                case Attributes.MinimumSamplingInterval:
                {
                    double minimumSamplingInterval = m_minimumSamplingInterval;

                    if (OnReadMinimumSamplingInterval != null)
                    {
                        result = OnReadMinimumSamplingInterval(context, this, ref minimumSamplingInterval);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = minimumSamplingInterval;
                    }

                    return result;
                }

                case Attributes.Historizing:
                {
                    bool historizing = m_historizing;

                    if (OnReadHistorizing != null)
                    {
                        result = OnReadHistorizing(context, this, ref historizing);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        value = historizing;
                    }

                    return result;
                }
            }

            return base.ReadNonValueAttribute(context, attributeId, ref value);
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
            ref object value,
            ref DateTime sourceTimestamp)
        {
            // check the access level for the variable.
            if ((m_accessLevel & AccessLevels.CurrentRead) == 0)
            {
                return StatusCodes.BadNotReadable;
            }

            if ((m_userAccessLevel & AccessLevels.CurrentRead) == 0)
            {
                return StatusCodes.BadUserAccessDenied;
            }

            // ensure a value timestamp exists.
            if (m_timestamp == DateTime.MinValue)
            {
                m_timestamp = DateTime.UtcNow;
            }

            value = m_value;
            sourceTimestamp = m_timestamp;
            StatusCode statusCode = m_statusCode;

            ServiceResult result = null;

            // check if the read behavior has been overridden.
            if (OnReadValue != null)
            {
                result = OnReadValue(
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
            }
            
            // apply the index range and encoding.
            result = ApplyIndexRangeAndDataEncoding(context, indexRange, dataEncoding, ref value);

            if (ServiceResult.IsBad(result))
            {
                return result;
            }

            // copy returned value.
            if (m_copyPolicy == VariableCopyPolicy.CopyOnRead || m_copyPolicy == VariableCopyPolicy.Always)
            {
                value = Utils.Clone(value);
            }

            // return the correct status code if no errors.
            if (ServiceResult.IsGood(result) && statusCode != StatusCodes.Good)
            {
                result = statusCode;
            }

            return result;
        }

        /// <summary>
        /// Applys the index range and the data encoding to the value.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="indexRange">The index range.</param>
        /// <param name="dataEncoding">The data encoding.</param>
        /// <param name="value">The value.</param>
        /// <returns></returns>
        public static ServiceResult ApplyIndexRangeAndDataEncoding(
            ISystemContext context,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value)
        {
            ServiceResult result = null;

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
            if (!QualifiedName.IsNull(dataEncoding))
            {
                ServiceMessageContext messageContext = new ServiceMessageContext();

                messageContext.NamespaceUris = context.NamespaceUris;
                messageContext.ServerUris = context.ServerUris;
                messageContext.Factory = context.EncodeableFactory;

                result = EncodeableObject.ApplyDataEncoding(messageContext, dataEncoding, ref value);

                if (ServiceResult.IsBad(result))
                {
                    return result;
                }
            }

            return ServiceResult.Good;
        }

        #endregion
        
        #region Write Support Functions
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
                            ArrayDimensions = new ReadOnlyList<uint>(arrayDimensions);
                        }
                        else
                        {
                            ArrayDimensions = null;
                        }
                    }

                    return result;
                }

                case Attributes.AccessLevel:
                {
                    byte? accessLevelRef = value as byte?;

                    if (accessLevelRef == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.AccessLevel) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    byte accessLevel = accessLevelRef.Value;

                    if (OnWriteAccessLevel != null)
                    {
                        result = OnWriteAccessLevel(context, this, ref accessLevel);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        AccessLevel = accessLevel;
                    }

                    return result;
                }

                case Attributes.UserAccessLevel:
                {
                    byte? userAccessLevelRef = value as byte?;

                    if (userAccessLevelRef == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.UserAccessLevel) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    byte userAccessLevel = userAccessLevelRef.Value;

                    if (OnWriteUserAccessLevel != null)
                    {
                        result = OnWriteUserAccessLevel(context, this, ref userAccessLevel);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        UserAccessLevel = userAccessLevel;
                    }

                    return result;
                }

                case Attributes.MinimumSamplingInterval:
                {
                    double? minimumSamplingIntervalRef = value as double?;

                    if (minimumSamplingIntervalRef == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.MinimumSamplingInterval) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    double minimumSamplingInterval = minimumSamplingIntervalRef.Value;

                    if (OnWriteMinimumSamplingInterval != null)
                    {
                        result = OnWriteMinimumSamplingInterval(context, this, ref minimumSamplingInterval);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        MinimumSamplingInterval = minimumSamplingInterval;
                    }

                    return result;
                }

                case Attributes.Historizing:
                {
                    bool? historizingRef = value as bool?;

                    if (historizingRef == null)
                    {
                        return StatusCodes.BadTypeMismatch;
                    }

                    if ((WriteMask & AttributeWriteMask.Historizing) == 0)
                    {
                        return StatusCodes.BadNotWritable;
                    }

                    bool historizing = historizingRef.Value;

                    if (OnWriteHistorizing != null)
                    {
                        result = OnWriteHistorizing(context, this, ref historizing);
                    }

                    if (ServiceResult.IsGood(result))
                    {
                        Historizing = historizing;
                    }

                    return result;
                }
            }
            
            return base.WriteNonValueAttribute(context, attributeId, value);
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
            object value,
            StatusCode statusCode,
            DateTime sourceTimestamp)
        {
            ServiceResult result = null;

            // check the access level for the variable.
            if ((m_accessLevel & AccessLevels.CurrentWrite) == 0)
            {
                return StatusCodes.BadNotWritable;
            }

            if ((m_userAccessLevel & AccessLevels.CurrentWrite) == 0)
            {
                return StatusCodes.BadUserAccessDenied;
            }

            // check if the write behavior has been overridden.
            if (OnWriteValue != null)
            {
                result = OnWriteValue(
                    context,
                    this,
                    indexRange,
                    null,
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
            TypeInfo typeInfo = TypeInfo.IsInstanceOfDataType(
                value,
                m_dataType,
                m_valueRank,
                context.NamespaceUris,
                context.TypeTable);

            if (typeInfo == null || typeInfo == TypeInfo.Unknown)
            {
                //if xml element data decoding error appeared : a value of type status code is received with the error code
                if (DataTypeIds.XmlElement == m_dataType)
                {
                    TypeInfo statusCodeTypeInfo = TypeInfo.IsInstanceOfDataType(value,DataTypeIds.UInt32,-1,context.NamespaceUris,context.TypeTable);
                    if (statusCodeTypeInfo != null)
                    {
                        //the error code
                        return (StatusCode)(uint)value;
                    }
                }
                // test for special case Null type
                if (!(m_dataType.IsNullNodeId && value == null))
                {
                    return StatusCodes.BadTypeMismatch;
                }
            }

            value = ExtractValueFromVariant(context, value, true);

            // copy passed in value.
            if (m_copyPolicy == VariableCopyPolicy.CopyOnWrite || m_copyPolicy == VariableCopyPolicy.Always)
            {
                value = Utils.Clone(value);
            }

            // check for simple write value handler.
            if (OnSimpleWriteValue != null)
            {
                // index range writes not supported.
                if (indexRange != NumericRange.Empty)
                {
                    return StatusCodes.BadIndexRangeInvalid;
                }

                result = OnSimpleWriteValue(
                    context,
                    this,
                    ref value);

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

                    value = target;
                }
            }
            
            // update cached values.
            m_value = value;
            m_statusCode = statusCode;
            m_timestamp = sourceTimestamp;

            ChangeMasks |= NodeStateChangeMasks.Value;

            return ServiceResult.Good;
        }
        #endregion

        #region Private Fields
        private object m_value;
        private bool m_isValueType;
        private DateTime m_timestamp;
        private StatusCode m_statusCode;
        private NodeId m_dataType;
        private int m_valueRank;
        private ReadOnlyList<uint> m_arrayDimensions;
        private uint m_accessLevel;
        private byte m_userAccessLevel;
        private double m_minimumSamplingInterval;
        private bool m_historizing;
        private VariableCopyPolicy m_copyPolicy;
        #endregion
    }
    
    /// <summary> 
    /// A typed base class for all data variable nodes.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class PropertyState : BaseVariableState
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with its defalt attribute values.
        /// </summary>
        public PropertyState(NodeState parent) : base(parent)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public static NodeState Construct(NodeState parent)
        {
            return new PropertyState(parent);
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            SymbolicName = Utils.Format("{0}_Instance1", Opc.Ua.BrowseNames.PropertyType);
            NodeId = null;
            BrowseName = new QualifiedName(SymbolicName, 1);
            DisplayName = SymbolicName;
            Description = null;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty;
            TypeDefinitionId = GetDefaultTypeDefinitionId(context.NamespaceUris);
            NumericId = Opc.Ua.VariableTypes.PropertyType;
            Value = null;
            DataType = GetDefaultDataTypeId(context.NamespaceUris);
            ValueRank = GetDefaultValueRank();
            ArrayDimensions = null;
            AccessLevel = AccessLevels.CurrentReadOrWrite;
            UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            MinimumSamplingInterval = MinimumSamplingIntervals.Continuous;
            Historizing = false;
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return VariableTypes.PropertyType;
        }
        #endregion
    }

    /// <summary> 
    /// A typed base class for all data variable nodes.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class PropertyState<T> : PropertyState
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with its defalt attribute values.
        /// </summary>
        public PropertyState(NodeState parent) : base(parent)
        {
            Value = default(T);
            IsValueType = !typeof(T).GetTypeInfo().IsValueType;
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
            return ExtractValueFromVariant<T>(context, value, throwOnError);
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
                return CheckTypeBeforeCast<T>(base.Value, true);
            }

            set
            {
                base.Value = value;
            }
        }
        #endregion
    }
    
    /// <summary> 
    /// A typed base class for all data variable nodes.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class BaseDataVariableState : BaseVariableState
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with its defalt attribute values.
        /// </summary>
        public BaseDataVariableState(NodeState parent) : base(parent)
        {
            if (parent != null)
            {
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            }
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public static NodeState Construct(NodeState parent)
        {
            return new BaseDataVariableState(parent);
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            SymbolicName = Utils.Format("{0}_Instance1", Opc.Ua.BrowseNames.BaseDataVariableType);
            NodeId = null;
            BrowseName = new QualifiedName(SymbolicName, 1);
            DisplayName = SymbolicName;
            Description = null;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            TypeDefinitionId = GetDefaultTypeDefinitionId(context.NamespaceUris);
            NumericId = Opc.Ua.VariableTypes.BaseDataVariableType;
            Value = null;
            DataType = GetDefaultDataTypeId(context.NamespaceUris);
            ValueRank = GetDefaultValueRank();
            ArrayDimensions = null;
            AccessLevel = AccessLevels.CurrentReadOrWrite;
            UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            MinimumSamplingInterval = MinimumSamplingIntervals.Continuous;
            Historizing = false;
        }

        /// <summary>
        /// Returns the id of the default type definition node for the instance.
        /// </summary>
        protected override NodeId GetDefaultTypeDefinitionId(NamespaceTable namespaceUris)
        {
            return VariableTypes.BaseDataVariableType;
        }
        #endregion
        
        #region Public Properties
        /// <summary>
        /// The strings that describe the values for an enumeration.
        /// </summary>
        public PropertyState<LocalizedText[]> EnumStrings
        {
            get
            { 
                return m_enumStrings;  
            }
            
            set
            {
                if (!Object.ReferenceEquals(m_enumStrings, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_enumStrings = value;
            }
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Populates a list with the children that belong to the node.
        /// </summary>
        /// <param name="context">The context for the system being accessed.</param>
        /// <param name="children">The list of children to populate.</param>
        public override void GetChildren(
            ISystemContext context, 
            IList<BaseInstanceState> children)
        {
            if (m_enumStrings != null)
            {
                children.Add(m_enumStrings);
            }

            base.GetChildren(context, children);
        }

        /// <summary>
        /// Finds the child with the specified browse name.
        /// </summary>
        protected override BaseInstanceState FindChild(
            ISystemContext context,
            QualifiedName browseName,
            bool createOrReplace,
            BaseInstanceState replacement)
        {
            if (QualifiedName.IsNull(browseName))
            {
                return null;
            }

            BaseInstanceState instance = null;

            switch (browseName.Name)
            {
                case BrowseNames.EnumStrings:
                {
                    if (createOrReplace)
                    {
                        if (EnumStrings == null)
                        {
                            if (replacement == null)
                            {
                                EnumStrings = new PropertyState<LocalizedText[]>(this);
                            }
                            else
                            {
                                EnumStrings = (PropertyState<LocalizedText[]>)replacement;
                            }
                        }
                    }

                    instance = EnumStrings;
                    break;
                }
            }

            if (instance != null)
            {
                return instance;
            }

            return base.FindChild(context, browseName, createOrReplace, replacement);
        }
        #endregion

        #region Private Fields
        private PropertyState<LocalizedText[]> m_enumStrings;
        #endregion
    }

    /// <summary> 
    /// A typed base class for all data variable nodes.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class BaseDataVariableState<T> : BaseDataVariableState
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with its defalt attribute values.
        /// </summary>
        public BaseDataVariableState(NodeState parent) : base(parent)
        {
            Value = default(T);
            IsValueType = !typeof(T).GetTypeInfo().IsValueType;
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        /// <param name="context">An object that describes how access the system containing the data. </param>
        protected override void Initialize(ISystemContext context)
        {
            base.Initialize(context);

            Value = default(T);
            DataType = TypeInfo.GetDataTypeId(typeof(T));
            ValueRank = TypeInfo.GetValueRank(typeof(T));
        }

        /// <summary>
        /// Extracts a value of the specified type from a value stored in a variant.
        /// </summary>
        [Obsolete("Should use the version that takes a ISystemContext (pass null if ISystemContext is not available).")]
        protected override object ExtractValueFromVariant(object value, bool throwOnError)
        {
            return ExtractValueFromVariant<T>(null, value, throwOnError);
        }

        /// <summary>
        /// Extracts a value of the specified type from a value stored in a variant.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="value">The value.</param>
        /// <param name="throwOnError">if set to <c>true</c> throw an exception on error.</param>
        /// <remarks>
        /// If throwOnError is false the default value for the type is returned if the value is not valid.
        /// </remarks>
        /// <returns>Returns value of the <c>T</c> type</returns>
        protected override object ExtractValueFromVariant(ISystemContext context, object value, bool throwOnError)
        {
            return ExtractValueFromVariant<T>(context, value, throwOnError);
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
                return CheckTypeBeforeCast<T>(base.Value, true);
            }
            
            set 
            { 
                base.Value = value; 
            }
        }
        #endregion
    }

    #region BaseVariableValue Class
    /// <summary>
    /// A thread safe object that can be used to access the value of a structure variable.
    /// </summary>
    public class BaseVariableValue
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with a synchronization object.
        /// </summary>
        public BaseVariableValue(object dataLock)
        {
            m_lock = dataLock;
            m_copyPolicy = VariableCopyPolicy.CopyOnRead;

            if (m_lock == null)
            {
                m_lock = new object();
            }
        }
        #endregion

        #region Public Members
        /// <summary>
        /// An object used to synchronize access to the value.
        /// </summary>
        public object Lock
        {
            get { return m_lock; }
        }

        /// <summary>
        /// The behavior to use when reading or writing all or part of the object.
        /// </summary>
        public VariableCopyPolicy CopyPolicy
        {
            get { return m_copyPolicy; }
            set { m_copyPolicy = value; }
        }

        /// <summary>
        /// Gets or sets the current error state.
        /// </summary>
        public ServiceResult Error
        {
            get { return m_error; }
            set { m_error = value; }
        }

        /// <summary>
        /// Gets or sets the timestamp associated with the value.
        /// </summary>
        public DateTime Timestamp
        {
            get { return m_timestamp; }
            set { m_timestamp = value; }
        }

        /// <summary>
        /// Clears the change masks for all nodes in the update list.
        /// </summary>
        public void ChangesComplete(ISystemContext context)
        {
            lock (m_lock)
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
        #endregion
        
        #region Event Callbacks
        /// <summary>
        /// Raised before the value is read.
        /// </summary>
        public VariableValueEventHandler OnBeforeRead;

        /// <summary>
        /// Raised after the value is written.
        /// </summary>
        public VariableValueEventHandler OnAfterWrite;
        #endregion

        #region Protected Methods
        /// <summary>
        /// Does any processing before a read operation takes place.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="node">The node.</param>
        protected void DoBeforeReadProcessing(
            ISystemContext context,
            NodeState node)
        {
            if (OnBeforeRead != null)
            {
                OnBeforeRead(context, this, node);
            }
        }

        /// <summary>
        /// Reads the value or a component of the value.
        /// </summary>
        protected ServiceResult Read(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            lock (m_lock)
            {
                // ensure a value timestamp exists.
                if (m_timestamp == DateTime.MinValue)
                {
                    m_timestamp = DateTime.UtcNow;
                }
                   
                timestamp = m_timestamp;

                // check for errors.
                if (ServiceResult.IsBad(m_error))
                {
                    value = null;
                    statusCode = m_error.StatusCode;
                    return m_error;
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
                if ((m_copyPolicy & VariableCopyPolicy.CopyOnRead) != 0)
                {
                    value = Utils.Clone(value);
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
            lock (m_lock)
            {
                if (ServiceResult.IsBad(m_error))
                {
                    valueToRead = null;
                    return m_error;
                }
                
                if ((m_copyPolicy & VariableCopyPolicy.CopyOnRead) != 0)
                {
                    valueToRead = Utils.Clone(currentValue);
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
            lock (m_lock)
            {
                if ((m_copyPolicy & VariableCopyPolicy.CopyOnWrite) != 0)
                {
                    return Utils.Clone(valueToWrite);
                }

                return valueToWrite;
            }
        }

        /// <summary>
        /// Sets the list of nodes which are updated when ClearChangeMasks is called.
        /// </summary>
        protected void SetUpdateList(IList<BaseInstanceState> updateList)
        {
            lock (m_lock)
            {
                m_updateList = null;

                if (updateList != null && updateList.Count > 0)
                {
                    m_updateList = new BaseInstanceState[updateList.Count];

                    for (int ii = 0; ii < m_updateList.Length; ii++)
                    {
                        m_updateList[ii] = updateList[ii];

                        // the copy copy is enforced by the value wrapper.
                        BaseVariableState variable = m_updateList[ii] as BaseVariableState;
                        
                        if (variable != null)
                        {
                            variable.CopyPolicy = VariableCopyPolicy.Never;
                        }
                    }
                }
            }
        }
        #endregion

        #region Private Fields
        private object m_lock;
        private VariableCopyPolicy m_copyPolicy;
        private BaseInstanceState[] m_updateList;
        private ServiceResult m_error;
        private DateTime m_timestamp;
        #endregion
    }
    #endregion
    
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
        /// The value is copied when is is read.
        /// </summary>
        CopyOnRead = 0x1,

        /// <summary>
        /// The value is copied before it is written.
        /// </summary>
        CopyOnWrite = 0x2,

        /// <summary>
        /// The value is never copied (only useful for value types that do not contain reference types).
        /// </summary>
        Never = 0x0,

        /// <summary>
        /// Data is copied when it is written and when it is read.
        /// </summary>
        Always = 0x3
    }
}
