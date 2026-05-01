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
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Stores information about a type.
    /// </summary>
    public readonly struct TypeInfo : IFormattable, IEquatable<TypeInfo>
    {
        /// <summary>
        /// Construct the object with a built-in type and a value rank.
        /// </summary>
        /// <param name="builtInType">Type of the built in.</param>
        /// <param name="valueRank">The value rank.</param>
        [JsonConstructor]
        public TypeInfo(BuiltInType builtInType, int valueRank)
        {
            // We pack value ranks into a short to save space.
            // If we need more we can pack it to 3 bytes later.
            if (valueRank is < short.MinValue or > short.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(valueRank));
            }
            m_builtInType = unchecked((byte)builtInType);
            m_valid = 66;
            m_valueRank = (short)valueRank;
        }

        /// <summary>
        /// If the type is unknown.
        /// </summary>
        [JsonIgnore]
        public bool IsUnknown => m_valid == 0;

        /// <summary>
        /// A constant representing an unknown type.
        /// </summary>
        /// <value>The constant representing an unknown type.</value>
        [JsonIgnore]
        public static readonly TypeInfo Unknown;

        /// <summary>
        /// True if scalar type
        /// </summary>
        [JsonIgnore]
        public bool IsScalar => ValueRank < 0;

        /// <summary>
        /// True if scalar type
        /// </summary>
        [JsonIgnore]
        public bool IsArray => ValueRank is 0 or 1;

        /// <summary>
        /// True if matrix type
        /// </summary>
        [JsonIgnore]
        public bool IsMatrix => ValueRank > 1;

        /// <summary>
        /// The built-in type.
        /// </summary>
        /// <value>The type of the type represented by this instance.</value>
        public BuiltInType BuiltInType => (BuiltInType)m_builtInType;

        /// <summary>
        /// The value rank.
        /// </summary>
        /// <value>The value rank of the type represented by this instance.</value>
        public int ValueRank => m_valueRank;

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            if (IsUnknown)
            {
                return 0;
            }
            return HashCode.Combine(BuiltInType, ValueRank);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj switch
            {
                null => IsUnknown,
                TypeInfo typeInfo => Equals(typeInfo),
                _ => base.Equals(obj)
            };
        }

        /// <inheritdoc/>
        public bool Equals(TypeInfo typeInfo)
        {
            return
                m_valid == typeInfo.m_valid &&
                BuiltInType == typeInfo.BuiltInType &&
                ValueRank == typeInfo.ValueRank;
        }

        /// <inheritdoc/>
        public static bool operator ==(TypeInfo left, TypeInfo right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(TypeInfo left, TypeInfo right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <inheritdoc/>
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

            throw new FormatException(CoreUtils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Creates a new type info with the built in type and current
        /// value rank
        /// </summary>
        /// <param name="builtInType">The new built in type</param>
        /// <returns></returns>
        public TypeInfo WithBuiltInType(BuiltInType builtInType)
        {
            return new TypeInfo(builtInType, m_valueRank);
        }

        /// <summary>
        /// Creates a new type info with the built in type and new value
        /// rank.
        /// </summary>
        /// <param name="valueRank">The new value rank</param>
        /// <returns></returns>
        public TypeInfo WithValueRank(int valueRank)
        {
            return new TypeInfo(BuiltInType, valueRank);
        }

        /// <summary>
        /// Construct the object with a built-in type and a value rank.
        /// Uses static TypeInfo definitions if available.
        /// </summary>
        /// <param name="builtInType">Type of the built in.</param>
        /// <param name="valueRank">The value rank.</param>
        public static TypeInfo Create(BuiltInType builtInType, int valueRank)
        {
            return new TypeInfo(builtInType, valueRank);
        }

        /// <summary>
        /// Returns a static or allocated type info object for a scalar of the specified type.
        /// </summary>
        public static TypeInfo CreateScalar(BuiltInType builtInType)
        {
            return new TypeInfo(builtInType, ValueRanks.Scalar);
        }

        /// <summary>
        /// Returns a static of allocated type info object for a one
        /// dimensional array of the specified type.
        /// </summary>
        private static TypeInfo CreateArray(BuiltInType builtInType)
        {
            return new TypeInfo(builtInType, ValueRanks.OneDimension);
        }

        /// <summary>
        /// Returns a static of allocated type info object for a multi
        /// dimensional array of the specified type.
        /// </summary>
        private static TypeInfo CreateOneOrMoreDimensions(BuiltInType builtInType)
        {
            return new TypeInfo(builtInType, ValueRanks.OneOrMoreDimensions);
        }

        /// <summary>
        /// Constants for scalar types.
        /// </summary>
        public static class Scalars
        {
            /// <summary>
            /// A boolean logic value (true or false).
            /// </summary>
            public static readonly TypeInfo Boolean =
                CreateScalar(BuiltInType.Boolean);

            /// <summary>
            /// An 8 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo SByte =
                CreateScalar(BuiltInType.SByte);

            /// <summary>
            /// An 8 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo Byte =
                CreateScalar(BuiltInType.Byte);

            /// <summary>
            /// A 16 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int16 =
                CreateScalar(BuiltInType.Int16);

            /// <summary>
            /// A 16 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo UInt16 =
                CreateScalar(BuiltInType.UInt16);

            /// <summary>
            /// A 32 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int32 =
                CreateScalar(BuiltInType.Int32);

            /// <summary>
            /// A 32 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo UInt32 =
                CreateScalar(BuiltInType.UInt32);

            /// <summary>
            /// A 64 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int64 =
                CreateScalar(BuiltInType.Int64);

            /// <summary>
            /// A 64 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo UInt64 =
                CreateScalar(BuiltInType.UInt64);

            /// <summary>
            /// An IEEE single precision (32 bit) floating point value.
            /// </summary>
            public static readonly TypeInfo Float =
                CreateScalar(BuiltInType.Float);

            /// <summary>
            /// An IEEE double precision (64 bit) floating point value.
            /// </summary>
            public static readonly TypeInfo Double =
                CreateScalar(BuiltInType.Double);

            /// <summary>
            /// A sequence of Unicode characters.
            /// </summary>
            public static readonly TypeInfo String =
                CreateScalar(BuiltInType.String);

            /// <summary>
            /// An instance in time.
            /// </summary>
            public static readonly TypeInfo DateTime =
                CreateScalar(BuiltInType.DateTime);

            /// <summary>
            /// A 128-bit globally unique identifier.
            /// </summary>
            public static readonly TypeInfo Guid =
                CreateScalar(BuiltInType.Guid);

            /// <summary>
            /// A sequence of bytes.
            /// </summary>
            public static readonly TypeInfo ByteString =
                CreateScalar(BuiltInType.ByteString);

            /// <summary>
            /// An XML element.
            /// </summary>
            public static readonly TypeInfo XmlElement =
                CreateScalar(BuiltInType.XmlElement);

            /// <summary>
            /// An identifier for a node in the address space of a UA server.
            /// </summary>
            public static readonly TypeInfo NodeId =
                CreateScalar(BuiltInType.NodeId);

            /// <summary>
            /// A node id that stores the namespace URI instead of the namespace index.
            /// </summary>
            public static readonly TypeInfo ExpandedNodeId =
                CreateScalar(BuiltInType.ExpandedNodeId);

            /// <summary>
            /// A structured result code.
            /// </summary>
            public static readonly TypeInfo StatusCode =
                CreateScalar(BuiltInType.StatusCode);

            /// <summary>
            /// A string qualified with a namespace.
            /// </summary>
            public static readonly TypeInfo QualifiedName =
                CreateScalar(BuiltInType.QualifiedName);

            /// <summary>
            /// A localized text string with an locale identifier.
            /// </summary>
            public static readonly TypeInfo LocalizedText =
                CreateScalar(BuiltInType.LocalizedText);

            /// <summary>
            /// An opaque object with a syntax that may be unknown to the receiver.
            /// </summary>
            public static readonly TypeInfo ExtensionObject =
                CreateScalar(BuiltInType.ExtensionObject);

            /// <summary>
            /// A data value with an associated quality and timestamp.
            /// </summary>
            public static readonly TypeInfo DataValue =
                CreateScalar(BuiltInType.DataValue);

            /// <summary>
            /// Any of the other built-in types.
            /// </summary>
            public static readonly TypeInfo Variant =
                CreateScalar(BuiltInType.Variant);

            /// <summary>
            /// A diagnostic information associated with a result code.
            /// </summary>
            public static readonly TypeInfo DiagnosticInfo =
                CreateScalar(BuiltInType.DiagnosticInfo);

            /// <summary>
            /// An enum type info.
            /// </summary>
            public static readonly TypeInfo Enumeration =
                CreateScalar(BuiltInType.Enumeration);
        }

        /// <summary>
        /// Constants for one dimensional array types.
        /// </summary>
        public static class Arrays
        {
            /// <summary>
            /// A boolean logic value (true or false).
            /// </summary>
            public static readonly TypeInfo Boolean =
                CreateArray(BuiltInType.Boolean);

            /// <summary>
            /// An 8 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo SByte =
                CreateArray(BuiltInType.SByte);

            /// <summary>
            /// An 8 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo Byte =
                CreateArray(BuiltInType.Byte);

            /// <summary>
            /// A 16 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int16 =
                CreateArray(BuiltInType.Int16);

            /// <summary>
            /// A 16 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo UInt16 =
                CreateArray(BuiltInType.UInt16);

            /// <summary>
            /// A 32 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int32 =
                CreateArray(BuiltInType.Int32);

            /// <summary>
            /// A 32 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo UInt32 =
                CreateArray(BuiltInType.UInt32);

            /// <summary>
            /// A 64 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int64 =
                CreateArray(BuiltInType.Int64);

            /// <summary>
            /// A 64 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo UInt64 =
                CreateArray(BuiltInType.UInt64);

            /// <summary>
            /// An IEEE single precision (32 bit) floating point value.
            /// </summary>
            public static readonly TypeInfo Float =
                CreateArray(BuiltInType.Float);

            /// <summary>
            /// An IEEE double precision (64 bit) floating point value.
            /// </summary>
            public static readonly TypeInfo Double =
                CreateArray(BuiltInType.Double);

            /// <summary>
            /// A sequence of Unicode characters.
            /// </summary>
            public static readonly TypeInfo String =
                CreateArray(BuiltInType.String);

            /// <summary>
            /// An instance in time.
            /// </summary>
            public static readonly TypeInfo DateTime =
                CreateArray(BuiltInType.DateTime);

            /// <summary>
            /// A 128-bit globally unique identifier.
            /// </summary>
            public static readonly TypeInfo Guid =
                CreateArray(BuiltInType.Guid);

            /// <summary>
            /// A sequence of bytes.
            /// </summary>
            public static readonly TypeInfo ByteString =
                CreateArray(BuiltInType.ByteString);

            /// <summary>
            /// An XML element.
            /// </summary>
            public static readonly TypeInfo XmlElement =
                CreateArray(BuiltInType.XmlElement);

            /// <summary>
            /// An identifier for a node in the address space of a UA server.
            /// </summary>
            public static readonly TypeInfo NodeId =
                CreateArray(BuiltInType.NodeId);

            /// <summary>
            /// A node id that stores the namespace URI instead of the namespace index.
            /// </summary>
            public static readonly TypeInfo ExpandedNodeId =
                CreateArray(BuiltInType.ExpandedNodeId);

            /// <summary>
            /// A structured result code.
            /// </summary>
            public static readonly TypeInfo StatusCode =
                CreateArray(BuiltInType.StatusCode);

            /// <summary>
            /// A string qualified with a namespace.
            /// </summary>
            public static readonly TypeInfo QualifiedName =
                CreateArray(BuiltInType.QualifiedName);

            /// <summary>
            /// A localized text string with an locale identifier.
            /// </summary>
            public static readonly TypeInfo LocalizedText =
                CreateArray(BuiltInType.LocalizedText);

            /// <summary>
            /// An opaque object with a syntax that may be unknown to the receiver.
            /// </summary>
            public static readonly TypeInfo ExtensionObject =
                CreateArray(BuiltInType.ExtensionObject);

            /// <summary>
            /// A data value with an associated quality and timestamp.
            /// </summary>
            public static readonly TypeInfo DataValue =
                CreateArray(BuiltInType.DataValue);

            /// <summary>
            /// Any of the other built-in types.
            /// </summary>
            public static readonly TypeInfo Variant =
                CreateArray(BuiltInType.Variant);

            /// <summary>
            /// A diagnostic information associated with a result code.
            /// </summary>
            public static readonly TypeInfo DiagnosticInfo =
                CreateArray(BuiltInType.DiagnosticInfo);

            /// <summary>
            /// An array of enum values.
            /// </summary>
            public static readonly TypeInfo Enumeration =
                CreateArray(BuiltInType.Enumeration);
        }

        /// <summary>
        /// Constants for one dimensional array types.
        /// </summary>
        public static class OneOrMoreDimensions
        {
            /// <summary>
            /// A boolean logic value (true or false).
            /// </summary>
            public static readonly TypeInfo Boolean =
                CreateOneOrMoreDimensions(BuiltInType.Boolean);

            /// <summary>
            /// An 8 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo SByte =
                CreateOneOrMoreDimensions(BuiltInType.SByte);

            /// <summary>
            /// An 8 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo Byte =
                CreateOneOrMoreDimensions(BuiltInType.Byte);

            /// <summary>
            /// A 16 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int16 =
                CreateOneOrMoreDimensions(BuiltInType.Int16);

            /// <summary>
            /// A 16 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo UInt16 =
                CreateOneOrMoreDimensions(BuiltInType.UInt16);

            /// <summary>
            /// A 32 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int32 =
                CreateOneOrMoreDimensions(BuiltInType.Int32);

            /// <summary>
            /// A 32 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo UInt32 =
                CreateOneOrMoreDimensions(BuiltInType.UInt32);

            /// <summary>
            /// A 64 bit signed integer value.
            /// </summary>
            public static readonly TypeInfo Int64 =
                CreateOneOrMoreDimensions(BuiltInType.Int64);

            /// <summary>
            /// A 64 bit unsigned integer value.
            /// </summary>
            public static readonly TypeInfo UInt64 =
                CreateOneOrMoreDimensions(BuiltInType.UInt64);

            /// <summary>
            /// An IEEE single precision (32 bit) floating point value.
            /// </summary>
            public static readonly TypeInfo Float =
                CreateOneOrMoreDimensions(BuiltInType.Float);

            /// <summary>
            /// An IEEE double precision (64 bit) floating point value.
            /// </summary>
            public static readonly TypeInfo Double =
                CreateOneOrMoreDimensions(BuiltInType.Double);

            /// <summary>
            /// A sequence of Unicode characters.
            /// </summary>
            public static readonly TypeInfo String =
                CreateOneOrMoreDimensions(BuiltInType.String);

            /// <summary>
            /// An instance in time.
            /// </summary>
            public static readonly TypeInfo DateTime =
                CreateOneOrMoreDimensions(BuiltInType.DateTime);

            /// <summary>
            /// A 128-bit globally unique identifier.
            /// </summary>
            public static readonly TypeInfo Guid =
                CreateOneOrMoreDimensions(BuiltInType.Guid);

            /// <summary>
            /// A sequence of bytes.
            /// </summary>
            public static readonly TypeInfo ByteString =
                CreateOneOrMoreDimensions(BuiltInType.ByteString);

            /// <summary>
            /// An XML element.
            /// </summary>
            public static readonly TypeInfo XmlElement =
                CreateOneOrMoreDimensions(BuiltInType.XmlElement);

            /// <summary>
            /// An identifier for a node in the address space of a UA server.
            /// </summary>
            public static readonly TypeInfo NodeId =
                CreateOneOrMoreDimensions(BuiltInType.NodeId);

            /// <summary>
            /// A node id that stores the namespace URI instead of the namespace index.
            /// </summary>
            public static readonly TypeInfo ExpandedNodeId =
                CreateOneOrMoreDimensions(BuiltInType.ExpandedNodeId);

            /// <summary>
            /// A structured result code.
            /// </summary>
            public static readonly TypeInfo StatusCode =
                CreateOneOrMoreDimensions(BuiltInType.StatusCode);

            /// <summary>
            /// A string qualified with a namespace.
            /// </summary>
            public static readonly TypeInfo QualifiedName =
                CreateOneOrMoreDimensions(BuiltInType.QualifiedName);

            /// <summary>
            /// A localized text string with an locale identifier.
            /// </summary>
            public static readonly TypeInfo LocalizedText =
                CreateOneOrMoreDimensions(BuiltInType.LocalizedText);

            /// <summary>
            /// An opaque object with a syntax that may be unknown to the receiver.
            /// </summary>
            public static readonly TypeInfo ExtensionObject =
                CreateOneOrMoreDimensions(BuiltInType.ExtensionObject);

            /// <summary>
            /// A data value with an associated quality and timestamp.
            /// </summary>
            public static readonly TypeInfo DataValue =
                CreateOneOrMoreDimensions(BuiltInType.DataValue);

            /// <summary>
            /// Any of the other built-in types.
            /// </summary>
            public static readonly TypeInfo Variant =
                CreateOneOrMoreDimensions(BuiltInType.Variant);

            /// <summary>
            /// A diagnostic information associated with a result code.
            /// </summary>
            public static readonly TypeInfo DiagnosticInfo =
                CreateOneOrMoreDimensions(BuiltInType.DiagnosticInfo);

            /// <summary>
            /// An array of enum values.
            /// </summary>
            public static readonly TypeInfo Enumeration =
                CreateOneOrMoreDimensions(BuiltInType.Enumeration);
        }

        /// <summary>
        /// Returns the data type id that describes a value.
        /// </summary>
        /// <param name="value">The value instance to check the data type.</param>
        /// <param name="namespaceTable">The namespace table.</param>
        /// <returns>An data type identifier for a node in a server's address space.</returns>
        public static NodeId GetDataTypeId(Variant value, NamespaceTable namespaceTable = null)
        {
            if (value.IsNull)
            {
                return NodeId.Null;
            }

            if (value.TryGetValue(out ExtensionObject eo) &&
                eo.TryGetValue(out IEncodeable encodable) &&
                !encodable.TypeId.IsNull)
            {
                namespaceTable ??= AmbientMessageContext.CurrentContext?.NamespaceUris;
                return ExpandedNodeId.ToNodeId(encodable.TypeId, namespaceTable);
            }

            NodeId dataTypeId = GetDataTypeId(value.TypeInfo);

            if (dataTypeId.IsNull && value.AsBoxedObject() is Matrix matrix) // TODO
            {
                return GetDataTypeId(matrix.TypeInfo);
            }

            return dataTypeId;
        }

        /// <summary>
        /// Returns the data type id that describes a value.
        /// </summary>
        /// <param name="typeInfo">The type info.</param>
        /// <returns>An data type identifier for a node in a server's address space.</returns>
        /// <exception cref="ServiceResultException"></exception>
        public static NodeId GetDataTypeId(TypeInfo typeInfo)
        {
            switch (typeInfo.BuiltInType)
            {
                case BuiltInType.Boolean:
                    return DataTypeIds.Boolean;
                case BuiltInType.SByte:
                    return DataTypeIds.SByte;
                case BuiltInType.Byte:
                    return DataTypeIds.Byte;
                case BuiltInType.Int16:
                    return DataTypeIds.Int16;
                case BuiltInType.UInt16:
                    return DataTypeIds.UInt16;
                case BuiltInType.Int32:
                    return DataTypeIds.Int32;
                case BuiltInType.UInt32:
                    return DataTypeIds.UInt32;
                case BuiltInType.Int64:
                    return DataTypeIds.Int64;
                case BuiltInType.UInt64:
                    return DataTypeIds.UInt64;
                case BuiltInType.Float:
                    return DataTypeIds.Float;
                case BuiltInType.Double:
                    return DataTypeIds.Double;
                case BuiltInType.String:
                    return DataTypeIds.String;
                case BuiltInType.DateTime:
                    return DataTypeIds.DateTime;
                case BuiltInType.Guid:
                    return DataTypeIds.Guid;
                case BuiltInType.ByteString:
                    return DataTypeIds.ByteString;
                case BuiltInType.XmlElement:
                    return DataTypeIds.XmlElement;
                case BuiltInType.NodeId:
                    return DataTypeIds.NodeId;
                case BuiltInType.ExpandedNodeId:
                    return DataTypeIds.ExpandedNodeId;
                case BuiltInType.StatusCode:
                    return DataTypeIds.StatusCode;
                case BuiltInType.DiagnosticInfo:
                    return DataTypeIds.DiagnosticInfo;
                case BuiltInType.QualifiedName:
                    return DataTypeIds.QualifiedName;
                case BuiltInType.LocalizedText:
                    return DataTypeIds.LocalizedText;
                case BuiltInType.ExtensionObject:
                    return DataTypeIds.Structure;
                case BuiltInType.DataValue:
                    return DataTypeIds.DataValue;
                case BuiltInType.Variant:
                    return DataTypeIds.BaseDataType;
                case BuiltInType.Number:
                    return DataTypeIds.Number;
                case BuiltInType.Integer:
                    return DataTypeIds.Integer;
                case BuiltInType.UInteger:
                    return DataTypeIds.UInteger;
                case BuiltInType.Enumeration:
                    return DataTypeIds.Enumeration;
                case BuiltInType.Null:
                    return NodeId.Null;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {typeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Returns the BuiltInType type for the DataTypeId.
        /// </summary>
        /// <param name="datatypeId">The data type identifier.</param>
        /// <returns>An <see cref="BuiltInType"/> for  <paramref name="datatypeId"/></returns>
        public static BuiltInType GetBuiltInType(NodeId datatypeId)
        {
            if (datatypeId.IsNull ||
                datatypeId.NamespaceIndex != 0 ||
                !datatypeId.TryGetValue(out uint id))
            {
                return BuiltInType.Null;
            }
            switch (id)
            {
                // subtype of DateTime
                case DataTypes.UtcTime:
                    return BuiltInType.DateTime;
                // subtype of ByteString
                case DataTypes.ApplicationInstanceCertificate:
                case DataTypes.AudioDataType:
                case DataTypes.ContinuationPoint:
                case DataTypes.Image:
                case DataTypes.ImageBMP:
                case DataTypes.ImageGIF:
                case DataTypes.ImageJPG:
                case DataTypes.ImagePNG:
                    return BuiltInType.ByteString;
                // subtype of NodeId
                case DataTypes.SessionAuthenticationToken:
                    return BuiltInType.NodeId;
                // subtype of Double
                case DataTypes.Duration:
                    return BuiltInType.Double;
                // subtype of UInt32
                case DataTypes.IntegerId:
                case DataTypes.Index:
                case DataTypes.VersionTime:
                case DataTypes.Counter:
                    return BuiltInType.UInt32;
                // subtype of UInt64
                case DataTypes.BitFieldMaskDataType:
                    return BuiltInType.UInt64;
                // subtype of String
                case DataTypes.DateString:
                case DataTypes.DecimalString:
                case DataTypes.DurationString:
                case DataTypes.LocaleId:
                case DataTypes.NormalizedString:
                case DataTypes.NumericRange:
                case DataTypes.TimeString:
                case DataTypes.UriString:
                    return BuiltInType.String;
                default:
                    if (id is > (uint)BuiltInType.DiagnosticInfo and not (uint)BuiltInType.Enumeration)
                    {
                        return BuiltInType.Null;
                    }
                    return (BuiltInType)(int)id;
            }
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
            if (builtInType is >= BuiltInType.SByte and <= BuiltInType.Double)
            {
                return true;
            }

            if (builtInType is >= BuiltInType.Number and <= BuiltInType.UInteger)
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
        /// Returns true if a 'null' value exists for the built-in type
        /// in all data encodings.
        /// </summary>
        /// <param name="builtInType">The built in type to check.</param>
        /// <returns>
        /// True if the built-in type is a type that is nullable.
        /// </returns>
        public static bool IsEncodingNullableType(BuiltInType builtInType)
        {
            if (builtInType is >= BuiltInType.Boolean and <= BuiltInType.Double)
            {
                return false;
            }

            if (builtInType is BuiltInType.DataValue or BuiltInType.DiagnosticInfo)
            {
                return false;
            }

            return true;
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

            while (!typeId.IsNull)
            {
                if (typeId.NamespaceIndex == 0 && typeId.TryGetValue(out uint numericId))
                {
                    var id = (BuiltInType)(int)numericId;

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

        /// <summary>
        /// Returns the BuiltInType type for the DataTypeId.
        /// </summary>
        /// <param name="datatypeId">The data type identifier for a node in a
        /// server's address space..</param>
        /// <param name="typeTree">The type tree for a server. .</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>
        /// A <see cref="BuiltInType"/> value for <paramref name="datatypeId"/>
        /// </returns>
        public static async Task<BuiltInType> GetBuiltInTypeAsync(
            NodeId datatypeId,
            ITypeTable typeTree,
            CancellationToken ct = default)
        {
            NodeId typeId = datatypeId;

            while (!typeId.IsNull)
            {
                if (typeId.NamespaceIndex == 0 && typeId.TryGetValue(out uint numericId))
                {
                    var id = (BuiltInType)(int)numericId;
                    if (id is > BuiltInType.Null and <= BuiltInType.Enumeration and not BuiltInType.DiagnosticInfo)
                    {
                        return id;
                    }
                }

                if (typeTree == null)
                {
                    break;
                }

                typeId = await typeTree.FindSuperTypeAsync(typeId, ct).ConfigureAwait(false);
            }

            return BuiltInType.Null;
        }

        /// <summary>
        /// Returns the system type for the datatype.
        /// </summary>
        /// <param name="datatypeId">The datatype id.</param>
        /// <param name="factory">The factory used to store and retrieve underlying OPC UA system types.</param>
        /// <returns>The system type for the <paramref name="datatypeId"/>.</returns>
        public static IType GetSystemType(ExpandedNodeId datatypeId, IEncodeableTypeLookup factory)
        {
            if (datatypeId.IsNull)
            {
                return null;
            }

            if (datatypeId.NamespaceIndex == 0 &&
                !datatypeId.IsAbsolute &&
                datatypeId.TryGetValue(out uint numericId))
            {
                switch (numericId)
                {
                    case DataTypes.Boolean:
                        return new SystemType<bool>(BuiltInType.Boolean);
                    case DataTypes.SByte:
                        return new SystemType<sbyte>(BuiltInType.SByte);
                    case DataTypes.Byte:
                        return new SystemType<byte>(BuiltInType.Byte);
                    case DataTypes.Int16:
                        return new SystemType<short>(BuiltInType.Int16);
                    case DataTypes.UInt16:
                        return new SystemType<ushort>(BuiltInType.UInt16);
                    case DataTypes.Int32:
                        return new SystemType<int>(BuiltInType.Int32);
                    case DataTypes.UInt32:
                        return new SystemType<uint>(BuiltInType.UInt32);
                    case DataTypes.Int64:
                        return new SystemType<long>(BuiltInType.Int64);
                    case DataTypes.UInt64:
                        return new SystemType<ulong>(BuiltInType.UInt64);
                    case DataTypes.Float:
                        return new SystemType<float>(BuiltInType.Float);
                    case DataTypes.Double:
                        return new SystemType<double>(BuiltInType.Double);
                    case DataTypes.String:
                        return new SystemType<string>(BuiltInType.String);
                    case DataTypes.DateTime:
                        return new SystemType<DateTimeUtc>(BuiltInType.DateTime);
                    case DataTypes.Guid:
                        return new SystemType<Uuid>(BuiltInType.Guid);
                    case DataTypes.ByteString:
                        return new SystemType<ByteString>(BuiltInType.ByteString);
                    case DataTypes.XmlElement:
                        return new SystemType<XmlElement>(BuiltInType.XmlElement);
                    case DataTypes.NodeId:
                        return new SystemType<NodeId>(BuiltInType.NodeId);
                    case DataTypes.ExpandedNodeId:
                        return new SystemType<ExpandedNodeId>(BuiltInType.ExpandedNodeId);
                    case DataTypes.StatusCode:
                        return new SystemType<StatusCode>(BuiltInType.StatusCode);
                    case DataTypes.DiagnosticInfo:
                        return new SystemType<DiagnosticInfo>(BuiltInType.DiagnosticInfo);
                    case DataTypes.QualifiedName:
                        return new SystemType<QualifiedName>(BuiltInType.QualifiedName);
                    case DataTypes.LocalizedText:
                        return new SystemType<LocalizedText>(BuiltInType.LocalizedText);
                    case DataTypes.DataValue:
                        return new SystemType<DataValue>(BuiltInType.DataValue);
                    case DataTypes.BaseDataType:
                        return new SystemType<Variant>(BuiltInType.Variant);
                    case DataTypes.Structure:
                        return new SystemType<ExtensionObject>(BuiltInType.ExtensionObject);
                    case DataTypes.Number:
                        return new SystemType<Variant>(BuiltInType.Number);
                    case DataTypes.Integer:
                        return new SystemType<Variant>(BuiltInType.Integer);
                    case DataTypes.UInteger:
                        return new SystemType<Variant>(BuiltInType.UInteger);
                    case DataTypes.Enumeration:
                        return new SystemType<int>(BuiltInType.Enumeration);
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
                    case DataTypes.UriString:
                        goto case DataTypes.String;
                }
            }

            return factory.TryGetType(datatypeId, out IType type) ? type : default;
        }

        /// <summary>
        /// Returns the system type for a scalar built-in type.
        /// </summary>
        /// <param name="builtInType">A built-in type.</param>
        /// <returns>A system type equivalent to the built-in type.</returns>
        /// <exception cref="ServiceResultException"></exception>
        public static IBuiltInType GetSystemType(BuiltInType builtInType)
        {
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                    return new SystemType<bool>(builtInType);
                case BuiltInType.SByte:
                    return new SystemType<sbyte>(builtInType);
                case BuiltInType.Byte:
                    return new SystemType<byte>(builtInType);
                case BuiltInType.Int16:
                    return new SystemType<short>(builtInType);
                case BuiltInType.UInt16:
                    return new SystemType<ushort>(builtInType);
                case BuiltInType.Int32:
                    return new SystemType<int>(builtInType);
                case BuiltInType.UInt32:
                    return new SystemType<uint>(builtInType);
                case BuiltInType.Int64:
                    return new SystemType<long>(builtInType);
                case BuiltInType.UInt64:
                    return new SystemType<ulong>(builtInType);
                case BuiltInType.Float:
                    return new SystemType<float>(builtInType);
                case BuiltInType.Double:
                    return new SystemType<double>(builtInType);
                case BuiltInType.String:
                    return new SystemType<string>(builtInType);
                case BuiltInType.DateTime:
                    return new SystemType<DateTimeUtc>(builtInType);
                case BuiltInType.Guid:
                    return new SystemType<Uuid>(builtInType);
                case BuiltInType.ByteString:
                    return new SystemType<ByteString>(builtInType);
                case BuiltInType.XmlElement:
                    return new SystemType<XmlElement>(builtInType);
                case BuiltInType.NodeId:
                    return new SystemType<NodeId>(builtInType);
                case BuiltInType.ExpandedNodeId:
                    return new SystemType<ExpandedNodeId>(builtInType);
                case BuiltInType.StatusCode:
                    return new SystemType<StatusCode>(builtInType);
                case BuiltInType.DiagnosticInfo:
                    return new SystemType<DiagnosticInfo>(builtInType);
                case BuiltInType.QualifiedName:
                    return new SystemType<QualifiedName>(builtInType);
                case BuiltInType.LocalizedText:
                    return new SystemType<LocalizedText>(builtInType);
                case BuiltInType.DataValue:
                    return new SystemType<DataValue>(builtInType);
                case BuiltInType.Variant:
                    return new SystemType<Variant>(builtInType);
                case BuiltInType.ExtensionObject:
                    return new SystemType<ExtensionObject>(builtInType);
                case BuiltInType.Number:
                    return new SystemType<Variant>(builtInType);
                case BuiltInType.Integer:
                    return new SystemType<Variant>(builtInType);
                case BuiltInType.UInteger:
                    return new SystemType<Variant>(builtInType);
                case BuiltInType.Enumeration:
                    return new SystemType<int>(builtInType);
                case BuiltInType.Null:
                    // for backcompat. -- should be: return null;
                    return new SystemType<Variant>(builtInType);
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {builtInType}");
            }
        }

        /// <summary>
        /// Returns the type info if the value is an instance of the data
        /// type with the specified value rank.
        /// </summary>
        /// <param name="value">The value instance to check.</param>
        /// <param name="expectedDataTypeId">The expected data type
        /// identifier for a node.</param>
        /// <param name="expectedValueRank">The expected value rank.</param>
        /// <param name="namespaceUris">The namespace URI's.</param>
        /// <param name="typeTree">The type tree for a server.</param>
        /// <returns>
        /// A type info if the Variant is an instance of the data type
        /// with the specified value rank
        /// </returns>
        /// <exception cref="ServiceResultException"></exception>
        public static TypeInfo IsInstanceOfDataType(
            Variant value,
            NodeId expectedDataTypeId,
            int expectedValueRank,
            NamespaceTable namespaceUris,
            ITypeTable typeTree)
        {
            // get the type info.
            TypeInfo typeInfo = value.TypeInfo;

            BuiltInType expectedType;
            if (typeInfo.BuiltInType == BuiltInType.Null)
            {
                expectedType = GetBuiltInType(expectedDataTypeId, typeTree);

                // nulls allowed for all array types.
                if (expectedValueRank != ValueRanks.Scalar)
                {
                    return new TypeInfo(expectedType, expectedValueRank);
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
                        return default;
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
                return default;
            }

            // check the value rank.
            if (!ValueRanks.IsValid(typeInfo.ValueRank, expectedValueRank))
            {
                return default;
            }

            // check for special predefined types.
            if (expectedDataTypeId.NamespaceIndex == 0 &&
                expectedDataTypeId.TryGetValue(out uint numericId))
            {
                BuiltInType actualType = typeInfo.BuiltInType;

                switch (numericId)
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
                                    return default;
                                }

                                break;
                            case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                                return default;
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
                                    return default;
                                }

                                break;
                            case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                                return default;
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
                                    return default;
                                }

                                break;
                            case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                                return default;
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

                        return default;
                    case DataTypes.Structure:
                        if (typeInfo.BuiltInType == BuiltInType.ExtensionObject)
                        {
                            return typeInfo;
                        }

                        return default;
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

                return default;
            }

            // handle scalar.
            if (typeInfo.IsScalar)
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
                        return default;
                    }
                }

                // expected type is extension object so compare type tree.
                NodeId actualDataTypeId = typeInfo.GetDataTypeId(value, namespaceUris, typeTree);

                if (typeTree.IsTypeOf(actualDataTypeId, expectedDataTypeId))
                {
                    return typeInfo;
                }

                return default;
            }

            // Check each element in an array or matrix against the expected type.
            // Expand the variant into its elements. This handles multi-dimensional
            // arrays and matrices without needing to know the dimensions. However,
            // some edge cases are not handled (e.g. variant with array of variants
            // with variant arrays). The assumption is that this is not common and
            // therefore we can fail this situation here.
            foreach (Variant element in value.Expand())
            {
                TypeInfo elementInfo = IsInstanceOfDataType(
                    element,
                    expectedDataTypeId,
                    ValueRanks.Scalar, // Expands (mostly) to scalar elements --> see TODO in Variant.
                    namespaceUris,
                    typeTree);

                // give up at the first invalid element.
                if (elementInfo.IsUnknown)
                {
                    return default;
                }
            }

            // all elements valid.
            return typeInfo;
        }

        /// <summary>
        /// Returns the data type id that describes a value.
        /// </summary>
        /// <param name="value">The value to describe.</param>
        /// <param name="namespaceUris">The namespace uris.</param>
        /// <param name="typeTree">The type tree for a server.</param>
        /// <returns>Returns the data type identifier that describes a value.</returns>
        public NodeId GetDataTypeId(Variant value, NamespaceTable namespaceUris, ITypeTable typeTree)
        {
            if (BuiltInType == BuiltInType.Null)
            {
                return NodeId.Null;
            }

            if (BuiltInType == BuiltInType.ExtensionObject)
            {
                if (value.TryGetStructure(out IEncodeable encodeable))
                {
                    return ExpandedNodeId.ToNodeId(encodeable.TypeId, namespaceUris);
                }

                if (value.TryGetValue(out ExtensionObject extension))
                {
                    if (extension.TryGetValue(out encodeable))
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
        [Obsolete("Use TypeInfo property on Variant value directly")]
        public static TypeInfo Construct(Variant value)
        {
            return value.TypeInfo;
        }

        /// <summary>
        /// Returns the type info for the provided value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns><see cref="TypeInfo"/> instance storing information about
        /// the <paramref name="value"/> type.</returns>
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
        /// <returns><see cref="TypeInfo"/> instance storing information equivalent
        /// to the <paramref name="systemType"/> type.</returns>
        public static TypeInfo Construct(Type systemType)
        {
            // check for null.
            if (systemType == null)
            {
                return Unknown;
            }

            if (systemType.IsGenericType)
            {
                Type genericTypeDefinition = systemType.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(ArrayOf<>))
                {
                    return Construct(systemType.GetGenericArguments()[0]).WithValueRank(ValueRanks.OneDimension);
                }
                if (genericTypeDefinition == typeof(MatrixOf<>))
                {
                    return Construct(systemType.GetGenericArguments()[0]).WithValueRank(ValueRanks.TwoDimensions);
                }
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
                    // at least require IEnumerable implementation to avoid trying to represent types like
                    // Task<T> as T[].
                    if (!typeof(IEnumerable).IsAssignableFrom(systemType))
                    {
                        return Unknown;
                    }

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
        public static Variant GetDefaultVariantValue(BuiltInType type)
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
                    return (string)null;
                case BuiltInType.DateTime:
                    return DateTimeUtc.MinValue;
                case BuiltInType.Guid:
                    return Uuid.Empty;
                case BuiltInType.ByteString:
                    return ByteString.Empty;
                case BuiltInType.XmlElement:
                    return XmlElement.Empty;
                case BuiltInType.StatusCode:
                    return StatusCodes.Good;
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
                    return (DataValue)null;
                case BuiltInType.Enumeration:
                    return 0;
                case BuiltInType.Number:
                    return (double)0;
                case BuiltInType.Integer:
                    return (long)0;
                case BuiltInType.UInteger:
                    return (ulong)0;
                case BuiltInType.ExtensionObject:
                    return ExtensionObject.Null;
                case BuiltInType.Null:
                case BuiltInType.DiagnosticInfo:
                    return Variant.Null;
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
        public static Variant GetDefaultVariantValue(NodeId dataType, int valueRank)
        {
            return GetDefaultVariantValue(dataType, valueRank, null);
        }

        /// <summary>
        /// Returns the default value for the specified data type and value rank.
        /// </summary>
        /// <param name="dataType">The data type.</param>
        /// <param name="valueRank">The value rank.</param>
        /// <param name="typeTree">The type tree for a server.</param>
        /// <returns>A default value.</returns>
        public static Variant GetDefaultVariantValue(NodeId dataType, int valueRank, ITypeTable typeTree)
        {
            if (valueRank != ValueRanks.Scalar)
            {
                return default;
            }

            if (dataType.IsNull ||
                dataType.NamespaceIndex != 0 ||
                !dataType.TryGetValue(out uint id))
            {
                return GetDefaultValueInternal(dataType, typeTree);
            }

            if (id <= DataTypes.DiagnosticInfo)
            {
                // Handle built-in types.
                return GetDefaultVariantValue((BuiltInType)(int)id);
            }

            // Handle known UA types that derive from built in type
            switch (id)
            {
                case DataTypes.Duration:
                    return (double)0;
                case DataTypes.UtcTime:
                    return DateTimeUtc.MinValue;
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

            static Variant GetDefaultValueInternal(NodeId dataType, ITypeTable typeTree)
            {
                BuiltInType builtInType = GetBuiltInType(dataType, typeTree);
                if (builtInType != BuiltInType.Null)
                {
                    return GetDefaultVariantValue(builtInType);
                }
                return default;
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
                case "DateTimeUtc":
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
                case BuiltInType.DateTime:
                    return DateTimeUtc.MinValue;
                case BuiltInType.Guid:
                    return Uuid.Empty;
                case BuiltInType.StatusCode:
                    return StatusCodes.Good;
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
                case BuiltInType.Enumeration:
                    return 0;
                case BuiltInType.Number:
                    return (double)0;
                case BuiltInType.Integer:
                    return (long)0;
                case BuiltInType.UInteger:
                    return (ulong)0;
                case BuiltInType.ExtensionObject:
                    return ExtensionObject.Null;
                case BuiltInType.DataValue:
                case BuiltInType.Null:
                case BuiltInType.DiagnosticInfo:
                case BuiltInType.String:
                case BuiltInType.ByteString:
                case BuiltInType.XmlElement:
                    return null;
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {type}");
            }
        }

        /// <summary>
        /// Returns the default value for the specified built-in type.
        /// </summary>
        /// <param name="type">The Built-in type.</param>
        /// <param name="valueRank">The value rank.</param>
        /// <returns>The default value.</returns>
        /// <exception cref="ServiceResultException"></exception>
        public static object GetDefaultValue(BuiltInType type, int valueRank)
        {
            return GetDefaultValue((NodeId)(uint)type, valueRank);
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
                return default;
            }

            if (dataType.IsNull ||
                dataType.NamespaceIndex != 0 ||
                !dataType.TryGetValue(out uint id))
            {
                return GetDefaultValueInternal(dataType, typeTree);
            }

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
                    return DateTimeUtc.MinValue;
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
                return default;
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
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "Array.CreateInstance is used with known OPC UA built-in types.")]
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
                        return new DateTimeUtc[length];
                    case BuiltInType.Guid:
                        return new Uuid[length];
                    case BuiltInType.ByteString:
                        return new ByteString[length];
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
                        return Array.CreateInstance(typeof(DateTimeUtc), dimensions);
                    case BuiltInType.Guid:
                        return Array.CreateInstance(typeof(Uuid), dimensions);
                    case BuiltInType.ByteString:
                        return Array.CreateInstance(typeof(ByteString), dimensions);
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
        /// Returns the data type id that describes a value.
        /// </summary>
        /// <param name="type">The framework type.</param>
        /// <param name="namespaceTable">The namespace table.</param>
        /// <returns>An data type identifier for a node in a server's address space.</returns>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2067",
            Justification = "IEncodeable types registered in the factory have parameterless constructors preserved.")]
        public static NodeId GetDataTypeId(Type type, NamespaceTable namespaceTable = null)
        {
            TypeInfo typeInfo = Construct(type);

            NodeId dataTypeId = GetDataTypeId(typeInfo);

            if (dataTypeId.IsNull)
            {
                if (type.GetTypeInfo().IsEnum ||
                    (type.IsArray && type.GetElementType().GetTypeInfo().IsEnum))
                {
                    return DataTypeIds.Enumeration;
                }
            }

            // Check if the type implements IEncodeable and has a specific TypeId
            if (dataTypeId == DataTypeIds.Structure &&
                typeof(IEncodeable).GetTypeInfo().IsAssignableFrom(type.GetTypeInfo()))
            {
                // Try to create an instance and get its TypeId
                // All well-known IEncodeable types have parameterless constructors and instance TypeId properties
                try
                {
                    var instance = Activator.CreateInstance(type) as IEncodeable;
                    if (instance?.TypeId != null)
                    {
                        namespaceTable ??= AmbientMessageContext.CurrentContext?.NamespaceUris;
                        return ExpandedNodeId.ToNodeId(instance.TypeId, namespaceTable);
                    }
                }
                catch (MissingMethodException)
                {
                    // Type doesn't have a parameterless constructor, fall back to Structure
                }
                catch (TargetInvocationException)
                {
                    // Constructor threw an exception, fall back to Structure
                }
                catch (MethodAccessException)
                {
                    // No permission to create instance, fall back to Structure
                }
                catch (NotSupportedException)
                {
                    // Type is not supported for instantiation, fall back to Structure
                }
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
            // Check for [DataType] attribute (source-generated IEncodeable types)
            if (DataTypeAttribute.TryGetTypeIdsFromType(
                systemType,
                out ExpandedNodeId typeId,
                out _, out _) &&
                !typeId.IsNull)
            {
                return new XmlQualifiedName(systemType.Name, typeId.NamespaceUri);
            }

            return new XmlQualifiedName(systemType.Name, Namespaces.OpcUaXsd);
        }

        /// <summary>
        /// Returns the xml qualified name for the specified object.
        /// </summary>
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
        /// System type wrapper
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private record class SystemType<T>(BuiltInType BuiltInType) :
            IBuiltInType
        {
            /// <inheritdoc/>
            public Type Type => typeof(T);

            /// <inheritdoc/>
            public XmlQualifiedName XmlName =>
                new(BuiltInType.ToString(), Namespaces.OpcUaXsd);
        }

        private readonly short m_valueRank;
        private readonly byte m_valid;
        private readonly byte m_builtInType;
    }
}
