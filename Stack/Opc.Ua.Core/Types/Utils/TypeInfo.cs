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
using System.Reflection;
using System.Xml;

namespace Opc.Ua
{
    /// <summary>
    /// The set of built-in data types for UA type descriptions.
    /// </summary>
    /// <remarks>
    /// An enumeration that lists all of the built-in data types for OPC UA Type Descriptions.
    /// </remarks>
    public enum BuiltInType : int
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

        /// <remarks>
        /// The following BuiltInTypes are for coding convenience
        /// internally used in the .NET Standard library.
        /// The enumerations are not used for encoding/decoding.
        /// </remarks>

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
    public class TypeInfo : IFormattable
    {
        #region Constructors
        /// <summary>
        /// Constructs an unknown type.
        /// </summary>
        internal TypeInfo()
        {
            m_builtInType = BuiltInType.Null;
            m_valueRank = ValueRanks.Any;
        }

        /// <summary>
        /// Construct the object with a built-in type and a value rank.
        /// </summary>
        /// <param name="builtInType">Type of the built in.</param>
        /// <param name="valueRank">The value rank.</param>
        public TypeInfo(BuiltInType builtInType, int valueRank)
        {
            m_builtInType = builtInType;
            m_valueRank = valueRank;
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Returns the data type id that describes a value.
        /// </summary>
        /// <param name="value">The value instance to check the data type.</param>
        /// <returns>An data type identifier for a node in a server's address space.</returns>
        public static NodeId GetDataTypeId(object value)
        {
            if (value == null)
            {
                return Opc.Ua.NodeId.Null;
            }

            NodeId dataTypeId = GetDataTypeId(value.GetType());

            if (dataTypeId == Opc.Ua.NodeId.Null)
            {
                Matrix matrix = value as Matrix;

                if (matrix != null)
                {
                    return GetDataTypeId(matrix.TypeInfo);
                }
            }

            return dataTypeId;
        }

        /// <summary>
        /// Returns the data type id that describes a value.
        /// </summary>
        /// <param name="type">The framework type.</param>
        /// <returns>An data type identifier for a node in a server's address space.</returns>
        public static NodeId GetDataTypeId(Type type)
        {
            TypeInfo typeInfo = TypeInfo.Construct(type);

            NodeId dataTypeId = GetDataTypeId(typeInfo);

            if (Opc.Ua.NodeId.IsNull(dataTypeId))
            {
                if (type.GetTypeInfo().IsEnum || (type.IsArray && type.GetElementType().GetTypeInfo().IsEnum))
                {
                    return DataTypes.Enumeration;
                }
            }

            return dataTypeId;
        }

        /// <summary>
        /// Returns the data type id that describes a value.
        /// </summary>
        /// <param name="typeInfo">The type info.</param>
        /// <returns>An data type identifier for a node in a server's address space.</returns>
        public static NodeId GetDataTypeId(TypeInfo typeInfo)
        {
            switch (typeInfo.BuiltInType)
            {
                case BuiltInType.Boolean: { return DataTypeIds.Boolean; }
                case BuiltInType.SByte: { return DataTypeIds.SByte; }
                case BuiltInType.Byte: { return DataTypeIds.Byte; }
                case BuiltInType.Int16: { return DataTypeIds.Int16; }
                case BuiltInType.UInt16: { return DataTypeIds.UInt16; }
                case BuiltInType.Int32: { return DataTypeIds.Int32; }
                case BuiltInType.UInt32: { return DataTypeIds.UInt32; }
                case BuiltInType.Int64: { return DataTypeIds.Int64; }
                case BuiltInType.UInt64: { return DataTypeIds.UInt64; }
                case BuiltInType.Float: { return DataTypeIds.Float; }
                case BuiltInType.Double: { return DataTypeIds.Double; }
                case BuiltInType.String: { return DataTypeIds.String; }
                case BuiltInType.DateTime: { return DataTypeIds.DateTime; }
                case BuiltInType.Guid: { return DataTypeIds.Guid; }
                case BuiltInType.ByteString: { return DataTypeIds.ByteString; }
                case BuiltInType.XmlElement: { return DataTypeIds.XmlElement; }
                case BuiltInType.NodeId: { return DataTypeIds.NodeId; }
                case BuiltInType.ExpandedNodeId: { return DataTypeIds.ExpandedNodeId; }
                case BuiltInType.StatusCode: { return DataTypeIds.StatusCode; }
                case BuiltInType.DiagnosticInfo: { return DataTypeIds.DiagnosticInfo; }
                case BuiltInType.QualifiedName: { return DataTypeIds.QualifiedName; }
                case BuiltInType.LocalizedText: { return DataTypeIds.LocalizedText; }
                case BuiltInType.ExtensionObject: { return DataTypeIds.Structure; }
                case BuiltInType.DataValue: { return DataTypeIds.DataValue; }
                case BuiltInType.Variant: { return DataTypeIds.BaseDataType; }
                case BuiltInType.Number: { return DataTypeIds.Number; }
                case BuiltInType.Integer: { return DataTypeIds.Integer; }
                case BuiltInType.UInteger: { return DataTypeIds.UInteger; }
                case BuiltInType.Enumeration: { return DataTypeIds.Enumeration; }
            }

            return Opc.Ua.NodeId.Null;
        }

        /// <summary>
        /// Returns the array rank for a value.
        /// </summary>
        /// <param name="value">The value instance to check the array rank.</param>
        /// <returns>The array rank of the <paramref name="value"/></returns>
        public static int GetValueRank(object value)
        {
            if (value == null)
            {
                return ValueRanks.Any;
            }

            TypeInfo typeInfo = TypeInfo.Construct(value);

            if (typeInfo.BuiltInType == BuiltInType.Null)
            {
                Matrix matrix = value as Matrix;

                if (matrix != null)
                {
                    return matrix.TypeInfo.ValueRank;
                }
            }

            return typeInfo.ValueRank;
        }

        /// <summary>
        /// Returns the array rank for a type.
        /// </summary>
        /// <param name="type">The framework type to check the array rank.</param>
        /// <returns>The array rank of the <paramref name="type"/> </returns>
        public static int GetValueRank(Type type)
        {
            TypeInfo typeInfo = TypeInfo.Construct(type);

            if (typeInfo.BuiltInType == BuiltInType.Null)
            {
                if (type.GetTypeInfo().IsEnum || (type.IsArray && type.GetElementType().GetTypeInfo().IsEnum))
                {
                    if (type.IsArray)
                    {
                        return ValueRanks.OneOrMoreDimensions;
                    }
                    else
                    {
                        return ValueRanks.Scalar;
                    }
                }
            }

            return typeInfo.ValueRank;
        }

        /// <summary>
        /// Returns the BuiltInType type for the DataTypeId.
        /// </summary>
        /// <param name="datatypeId">The data type identifier.</param>
        /// <returns>An <see cref="BuiltInType"/> for  <paramref name="datatypeId"/></returns>
        public static BuiltInType GetBuiltInType(NodeId datatypeId)
        {
            if (datatypeId == null || datatypeId.NamespaceIndex != 0 || datatypeId.IdType != Opc.Ua.IdType.Numeric)
            {
                return BuiltInType.Null;
            }
            switch ((uint)datatypeId.Identifier)
            {
                // subtype of DateTime
                case DataTypes.UtcTime: return BuiltInType.DateTime;
                // subtype of ByteString
                case DataTypes.ApplicationInstanceCertificate:
                case DataTypes.AudioDataType:
                case DataTypes.ContinuationPoint:
                case DataTypes.Image:
                case DataTypes.ImageBMP:
                case DataTypes.ImageGIF:
                case DataTypes.ImageJPG:
                case DataTypes.ImagePNG: return BuiltInType.ByteString;
                // subtype of NodeId
                case DataTypes.SessionAuthenticationToken: return BuiltInType.NodeId;
                // subtype of Double
                case DataTypes.Duration: return BuiltInType.Double;
                // subtype of UInt32
                case DataTypes.IntegerId:
                case DataTypes.Index:
                case DataTypes.VersionTime:
                case DataTypes.Counter: return BuiltInType.UInt32;
                // subtype of UInt64
                case DataTypes.BitFieldMaskDataType: return BuiltInType.UInt64;
                // subtype of String
                case DataTypes.DateString:
                case DataTypes.DecimalString:
                case DataTypes.DurationString:
                case DataTypes.LocaleId:
                case DataTypes.NormalizedString:
                case DataTypes.NumericRange:
                case DataTypes.TimeString: return BuiltInType.String;
            }

            BuiltInType builtInType = (BuiltInType)Enum.ToObject(typeof(BuiltInType), datatypeId.Identifier);

            if (builtInType > BuiltInType.DiagnosticInfo && builtInType != BuiltInType.Enumeration)
            {
                return BuiltInType.Null;
            }

            return builtInType;
        }

        /// <summary>
        /// Returns true if the built-in type is a numeric type.
        /// </summary>
        /// <param name="builtInType">The built-in type to check.</param>
        /// <returns>
        /// True if the built-in type is a numeric type.
        /// </returns>
        public static bool IsNumericType(BuiltInType builtInType)
        {
            if (builtInType >= BuiltInType.SByte && builtInType <= BuiltInType.Double)
            {
                return true;
            }

            if (builtInType >= BuiltInType.Number && builtInType <= BuiltInType.UInteger)
            {
                return true;
            }

            return false;
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
            if (builtInType >= BuiltInType.Boolean && builtInType <= BuiltInType.Double)
            {
                return true;
            }

            if (builtInType == BuiltInType.DateTime || builtInType == BuiltInType.Guid || builtInType == BuiltInType.StatusCode)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns true if a 'null' value exists for the built-in type
        /// in all data encodings.
        /// </summary>
        /// <param name="builtInType">The built in type to check.</param>
        /// <returns>
        /// True if the built-in type is a type that is nullable.
        /// </returns>
        public static bool IsEncodingNullableType(BuiltInType builtInType)
        {
            if (builtInType >= BuiltInType.Boolean && builtInType <= BuiltInType.Double)
            {
                return false;
            }

            if (builtInType == BuiltInType.DataValue || builtInType == BuiltInType.DiagnosticInfo)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns the BuiltInType type for the DataTypeId.
        /// </summary>
        /// <param name="datatypeId">The data type identyfier for a node in a server's address space..</param>
        /// <param name="typeTree">The type tree for a server. .</param>
        /// <returns>
        /// A <see cref="BuiltInType"/> value for <paramref name="datatypeId"/>
        /// </returns>
        public static BuiltInType GetBuiltInType(NodeId datatypeId, ITypeTable typeTree)
        {
            NodeId typeId = datatypeId;

            while (!Opc.Ua.NodeId.IsNull(typeId))
            {
                if (typeId != null && typeId.NamespaceIndex == 0 && typeId.IdType == Opc.Ua.IdType.Numeric)
                {
                    BuiltInType id = (BuiltInType)(int)(uint)typeId.Identifier;

                    if (id > BuiltInType.Null && id <= BuiltInType.Enumeration && id != BuiltInType.DiagnosticInfo)
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

        /// <summary>
        /// Returns the system type for the datatype.
        /// </summary>
        /// <param name="datatypeId">The datatype id.</param>
        /// <param name="factory">The factory used to store and retrieve underlying OPC UA system types.</param>
        /// <returns>The system type for the <paramref name="datatypeId"/>.</returns>
        public static Type GetSystemType(ExpandedNodeId datatypeId, IEncodeableFactory factory)
        {
            if (datatypeId == null)
            {
                return null;
            }

            if (datatypeId.NamespaceIndex != 0 || datatypeId.IdType != Opc.Ua.IdType.Numeric || datatypeId.IsAbsolute)
            {
                return factory.GetSystemType(datatypeId);
            }

            switch ((uint)datatypeId.Identifier)
            {
                case DataTypes.Boolean: { return typeof(Boolean); }
                case DataTypes.SByte: { return typeof(SByte); }
                case DataTypes.Byte: { return typeof(Byte); }
                case DataTypes.Int16: { return typeof(Int16); }
                case DataTypes.UInt16: { return typeof(UInt16); }
                case DataTypes.Int32: { return typeof(Int32); }
                case DataTypes.UInt32: { return typeof(UInt32); }
                case DataTypes.Int64: { return typeof(Int64); }
                case DataTypes.UInt64: { return typeof(UInt64); }
                case DataTypes.Float: { return typeof(Single); }
                case DataTypes.Double: { return typeof(Double); }
                case DataTypes.String: { return typeof(String); }
                case DataTypes.DateTime: { return typeof(DateTime); }
                case DataTypes.Guid: { return typeof(Uuid); }
                case DataTypes.ByteString: { return typeof(Byte[]); }
                case DataTypes.XmlElement: { return typeof(XmlElement); }
                case DataTypes.NodeId: { return typeof(NodeId); }
                case DataTypes.ExpandedNodeId: { return typeof(ExpandedNodeId); }
                case DataTypes.StatusCode: { return typeof(StatusCode); }
                case DataTypes.DiagnosticInfo: { return typeof(DiagnosticInfo); }
                case DataTypes.QualifiedName: { return typeof(QualifiedName); }
                case DataTypes.LocalizedText: { return typeof(LocalizedText); }
                case DataTypes.DataValue: { return typeof(DataValue); }
                case DataTypes.BaseDataType: { return typeof(Variant); }
                case DataTypes.Structure: { return typeof(ExtensionObject); }
                case DataTypes.Number: { return typeof(Variant); }
                case DataTypes.Integer: { return typeof(Variant); }
                case DataTypes.UInteger: { return typeof(Variant); }
                case DataTypes.Enumeration: { return typeof(Int32); }

                // subtype of DateTime
                case DataTypes.UtcTime: goto case DataTypes.DateTime;
                // subtype of ByteString
                case DataTypes.ApplicationInstanceCertificate:
                case DataTypes.AudioDataType:
                case DataTypes.ContinuationPoint:
                case DataTypes.Image:
                case DataTypes.ImageBMP:
                case DataTypes.ImageGIF:
                case DataTypes.ImageJPG:
                case DataTypes.ImagePNG: goto case DataTypes.ByteString;
                // subtype of NodeId
                case DataTypes.SessionAuthenticationToken: goto case DataTypes.NodeId;
                // subtype of Double
                case DataTypes.Duration: goto case DataTypes.Double;
                // subtype of UInt32
                case DataTypes.IntegerId:
                case DataTypes.Index:
                case DataTypes.VersionTime:
                case DataTypes.Counter: goto case DataTypes.UInt32;
                // subtype of UInt64
                case DataTypes.BitFieldMaskDataType: goto case DataTypes.UInt64;
                // subtype of String
                case DataTypes.DateString:
                case DataTypes.DecimalString:
                case DataTypes.DurationString:
                case DataTypes.LocaleId:
                case DataTypes.NormalizedString:
                case DataTypes.NumericRange:
                case DataTypes.TimeString: goto case DataTypes.String;
            }

            return factory.GetSystemType(datatypeId);
        }

        /// <summary>
        /// A constant representing an unknown type.
        /// </summary>
        /// <value>The constant representing an unknown type.</value>
        public static TypeInfo Unknown => s_Unknown;

        /// <summary>
        /// The built-in type.
        /// </summary>
        /// <value>The type of the type represented by this instance.</value>
        public BuiltInType BuiltInType => m_builtInType;

        /// <summary>
        /// The value rank.
        /// </summary>
        /// <value>The value rank of the type represented by this instance.</value>
        public int ValueRank => m_valueRank;

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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public static TypeInfo IsInstanceOfDataType(
            object value,
            NodeId expectedDataTypeId,
            int expectedValueRank,
            NamespaceTable namespaceUris,
            ITypeTable typeTree)
        {
            BuiltInType expectedType = BuiltInType.Null;

            // get the type info.
            TypeInfo typeInfo = Construct(value);

            if (typeInfo.BuiltInType == BuiltInType.Null)
            {
                expectedType = TypeInfo.GetBuiltInType(expectedDataTypeId, typeTree);

                // nulls allowed for all array types.
                if (expectedValueRank != ValueRanks.Scalar)
                {
                    return new TypeInfo(expectedType, ValueRanks.OneDimension);
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
                    {
                        return new TypeInfo(expectedType, ValueRanks.Scalar);
                    }
                }

                // nulls not allowed.
                return null;
            }

            // A ByteString is equivalent to an Array of Bytes.
            if (typeInfo.BuiltInType == BuiltInType.ByteString && typeInfo.ValueRank == ValueRanks.Scalar)
            {
                if (expectedValueRank == ValueRanks.OneOrMoreDimensions || expectedValueRank == ValueRanks.OneDimension)
                {
                    if (typeTree.IsTypeOf(expectedDataTypeId, DataTypes.Byte))
                    {
                        return typeInfo;
                    }

                    return null;
                }
            }

            // check the value rank.
            if (!ValueRanks.IsValid(typeInfo.ValueRank, expectedValueRank))
            {
                return null;
            }

            // check for special predefined types.
            if (expectedDataTypeId.IdType == IdType.Numeric && expectedDataTypeId.NamespaceIndex == 0)
            {
                BuiltInType actualType = typeInfo.BuiltInType;

                switch ((uint)expectedDataTypeId.Identifier)
                {
                    case DataTypes.Number:
                    {
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
                            {
                                return typeInfo;
                            }

                            case BuiltInType.Variant:
                            {
                                if (typeInfo.ValueRank == ValueRanks.Scalar)
                                {
                                    return null;
                                }

                                break;
                            }

                            default:
                            {
                                return null;
                            }
                        }

                        break;
                    }

                    case DataTypes.Integer:
                    {
                        switch (actualType)
                        {
                            case BuiltInType.SByte:
                            case BuiltInType.Int16:
                            case BuiltInType.Int32:
                            case BuiltInType.Int64:
                            {
                                return typeInfo;
                            }

                            case BuiltInType.Variant:
                            {
                                if (typeInfo.ValueRank == ValueRanks.Scalar)
                                {
                                    return null;
                                }

                                break;
                            }

                            default:
                            {
                                return null;
                            }
                        }

                        break;
                    }

                    case DataTypes.UInteger:
                    {
                        switch (actualType)
                        {
                            case BuiltInType.Byte:
                            case BuiltInType.UInt16:
                            case BuiltInType.UInt32:
                            case BuiltInType.UInt64:
                            {
                                return typeInfo;
                            }

                            case BuiltInType.Variant:
                            {
                                if (typeInfo.ValueRank == ValueRanks.Scalar)
                                {
                                    return null;
                                }

                                break;
                            }

                            default:
                            {
                                return null;
                            }
                        }

                        break;
                    }

                    case DataTypes.Enumeration:
                    {
                        if (typeInfo.BuiltInType == BuiltInType.Int32)
                        {
                            return typeInfo;
                        }

                        return null;
                    }

                    case DataTypes.Structure:
                    {
                        if (typeInfo.BuiltInType == BuiltInType.ExtensionObject)
                        {
                            return typeInfo;
                        }

                        return null;
                    }

                    case DataTypes.BaseDataType:
                    {
                        if (typeInfo.BuiltInType != BuiltInType.Variant)
                        {
                            return typeInfo;
                        }

                        break;
                    }
                }
            }

            // check simple types.
            if (typeInfo.BuiltInType != BuiltInType.ExtensionObject && typeInfo.BuiltInType != BuiltInType.Variant)
            {
                if (typeTree.IsTypeOf(expectedDataTypeId, new NodeId((uint)(int)typeInfo.BuiltInType)))
                {
                    return typeInfo;
                }

                // check for enumerations.
                if (typeInfo.BuiltInType == BuiltInType.Int32)
                {
                    if (typeTree.IsTypeOf(expectedDataTypeId, DataTypes.Enumeration))
                    {
                        return typeInfo;
                    }
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
            Array array = value as Array;
            if (array == null)
            {
                Matrix matrix = value as Matrix;
                if (matrix != null)
                {
                    array = matrix.Elements;
                }
            }

            if (array != null)
            {
                BuiltInType expectedElementType = TypeInfo.GetBuiltInType(expectedDataTypeId, typeTree);
                BuiltInType actualElementType = GetBuiltInType(array.GetType().GetElementType().Name);
                // system type of array matches the expected type - nothing more to do.
                if (actualElementType != BuiltInType.ExtensionObject && actualElementType == expectedElementType)
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
                        indexes[jj] = (ii / divisor) % dimensions[jj];
                    }

                    object element = array.GetValue(indexes);

                    if (actualElementType == BuiltInType.Variant)
                    {
                        element = ((Variant)element).Value;
                    }

                    TypeInfo elementInfo = TypeInfo.IsInstanceOfDataType(
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
                IEncodeable encodeable = value as IEncodeable;
                if (encodeable != null)
                {
                    return ExpandedNodeId.ToNodeId(encodeable.TypeId, namespaceUris);
                }

                ExtensionObject extension = value as ExtensionObject;
                if (extension != null)
                {
                    encodeable = extension.Body as IEncodeable;
                    if (encodeable != null)
                    {
                        return ExpandedNodeId.ToNodeId(encodeable.TypeId, namespaceUris);
                    }

                    return typeTree.FindDataTypeId(extension.TypeId);
                }

                return DataTypes.Structure;
            }

            return new NodeId((uint)(int)BuiltInType);
        }

        /// <summary>
        /// Returns the system type a scalar or array instance of the built-in type.
        /// </summary>
        /// <param name="builtInType">A built-in type.</param>
        /// <param name="valueRank">The value rank.</param>
        /// <returns>A system type equivalent to the built-in type.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public static Type GetSystemType(BuiltInType builtInType, int valueRank)
        {
            if (valueRank == ValueRanks.Scalar)
            {
                switch (builtInType)
                {
                    case BuiltInType.Boolean: return typeof(bool);
                    case BuiltInType.SByte: return typeof(sbyte);
                    case BuiltInType.Byte: return typeof(byte);
                    case BuiltInType.Int16: return typeof(short);
                    case BuiltInType.UInt16: return typeof(ushort);
                    case BuiltInType.Int32: return typeof(int);
                    case BuiltInType.UInt32: return typeof(uint);
                    case BuiltInType.Int64: return typeof(long);
                    case BuiltInType.UInt64: return typeof(ulong);
                    case BuiltInType.Float: return typeof(float);
                    case BuiltInType.Double: return typeof(double);
                    case BuiltInType.String: return typeof(string);
                    case BuiltInType.DateTime: return typeof(DateTime);
                    case BuiltInType.Guid: return typeof(Uuid);
                    case BuiltInType.ByteString: return typeof(byte[]);
                    case BuiltInType.XmlElement: return typeof(XmlElement);
                    case BuiltInType.NodeId: return typeof(NodeId);
                    case BuiltInType.ExpandedNodeId: return typeof(ExpandedNodeId);
                    case BuiltInType.LocalizedText: return typeof(LocalizedText);
                    case BuiltInType.QualifiedName: return typeof(QualifiedName);
                    case BuiltInType.StatusCode: return typeof(StatusCode);
                    case BuiltInType.DiagnosticInfo: return typeof(DiagnosticInfo);
                    case BuiltInType.DataValue: return typeof(DataValue);
                    case BuiltInType.Variant: return typeof(Variant);
                    case BuiltInType.ExtensionObject: return typeof(ExtensionObject);
                    case BuiltInType.Enumeration: return typeof(int);
                    case BuiltInType.Number: return typeof(Variant);
                    case BuiltInType.Integer: return typeof(Variant);
                    case BuiltInType.UInteger: return typeof(Variant);
                }
            }

            else if (valueRank == ValueRanks.OneDimension)
            {
                switch (builtInType)
                {
                    case BuiltInType.Boolean: return typeof(bool[]);
                    case BuiltInType.SByte: return typeof(sbyte[]);
                    case BuiltInType.Byte: return typeof(byte[]);
                    case BuiltInType.Int16: return typeof(short[]);
                    case BuiltInType.UInt16: return typeof(ushort[]);
                    case BuiltInType.Int32: return typeof(int[]);
                    case BuiltInType.UInt32: return typeof(uint[]);
                    case BuiltInType.Int64: return typeof(long[]);
                    case BuiltInType.UInt64: return typeof(ulong[]);
                    case BuiltInType.Float: return typeof(float[]);
                    case BuiltInType.Double: return typeof(double[]);
                    case BuiltInType.String: return typeof(string[]);
                    case BuiltInType.DateTime: return typeof(DateTime[]);
                    case BuiltInType.Guid: return typeof(Uuid[]);
                    case BuiltInType.ByteString: return typeof(byte[][]);
                    case BuiltInType.XmlElement: return typeof(XmlElement[]);
                    case BuiltInType.NodeId: return typeof(NodeId[]);
                    case BuiltInType.ExpandedNodeId: return typeof(ExpandedNodeId[]);
                    case BuiltInType.LocalizedText: return typeof(LocalizedText[]);
                    case BuiltInType.QualifiedName: return typeof(QualifiedName[]);
                    case BuiltInType.StatusCode: return typeof(StatusCode[]);
                    case BuiltInType.DiagnosticInfo: return typeof(DiagnosticInfo[]);
                    case BuiltInType.DataValue: return typeof(DataValue[]);
                    case BuiltInType.Variant: return typeof(Variant[]);
                    case BuiltInType.ExtensionObject: return typeof(ExtensionObject[]);
                    case BuiltInType.Enumeration: return typeof(int[]);
                    case BuiltInType.Number: return typeof(Variant[]);
                    case BuiltInType.Integer: return typeof(Variant[]);
                    case BuiltInType.UInteger: return typeof(Variant[]);
                }
            }

            else if (valueRank >= ValueRanks.TwoDimensions)
            {
                switch (builtInType)
                {
                    case BuiltInType.Boolean: return typeof(bool).MakeArrayType(valueRank);
                    case BuiltInType.SByte: return typeof(sbyte).MakeArrayType(valueRank);
                    case BuiltInType.Byte: return typeof(byte).MakeArrayType(valueRank);
                    case BuiltInType.Int16: return typeof(short).MakeArrayType(valueRank);
                    case BuiltInType.UInt16: return typeof(ushort).MakeArrayType(valueRank);
                    case BuiltInType.Int32: return typeof(int).MakeArrayType(valueRank);
                    case BuiltInType.UInt32: return typeof(uint).MakeArrayType(valueRank);
                    case BuiltInType.Int64: return typeof(long).MakeArrayType(valueRank);
                    case BuiltInType.UInt64: return typeof(ulong).MakeArrayType(valueRank);
                    case BuiltInType.Float: return typeof(float).MakeArrayType(valueRank);
                    case BuiltInType.Double: return typeof(double).MakeArrayType(valueRank);
                    case BuiltInType.String: return typeof(string).MakeArrayType(valueRank);
                    case BuiltInType.DateTime: return typeof(DateTime).MakeArrayType(valueRank);
                    case BuiltInType.Guid: return typeof(Uuid).MakeArrayType(valueRank);
                    case BuiltInType.ByteString: return typeof(byte[]).MakeArrayType(valueRank);
                    case BuiltInType.XmlElement: return typeof(XmlElement).MakeArrayType(valueRank);
                    case BuiltInType.NodeId: return typeof(NodeId).MakeArrayType(valueRank);
                    case BuiltInType.ExpandedNodeId: return typeof(ExpandedNodeId).MakeArrayType(valueRank);
                    case BuiltInType.LocalizedText: return typeof(LocalizedText).MakeArrayType(valueRank);
                    case BuiltInType.QualifiedName: return typeof(QualifiedName).MakeArrayType(valueRank);
                    case BuiltInType.StatusCode: return typeof(StatusCode).MakeArrayType(valueRank);
                    case BuiltInType.DiagnosticInfo: return typeof(DiagnosticInfo).MakeArrayType(valueRank);
                    case BuiltInType.DataValue: return typeof(DataValue).MakeArrayType(valueRank);
                    case BuiltInType.Variant: return typeof(Variant).MakeArrayType(valueRank);
                    case BuiltInType.ExtensionObject: return typeof(ExtensionObject).MakeArrayType(valueRank);
                    case BuiltInType.Enumeration: return typeof(int).MakeArrayType(valueRank);
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger: return typeof(Variant).MakeArrayType(valueRank);
                }
            }

            return typeof(Variant);
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
                return TypeInfo.Unknown;
            }

            TypeInfo typeInfo = Construct(value.GetType());

            // check for instances of matrices.
            if (typeInfo.BuiltInType == BuiltInType.Null)
            {
                Matrix matrix = value as Matrix;

                if (matrix != null)
                {
                    return matrix.TypeInfo;
                }
            }

            return typeInfo;
        }

        /// <summary>
        /// Returns the type info for the specified system type.
        /// </summary>
        /// <param name="systemType">The specified system (framework) type.</param>
        /// <returns><see cref="TypeInfo"/> instance storing information equivalent to the <paramref name="systemType"/> type.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public static TypeInfo Construct(Type systemType)
        {
            // check for null.
            if (systemType == null)
            {
                return TypeInfo.Unknown;
            }

            // using strings in switch statements is much faster than the typeof() operator.
            string name = systemType.Name;

            // parse array types.
            string dimensions = null;

            if (name[name.Length - 1] == ']')
            {
                int index = name.IndexOf('[');

                if (index != -1)
                {
                    dimensions = name.Substring(index);
                    name = name.Substring(0, index);
                }
            }

            // handle scalar.
            if (dimensions == null)
            {
                BuiltInType builtInType = GetBuiltInType(name);

                if (builtInType != BuiltInType.Null)
                {
                    return new TypeInfo(builtInType, ValueRanks.Scalar);
                }

                if (systemType.GetTypeInfo().IsEnum)
                {
                    return new TypeInfo(BuiltInType.Enumeration, ValueRanks.Scalar);
                }

                // check for collection.
                if (name.EndsWith("Collection", StringComparison.Ordinal))
                {
                    builtInType = GetBuiltInType(name.Substring(0, name.Length - "Collection".Length));

                    if (builtInType != BuiltInType.Null)
                    {
                        return new TypeInfo(builtInType, ValueRanks.OneDimension);
                    }

                    // check for encodeable object.
                    if (systemType.GetTypeInfo().BaseType.GetTypeInfo().IsGenericType)
                    {
                        return Construct(systemType.GetTypeInfo().BaseType);
                    }

                    return TypeInfo.Unknown;
                }

                // check for generic type.                
                if (systemType.GetTypeInfo().IsGenericType)
                {
                    Type[] argTypes = systemType.GetGenericArguments();

                    if (argTypes != null && argTypes.Length == 1)
                    {
                        TypeInfo typeInfo = Construct(argTypes[0]);

                        if (typeInfo.BuiltInType != BuiltInType.Null && typeInfo.ValueRank == ValueRanks.Scalar)
                        {
                            return new TypeInfo(typeInfo.BuiltInType, ValueRanks.OneDimension);
                        }
                    }

                    return TypeInfo.Unknown;
                }

                // check for encodeable object.
                if (typeof(IEncodeable).GetTypeInfo().IsAssignableFrom(systemType.GetTypeInfo()) || name == "IEncodeable")
                {
                    return new TypeInfo(BuiltInType.ExtensionObject, ValueRanks.Scalar);
                }

                return TypeInfo.Unknown;
            }

            // handle one dimensional array.
            if (dimensions.Length == 2)
            {
                BuiltInType builtInType = GetBuiltInType(name);

                if (builtInType == BuiltInType.Byte)
                {
                    return new TypeInfo(BuiltInType.ByteString, ValueRanks.Scalar);
                }

                if (builtInType != BuiltInType.Null)
                {
                    return new TypeInfo(builtInType, ValueRanks.OneDimension);
                }

                // check for encodeable object.
                if (typeof(IEncodeable).GetTypeInfo().IsAssignableFrom(systemType.GetElementType().GetTypeInfo()) || name == "IEncodeable")
                {
                    return new TypeInfo(BuiltInType.ExtensionObject, ValueRanks.OneDimension);
                }

                if (systemType.GetTypeInfo().GetElementType().IsEnum)
                {
                    return new TypeInfo(BuiltInType.Enumeration, ValueRanks.OneDimension);
                }

                return TypeInfo.Unknown;
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
                    return new TypeInfo(builtInType, count);
                }

                // check for encodeable object.
                if (typeof(IEncodeable).GetTypeInfo().IsAssignableFrom(systemType.GetTypeInfo()) || name == "IEncodeable")
                {
                    return new TypeInfo(BuiltInType.ExtensionObject, count);
                }

                return TypeInfo.Unknown;
            }

            // handle multi-dimensional array of byte strings.
            if (dimensions[1] == ']')
            {
                // syntax of type is [][,,,] - adding three checks for the middle ']['
                if (name == "Byte" && count + 3 == dimensions.Length)
                {
                    return new TypeInfo(BuiltInType.ByteString, count);
                }
            }

            // unknown type.   
            return TypeInfo.Unknown;
        }

        /// <summary>
        /// Returns the default value for the specified built-in type.
        /// </summary>
        /// <param name="type">The Built-in type.</param>
        /// <returns>The default value.</returns>
        public static object GetDefaultValue(BuiltInType type)
        {
            switch (type)
            {
                case BuiltInType.Boolean: { return false; }
                case BuiltInType.SByte: { return (sbyte)0; }
                case BuiltInType.Byte: { return (byte)0; }
                case BuiltInType.Int16: { return (short)0; }
                case BuiltInType.UInt16: { return (ushort)0; }
                case BuiltInType.Int32: { return (int)0; }
                case BuiltInType.UInt32: { return (uint)0; }
                case BuiltInType.Int64: { return (long)0; }
                case BuiltInType.UInt64: { return (ulong)0; }
                case BuiltInType.Float: { return (float)0; }
                case BuiltInType.Double: { return (double)0; }
                case BuiltInType.String: { return null; }
                case BuiltInType.DateTime: { return DateTime.MinValue; }
                case BuiltInType.Guid: { return Uuid.Empty; }
                case BuiltInType.ByteString: { return null; }
                case BuiltInType.XmlElement: { return null; }
                case BuiltInType.StatusCode: { return new StatusCode(StatusCodes.Good); }
                case BuiltInType.NodeId: { return NodeId.Null; }
                case BuiltInType.ExpandedNodeId: { return ExpandedNodeId.Null; }
                case BuiltInType.QualifiedName: { return QualifiedName.Null; }
                case BuiltInType.LocalizedText: { return LocalizedText.Null; }
                case BuiltInType.Variant: { return Variant.Null; }
                case BuiltInType.DataValue: { return null; }
                case BuiltInType.Enumeration: { return (int)0; }
                case BuiltInType.Number: { return (double)0; }
                case BuiltInType.Integer: { return (long)0; }
                case BuiltInType.UInteger: { return (ulong)0; }
            }

            return null;
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

            BuiltInType builtInType = BuiltInType.Null;

            if (dataType != null && dataType.IdType == IdType.Numeric && dataType.NamespaceIndex == 0)
            {
                uint id = (uint)dataType.Identifier;

                // if we want an array then go into a loop
                if (id <= DataTypes.DiagnosticInfo)
                {
                    return GetDefaultValue((BuiltInType)(int)id);
                }

                switch (id)
                {
                    case DataTypes.Duration: { return (double)0; }
                    case DataTypes.UtcTime: { return DateTime.MinValue; }
                    case DataTypes.Counter: { return (uint)0; }
                    case DataTypes.IntegerId: { return (uint)0; }
                    case DataTypes.Number: { return (double)0; }
                    case DataTypes.UInteger: { return (ulong)0; }
                    case DataTypes.Integer: { return (long)0; }
                    case DataTypes.IdType: { return (int)IdType.Numeric; }
                    case DataTypes.NodeClass: { return (int)NodeClass.Unspecified; }
                    case DataTypes.Enumeration: { return (int)0; }
                }
            }

            builtInType = GetBuiltInType(dataType, typeTree);

            if (builtInType != BuiltInType.Null)
            {
                return GetDefaultValue(builtInType);
            }

            return null;
        }

        /// <summary>
        /// Returns the default value for the specified built-in type.
        /// </summary>
        /// <param name="type">The built-in type.</param>
        /// <param name="dimensions">The dimensions.</param>
        /// <returns>The default value for the specified built-in type</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2208:InstantiateArgumentExceptionsCorrectly"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public static Array CreateArray(BuiltInType type, params int[] dimensions)
        {
            if (dimensions == null || dimensions.Length == 0)
            {
                throw new ArgumentOutOfRangeException("Array dimensions must be specifed.");
            }

            int length = dimensions[0];

            // create one dimensional array.
            if (dimensions.Length == 1)
            {
                switch (type)
                {
                    case BuiltInType.Null: { return new object[length]; }
                    case BuiltInType.Boolean: { return new bool[length]; }
                    case BuiltInType.SByte: { return new sbyte[length]; }
                    case BuiltInType.Byte: { return new byte[length]; }
                    case BuiltInType.Int16: { return new short[length]; }
                    case BuiltInType.UInt16: { return new ushort[length]; }
                    case BuiltInType.Int32: { return new int[length]; }
                    case BuiltInType.UInt32: { return new uint[length]; }
                    case BuiltInType.Int64: { return new long[length]; }
                    case BuiltInType.UInt64: { return new ulong[length]; }
                    case BuiltInType.Float: { return new float[length]; }
                    case BuiltInType.Double: { return new double[length]; }
                    case BuiltInType.String: { return new string[length]; }
                    case BuiltInType.DateTime: { return new DateTime[length]; }
                    case BuiltInType.Guid: { return new Uuid[length]; }
                    case BuiltInType.ByteString: { return new byte[length][]; }
                    case BuiltInType.XmlElement: { return new XmlElement[length]; }
                    case BuiltInType.StatusCode: { return new StatusCode[length]; }
                    case BuiltInType.NodeId: { return new NodeId[length]; }
                    case BuiltInType.ExpandedNodeId: { return new ExpandedNodeId[length]; }
                    case BuiltInType.QualifiedName: { return new QualifiedName[length]; }
                    case BuiltInType.LocalizedText: { return new LocalizedText[length]; }
                    case BuiltInType.Variant: { return new Variant[length]; }
                    case BuiltInType.DataValue: { return new DataValue[length]; }
                    case BuiltInType.ExtensionObject: { return new ExtensionObject[length]; }
                    case BuiltInType.DiagnosticInfo: { return new DiagnosticInfo[length]; }
                    case BuiltInType.Enumeration: { return new int[length]; }
                    case BuiltInType.Number: { return new Variant[length]; }
                    case BuiltInType.Integer: { return new Variant[length]; }
                    case BuiltInType.UInteger: { return new Variant[length]; }
                }
            }

            // create higher dimension arrays.
            else
            {
                switch (type)
                {
                    case BuiltInType.Null: { return Array.CreateInstance(typeof(object), dimensions); }
                    case BuiltInType.Boolean: { return Array.CreateInstance(typeof(bool), dimensions); }
                    case BuiltInType.SByte: { return Array.CreateInstance(typeof(sbyte), dimensions); }
                    case BuiltInType.Byte: { return Array.CreateInstance(typeof(byte), dimensions); }
                    case BuiltInType.Int16: { return Array.CreateInstance(typeof(short), dimensions); }
                    case BuiltInType.UInt16: { return Array.CreateInstance(typeof(ushort), dimensions); }
                    case BuiltInType.Int32: { return Array.CreateInstance(typeof(int), dimensions); }
                    case BuiltInType.UInt32: { return Array.CreateInstance(typeof(uint), dimensions); }
                    case BuiltInType.Int64: { return Array.CreateInstance(typeof(long), dimensions); }
                    case BuiltInType.UInt64: { return Array.CreateInstance(typeof(ulong), dimensions); }
                    case BuiltInType.Float: { return Array.CreateInstance(typeof(float), dimensions); }
                    case BuiltInType.Double: { return Array.CreateInstance(typeof(double), dimensions); }
                    case BuiltInType.String: { return Array.CreateInstance(typeof(string), dimensions); }
                    case BuiltInType.DateTime: { return Array.CreateInstance(typeof(DateTime), dimensions); }
                    case BuiltInType.Guid: { return Array.CreateInstance(typeof(Uuid), dimensions); }
                    case BuiltInType.ByteString: { return Array.CreateInstance(typeof(byte[]), dimensions); }
                    case BuiltInType.XmlElement: { return Array.CreateInstance(typeof(XmlElement), dimensions); }
                    case BuiltInType.StatusCode: { return Array.CreateInstance(typeof(StatusCode), dimensions); }
                    case BuiltInType.NodeId: { return Array.CreateInstance(typeof(NodeId), dimensions); }
                    case BuiltInType.ExpandedNodeId: { return Array.CreateInstance(typeof(ExpandedNodeId), dimensions); }
                    case BuiltInType.QualifiedName: { return Array.CreateInstance(typeof(QualifiedName), dimensions); }
                    case BuiltInType.LocalizedText: { return Array.CreateInstance(typeof(LocalizedText), dimensions); }
                    case BuiltInType.Variant: { return Array.CreateInstance(typeof(Variant), dimensions); }
                    case BuiltInType.DataValue: { return Array.CreateInstance(typeof(DataValue), dimensions); }
                    case BuiltInType.ExtensionObject: { return Array.CreateInstance(typeof(ExtensionObject), dimensions); }
                    case BuiltInType.DiagnosticInfo: { return Array.CreateInstance(typeof(DiagnosticInfo), dimensions); }
                    case BuiltInType.Enumeration: { return Array.CreateInstance(typeof(int), dimensions); }
                    case BuiltInType.Number: { return Array.CreateInstance(typeof(Variant), dimensions); }
                    case BuiltInType.Integer: { return Array.CreateInstance(typeof(Variant), dimensions); }
                    case BuiltInType.UInteger: { return Array.CreateInstance(typeof(Variant), dimensions); }
                }
            }

            // should never happen.
            return null;
        }

        /// <summary>
        /// Casts a value to the specified target type.
        /// </summary>
        /// <param name="source">The instance of a source value.</param>
        /// <param name="targetType">Type of the target.</param>
        /// <returns>Return casted value.<see cref="DBNull"/></returns>
        /// <exception cref="InvalidCastException">if imposible to cast.</exception>
        public static object Cast(object source, BuiltInType targetType)
        {
            return Cast(source, TypeInfo.Construct(source), targetType);
        }

        /// <summary>
        /// Casts a value to the specified target type.
        /// </summary>
        /// <param name="source">The instance of a source value.</param>
        /// <param name="sourceType">Type of the source.</param>
        /// <param name="targetType">Type of the target.</param>
        /// <returns>Return casted value.</returns>
        /// <exception cref="InvalidCastException">if imposible to cast.</exception>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        public static object Cast(object source, TypeInfo sourceType, BuiltInType targetType)
        {
            // null always casts to null.
            if (sourceType.BuiltInType == BuiltInType.Null)
            {
                return null;
            }

            // check for trivial case.
            if (sourceType.BuiltInType == targetType)
            {
                return source;
            }

            // check for trivial case.
            if (targetType == BuiltInType.Variant && sourceType.ValueRank < 0)
            {
                return new Variant(source);
            }

            // check for guid.
            if (sourceType.BuiltInType == BuiltInType.Guid)
            {
                source = Cast<Uuid>(source, sourceType, ToGuid);
            }

            switch (targetType)
            {
                case BuiltInType.Boolean: return Cast<bool>(source, sourceType, ToBoolean);
                case BuiltInType.SByte: return Cast<sbyte>(source, sourceType, ToSByte);
                case BuiltInType.Byte: return Cast<byte>(source, sourceType, ToByte);
                case BuiltInType.Int16: return Cast<short>(source, sourceType, ToInt16);
                case BuiltInType.UInt16: return Cast<ushort>(source, sourceType, ToUInt16);
                case BuiltInType.Int32: return Cast<int>(source, sourceType, ToInt32);
                case BuiltInType.UInt32: return Cast<uint>(source, sourceType, ToUInt32);
                case BuiltInType.Int64: return Cast<long>(source, sourceType, ToInt64);
                case BuiltInType.UInt64: return Cast<ulong>(source, sourceType, ToUInt64);
                case BuiltInType.Float: return Cast<float>(source, sourceType, ToFloat);
                case BuiltInType.Double: return Cast<double>(source, sourceType, ToDouble);
                case BuiltInType.String: return Cast<string>(source, sourceType, ToString);
                case BuiltInType.DateTime: return Cast<DateTime>(source, sourceType, ToDateTime);
                case BuiltInType.Guid: return Cast<Uuid>(source, sourceType, ToGuid);
                case BuiltInType.ByteString: return Cast<byte[]>(source, sourceType, ToByteString);
                case BuiltInType.NodeId: return Cast<NodeId>(source, sourceType, ToNodeId);
                case BuiltInType.ExpandedNodeId: return Cast<ExpandedNodeId>(source, sourceType, ToExpandedNodeId);
                case BuiltInType.StatusCode: return Cast<StatusCode>(source, sourceType, ToStatusCode);
                case BuiltInType.QualifiedName: return Cast<QualifiedName>(source, sourceType, ToQualifiedName);
                case BuiltInType.LocalizedText: return Cast<LocalizedText>(source, sourceType, ToLocalizedText);
                case BuiltInType.Variant: return Cast<Variant>(source, sourceType, ToVariant);
                case BuiltInType.Number: return Cast<double>(source, sourceType, ToDouble);
                case BuiltInType.Integer: return Cast<long>(source, sourceType, ToInt64);
                case BuiltInType.UInteger: return Cast<ulong>(source, sourceType, ToUInt64);
                case BuiltInType.Enumeration: return Cast<int>(source, sourceType, ToInt32);
                case BuiltInType.XmlElement: return Cast<XmlElement>(source, sourceType, ToXmlElement);
            }

            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts the array using the specified conversion function.
        /// </summary>
        /// <param name="dst">The destination array (must have the same size as the source array).</param>
        /// <param name="dstType">The data type of the elements in the destination array.</param>
        /// <param name="src">The source array.</param>
        /// <param name="srcType">The data type of the elements in the source array.</param>
        /// <param name="convertor">The handler which does the conversion.</param>
        public static void CastArray(Array dst, BuiltInType dstType, Array src, BuiltInType srcType, CastArrayElementHandler convertor)
        {
            bool isSrcVariant = src.GetType().GetElementType() == typeof(Variant);
            bool isDstVariant = dst.GetType().GetElementType() == typeof(Variant);

            // optimize performance if dealing with a one dimensional array.
            if (src.Rank == 1)
            {
                for (int ii = 0; ii < dst.Length; ii++)
                {
                    object element = src.GetValue(ii);

                    if (isSrcVariant)
                    {
                        element = ((Variant)element).Value;
                    }

                    if (convertor != null)
                    {
                        element = convertor(element, srcType, dstType);
                    }

                    if (isDstVariant)
                    {
                        element = new Variant(element);
                    }

                    dst.SetValue(element, ii);
                }

                return;
            }

            // do it the hard way for multidimensional arrays.
            int[] dimensions = new int[src.Rank];

            for (int ii = 0; ii < dimensions.Length; ii++)
            {
                dimensions[ii] = src.GetLength(ii);
            }

            int length = dst.Length;
            int[] indexes = new int[dimensions.Length];

            for (int ii = 0; ii < length; ii++)
            {
                int divisor = dst.Length;

                for (int jj = 0; jj < indexes.Length; jj++)
                {
                    divisor /= dimensions[jj];
                    indexes[jj] = (ii / divisor) % dimensions[jj];
                }

                object element = src.GetValue(indexes);

                if (element != null)
                {
                    if (isSrcVariant)
                    {
                        element = ((Variant)element).Value;
                    }

                    if (convertor != null)
                    {
                        element = convertor(element, srcType, dstType);
                    }

                    if (isDstVariant)
                    {
                        element = new Variant(element);
                    }

                    dst.SetValue(element, indexes);
                }
            }
        }

        /// <summary>
        /// Converts the array.
        /// </summary>
        /// <param name="srcArray">The source array.</param>
        /// <param name="srcType">The type of the source array.</param>
        /// <param name="dstType">The type of the converted array.</param>
        /// <param name="convertor">The handler which does the conversion.</param>
        /// <returns>The converted array.</returns>
        public static Array CastArray(Array srcArray, BuiltInType srcType, BuiltInType dstType, CastArrayElementHandler convertor)
        {
            if (srcArray == null)
            {
                return null;
            }

            int[] dimensions = new int[srcArray.Rank];

            for (int ii = 0; ii < dimensions.Length; ii++)
            {
                dimensions[ii] = srcArray.GetLength(ii);
            }

            Array dstArray = TypeInfo.CreateArray(dstType, dimensions);
            CastArray(dstArray, dstType, srcArray, srcType, convertor);

            return dstArray;
        }

        /// <summary>
        /// A delegate for a function that converts an array element.
        /// </summary>
        /// <param name="source">The element to be converted.</param>
        /// <param name="srcType">The type of the source element.</param>
        /// <param name="dstType">The type of the converted value.</param>
        /// <returns>The converted</returns>
        public delegate object CastArrayElementHandler(object source, BuiltInType srcType, BuiltInType dstType);
        #endregion

        #region Private Methods
        /// <summary>
        /// Maps the type name to a built-in type.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        private static BuiltInType GetBuiltInType(string typeName)
        {
            switch (typeName)
            {
                case "Boolean": return BuiltInType.Boolean;
                case "SByte": return BuiltInType.SByte;
                case "Byte": return BuiltInType.Byte;
                case "Int16": return BuiltInType.Int16;
                case "UInt16": return BuiltInType.UInt16;
                case "Int32": return BuiltInType.Int32;
                case "UInt32": return BuiltInType.UInt32;
                case "Int64": return BuiltInType.Int64;
                case "UInt64": return BuiltInType.UInt64;
                case "Float": return BuiltInType.Float;
                case "Single": return BuiltInType.Float;
                case "Double": return BuiltInType.Double;
                case "String": return BuiltInType.String;
                case "DateTime": return BuiltInType.DateTime;
                case "Guid": return BuiltInType.Guid;
                case "Uuid": return BuiltInType.Guid;
                case "ByteString": return BuiltInType.ByteString;
                case "XmlElement": return BuiltInType.XmlElement;
                case "NodeId": return BuiltInType.NodeId;
                case "ExpandedNodeId": return BuiltInType.ExpandedNodeId;
                case "LocalizedText": return BuiltInType.LocalizedText;
                case "QualifiedName": return BuiltInType.QualifiedName;
                case "StatusCode": return BuiltInType.StatusCode;
                case "DiagnosticInfo": return BuiltInType.DiagnosticInfo;
                case "DataValue": return BuiltInType.DataValue;
                case "Variant": return BuiltInType.Variant;
                case "ExtensionObject": return BuiltInType.ExtensionObject;
                case "Object": return BuiltInType.Variant;
            }

            return BuiltInType.Null;
        }

        /// <summary>
        /// Converts a value to a Boolean
        /// </summary>
        private static bool ToBoolean(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.Boolean:
                {
                    return (bool)value;
                }

                case BuiltInType.SByte: return Convert.ToBoolean((sbyte)value);
                case BuiltInType.Byte: return Convert.ToBoolean((byte)value);
                case BuiltInType.Int16: return Convert.ToBoolean((short)value);
                case BuiltInType.UInt16: return Convert.ToBoolean((ushort)value);
                case BuiltInType.Int32: return Convert.ToBoolean((int)value);
                case BuiltInType.UInt32: return Convert.ToBoolean((uint)value);
                case BuiltInType.Int64: return Convert.ToBoolean((long)value);
                case BuiltInType.UInt64: return Convert.ToBoolean((ulong)value);
                case BuiltInType.Float: return Convert.ToBoolean((float)value);
                case BuiltInType.Double: return Convert.ToBoolean((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToBoolean((string)value);
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a SByte
        /// </summary>
        private static sbyte ToSByte(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.SByte:
                {
                    return (sbyte)value;
                }

                case BuiltInType.Boolean: return Convert.ToSByte((bool)value);
                case BuiltInType.Byte: return Convert.ToSByte((byte)value);
                case BuiltInType.Int16: return Convert.ToSByte((short)value);
                case BuiltInType.UInt16: return Convert.ToSByte((ushort)value);
                case BuiltInType.Int32: return Convert.ToSByte((int)value);
                case BuiltInType.UInt32: return Convert.ToSByte((uint)value);
                case BuiltInType.Int64: return Convert.ToSByte((long)value);
                case BuiltInType.UInt64: return Convert.ToSByte((ulong)value);
                case BuiltInType.Float: return Convert.ToSByte((float)value);
                case BuiltInType.Double: return Convert.ToSByte((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToSByte((string)value);
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a Byte
        /// </summary>
        private static byte ToByte(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.Byte:
                {
                    return (byte)value;
                }

                case BuiltInType.Boolean: return Convert.ToByte((bool)value);
                case BuiltInType.SByte: return Convert.ToByte((sbyte)value);
                case BuiltInType.Int16: return Convert.ToByte((short)value);
                case BuiltInType.UInt16: return Convert.ToByte((ushort)value);
                case BuiltInType.Int32: return Convert.ToByte((int)value);
                case BuiltInType.UInt32: return Convert.ToByte((uint)value);
                case BuiltInType.Int64: return Convert.ToByte((long)value);
                case BuiltInType.UInt64: return Convert.ToByte((ulong)value);
                case BuiltInType.Float: return Convert.ToByte((float)value);
                case BuiltInType.Double: return Convert.ToByte((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToByte((string)value);
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a Int16
        /// </summary>
        private static short ToInt16(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.Int16:
                {
                    return (short)value;
                }

                case BuiltInType.Boolean: return Convert.ToInt16((bool)value);
                case BuiltInType.SByte: return Convert.ToInt16((sbyte)value);
                case BuiltInType.Byte: return Convert.ToInt16((byte)value);
                case BuiltInType.UInt16: return Convert.ToInt16((ushort)value);
                case BuiltInType.Int32: return Convert.ToInt16((int)value);
                case BuiltInType.UInt32: return Convert.ToInt16((uint)value);
                case BuiltInType.Int64: return Convert.ToInt16((long)value);
                case BuiltInType.UInt64: return Convert.ToInt16((ulong)value);
                case BuiltInType.Float: return Convert.ToInt16((float)value);
                case BuiltInType.Double: return Convert.ToInt16((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToInt16((string)value);
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a UInt16
        /// </summary>
        private static ushort ToUInt16(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.UInt16:
                {
                    return (ushort)value;
                }

                case BuiltInType.Boolean: return Convert.ToUInt16((bool)value);
                case BuiltInType.SByte: return Convert.ToUInt16((sbyte)value);
                case BuiltInType.Byte: return Convert.ToUInt16((byte)value);
                case BuiltInType.Int16: return Convert.ToUInt16((short)value);
                case BuiltInType.Int32: return Convert.ToUInt16((int)value);
                case BuiltInType.UInt32: return Convert.ToUInt16((uint)value);
                case BuiltInType.Int64: return Convert.ToUInt16((long)value);
                case BuiltInType.UInt64: return Convert.ToUInt16((ulong)value);
                case BuiltInType.Float: return Convert.ToUInt16((float)value);
                case BuiltInType.Double: return Convert.ToUInt16((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToUInt16((string)value);
                }

                case BuiltInType.StatusCode:
                {
                    StatusCode code = (StatusCode)value;
                    return (ushort)(code.CodeBits >> 16);
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a Int32
        /// </summary>
        private static int ToInt32(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.Int32:
                {
                    return (int)value;
                }

                case BuiltInType.Boolean: return Convert.ToInt32((bool)value);
                case BuiltInType.SByte: return Convert.ToInt32((sbyte)value);
                case BuiltInType.Byte: return Convert.ToInt32((byte)value);
                case BuiltInType.Int16: return Convert.ToInt32((short)value);
                case BuiltInType.UInt16: return Convert.ToInt32((ushort)value);
                case BuiltInType.UInt32: return Convert.ToInt32((uint)value);
                case BuiltInType.Int64: return Convert.ToInt32((long)value);
                case BuiltInType.UInt64: return Convert.ToInt32((ulong)value);
                case BuiltInType.Float: return Convert.ToInt32((float)value);
                case BuiltInType.Double: return Convert.ToInt32((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToInt32((string)value);
                }

                case BuiltInType.StatusCode:
                {
                    return Convert.ToInt32(((StatusCode)value).Code);
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a UInt32
        /// </summary>
        private static uint ToUInt32(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.UInt32:
                {
                    return (uint)value;
                }

                case BuiltInType.Boolean: return Convert.ToUInt32((bool)value);
                case BuiltInType.SByte: return Convert.ToUInt32((sbyte)value);
                case BuiltInType.Byte: return Convert.ToUInt32((byte)value);
                case BuiltInType.Int16: return Convert.ToUInt32((short)value);
                case BuiltInType.UInt16: return Convert.ToUInt32((ushort)value);
                case BuiltInType.Int32: return Convert.ToUInt32((int)value);
                case BuiltInType.Int64: return Convert.ToUInt32((long)value);
                case BuiltInType.UInt64: return Convert.ToUInt32((ulong)value);
                case BuiltInType.Float: return Convert.ToUInt32((float)value);
                case BuiltInType.Double: return Convert.ToUInt32((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToUInt32((string)value);
                }

                case BuiltInType.StatusCode:
                {
                    return Convert.ToUInt32(((StatusCode)value).Code);
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a Int64
        /// </summary>
        private static long ToInt64(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.Int64:
                {
                    return (long)value;
                }

                case BuiltInType.Boolean: return Convert.ToInt64((bool)value);
                case BuiltInType.SByte: return Convert.ToInt64((sbyte)value);
                case BuiltInType.Byte: return Convert.ToInt64((byte)value);
                case BuiltInType.Int16: return Convert.ToInt64((short)value);
                case BuiltInType.UInt16: return Convert.ToInt64((ushort)value);
                case BuiltInType.Int32: return Convert.ToInt64((int)value);
                case BuiltInType.UInt32: return Convert.ToInt64((uint)value);
                case BuiltInType.UInt64: return Convert.ToInt64((ulong)value);
                case BuiltInType.Float: return Convert.ToInt64((float)value);
                case BuiltInType.Double: return Convert.ToInt64((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToInt64((string)value);
                }

                case BuiltInType.StatusCode:
                {
                    return Convert.ToInt64(((StatusCode)value).Code);
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a UInt64
        /// </summary>
        private static ulong ToUInt64(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.UInt64:
                {
                    return (ulong)value;
                }

                case BuiltInType.Boolean: return Convert.ToUInt64((bool)value);
                case BuiltInType.SByte: return Convert.ToUInt64((sbyte)value);
                case BuiltInType.Byte: return Convert.ToUInt64((byte)value);
                case BuiltInType.Int16: return Convert.ToUInt64((short)value);
                case BuiltInType.UInt16: return Convert.ToUInt64((ushort)value);
                case BuiltInType.Int32: return Convert.ToUInt64((int)value);
                case BuiltInType.UInt32: return Convert.ToUInt64((uint)value);
                case BuiltInType.Int64: return Convert.ToUInt64((long)value);
                case BuiltInType.Float: return Convert.ToUInt64((float)value);
                case BuiltInType.Double: return Convert.ToUInt64((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToUInt64((string)value);
                }

                case BuiltInType.StatusCode:
                {
                    return Convert.ToUInt64(((StatusCode)value).Code);
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a Float
        /// </summary>
        private static float ToFloat(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.Float:
                {
                    return (float)value;
                }

                case BuiltInType.Boolean: return Convert.ToSingle((bool)value);
                case BuiltInType.SByte: return Convert.ToSingle((sbyte)value);
                case BuiltInType.Byte: return Convert.ToSingle((byte)value);
                case BuiltInType.Int16: return Convert.ToSingle((short)value);
                case BuiltInType.UInt16: return Convert.ToSingle((ushort)value);
                case BuiltInType.Int32: return Convert.ToSingle((int)value);
                case BuiltInType.UInt32: return Convert.ToSingle((uint)value);
                case BuiltInType.Int64: return Convert.ToSingle((long)value);
                case BuiltInType.UInt64: return Convert.ToSingle((ulong)value);
                case BuiltInType.Double: return Convert.ToSingle((double)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToSingle((string)value);
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a Double
        /// </summary>
        private static double ToDouble(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.Double:
                {
                    return (double)value;
                }

                case BuiltInType.Boolean: return Convert.ToDouble((bool)value);
                case BuiltInType.SByte: return Convert.ToDouble((sbyte)value);
                case BuiltInType.Byte: return Convert.ToDouble((byte)value);
                case BuiltInType.Int16: return Convert.ToDouble((short)value);
                case BuiltInType.UInt16: return Convert.ToDouble((ushort)value);
                case BuiltInType.Int32: return Convert.ToDouble((int)value);
                case BuiltInType.UInt32: return Convert.ToDouble((uint)value);
                case BuiltInType.Int64: return Convert.ToDouble((long)value);
                case BuiltInType.UInt64: return Convert.ToDouble((ulong)value);
                case BuiltInType.Float: return Convert.ToDouble((float)value);

                case BuiltInType.String:
                {
                    return XmlConvert.ToDouble((string)value);
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a String
        /// </summary>
        private static string ToString(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.String:
                {
                    return (string)value;
                }

                case BuiltInType.Boolean:
                {
                    return XmlConvert.ToString((bool)value);
                }

                case BuiltInType.SByte:
                {
                    return XmlConvert.ToString((sbyte)value);
                }

                case BuiltInType.Byte:
                {
                    return XmlConvert.ToString((byte)value);
                }

                case BuiltInType.Int16:
                {
                    return XmlConvert.ToString((short)value);
                }

                case BuiltInType.UInt16:
                {
                    return XmlConvert.ToString((ushort)value);
                }

                case BuiltInType.Int32:
                {
                    return XmlConvert.ToString((int)value);
                }

                case BuiltInType.UInt32:
                {
                    return XmlConvert.ToString((uint)value);
                }

                case BuiltInType.Int64:
                {
                    return XmlConvert.ToString((long)value);
                }

                case BuiltInType.UInt64:
                {
                    return XmlConvert.ToString((ulong)value);
                }

                case BuiltInType.Float:
                {
                    return XmlConvert.ToString((float)value);
                }

                case BuiltInType.Double:
                {
                    return XmlConvert.ToString((double)value);
                }

                case BuiltInType.DateTime:
                {
                    return XmlConvert.ToString((DateTime)value, XmlDateTimeSerializationMode.Unspecified);
                }

                case BuiltInType.Guid:
                {
                    return ((Uuid)value).ToString();
                }

                case BuiltInType.NodeId:
                {
                    return ((NodeId)value).ToString();
                }

                case BuiltInType.ExpandedNodeId:
                {
                    return ((ExpandedNodeId)value).ToString();
                }

                case BuiltInType.LocalizedText:
                {
                    return ((LocalizedText)value).Text;
                }

                case BuiltInType.QualifiedName:
                {
                    return ((QualifiedName)value).ToString();
                }

                case BuiltInType.XmlElement:
                {
                    return ((XmlElement)value).OuterXml;
                }

                case BuiltInType.StatusCode:
                {
                    return ((StatusCode)value).Code.ToString();
                }

                case BuiltInType.ExtensionObject:
                {
                    return ((ExtensionObject)value).ToString();
                }

                case BuiltInType.Null:
                {
                    return null;
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a DateTime
        /// </summary>
        private static DateTime ToDateTime(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.DateTime:
                {
                    return (DateTime)value;
                }

                case BuiltInType.String:
                {
                    return XmlConvert.ToDateTime((string)value, XmlDateTimeSerializationMode.Unspecified);
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a Guid
        /// </summary>
        private static Uuid ToGuid(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.String:
                {
                    return new Uuid((string)value);
                }

                case BuiltInType.ByteString:
                {
                    return new Uuid(new Guid((byte[])value));
                }

                case BuiltInType.Guid:
                {
                    Guid? guid = value as Guid?;

                    if (guid != null)
                    {
                        return new Uuid(guid.Value);
                    }

                    return (Uuid)value;
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a ByteString
        /// </summary>
        private static byte[] ToByteString(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.ByteString:
                {
                    return (byte[])value;
                }

                case BuiltInType.String:
                {
                    string text = (string)value;

                    if (text == null)
                    {
                        return null;
                    }

                    if (text.Length == 0)
                    {
                        return Array.Empty<byte>();
                    }

                    using (System.IO.MemoryStream ostrm = new System.IO.MemoryStream())
                    {
                        byte buffer = 0;
                        bool firstByte = false;
                        const string digits = "0123456789ABCDEF";

                        for (int ii = 0; ii < text.Length; ii++)
                        {
                            if (!Char.IsWhiteSpace(text, ii) && !Char.IsLetterOrDigit(text, ii))
                            {
                                throw new FormatException("Invalid character in ByteString. " + text[ii]);
                            }

                            if (Char.IsWhiteSpace(text, ii))
                            {
                                continue;
                            }

                            int index = digits.IndexOf(Char.ToUpper(text[ii]));

                            if (index < 0)
                            {
                                throw new FormatException("Invalid character in ByteString." + text[ii]);
                            }

                            buffer <<= 4;
                            buffer += (byte)index;

                            if (firstByte)
                            {
                                ostrm.WriteByte(buffer);
                                firstByte = false;
                                continue;
                            }

                            firstByte = true;
                        }

                        if (firstByte)
                        {
                            buffer <<= 4;
                            ostrm.WriteByte(buffer);
                        }

                        // you should not access a closed stream, ever.
                        return ostrm.ToArray();
                    }
                }

                case BuiltInType.Guid:
                {
                    return ((Guid)((Uuid)value)).ToByteArray();
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a XmlElement
        /// </summary>
        private static XmlElement ToXmlElement(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.XmlElement:
                {
                    return (XmlElement)value;
                }

                case BuiltInType.String:
                {
                    XmlDocument document = new XmlDocument();
                    document.LoadInnerXml((string)value);
                    return document.DocumentElement;
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a NodeId
        /// </summary>
        private static NodeId ToNodeId(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.NodeId:
                {
                    return (NodeId)value;
                }

                case BuiltInType.ExpandedNodeId:
                {
                    return (NodeId)(ExpandedNodeId)value;
                }

                case BuiltInType.String:
                {
                    return NodeId.Parse((string)value);
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a ExpandedNodeId
        /// </summary>
        private static ExpandedNodeId ToExpandedNodeId(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.ExpandedNodeId:
                {
                    return (ExpandedNodeId)value;
                }

                case BuiltInType.NodeId:
                {
                    return (ExpandedNodeId)(NodeId)value;
                }

                case BuiltInType.String:
                {
                    return ExpandedNodeId.Parse((string)value);
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a StatusCode
        /// </summary>
        private static StatusCode ToStatusCode(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.StatusCode:
                {
                    return (StatusCode)value;
                }

                case BuiltInType.UInt16:
                {
                    uint code = Convert.ToUInt32((ushort)value);
                    code <<= 16;
                    return (StatusCode)code;
                }

                case BuiltInType.Int32:
                {
                    return (StatusCode)Convert.ToUInt32((int)value);
                }

                case BuiltInType.UInt32:
                {
                    return (StatusCode)(uint)value;
                }

                case BuiltInType.Int64:
                {
                    return (StatusCode)Convert.ToUInt32((long)value);
                }

                case BuiltInType.UInt64:
                {
                    return (StatusCode)Convert.ToUInt32((ulong)value);
                }

                case BuiltInType.String:
                {
                    string text = (string)value;

                    if (text == null)
                    {
                        return StatusCodes.Good;
                    }

                    text = text.Trim();

                    if (text.StartsWith("0x"))
                    {
                        return (StatusCode)Convert.ToUInt32(text.Substring(2), 16);
                    }

                    return (StatusCode)Convert.ToUInt32((string)value);
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a QualifiedName
        /// </summary>
        private static QualifiedName ToQualifiedName(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.QualifiedName:
                {
                    return (QualifiedName)value;
                }

                case BuiltInType.String:
                {
                    return QualifiedName.Parse((string)value);
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a LocalizedText
        /// </summary>
        private static LocalizedText ToLocalizedText(object value, TypeInfo sourceType)
        {
            // handle for supported conversions.
            switch (sourceType.BuiltInType)
            {
                case BuiltInType.LocalizedText:
                {
                    return (LocalizedText)value;
                }

                case BuiltInType.String:
                {
                    return new LocalizedText((string)value);
                }
            }

            // conversion not supported.
            throw new InvalidCastException();
        }

        /// <summary>
        /// Converts a value to a Variant
        /// </summary>
        private static Variant ToVariant(object value, TypeInfo sourceType)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Delegate for a function used to cast a value to the specified type.
        /// </summary>
        private delegate T CastDelegate<T>(object value, TypeInfo sourceType);

        /// <summary>
        /// Casts a scalar or array value to the specified type.
        /// </summary>
        private static object Cast<T>(object input, TypeInfo sourceType, CastDelegate<T> handler)
        {
            if (sourceType == null)
            {
                sourceType = TypeInfo.Construct(input);
            }

            if (sourceType.ValueRank >= 0)
            {
                return Cast<T>((Array)input, sourceType, handler);
            }

            if (sourceType.BuiltInType == BuiltInType.Variant)
            {
                object value = ((Variant)input).Value;
                sourceType = TypeInfo.Construct(value);
                return handler(value, sourceType);
            }

            return handler(input, sourceType);
        }

        /// <summary>
        /// Casts an array to an array of the specified type.
        /// </summary>
        private static Array Cast<T>(Array input, TypeInfo sourceType, CastDelegate<T> handler)
        {
            if (input == null)
            {
                return null;
            }

            TypeInfo elementType = new TypeInfo(sourceType.BuiltInType, ValueRanks.Scalar);

            if (input.Rank == 1)
            {
                T[] copy = new T[input.Length];

                for (int ii = 0; ii < input.Length; ii++)
                {
                    object value = input.GetValue(ii);

                    if (value != null)
                    {
                        if (sourceType.BuiltInType == BuiltInType.Variant)
                        {
                            value = ((Variant)value).Value;
                            elementType = TypeInfo.Construct(value);
                        }

                        copy[ii] = handler(value, elementType);
                    }
                }

                return copy;
            }

            if (input.Rank == 2)
            {
                int x = input.GetLength(0);
                int y = input.GetLength(1);

                T[,] copy = new T[x, y];

                for (int ii = 0; ii < x; ii++)
                {
                    for (int jj = 0; jj < y; jj++)
                    {
                        object value = input.GetValue(ii, jj);

                        if (value != null)
                        {
                            if (sourceType.BuiltInType == BuiltInType.Variant)
                            {
                                value = ((Variant)value).Value;
                                elementType = TypeInfo.Construct(value);
                            }

                            copy[ii, jj] = handler(value, elementType);
                        }
                    }
                }

                return copy;
            }

            int[] dimensions = new int[input.Rank];

            for (int ii = 0; ii < dimensions.Length; ii++)
            {
                dimensions[ii] = input.GetLength(ii);
            }

            Array output = Array.CreateInstance(typeof(T), dimensions);

            int length = output.Length;
            int[] indexes = new int[dimensions.Length];

            for (int ii = 0; ii < length; ii++)
            {
                int divisor = output.Length;

                for (int jj = 0; jj < indexes.Length; jj++)
                {
                    divisor /= dimensions[jj];
                    indexes[jj] = (ii / divisor) % dimensions[jj];
                }

                object value = input.GetValue(indexes);

                if (value != null)
                {
                    if (sourceType.BuiltInType == BuiltInType.Variant)
                    {
                        value = ((Variant)value).Value;
                        elementType = TypeInfo.Construct(value);
                    }

                    output.SetValue(handler(value, elementType), indexes);
                }
            }

            return output;
        }
        #endregion

        #region Private Fields
        private BuiltInType m_builtInType;
        private int m_valueRank;
        private static readonly TypeInfo s_Unknown = new TypeInfo();
        #endregion

        #region Scalars Class
        /// <summary>
        /// Constants for scalar types.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1034:NestedTypesShouldNotBeVisible")]
        public static class Scalars
        {
            /// <summary>
            /// A boolean logic value (true or false).
            /// </summary>
            public static readonly TypeInfo Boolean = new TypeInfo(BuiltInType.Boolean, ValueRanks.Scalar);

            /// <summary>
            /// An 8 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo SByte = new TypeInfo(BuiltInType.SByte, ValueRanks.Scalar);

            /// <summary>
            /// An 8 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo Byte = new TypeInfo(BuiltInType.Byte, ValueRanks.Scalar);

            /// <summary>
            /// A 16 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int16 = new TypeInfo(BuiltInType.Int16, ValueRanks.Scalar);

            /// <summary>
            /// A 16 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo UInt16 = new TypeInfo(BuiltInType.UInt16, ValueRanks.Scalar);

            /// <summary>
            /// A 32 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int32 = new TypeInfo(BuiltInType.Int32, ValueRanks.Scalar);

            /// <summary>
            /// A 32 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo UInt32 = new TypeInfo(BuiltInType.UInt32, ValueRanks.Scalar);

            /// <summary>
            /// A 64 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int64 = new TypeInfo(BuiltInType.Int64, ValueRanks.Scalar);

            /// <summary>
            /// A 64 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo UInt64 = new TypeInfo(BuiltInType.UInt64, ValueRanks.Scalar);

            /// <summary>
            /// An IEEE single precision (32 bit) floating point value.
            /// </summary>
            public static readonly TypeInfo Float = new TypeInfo(BuiltInType.Float, ValueRanks.Scalar);

            /// <summary>
            /// An IEEE double precision (64 bit) floating point value.
            /// </summary>
            public static readonly TypeInfo Double = new TypeInfo(BuiltInType.Double, ValueRanks.Scalar);

            /// <summary>
            /// A sequence of Unicode characters.
            /// </summary>
            public static readonly TypeInfo String = new TypeInfo(BuiltInType.String, ValueRanks.Scalar);

            /// <summary>
            /// An instance in time.
            /// </summary>
            public static readonly TypeInfo DateTime = new TypeInfo(BuiltInType.DateTime, ValueRanks.Scalar);

            /// <summary>
            /// A 128-bit globally unique identifier.
            /// </summary>
            public static readonly TypeInfo Guid = new TypeInfo(BuiltInType.Guid, ValueRanks.Scalar);

            /// <summary>
            /// A sequence of bytes.
            /// </summary>
            public static readonly TypeInfo ByteString = new TypeInfo(BuiltInType.ByteString, ValueRanks.Scalar);

            /// <summary>
            /// An XML element.
            /// </summary>
            public static readonly TypeInfo XmlElement = new TypeInfo(BuiltInType.XmlElement, ValueRanks.Scalar);

            /// <summary>
            /// An identifier for a node in the address space of a UA server.
            /// </summary>
            public static readonly TypeInfo NodeId = new TypeInfo(BuiltInType.NodeId, ValueRanks.Scalar);

            /// <summary>
            /// A node id that stores the namespace URI instead of the namespace index.
            /// </summary>
            public static readonly TypeInfo ExpandedNodeId = new TypeInfo(BuiltInType.ExpandedNodeId, ValueRanks.Scalar);

            /// <summary>
            /// A structured result code.
            /// </summary>
            public static readonly TypeInfo StatusCode = new TypeInfo(BuiltInType.StatusCode, ValueRanks.Scalar);

            /// <summary>
            /// A string qualified with a namespace.
            /// </summary>
            public static readonly TypeInfo QualifiedName = new TypeInfo(BuiltInType.QualifiedName, ValueRanks.Scalar);

            /// <summary>
            /// A localized text string with an locale identifier.
            /// </summary>
            public static readonly TypeInfo LocalizedText = new TypeInfo(BuiltInType.LocalizedText, ValueRanks.Scalar);

            /// <summary>
            /// An opaque object with a syntax that may be unknown to the receiver.
            /// </summary>
            public static readonly TypeInfo ExtensionObject = new TypeInfo(BuiltInType.ExtensionObject, ValueRanks.Scalar);

            /// <summary>
            /// A data value with an associated quality and timestamp.
            /// </summary>
            public static readonly TypeInfo DataValue = new TypeInfo(BuiltInType.DataValue, ValueRanks.Scalar);

            /// <summary>
            /// Any of the other built-in types.
            /// </summary>
            public static readonly TypeInfo Variant = new TypeInfo(BuiltInType.Variant, ValueRanks.Scalar);

            /// <summary>
            /// A diagnostic information associated with a result code.
            /// </summary>
            public static readonly TypeInfo DiagnosticInfo = new TypeInfo(BuiltInType.DiagnosticInfo, ValueRanks.Scalar);
        }
        #endregion

        #region Arrays Class
        /// <summary>
        /// Constants for one dimensional array types.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1034:NestedTypesShouldNotBeVisible")]
        public static class Arrays
        {
            /// <summary>
            /// A boolean logic value (true or false).
            /// </summary>
            public static readonly TypeInfo Boolean = new TypeInfo(BuiltInType.Boolean, ValueRanks.OneDimension);

            /// <summary>
            /// An 8 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo SByte = new TypeInfo(BuiltInType.SByte, ValueRanks.OneDimension);

            /// <summary>
            /// An 8 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo Byte = new TypeInfo(BuiltInType.Byte, ValueRanks.OneDimension);

            /// <summary>
            /// A 16 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int16 = new TypeInfo(BuiltInType.Int16, ValueRanks.OneDimension);

            /// <summary>
            /// A 16 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo UInt16 = new TypeInfo(BuiltInType.UInt16, ValueRanks.OneDimension);

            /// <summary>
            /// A 32 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int32 = new TypeInfo(BuiltInType.Int32, ValueRanks.OneDimension);

            /// <summary>
            /// A 32 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo UInt32 = new TypeInfo(BuiltInType.UInt32, ValueRanks.OneDimension);

            /// <summary>
            /// A 64 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int64 = new TypeInfo(BuiltInType.Int64, ValueRanks.OneDimension);

            /// <summary>
            /// A 64 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo UInt64 = new TypeInfo(BuiltInType.UInt64, ValueRanks.OneDimension);

            /// <summary>
            /// An IEEE single precision (32 bit) floating point value.
            /// </summary>
            public static readonly TypeInfo Float = new TypeInfo(BuiltInType.Float, ValueRanks.OneDimension);

            /// <summary>
            /// An IEEE double precision (64 bit) floating point value.
            /// </summary>
            public static readonly TypeInfo Double = new TypeInfo(BuiltInType.Double, ValueRanks.OneDimension);

            /// <summary>
            /// A sequence of Unicode characters.
            /// </summary>
            public static readonly TypeInfo String = new TypeInfo(BuiltInType.String, ValueRanks.OneDimension);

            /// <summary>
            /// An instance in time.
            /// </summary>
            public static readonly TypeInfo DateTime = new TypeInfo(BuiltInType.DateTime, ValueRanks.OneDimension);

            /// <summary>
            /// A 128-bit globally unique identifier.
            /// </summary>
            public static readonly TypeInfo Guid = new TypeInfo(BuiltInType.Guid, ValueRanks.OneDimension);

            /// <summary>
            /// A sequence of bytes.
            /// </summary>
            public static readonly TypeInfo ByteString = new TypeInfo(BuiltInType.ByteString, ValueRanks.OneDimension);

            /// <summary>
            /// An XML element.
            /// </summary>
            public static readonly TypeInfo XmlElement = new TypeInfo(BuiltInType.XmlElement, ValueRanks.OneDimension);

            /// <summary>
            /// An identifier for a node in the address space of a UA server.
            /// </summary>
            public static readonly TypeInfo NodeId = new TypeInfo(BuiltInType.NodeId, ValueRanks.OneDimension);

            /// <summary>
            /// A node id that stores the namespace URI instead of the namespace index.
            /// </summary>
            public static readonly TypeInfo ExpandedNodeId = new TypeInfo(BuiltInType.ExpandedNodeId, ValueRanks.OneDimension);

            /// <summary>
            /// A structured result code.
            /// </summary>
            public static readonly TypeInfo StatusCode = new TypeInfo(BuiltInType.StatusCode, ValueRanks.OneDimension);

            /// <summary>
            /// A string qualified with a namespace.
            /// </summary>
            public static readonly TypeInfo QualifiedName = new TypeInfo(BuiltInType.QualifiedName, ValueRanks.OneDimension);

            /// <summary>
            /// A localized text string with an locale identifier.
            /// </summary>
            public static readonly TypeInfo LocalizedText = new TypeInfo(BuiltInType.LocalizedText, ValueRanks.OneDimension);

            /// <summary>
            /// An opaque object with a syntax that may be unknown to the receiver.
            /// </summary>
            public static readonly TypeInfo ExtensionObject = new TypeInfo(BuiltInType.ExtensionObject, ValueRanks.OneDimension);

            /// <summary>
            /// A data value with an associated quality and timestamp.
            /// </summary>
            public static readonly TypeInfo DataValue = new TypeInfo(BuiltInType.DataValue, ValueRanks.OneDimension);

            /// <summary>
            /// Any of the other built-in types.
            /// </summary>
            public static readonly TypeInfo Variant = new TypeInfo(BuiltInType.Variant, ValueRanks.OneDimension);

            /// <summary>
            /// A diagnostic information associated with a result code.
            /// </summary>
            public static readonly TypeInfo DiagnosticInfo = new TypeInfo(BuiltInType.DiagnosticInfo, ValueRanks.OneDimension);
        }
        #endregion

        #region IFormattable Members
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
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                System.Text.StringBuilder buffer = new System.Text.StringBuilder();
                buffer.Append(m_builtInType);

                if (m_valueRank >= 0)
                {
                    buffer.Append('[');

                    for (int ii = 1; ii < m_valueRank; ii++)
                    {
                        buffer.Append(',');
                    }

                    buffer.Append(']');
                }

                return buffer.ToString();
            }

            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Determines if the specified object is equal to the object.
        /// </summary>
        /// <remarks>
        /// Determines if the specified object is equal to the object.
        /// </remarks>
        public override bool Equals(object obj)
        {
            if (Object.ReferenceEquals(this, obj))
            {
                return true;
            }

            TypeInfo typeInfo = obj as TypeInfo;
            if (typeInfo != null)
            {
                return (m_builtInType == typeInfo.BuiltInType &&
                    m_valueRank == typeInfo.ValueRank);
            }

            return false;
        }

        /// <summary>
        /// Returns a suitable hash code.
        /// </summary>
        public override int GetHashCode()
        {
            return HashCode.Combine(m_builtInType, m_valueRank);
        }
        #endregion
    }
}
