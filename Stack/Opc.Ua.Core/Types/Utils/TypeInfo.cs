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

using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Collections.Generic;
using System.Runtime.Serialization;

#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#else
using System.Collections.ObjectModel;
#endif

namespace Opc.Ua
{
    /// <summary>
    /// The set of built-in data types for UA type descriptions.
    /// </summary>
    /// <remarks>
    /// An enumeration that lists all of the built-in data types for OPC UA Type Descriptions.
    /// </remarks>
    public enum BuiltInType
    {
        /// <summary>
        /// An invalid or unspecified value.
        /// </summary>
        Null = 0,

        /// <summary>
        /// A boolean logic value (true or false).
        /// </summary>
        Boolean = 1,

        /// <summary>
        /// An 8 bit signed integer value.
        /// </summary>
        SByte = 2,

        /// <summary>
        /// An 8 bit unsigned integer value.
        /// </summary>
        Byte = 3,

        /// <summary>
        /// A 16 bit signed integer value.
        /// </summary>
        Int16 = 4,

        /// <summary>
        /// A 16 bit signed integer value.
        /// </summary>
        UInt16 = 5,

        /// <summary>
        /// A 32 bit signed integer value.
        /// </summary>
        Int32 = 6,

        /// <summary>
        /// A 32 bit unsigned integer value.
        /// </summary>
        UInt32 = 7,

        /// <summary>
        /// A 64 bit signed integer value.
        /// </summary>
        Int64 = 8,

        /// <summary>
        /// A 64 bit unsigned integer value.
        /// </summary>
        UInt64 = 9,

        /// <summary>
        /// An IEEE single precision (32 bit) floating point value.
        /// </summary>
        Float = 10,

        /// <summary>
        /// An IEEE double precision (64 bit) floating point value.
        /// </summary>
        Double = 11,

        /// <summary>
        /// A sequence of Unicode characters.
        /// </summary>
        String = 12,

        /// <summary>
        /// An instance in time.
        /// </summary>
        DateTime = 13,

        /// <summary>
        /// A 128-bit globally unique identifier.
        /// </summary>
        Guid = 14,

        /// <summary>
        /// A sequence of bytes.
        /// </summary>
        ByteString = 15,

        /// <summary>
        /// An XML element.
        /// </summary>
        XmlElement = 16,

        /// <summary>
        /// An identifier for a node in the address space of a UA server.
        /// </summary>
        NodeId = 17,

        /// <summary>
        /// A node id that stores the namespace URI instead of the namespace index.
        /// </summary>
        ExpandedNodeId = 18,

        /// <summary>
        /// A structured result code.
        /// </summary>
        StatusCode = 19,

        /// <summary>
        /// A string qualified with a namespace.
        /// </summary>
        QualifiedName = 20,

        /// <summary>
        /// A localized text string with an locale identifier.
        /// </summary>
        LocalizedText = 21,

        /// <summary>
        /// An opaque object with a syntax that may be unknown to the receiver.
        /// </summary>
        ExtensionObject = 22,

        /// <summary>
        /// A data value with an associated quality and timestamp.
        /// </summary>
        DataValue = 23,

        /// <summary>
        /// Any of the other built-in types.
        /// </summary>
        Variant = 24,

        /// <summary>
        /// A diagnostic information associated with a result code.
        /// </summary>
        DiagnosticInfo = 25,

        //
        // The following BuiltInTypes are for coding convenience
        // internally used in the .NET Standard library.
        // The enumerations are not used for encoding/decoding.
        //

        /// <summary>
        /// Any numeric value.
        /// </summary>
        Number = 26,

        /// <summary>
        /// A signed integer.
        /// </summary>
        Integer = 27,

        /// <summary>
        /// An unsigned integer.
        /// </summary>
        UInteger = 28,

        /// <summary>
        /// An enumerated value
        /// </summary>
        Enumeration = 29
    }

    /// <summary>
    /// Stores information about a type.
    /// </summary>
    public sealed class TypeInfo : IFormattable
    {
        /// <summary>
        /// Constructs an unknown type.
        /// </summary>
        internal TypeInfo()
        {
            BuiltInType = BuiltInType.Null;
            ValueRank = ValueRanks.Any;
        }

        /// <summary>
        /// Construct the object with a built-in type and a value rank.
        /// </summary>
        /// <param name="builtInType">Type of the built in.</param>
        /// <param name="valueRank">The value rank.</param>
        public TypeInfo(BuiltInType builtInType, int valueRank)
        {
            BuiltInType = builtInType;
            ValueRank = valueRank;
        }

        /// <summary>
        /// Construct the object with a built-in type and a value rank.
        /// Uses static TypeInfo definitions if available.
        /// </summary>
        /// <param name="builtInType">Type of the built in.</param>
        /// <param name="valueRank">The value rank.</param>
        public static TypeInfo Create(BuiltInType builtInType, int valueRank)
        {
            switch (valueRank)
            {
                case ValueRanks.Scalar:
                    return CreateScalar(builtInType);
                case ValueRanks.OneDimension:
                    return CreateArray(builtInType);
                default:
                    return new TypeInfo(builtInType, valueRank);
            }
        }

        /// <summary>
        /// A constant representing an unknown type.
        /// </summary>
        /// <value>The constant representing an unknown type.</value>
        public static TypeInfo Unknown { get; } = new TypeInfo();

        /// <summary>
        /// The built-in type.
        /// </summary>
        /// <value>The type of the type represented by this instance.</value>
        public BuiltInType BuiltInType { get; }

        /// <summary>
        /// The value rank.
        /// </summary>
        /// <value>The value rank of the type represented by this instance.</value>
        public int ValueRank { get; }

        /// <summary>
        /// Returns the data type id that describes a value.
        /// </summary>
        /// <param name="value">The value instance to check the data type.</param>
        /// <returns>An data type identifier for a node in a server's address space.</returns>
        public static NodeId GetDataTypeId(object value)
        {
            if (value == null)
            {
                return NodeId.Null;
            }

            NodeId dataTypeId = GetDataTypeId(value.GetType());

            if (dataTypeId == NodeId.Null && value is Matrix matrix)
            {
                return GetDataTypeId(matrix.TypeInfo);
            }

            return dataTypeId;
        }

        /// <summary>
        /// Returns the array rank for a type.
        /// </summary>
        /// <param name="type">The framework type to check the array rank.</param>
        /// <returns>The array rank of the <paramref name="type"/> </returns>
        public static int GetValueRank(Type type)
        {
            TypeInfo typeInfo = Construct(type);

            if (typeInfo.BuiltInType == BuiltInType.Null)
            {
                if (type.GetTypeInfo().IsEnum ||
                    (type.IsArray && type.GetElementType().GetTypeInfo().IsEnum))
                {
                    if (type.IsArray)
                    {
                        return ValueRanks.OneOrMoreDimensions;
                    }

                    return ValueRanks.Scalar;
                }
            }

            return typeInfo.ValueRank;
        }

        /// <summary>
        /// Returns true if the built-in type is a type that cannot be null.
        /// </summary>
        /// <param name="builtInType">The built in type to check.</param>
        /// <returns>
        /// True if the built-in type is a type that cannot be null.
        /// </returns>
        public static bool IsValueType(BuiltInType builtInType)
        {
            if (builtInType is >= BuiltInType.Boolean and <= BuiltInType.Double)
            {
                return true;
            }

            if (builtInType is BuiltInType.DateTime or BuiltInType.Guid or BuiltInType.StatusCode)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns the BuiltInType type for the DataTypeId.
        /// </summary>
        /// <param name="datatypeId">The data type identifier for a node in a server's address space..</param>
        /// <param name="typeTree">The type tree for a server. .</param>
        /// <returns>
        /// A <see cref="BuiltInType"/> value for <paramref name="datatypeId"/>
        /// </returns>
        public static BuiltInType GetBuiltInType(NodeId datatypeId, ITypeTable typeTree)
        {
            NodeId typeId = datatypeId;

            while (!NodeId.IsNull(typeId))
            {
                if (typeId != null && typeId.NamespaceIndex == 0 && typeId.IdType == IdType.Numeric)
                {
                    var id = (BuiltInType)(int)(uint)typeId.Identifier;

                    if (id is > BuiltInType.Null and <= BuiltInType.Enumeration and not BuiltInType.DiagnosticInfo)
                    {
                        return id;
                    }
                }

                if (typeTree == null)
                {
                    break;
                }

                typeId = typeTree.FindSuperType(typeId);
            }

            return BuiltInType.Null;
        }
#if ZOMBIE // Manual
        /// <summary>
        /// Returns the system type for the datatype.
        /// </summary>
        /// <param name="datatypeId">The datatype id.</param>
        /// <param name="factory">The factory used to store and retrieve underlying OPC UA system types.</param>
        /// <returns>The system type for the <paramref name="datatypeId"/>.</returns>
        public static Type GetSystemType(ExpandedNodeId datatypeId, IEncodeableTypeLookup factory)
        {
            if (datatypeId == null)
            {
                return null;
            }

            if (datatypeId.NamespaceIndex != 0 ||
                datatypeId.IdType != IdType.Numeric ||
                datatypeId.IsAbsolute)
            {
                return factory.GetSystemType(datatypeId);
            }

            switch ((uint)datatypeId.Identifier)
            {
                case DataTypes.Boolean:
                    return typeof(bool);
                case DataTypes.SByte:
                    return typeof(sbyte);
                case DataTypes.Byte:
                    return typeof(byte);
                case DataTypes.Int16:
                    return typeof(short);
                case DataTypes.UInt16:
                    return typeof(ushort);
                case DataTypes.Int32:
                    return typeof(int);
                case DataTypes.UInt32:
                    return typeof(uint);
                case DataTypes.Int64:
                    return typeof(long);
                case DataTypes.UInt64:
                    return typeof(ulong);
                case DataTypes.Float:
                    return typeof(float);
                case DataTypes.Double:
                    return typeof(double);
                case DataTypes.String:
                    return typeof(string);
                case DataTypes.DateTime:
                    return typeof(DateTime);
                case DataTypes.Guid:
                    return typeof(Uuid);
                case DataTypes.ByteString:
                    return typeof(byte[]);
                case DataTypes.XmlElement:
                    return typeof(XmlElement);
                case DataTypes.NodeId:
                    return typeof(NodeId);
                case DataTypes.ExpandedNodeId:
                    return typeof(ExpandedNodeId);
                case DataTypes.StatusCode:
                    return typeof(StatusCode);
                case DataTypes.DiagnosticInfo:
                    return typeof(DiagnosticInfo);
                case DataTypes.QualifiedName:
                    return typeof(QualifiedName);
                case DataTypes.LocalizedText:
                    return typeof(LocalizedText);
                case DataTypes.DataValue:
                    return typeof(DataValue);
                case DataTypes.BaseDataType:
                    return typeof(Variant);
                case DataTypes.Structure:
                    return typeof(ExtensionObject);
                case DataTypes.Number:

                case DataTypes.Integer:

                case DataTypes.UInteger:
                    return typeof(Variant);
                case DataTypes.Enumeration:
                    return typeof(int);
                // subtype of DateTime
                case DataTypes.UtcTime:
                    goto case DataTypes.DateTime;
                // subtype of ByteString
                case DataTypes.ApplicationInstanceCertificate:
                case DataTypes.AudioDataType:
                case DataTypes.ContinuationPoint:
                case DataTypes.Image:
                case DataTypes.ImageBMP:
                case DataTypes.ImageGIF:
                case DataTypes.ImageJPG:
                case DataTypes.ImagePNG:
                    goto case DataTypes.ByteString;
                // subtype of NodeId
                case DataTypes.SessionAuthenticationToken:
                    goto case DataTypes.NodeId;
                // subtype of Double
                case DataTypes.Duration:
                    goto case DataTypes.Double;
                // subtype of UInt32
                case DataTypes.IntegerId:
                case DataTypes.Index:
                case DataTypes.VersionTime:
                case DataTypes.Counter:
                    goto case DataTypes.UInt32;
                // subtype of UInt64
                case DataTypes.BitFieldMaskDataType:
                    goto case DataTypes.UInt64;
                // subtype of String
                case DataTypes.DateString:
                case DataTypes.DecimalString:
                case DataTypes.DurationString:
                case DataTypes.LocaleId:
                case DataTypes.NormalizedString:
                case DataTypes.NumericRange:
                case DataTypes.TimeString:
                    goto case DataTypes.String;
                default:
                    return factory.GetSystemType(datatypeId);
            }
        }
#endif

        /// <summary>
        /// Returns the xml qualified name for the specified system type id.
        /// </summary>
        /// <remarks>
        /// Returns the xml qualified name for the specified system type id.
        /// </remarks>
        /// <param name="systemType">The underlying type to query and return the Xml qualified name of</param>
        public static XmlQualifiedName GetXmlName(Type systemType)
        {
            if (systemType == null)
            {
                return null;
            }

            object[] attributes =
            [
                .. systemType.GetTypeInfo().GetCustomAttributes(typeof(DataContractAttribute), true)
            ];

            if (attributes != null)
            {
                for (int ii = 0; ii < attributes.Length; ii++)
                {
                    if (attributes[ii] is DataContractAttribute contract)
                    {
                        if (string.IsNullOrEmpty(contract.Name))
                        {
                            return new XmlQualifiedName(systemType.Name, contract.Namespace);
                        }

                        return new XmlQualifiedName(contract.Name, contract.Namespace);
                    }
                }
            }

            attributes =
            [
                .. systemType.GetTypeInfo()
                    .GetCustomAttributes(typeof(CollectionDataContractAttribute), true)
            ];

            if (attributes != null)
            {
                for (int ii = 0; ii < attributes.Length; ii++)
                {
                    if (attributes[ii] is CollectionDataContractAttribute contract)
                    {
                        if (string.IsNullOrEmpty(contract.Name))
                        {
                            return new XmlQualifiedName(systemType.Name, contract.Namespace);
                        }

                        return new XmlQualifiedName(contract.Name, contract.Namespace);
                    }
                }
            }

            if (systemType == typeof(byte[]))
            {
                return new XmlQualifiedName("ByteString");
            }

            return new XmlQualifiedName(systemType.FullName);
        }

        /// <summary>
        /// Returns the xml qualified name for the specified object.
        /// </summary>
        /// <remarks>
        /// Returns the xml qualified name for the specified object.
        /// </remarks>
        /// <param name="value">The object to query and return the Xml qualified name of</param>
        /// <param name="context">Context</param>
        public static XmlQualifiedName GetXmlName(object value, IServiceMessageContext context)
        {
            if (value is IDynamicComplexTypeInstance xmlEncodeable)
            {
                XmlQualifiedName xmlName = xmlEncodeable.GetXmlName(context);
                if (xmlName != null)
                {
                    return xmlName;
                }
            }
            return GetXmlName(value?.GetType());
        }

        /// <summary>
        /// Returns the type info if the value is an instance of the data type with the specified value rank.
        /// </summary>
        /// <param name="value">The value instance to check.</param>
        /// <param name="expectedDataTypeId">The expected data type identifier for a node.</param>
        /// <param name="expectedValueRank">The expected value rank.</param>
        /// <param name="namespaceUris">The namespace URI's.</param>
        /// <param name="typeTree">The type tree for a server.</param>
        /// <returns>
        /// An data type info if the value is an instance of the data type with the specified value rank; otherwise <c>null</c>.
        /// </returns>
        /// <exception cref="ServiceResultException"></exception>
        public static TypeInfo IsInstanceOfDataType(
            object value,
            NodeId expectedDataTypeId,
            int expectedValueRank,
            NamespaceTable namespaceUris,
            ITypeTable typeTree)
        {
            // get the type info.
            TypeInfo typeInfo = Construct(value);

            BuiltInType expectedType;
            if (typeInfo.BuiltInType == BuiltInType.Null)
            {
                expectedType = GetBuiltInType(expectedDataTypeId, typeTree);

                // nulls allowed for all array types.
                if (expectedValueRank != ValueRanks.Scalar)
                {
                    return CreateArray(expectedType);
                }

                // check if the type supports nulls.
                switch (expectedType)
                {
                    case BuiltInType.String:
                    case BuiltInType.ByteString:
                    case BuiltInType.XmlElement:
                    case BuiltInType.NodeId:
                    case BuiltInType.ExpandedNodeId:
                    case BuiltInType.LocalizedText:
                    case BuiltInType.QualifiedName:
                    case BuiltInType.DataValue:
                    case BuiltInType.Variant:
                    case BuiltInType.ExtensionObject:
                        return CreateScalar(expectedType);
                    case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                        // nulls not allowed.
                        return null;
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unexpected BuiltInType {expectedType}");
                }
            }

            // A ByteString is equivalent to an Array of Bytes.
            if (typeInfo.BuiltInType == BuiltInType.ByteString &&
                typeInfo.ValueRank == ValueRanks.Scalar &&
                expectedValueRank is ValueRanks.OneOrMoreDimensions or ValueRanks.OneDimension)
            {
                if (typeTree.IsTypeOf(expectedDataTypeId, DataTypeIds.Byte))
                {
                    return typeInfo;
                }

                return null;
            }

            // check the value rank.
            if (!ValueRanks.IsValid(typeInfo.ValueRank, expectedValueRank))
            {
                return null;
            }

            // check for special predefined types.
            if (expectedDataTypeId.IdType == IdType.Numeric &&
                expectedDataTypeId.NamespaceIndex == 0)
            {
                BuiltInType actualType = typeInfo.BuiltInType;

                switch ((uint)expectedDataTypeId.Identifier)
                {
                    case DataTypes.Number:
                        switch (actualType)
                        {
                            case BuiltInType.SByte:
                            case BuiltInType.Int16:
                            case BuiltInType.Int32:
                            case BuiltInType.Int64:
                            case BuiltInType.Byte:
                            case BuiltInType.UInt16:
                            case BuiltInType.UInt32:
                            case BuiltInType.UInt64:
                            case BuiltInType.Double:
                            case BuiltInType.Float:
                                return typeInfo;
                            case BuiltInType.Variant:
                                if (typeInfo.ValueRank == ValueRanks.Scalar)
                                {
                                    return null;
                                }

                                break;
                            case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                                return null;
                            default:
                                throw ServiceResultException.Unexpected(
                                    $"Unexpected BuiltInType {typeInfo.BuiltInType}");
                        }

                        break;
                    case DataTypes.Integer:
                        switch (actualType)
                        {
                            case BuiltInType.SByte:
                            case BuiltInType.Int16:
                            case BuiltInType.Int32:
                            case BuiltInType.Int64:
                                return typeInfo;
                            case BuiltInType.Variant:
                                if (typeInfo.ValueRank == ValueRanks.Scalar)
                                {
                                    return null;
                                }

                                break;
                            case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                                return null;
                            default:
                                throw ServiceResultException.Unexpected(
                                    $"Unexpected BuiltInType {typeInfo.BuiltInType}");
                        }

                        break;
                    case DataTypes.UInteger:
                        switch (actualType)
                        {
                            case BuiltInType.Byte:
                            case BuiltInType.UInt16:
                            case BuiltInType.UInt32:
                            case BuiltInType.UInt64:
                                return typeInfo;
                            case BuiltInType.Variant:
                                if (typeInfo.ValueRank == ValueRanks.Scalar)
                                {
                                    return null;
                                }

                                break;
                            case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                                return null;
                            default:
                                throw ServiceResultException.Unexpected(
                                    $"Unexpected BuiltInType {typeInfo.BuiltInType}");
                        }
                        break;
                    case DataTypes.Enumeration:
                        if (typeInfo.BuiltInType == BuiltInType.Int32)
                        {
                            return typeInfo;
                        }

                        return null;
                    case DataTypes.Structure:
                        if (typeInfo.BuiltInType == BuiltInType.ExtensionObject)
                        {
                            return typeInfo;
                        }

                        return null;
                    case DataTypes.BaseDataType:
                        if (typeInfo.BuiltInType != BuiltInType.Variant)
                        {
                            return typeInfo;
                        }
                        break;
                }
            }

            // check simple types.
            if (typeInfo.BuiltInType is not BuiltInType.ExtensionObject and not BuiltInType.Variant)
            {
                if (typeTree.IsTypeOf(
                    expectedDataTypeId,
                    new NodeId((uint)(int)typeInfo.BuiltInType)))
                {
                    return typeInfo;
                }

                // check for enumerations.
                if (typeInfo.BuiltInType == BuiltInType.Int32 &&
                    typeTree.IsTypeOf(expectedDataTypeId, DataTypeIds.Enumeration))
                {
                    return typeInfo;
                }

                // check for direct subtypes of BaseDataType.
                if (GetBuiltInType(expectedDataTypeId, typeTree) == BuiltInType.Variant)
                {
                    return typeInfo;
                }

                return null;
            }

            // handle scalar.
            if (typeInfo.ValueRank < 0)
            {
                // check extension objects vs. expected type.
                if (typeInfo.BuiltInType == BuiltInType.ExtensionObject)
                {
                    expectedType = GetBuiltInType(expectedDataTypeId, typeTree);

                    if (expectedType == BuiltInType.Variant)
                    {
                        return typeInfo;
                    }

                    if (expectedType != BuiltInType.ExtensionObject)
                    {
                        return null;
                    }
                }

                // expected type is extension object so compare type tree.
                NodeId actualDataTypeId = typeInfo.GetDataTypeId(value, namespaceUris, typeTree);

                if (typeTree.IsTypeOf(actualDataTypeId, expectedDataTypeId))
                {
                    return typeInfo;
                }

                return null;
            }

            // check every element in the array or matrix.
            var array = value as Array;
            if (array == null && value is Matrix matrix)
            {
                array = matrix.Elements;
            }

            if (array != null)
            {
                BuiltInType expectedElementType = GetBuiltInType(expectedDataTypeId, typeTree);
                BuiltInType actualElementType = GetBuiltInType(
                    array.GetType().GetElementType().Name);
                // system type of array matches the expected type - nothing more to do.
                if (actualElementType != BuiltInType.ExtensionObject &&
                    actualElementType == expectedElementType)
                {
                    return typeInfo;
                }

                // check for variant arrays.
                if (expectedElementType == BuiltInType.Variant)
                {
                    return typeInfo;
                }

                // have to do it the hard way and check each element.
                int[] dimensions = new int[array.Rank];

                for (int ii = 0; ii < dimensions.Length; ii++)
                {
                    dimensions[ii] = array.GetLength(ii);
                }

                int[] indexes = new int[dimensions.Length];

                for (int ii = 0; ii < array.Length; ii++)
                {
                    int divisor = array.Length;

                    for (int jj = 0; jj < indexes.Length; jj++)
                    {
                        divisor /= dimensions[jj];
                        indexes[jj] = ii / divisor % dimensions[jj];
                    }

                    object element = array.GetValue(indexes);

                    if (actualElementType == BuiltInType.Variant)
                    {
                        element = ((Variant)element).Value;
                    }

                    TypeInfo elementInfo = IsInstanceOfDataType(
                        element,
                        expectedDataTypeId,
                        ValueRanks.Scalar,
                        namespaceUris,
                        typeTree);

                    // give up at the first invalid element.
                    if (elementInfo == null)
                    {
                        return null;
                    }
                }

                // all elements valid.
                return typeInfo;
            }

            return null;
        }

        /// <summary>
        /// Returns the data type id that describes a value.
        /// </summary>
        /// <param name="value">The value to describe.</param>
        /// <param name="namespaceUris">The namespace uris.</param>
        /// <param name="typeTree">The type tree for a server.</param>
        /// <returns>Returns the data type identifier that describes a value.</returns>
        public NodeId GetDataTypeId(object value, NamespaceTable namespaceUris, ITypeTable typeTree)
        {
            if (BuiltInType == BuiltInType.Null)
            {
                return NodeId.Null;
            }

            if (BuiltInType == BuiltInType.ExtensionObject)
            {
                if (value is IEncodeable encodeable)
                {
                    return ExpandedNodeId.ToNodeId(encodeable.TypeId, namespaceUris);
                }

                if (value is ExtensionObject extension)
                {
                    encodeable = extension.Body as IEncodeable;
                    if (encodeable != null)
                    {
                        return ExpandedNodeId.ToNodeId(encodeable.TypeId, namespaceUris);
                    }

                    return typeTree.FindDataTypeId(extension.TypeId);
                }

                return DataTypeIds.Structure;
            }

            return new NodeId((uint)(int)BuiltInType);
        }

        /// <summary>
        /// Returns the type info for the provided value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns><see cref="TypeInfo"/> instance storing information about the <paramref name="value"/> type.</returns>
        public static TypeInfo Construct(object value)
        {
            if (value == null)
            {
                return Unknown;
            }

            TypeInfo typeInfo = Construct(value.GetType());

            // check for instances of matrices.
            if (typeInfo.BuiltInType == BuiltInType.Null && value is Matrix matrix)
            {
                return matrix.TypeInfo;
            }

            return typeInfo;
        }

        /// <summary>
        /// Returns the type info for the specified system type.
        /// </summary>
        /// <param name="systemType">The specified system (framework) type.</param>
        /// <returns><see cref="TypeInfo"/> instance storing information equivalent to the <paramref name="systemType"/> type.</returns>
        public static TypeInfo Construct(Type systemType)
        {
            // check for null.
            if (systemType == null)
            {
                return Unknown;
            }

            // using strings in switch statements is much faster than the typeof() operator.
            string name = systemType.Name;

            // parse array types.
            string dimensions = null;

            if (name[^1] == ']')
            {
                int index = name.IndexOf('[', StringComparison.Ordinal);

                if (index != -1)
                {
                    dimensions = name[index..];
                    name = name[..index];
                }
            }

            // handle scalar.
            if (dimensions == null)
            {
                BuiltInType builtInType = GetBuiltInType(name);

                if (builtInType != BuiltInType.Null)
                {
                    return CreateScalar(builtInType);
                }

                if (systemType.GetTypeInfo().IsEnum)
                {
                    return Scalars.Enumeration;
                }

                // check for collection.
                if (name.EndsWith("Collection", StringComparison.Ordinal))
                {
                    builtInType = GetBuiltInType(name[..^"Collection".Length]);

                    if (builtInType != BuiltInType.Null)
                    {
                        return CreateArray(builtInType);
                    }

                    // check for encodeable object.
                    if (systemType.GetTypeInfo().BaseType.GetTypeInfo().IsGenericType)
                    {
                        return Construct(systemType.GetTypeInfo().BaseType);
                    }

                    return Unknown;
                }

                // check for generic type.
                if (systemType.GetTypeInfo().IsGenericType)
                {
                    Type[] argTypes = systemType.GetGenericArguments();

                    if (argTypes != null && argTypes.Length == 1)
                    {
                        TypeInfo typeInfo = Construct(argTypes[0]);

                        if (typeInfo.BuiltInType != BuiltInType.Null &&
                            typeInfo.ValueRank == ValueRanks.Scalar)
                        {
                            return CreateArray(typeInfo.BuiltInType);
                        }
                    }

                    return Unknown;
                }

                // check for encodeable object.
                if (typeof(IEncodeable).GetTypeInfo().IsAssignableFrom(systemType.GetTypeInfo()) ||
                    name == "IEncodeable")
                {
                    return Scalars.ExtensionObject;
                }

                return Unknown;
            }

            // handle one dimensional array.
            if (dimensions.Length == 2)
            {
                BuiltInType builtInType = GetBuiltInType(name);

                if (builtInType == BuiltInType.Byte)
                {
                    return Scalars.ByteString;
                }

                if (builtInType != BuiltInType.Null)
                {
                    return CreateArray(builtInType);
                }

                // check for encodeable object.
                if (typeof(IEncodeable).GetTypeInfo()
                        .IsAssignableFrom(systemType.GetElementType().GetTypeInfo()) ||
                    name == "IEncodeable")
                {
                    return Arrays.ExtensionObject;
                }

                if (systemType.GetTypeInfo().GetElementType().IsEnum)
                {
                    return Arrays.Enumeration;
                }

                return Unknown;
            }

            // count the number of dimensions (syntax is [,] - commas+1 is the number of dimensions).
            int count = 1;

            for (int ii = 1; ii < dimensions.Length - 1; ii++)
            {
                if (dimensions[ii] == ',')
                {
                    count++;
                }
            }

            // handle simple multi-dimensional array (enclosing [] + number of commas)
            if (count + 1 == dimensions.Length)
            {
                BuiltInType builtInType = GetBuiltInType(name);

                if (builtInType != BuiltInType.Null)
                {
                    return Create(builtInType, count);
                }

                // check for encodeable object.
                if (typeof(IEncodeable).GetTypeInfo().IsAssignableFrom(systemType.GetTypeInfo()) ||
                    name == "IEncodeable")
                {
                    return Create(BuiltInType.ExtensionObject, count);
                }

                return Unknown;
            }

            // handle multi-dimensional array of byte strings.
            if (dimensions[1] == ']')
            {
                // syntax of type is [][,,,] - adding three checks for the middle ']['
                if (name == "Byte" && count + 3 == dimensions.Length)
                {
                    return Create(BuiltInType.ByteString, count);
                }
            }

            // unknown type.
            return Unknown;
        }

        /// <summary>
        /// Returns the default value for the specified built-in type.
        /// </summary>
        /// <param name="type">The Built-in type.</param>
        /// <returns>The default value.</returns>
        /// <exception cref="ServiceResultException"></exception>
        public static object GetDefaultValue(BuiltInType type)
        {
            switch (type)
            {
                case BuiltInType.Boolean:
                    return false;
                case BuiltInType.SByte:
                    return (sbyte)0;
                case BuiltInType.Byte:
                    return (byte)0;
                case BuiltInType.Int16:
                    return (short)0;
                case BuiltInType.UInt16:
                    return (ushort)0;
                case BuiltInType.Int32:
                    return 0;
                case BuiltInType.UInt32:
                    return (uint)0;
                case BuiltInType.Int64:
                    return (long)0;
                case BuiltInType.UInt64:
                    return (ulong)0;
                case BuiltInType.Float:
                    return (float)0;
                case BuiltInType.Double:
                    return (double)0;
                case BuiltInType.String:
                    return null;
                case BuiltInType.DateTime:
                    return DateTime.MinValue;
                case BuiltInType.Guid:
                    return Uuid.Empty;
                case BuiltInType.ByteString:

                case BuiltInType.XmlElement:
                    return null;
                case BuiltInType.StatusCode:
                    return new StatusCode(StatusCodes.Good);
                case BuiltInType.NodeId:
                    return NodeId.Null;
                case BuiltInType.ExpandedNodeId:
                    return ExpandedNodeId.Null;
                case BuiltInType.QualifiedName:
                    return QualifiedName.Null;
                case BuiltInType.LocalizedText:
                    return LocalizedText.Null;
                case BuiltInType.Variant:
                    return Variant.Null;
                case BuiltInType.DataValue:
                    return null;
                case BuiltInType.Enumeration:
                    return 0;
                case BuiltInType.Number:
                    return (double)0;
                case BuiltInType.Integer:
                    return (long)0;
                case BuiltInType.UInteger:
                    return (ulong)0;
                case BuiltInType.Null:
                case BuiltInType.ExtensionObject:
                case BuiltInType.DiagnosticInfo:
                    return null;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {type}");
            }
        }

        /// <summary>
        /// Returns the default value for the specified data type and value rank.
        /// </summary>
        /// <param name="dataType">The data type.</param>
        /// <param name="valueRank">The value rank.</param>
        /// <returns>The default value.</returns>
        public static object GetDefaultValue(NodeId dataType, int valueRank)
        {
            return GetDefaultValue(dataType, valueRank, null);
        }

        /// <summary>
        /// Returns the default value for the specified data type and value rank.
        /// </summary>
        /// <param name="dataType">The data type.</param>
        /// <param name="valueRank">The value rank.</param>
        /// <param name="typeTree">The type tree for a server.</param>
        /// <returns>A default value.</returns>
        public static object GetDefaultValue(NodeId dataType, int valueRank, ITypeTable typeTree)
        {
            if (valueRank != ValueRanks.Scalar)
            {
                return null;
            }

            if (dataType == null ||
                dataType.IdType != IdType.Numeric ||
                dataType.NamespaceIndex != 0)
            {
                return GetDefaultValueInternal(dataType, typeTree);
            }

            uint id = (uint)dataType.Identifier;

            if (id <= DataTypes.DiagnosticInfo)
            {
                // Handle built-in types.
                return GetDefaultValue((BuiltInType)(int)id);
            }

            // Handle known UA types that derive from built in type
            switch (id)
            {
                case DataTypes.Duration:
                    return (double)0;
                case DataTypes.UtcTime:
                    return DateTime.MinValue;
                case DataTypes.Counter:
                case DataTypes.IntegerId:
                    return (uint)0;
                case DataTypes.Number:
                    return (double)0;
                case DataTypes.UInteger:
                    return (ulong)0;
                case DataTypes.Integer:
                    return (long)0;
                case DataTypes.IdType:
                    return (int)IdType.Numeric;
                case DataTypes.NodeClass:
                    return (int)NodeClass.Unspecified;
                case DataTypes.Enumeration:
                    return 0;
                default:
                    return GetDefaultValueInternal(dataType, typeTree);
            }

            static object GetDefaultValueInternal(NodeId dataType, ITypeTable typeTree)
            {
                BuiltInType builtInType = GetBuiltInType(dataType, typeTree);
                if (builtInType != BuiltInType.Null)
                {
                    return GetDefaultValue(builtInType);
                }
                return null;
            }
        }

        /// <summary>
        /// Returns the default value for the specified built-in type.
        /// </summary>
        /// <param name="type">The built-in type.</param>
        /// <param name="dimensions">The dimensions.</param>
        /// <returns>The default value for the specified built-in type</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public static Array CreateArray(BuiltInType type, params int[] dimensions)
        {
            if (dimensions == null || dimensions.Length == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dimensions),
                    "Array dimensions must be specified.");
            }

            int length = dimensions[0];

            // create one dimensional array.
            if (dimensions.Length == 1)
            {
                switch (type)
                {
                    case BuiltInType.Null:
                        return new object[length];
                    case BuiltInType.Boolean:
                        return new bool[length];
                    case BuiltInType.SByte:
                        return new sbyte[length];
                    case BuiltInType.Byte:
                        return new byte[length];
                    case BuiltInType.Int16:
                        return new short[length];
                    case BuiltInType.UInt16:
                        return new ushort[length];
                    case BuiltInType.Int32:
                        return new int[length];
                    case BuiltInType.UInt32:
                        return new uint[length];
                    case BuiltInType.Int64:
                        return new long[length];
                    case BuiltInType.UInt64:
                        return new ulong[length];
                    case BuiltInType.Float:
                        return new float[length];
                    case BuiltInType.Double:
                        return new double[length];
                    case BuiltInType.String:
                        return new string[length];
                    case BuiltInType.DateTime:
                        return new DateTime[length];
                    case BuiltInType.Guid:
                        return new Uuid[length];
                    case BuiltInType.ByteString:
                        return new byte[length][];
                    case BuiltInType.XmlElement:
                        return new XmlElement[length];
                    case BuiltInType.StatusCode:
                        return new StatusCode[length];
                    case BuiltInType.NodeId:
                        return new NodeId[length];
                    case BuiltInType.ExpandedNodeId:
                        return new ExpandedNodeId[length];
                    case BuiltInType.QualifiedName:
                        return new QualifiedName[length];
                    case BuiltInType.LocalizedText:
                        return new LocalizedText[length];
                    case BuiltInType.Variant:
                        return new Variant[length];
                    case BuiltInType.DataValue:
                        return new DataValue[length];
                    case BuiltInType.ExtensionObject:
                        return new ExtensionObject[length];
                    case BuiltInType.DiagnosticInfo:
                        return new DiagnosticInfo[length];
                    case BuiltInType.Enumeration:
                        return new int[length];
                    case BuiltInType.Number:

                    case BuiltInType.Integer:

                    case BuiltInType.UInteger:
                        return new Variant[length];
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unexpected BuiltInType {type}");
                }
            }
            // create higher dimension arrays.
            else
            {
                switch (type)
                {
                    case BuiltInType.Null:
                        return Array.CreateInstance(typeof(object), dimensions);
                    case BuiltInType.Boolean:
                        return Array.CreateInstance(typeof(bool), dimensions);
                    case BuiltInType.SByte:
                        return Array.CreateInstance(typeof(sbyte), dimensions);
                    case BuiltInType.Byte:
                        return Array.CreateInstance(typeof(byte), dimensions);
                    case BuiltInType.Int16:
                        return Array.CreateInstance(typeof(short), dimensions);
                    case BuiltInType.UInt16:
                        return Array.CreateInstance(typeof(ushort), dimensions);
                    case BuiltInType.Int32:
                        return Array.CreateInstance(typeof(int), dimensions);
                    case BuiltInType.UInt32:
                        return Array.CreateInstance(typeof(uint), dimensions);
                    case BuiltInType.Int64:
                        return Array.CreateInstance(typeof(long), dimensions);
                    case BuiltInType.UInt64:
                        return Array.CreateInstance(typeof(ulong), dimensions);
                    case BuiltInType.Float:
                        return Array.CreateInstance(typeof(float), dimensions);
                    case BuiltInType.Double:
                        return Array.CreateInstance(typeof(double), dimensions);
                    case BuiltInType.String:
                        return Array.CreateInstance(typeof(string), dimensions);
                    case BuiltInType.DateTime:
                        return Array.CreateInstance(typeof(DateTime), dimensions);
                    case BuiltInType.Guid:
                        return Array.CreateInstance(typeof(Uuid), dimensions);
                    case BuiltInType.ByteString:
                        return Array.CreateInstance(typeof(byte[]), dimensions);
                    case BuiltInType.XmlElement:
                        return Array.CreateInstance(typeof(XmlElement), dimensions);
                    case BuiltInType.StatusCode:
                        return Array.CreateInstance(typeof(StatusCode), dimensions);
                    case BuiltInType.NodeId:
                        return Array.CreateInstance(typeof(NodeId), dimensions);
                    case BuiltInType.ExpandedNodeId:
                        return Array.CreateInstance(typeof(ExpandedNodeId), dimensions);
                    case BuiltInType.QualifiedName:
                        return Array.CreateInstance(typeof(QualifiedName), dimensions);
                    case BuiltInType.LocalizedText:
                        return Array.CreateInstance(typeof(LocalizedText), dimensions);
                    case BuiltInType.Variant:
                        return Array.CreateInstance(typeof(Variant), dimensions);
                    case BuiltInType.DataValue:
                        return Array.CreateInstance(typeof(DataValue), dimensions);
                    case BuiltInType.ExtensionObject:
                        return Array.CreateInstance(typeof(ExtensionObject), dimensions);
                    case BuiltInType.DiagnosticInfo:
                        return Array.CreateInstance(typeof(DiagnosticInfo), dimensions);
                    case BuiltInType.Enumeration:
                        return Array.CreateInstance(typeof(int), dimensions);
                    case BuiltInType.Number:

                    case BuiltInType.Integer:

                    case BuiltInType.UInteger:
                        return Array.CreateInstance(typeof(Variant), dimensions);
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unexpected BuiltInType {type}");
                }
            }
        }

        /// <summary>
        /// Maps the type name to a built-in type.
        /// </summary>
        private static BuiltInType GetBuiltInType(string typeName)
        {
            switch (typeName)
            {
                case "Boolean":
                    return BuiltInType.Boolean;
                case "SByte":
                    return BuiltInType.SByte;
                case "Byte":
                    return BuiltInType.Byte;
                case "Int16":
                    return BuiltInType.Int16;
                case "UInt16":
                    return BuiltInType.UInt16;
                case "Int32":
                    return BuiltInType.Int32;
                case "UInt32":
                    return BuiltInType.UInt32;
                case "Int64":
                    return BuiltInType.Int64;
                case "UInt64":
                    return BuiltInType.UInt64;
                case "Float":
                case "Single":
                    return BuiltInType.Float;
                case "Double":
                    return BuiltInType.Double;
                case "String":
                    return BuiltInType.String;
                case "DateTime":
                    return BuiltInType.DateTime;
                case "Guid":
                case "Uuid":
                    return BuiltInType.Guid;
                case "ByteString":
                    return BuiltInType.ByteString;
                case "XmlElement":
                    return BuiltInType.XmlElement;
                case "NodeId":
                    return BuiltInType.NodeId;
                case "ExpandedNodeId":
                    return BuiltInType.ExpandedNodeId;
                case "LocalizedText":
                    return BuiltInType.LocalizedText;
                case "QualifiedName":
                    return BuiltInType.QualifiedName;
                case "StatusCode":
                    return BuiltInType.StatusCode;
                case "DiagnosticInfo":
                    return BuiltInType.DiagnosticInfo;
                case "DataValue":
                    return BuiltInType.DataValue;
                case "Variant":
                    return BuiltInType.Variant;
                case "ExtensionObject":
                    return BuiltInType.ExtensionObject;
                case "Object":
                    return BuiltInType.Variant;
                default:
                    return BuiltInType.Null;
            }
        }

        /// <summary>
        /// Returns a static or allocated type info object for a scalar of the specified type.
        /// </summary>
        public static TypeInfo CreateScalar(BuiltInType builtInType)
        {
            if (s_scalars.Value.TryGetValue(builtInType, out TypeInfo scalarTypeInfo))
            {
                return scalarTypeInfo;
            }
            return new TypeInfo(builtInType, ValueRanks.Scalar);
        }

        /// <summary>
        /// Constants for scalar types.
        /// </summary>
        public static class Scalars
        {
            /// <summary>
            /// A boolean logic value (true or false).
            /// </summary>
            public static readonly TypeInfo Boolean = new(BuiltInType.Boolean, ValueRanks.Scalar);

            /// <summary>
            /// An 8 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo SByte = new(BuiltInType.SByte, ValueRanks.Scalar);

            /// <summary>
            /// An 8 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo Byte = new(BuiltInType.Byte, ValueRanks.Scalar);

            /// <summary>
            /// A 16 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int16 = new(BuiltInType.Int16, ValueRanks.Scalar);

            /// <summary>
            /// A 16 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo UInt16 = new(BuiltInType.UInt16, ValueRanks.Scalar);

            /// <summary>
            /// A 32 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int32 = new(BuiltInType.Int32, ValueRanks.Scalar);

            /// <summary>
            /// A 32 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo UInt32 = new(BuiltInType.UInt32, ValueRanks.Scalar);

            /// <summary>
            /// A 64 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int64 = new(BuiltInType.Int64, ValueRanks.Scalar);

            /// <summary>
            /// A 64 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo UInt64 = new(BuiltInType.UInt64, ValueRanks.Scalar);

            /// <summary>
            /// An IEEE single precision (32 bit) floating point value.
            /// </summary>
            public static readonly TypeInfo Float = new(BuiltInType.Float, ValueRanks.Scalar);

            /// <summary>
            /// An IEEE double precision (64 bit) floating point value.
            /// </summary>
            public static readonly TypeInfo Double = new(BuiltInType.Double, ValueRanks.Scalar);

            /// <summary>
            /// A sequence of Unicode characters.
            /// </summary>
            public static readonly TypeInfo String = new(BuiltInType.String, ValueRanks.Scalar);

            /// <summary>
            /// An instance in time.
            /// </summary>
            public static readonly TypeInfo DateTime = new(BuiltInType.DateTime, ValueRanks.Scalar);

            /// <summary>
            /// A 128-bit globally unique identifier.
            /// </summary>
            public static readonly TypeInfo Guid = new(BuiltInType.Guid, ValueRanks.Scalar);

            /// <summary>
            /// A sequence of bytes.
            /// </summary>
            public static readonly TypeInfo ByteString = new(
                BuiltInType.ByteString,
                ValueRanks.Scalar);

            /// <summary>
            /// An XML element.
            /// </summary>
            public static readonly TypeInfo XmlElement = new(
                BuiltInType.XmlElement,
                ValueRanks.Scalar);

            /// <summary>
            /// An identifier for a node in the address space of a UA server.
            /// </summary>
            public static readonly TypeInfo NodeId = new(BuiltInType.NodeId, ValueRanks.Scalar);

            /// <summary>
            /// A node id that stores the namespace URI instead of the namespace index.
            /// </summary>
            public static readonly TypeInfo ExpandedNodeId = new(
                BuiltInType.ExpandedNodeId,
                ValueRanks.Scalar);

            /// <summary>
            /// A structured result code.
            /// </summary>
            public static readonly TypeInfo StatusCode = new(
                BuiltInType.StatusCode,
                ValueRanks.Scalar);

            /// <summary>
            /// A string qualified with a namespace.
            /// </summary>
            public static readonly TypeInfo QualifiedName = new(
                BuiltInType.QualifiedName,
                ValueRanks.Scalar);

            /// <summary>
            /// A localized text string with an locale identifier.
            /// </summary>
            public static readonly TypeInfo LocalizedText = new(
                BuiltInType.LocalizedText,
                ValueRanks.Scalar);

            /// <summary>
            /// An opaque object with a syntax that may be unknown to the receiver.
            /// </summary>
            public static readonly TypeInfo ExtensionObject = new(
                BuiltInType.ExtensionObject,
                ValueRanks.Scalar);

            /// <summary>
            /// A data value with an associated quality and timestamp.
            /// </summary>
            public static readonly TypeInfo DataValue = new(
                BuiltInType.DataValue,
                ValueRanks.Scalar);

            /// <summary>
            /// Any of the other built-in types.
            /// </summary>
            public static readonly TypeInfo Variant = new(BuiltInType.Variant, ValueRanks.Scalar);

            /// <summary>
            /// A diagnostic information associated with a result code.
            /// </summary>
            public static readonly TypeInfo DiagnosticInfo = new(
                BuiltInType.DiagnosticInfo,
                ValueRanks.Scalar);

            /// <summary>
            /// An enum type info.
            /// </summary>
            public static readonly TypeInfo Enumeration = new(
                BuiltInType.Enumeration,
                ValueRanks.Scalar);
        }

        /// <summary>
        /// The on demand look up table for one dimensional arrays.
        /// </summary>
        private static readonly Lazy<IReadOnlyDictionary<BuiltInType, TypeInfo>> s_scalars =
            new(() =>
            {
                FieldInfo[] fields = typeof(Scalars).GetFields(
                    BindingFlags.Public | BindingFlags.Static);

                var keyValuePairs = new Dictionary<BuiltInType, TypeInfo>();
                foreach (FieldInfo field in fields)
                {
                    var typeInfo = (TypeInfo)field.GetValue(typeof(TypeInfo));
                    keyValuePairs.Add(typeInfo.BuiltInType, typeInfo);
                }

#if NET8_0_OR_GREATER
                return keyValuePairs.ToFrozenDictionary();
#else
                return new ReadOnlyDictionary<BuiltInType, TypeInfo>(keyValuePairs);
#endif
            });

        /// <summary>
        /// Returns a static of allocated type info object for a one dimensional array of the specified type.
        /// </summary>
        public static TypeInfo CreateArray(BuiltInType builtInType)
        {
            if (s_arrays.Value.TryGetValue(builtInType, out TypeInfo arrayTypeInfo))
            {
                return arrayTypeInfo;
            }
            return new TypeInfo(builtInType, ValueRanks.OneDimension);
        }

        /// <summary>
        /// Constants for one dimensional array types.
        /// </summary>
        public static class Arrays
        {
            /// <summary>
            /// A boolean logic value (true or false).
            /// </summary>
            public static readonly TypeInfo Boolean = new(
                BuiltInType.Boolean,
                ValueRanks.OneDimension);

            /// <summary>
            /// An 8 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo SByte = new(BuiltInType.SByte, ValueRanks.OneDimension);

            /// <summary>
            /// An 8 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo Byte = new(BuiltInType.Byte, ValueRanks.OneDimension);

            /// <summary>
            /// A 16 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int16 = new(BuiltInType.Int16, ValueRanks.OneDimension);

            /// <summary>
            /// A 16 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo UInt16 = new(
                BuiltInType.UInt16,
                ValueRanks.OneDimension);

            /// <summary>
            /// A 32 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int32 = new(BuiltInType.Int32, ValueRanks.OneDimension);

            /// <summary>
            /// A 32 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo UInt32 = new(
                BuiltInType.UInt32,
                ValueRanks.OneDimension);

            /// <summary>
            /// A 64 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int64 = new(BuiltInType.Int64, ValueRanks.OneDimension);

            /// <summary>
            /// A 64 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo UInt64 = new(
                BuiltInType.UInt64,
                ValueRanks.OneDimension);

            /// <summary>
            /// An IEEE single precision (32 bit) floating point value.
            /// </summary>
            public static readonly TypeInfo Float = new(BuiltInType.Float, ValueRanks.OneDimension);

            /// <summary>
            /// An IEEE double precision (64 bit) floating point value.
            /// </summary>
            public static readonly TypeInfo Double = new(
                BuiltInType.Double,
                ValueRanks.OneDimension);

            /// <summary>
            /// A sequence of Unicode characters.
            /// </summary>
            public static readonly TypeInfo String = new(
                BuiltInType.String,
                ValueRanks.OneDimension);

            /// <summary>
            /// An instance in time.
            /// </summary>
            public static readonly TypeInfo DateTime = new(
                BuiltInType.DateTime,
                ValueRanks.OneDimension);

            /// <summary>
            /// A 128-bit globally unique identifier.
            /// </summary>
            public static readonly TypeInfo Guid = new(BuiltInType.Guid, ValueRanks.OneDimension);

            /// <summary>
            /// A sequence of bytes.
            /// </summary>
            public static readonly TypeInfo ByteString = new(
                BuiltInType.ByteString,
                ValueRanks.OneDimension);

            /// <summary>
            /// An XML element.
            /// </summary>
            public static readonly TypeInfo XmlElement = new(
                BuiltInType.XmlElement,
                ValueRanks.OneDimension);

            /// <summary>
            /// An identifier for a node in the address space of a UA server.
            /// </summary>
            public static readonly TypeInfo NodeId = new(
                BuiltInType.NodeId,
                ValueRanks.OneDimension);

            /// <summary>
            /// A node id that stores the namespace URI instead of the namespace index.
            /// </summary>
            public static readonly TypeInfo ExpandedNodeId = new(
                BuiltInType.ExpandedNodeId,
                ValueRanks.OneDimension);

            /// <summary>
            /// A structured result code.
            /// </summary>
            public static readonly TypeInfo StatusCode = new(
                BuiltInType.StatusCode,
                ValueRanks.OneDimension);

            /// <summary>
            /// A string qualified with a namespace.
            /// </summary>
            public static readonly TypeInfo QualifiedName = new(
                BuiltInType.QualifiedName,
                ValueRanks.OneDimension);

            /// <summary>
            /// A localized text string with an locale identifier.
            /// </summary>
            public static readonly TypeInfo LocalizedText = new(
                BuiltInType.LocalizedText,
                ValueRanks.OneDimension);

            /// <summary>
            /// An opaque object with a syntax that may be unknown to the receiver.
            /// </summary>
            public static readonly TypeInfo ExtensionObject = new(
                BuiltInType.ExtensionObject,
                ValueRanks.OneDimension);

            /// <summary>
            /// A data value with an associated quality and timestamp.
            /// </summary>
            public static readonly TypeInfo DataValue = new(
                BuiltInType.DataValue,
                ValueRanks.OneDimension);

            /// <summary>
            /// Any of the other built-in types.
            /// </summary>
            public static readonly TypeInfo Variant = new(
                BuiltInType.Variant,
                ValueRanks.OneDimension);

            /// <summary>
            /// A diagnostic information associated with a result code.
            /// </summary>
            public static readonly TypeInfo DiagnosticInfo = new(
                BuiltInType.DiagnosticInfo,
                ValueRanks.OneDimension);

            /// <summary>
            /// An array of enum values.
            /// </summary>
            public static readonly TypeInfo Enumeration = new(
                BuiltInType.Enumeration,
                ValueRanks.OneDimension);
        }

        /// <summary>
        /// The on demand look up table for one dimensional arrays.
        /// </summary>
        private static readonly Lazy<IReadOnlyDictionary<BuiltInType, TypeInfo>> s_arrays =
            new(() =>
            {
                FieldInfo[] fields = typeof(Arrays).GetFields(
                    BindingFlags.Public | BindingFlags.Static);

                var keyValuePairs = new Dictionary<BuiltInType, TypeInfo>();
                foreach (FieldInfo field in fields)
                {
                    var typeInfo = (TypeInfo)field.GetValue(typeof(TypeInfo));
                    keyValuePairs.Add(typeInfo.BuiltInType, typeInfo);
                }
#if NET8_0_OR_GREATER
                return keyValuePairs.ToFrozenDictionary();
#else
                return new ReadOnlyDictionary<BuiltInType, TypeInfo>(keyValuePairs);
#endif
            });

        /// <summary>
        /// Formats the type information as a string.
        /// </summary>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Formats the type information as a string.
        /// </summary>
        /// <exception cref="FormatException"></exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                var buffer = new System.Text.StringBuilder();
                buffer.Append(BuiltInType.ToString());

                if (ValueRank >= 0)
                {
                    buffer.Append('[');

                    for (int ii = 1; ii < ValueRank; ii++)
                    {
                        buffer.Append(',');
                    }

                    buffer.Append(']');
                }

                return buffer.ToString();
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Determines if the specified object is equal to the object.
        /// </summary>
        /// <remarks>
        /// Determines if the specified object is equal to the object.
        /// </remarks>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj is TypeInfo typeInfo)
            {
                return BuiltInType == typeInfo.BuiltInType && ValueRank == typeInfo.ValueRank;
            }

            return false;
        }

        /// <summary>
        /// Returns a suitable hash code.
        /// </summary>
        public override int GetHashCode()
        {
            return HashCode.Combine(BuiltInType, ValueRank);
        }
    }
}
