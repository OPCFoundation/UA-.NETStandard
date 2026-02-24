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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Xml;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// A structure that could contain value with any of the UA built-in
    /// data types.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Variant is described in <b>Part 6 - Mappings, Section 6.2.2.15</b>,
    /// titled <b>Variant</b>
    /// <br/></para>
    /// <para>
    /// Variant is a data type in COM, but not within the .NET Framework.
    /// Therefore OPC UA has its own Variant type that supports all of the
    /// OPC UA data-types.
    /// <br/></para>
    /// </remarks>
    public readonly struct Variant :
        IFormattable,
        IEquatable<Variant>,
        IEquatable<bool>, IVariantOf<bool>,
        IEquatable<sbyte>, IVariantOf<sbyte>,
        IEquatable<byte>, IVariantOf<byte>,
        IEquatable<short>, IVariantOf<short>,
        IEquatable<ushort>, IVariantOf<ushort>,
        IEquatable<int>, IVariantOf<int>,
        IEquatable<Enum>,
        IEquatable<uint>, IVariantOf<uint>,
        IEquatable<long>, IVariantOf<long>,
        IEquatable<ulong>, IVariantOf<ulong>,
        IEquatable<float>, IVariantOf<float>,
        IEquatable<double>, IVariantOf<double>,
        IEquatable<string>, IVariantOf<string>,
        IEquatable<DateTime>, IVariantOf<DateTime>,
        IEquatable<Uuid>, IVariantOf<Uuid>,
        IEquatable<ByteString>, IVariantOf<ByteString>,
        IEquatable<XmlElement>, IVariantOf<XmlElement>,
        IEquatable<NodeId>, IVariantOf<NodeId>,
        IEquatable<ExpandedNodeId>, IVariantOf<ExpandedNodeId>,
        IEquatable<StatusCode>, IVariantOf<StatusCode>,
        IEquatable<QualifiedName>, IVariantOf<QualifiedName>,
        IEquatable<LocalizedText>, IVariantOf<LocalizedText>,
        IEquatable<ExtensionObject>, IVariantOf<ExtensionObject>,
        IEquatable<DataValue>, IVariantOf<DataValue>,
        IEquatable<ArrayOf<bool>>, IVariantOf<ArrayOf<bool>>,
        IEquatable<ArrayOf<sbyte>>, IVariantOf<ArrayOf<sbyte>>,
        IEquatable<ArrayOf<byte>>, IVariantOf<ArrayOf<byte>>,
        IEquatable<ArrayOf<short>>, IVariantOf<ArrayOf<short>>,
        IEquatable<ArrayOf<ushort>>, IVariantOf<ArrayOf<ushort>>,
        IEquatable<ArrayOf<int>>, IVariantOf<ArrayOf<int>>,
        IEquatable<ArrayOf<Enum>>,
        IEquatable<ArrayOf<uint>>, IVariantOf<ArrayOf<uint>>,
        IEquatable<ArrayOf<long>>, IVariantOf<ArrayOf<long>>,
        IEquatable<ArrayOf<ulong>>, IVariantOf<ArrayOf<ulong>>,
        IEquatable<ArrayOf<float>>, IVariantOf<ArrayOf<float>>,
        IEquatable<ArrayOf<double>>, IVariantOf<ArrayOf<double>>,
        IEquatable<ArrayOf<string>>, IVariantOf<ArrayOf<string>>,
        IEquatable<ArrayOf<DateTime>>, IVariantOf<ArrayOf<DateTime>>,
        IEquatable<ArrayOf<Uuid>>, IVariantOf<ArrayOf<Uuid>>,
        IEquatable<ArrayOf<ByteString>>, IVariantOf<ArrayOf<ByteString>>,
        IEquatable<ArrayOf<XmlElement>>, IVariantOf<ArrayOf<XmlElement>>,
        IEquatable<ArrayOf<NodeId>>, IVariantOf<ArrayOf<NodeId>>,
        IEquatable<ArrayOf<ExpandedNodeId>>, IVariantOf<ArrayOf<ExpandedNodeId>>,
        IEquatable<ArrayOf<StatusCode>>, IVariantOf<ArrayOf<StatusCode>>,
        IEquatable<ArrayOf<QualifiedName>>, IVariantOf<ArrayOf<QualifiedName>>,
        IEquatable<ArrayOf<LocalizedText>>, IVariantOf<ArrayOf<LocalizedText>>,
        IEquatable<ArrayOf<ExtensionObject>>, IVariantOf<ArrayOf<ExtensionObject>>,
        IEquatable<ArrayOf<DataValue>>, IVariantOf<ArrayOf<DataValue>>,
        IEquatable<ArrayOf<Variant>>, IVariantOf<ArrayOf<Variant>>,
        IEquatable<MatrixOf<bool>>, IVariantOf<MatrixOf<bool>>,
        IEquatable<MatrixOf<sbyte>>, IVariantOf<MatrixOf<sbyte>>,
        IEquatable<MatrixOf<byte>>, IVariantOf<MatrixOf<byte>>,
        IEquatable<MatrixOf<short>>, IVariantOf<MatrixOf<short>>,
        IEquatable<MatrixOf<ushort>>, IVariantOf<MatrixOf<ushort>>,
        IEquatable<MatrixOf<int>>, IVariantOf<MatrixOf<int>>,
        IEquatable<MatrixOf<Enum>>,
        IEquatable<MatrixOf<uint>>, IVariantOf<MatrixOf<uint>>,
        IEquatable<MatrixOf<long>>, IVariantOf<MatrixOf<long>>,
        IEquatable<MatrixOf<ulong>>, IVariantOf<MatrixOf<ulong>>,
        IEquatable<MatrixOf<float>>, IVariantOf<MatrixOf<float>>,
        IEquatable<MatrixOf<double>>, IVariantOf<MatrixOf<double>>,
        IEquatable<MatrixOf<string>>, IVariantOf<MatrixOf<string>>,
        IEquatable<MatrixOf<DateTime>>, IVariantOf<MatrixOf<DateTime>>,
        IEquatable<MatrixOf<Uuid>>, IVariantOf<MatrixOf<Uuid>>,
        IEquatable<MatrixOf<ByteString>>, IVariantOf<MatrixOf<ByteString>>,
        IEquatable<MatrixOf<XmlElement>>, IVariantOf<MatrixOf<XmlElement>>,
        IEquatable<MatrixOf<NodeId>>, IVariantOf<MatrixOf<NodeId>>,
        IEquatable<MatrixOf<ExpandedNodeId>>, IVariantOf<MatrixOf<ExpandedNodeId>>,
        IEquatable<MatrixOf<StatusCode>>, IVariantOf<MatrixOf<StatusCode>>,
        IEquatable<MatrixOf<QualifiedName>>, IVariantOf<MatrixOf<QualifiedName>>,
        IEquatable<MatrixOf<LocalizedText>>, IVariantOf<MatrixOf<LocalizedText>>,
        IEquatable<MatrixOf<ExtensionObject>>, IVariantOf<MatrixOf<ExtensionObject>>,
        IEquatable<MatrixOf<DataValue>>, IVariantOf<MatrixOf<DataValue>>,
        IEquatable<MatrixOf<Variant>>, IVariantOf<MatrixOf<Variant>>
    {
        /// <summary>
        /// Creates a new Variant with a Boolean value.
        /// </summary>
        /// <param name="value">The value of the variant</param>
        public Variant(bool value)
        {
            m_union.Boolean = value;
            m_typeInfo = TypeInfo.Scalars.Boolean;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="sbyte"/> value
        /// </summary>
        /// <param name="value">The <see cref="sbyte"/> value of the Variant</param>
        public Variant(sbyte value)
        {
            m_union.SByte = value;
            m_typeInfo = TypeInfo.Scalars.SByte;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="byte"/> value
        /// </summary>
        /// <param name="value">The <see cref="byte"/> value of the Variant</param>
        public Variant(byte value)
        {
            m_union.Byte = value;
            m_typeInfo = TypeInfo.Scalars.Byte;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="short"/> value
        /// </summary>
        /// <param name="value">The <see cref="short"/> value of the Variant</param>
        public Variant(short value)
        {
            m_union.Int16 = value;
            m_typeInfo = TypeInfo.Scalars.Int16;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="ushort"/> value
        /// </summary>
        /// <param name="value">The <see cref="ushort"/> value of the Variant</param>
        public Variant(ushort value)
        {
            m_union.UInt16 = value;
            m_typeInfo = TypeInfo.Scalars.UInt16;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="int"/> value
        /// </summary>
        /// <param name="value">The <see cref="int"/> value of the Variant</param>
        public Variant(int value)
        {
            m_union.Int32 = value;
            m_typeInfo = TypeInfo.Scalars.Int32;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="uint"/> value
        /// </summary>
        /// <param name="value">The <see cref="uint"/> value of the Variant</param>
        public Variant(uint value)
        {
            m_union.UInt32 = value;
            m_typeInfo = TypeInfo.Scalars.UInt32;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="long"/> value
        /// </summary>
        /// <param name="value">The <see cref="long"/> value of the Variant</param>
        public Variant(long value)
        {
            m_union.Int64 = value;
            m_typeInfo = TypeInfo.Scalars.Int64;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="ulong"/> value
        /// </summary>
        /// <param name="value">The <see cref="ulong"/> value of the Variant</param>
        public Variant(ulong value)
        {
            m_union.UInt64 = value;
            m_typeInfo = TypeInfo.Scalars.UInt64;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="float"/> value
        /// </summary>
        /// <param name="value">The <see cref="float"/> value of the Variant</param>
        public Variant(float value)
        {
            m_union.Float = value;
            m_typeInfo = TypeInfo.Scalars.Float;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="double"/> value
        /// </summary>
        /// <param name="value">The <see cref="double"/> value of the Variant</param>
        public Variant(double value)
        {
            m_union.Double = value;
            m_typeInfo = TypeInfo.Scalars.Double;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="string"/> value
        /// </summary>
        /// <param name="value">The <see cref="string"/> value of the Variant</param>
        public Variant(string value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.String;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="DateTime"/> value
        /// </summary>
        /// <param name="value">The <see cref="DateTime"/> value of the Variant</param>
        public Variant(DateTime value)
        {
            m_union.DateTime = value;
            m_typeInfo = TypeInfo.Scalars.DateTime;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="Uuid"/> value
        /// </summary>
        /// <param name="value">The <see cref="Uuid"/> value of the Variant</param>
        public Variant(Uuid value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.Guid;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="ByteString"/> value
        /// </summary>
        /// <param name="value">The <see cref="ByteString"/> value of the Variant</param>
        public Variant(ByteString value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.ByteString;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="XmlElement"/> value
        /// </summary>
        /// <param name="value">The <see cref="XmlElement"/> value of the Variant</param>
        public Variant(XmlElement value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.XmlElement;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="NodeId"/> value
        /// </summary>
        /// <param name="value">The <see cref="NodeId"/> value of the Variant</param>
        public Variant(NodeId value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.NodeId;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="ExpandedNodeId"/> value
        /// </summary>
        /// <param name="value">The <see cref="ExpandedNodeId"/> value of the Variant</param>
        public Variant(ExpandedNodeId value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.ExpandedNodeId;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="StatusCode"/> value
        /// </summary>
        /// <param name="value">The <see cref="StatusCode"/> value of the Variant</param>
        public Variant(StatusCode value)
        {
            m_union.UInt32 = value.Code;
            m_value = value.SymbolicId;
            m_typeInfo = TypeInfo.Scalars.StatusCode;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="QualifiedName"/> value
        /// </summary>
        /// <param name="value">The <see cref="QualifiedName"/> value of the Variant</param>
        public Variant(QualifiedName value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.QualifiedName;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="LocalizedText"/> value
        /// </summary>
        /// <param name="value">The <see cref="LocalizedText"/> value of the Variant</param>
        public Variant(LocalizedText value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.LocalizedText;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="ExtensionObject"/> value
        /// </summary>
        /// <param name="value">The <see cref="ExtensionObject"/> value of the Variant</param>
        public Variant(ExtensionObject value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Scalars.ExtensionObject;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="DataValue"/> value
        /// </summary>
        /// <param name="value">The <see cref="DataValue"/> value of the Variant</param>
        public Variant(DataValue value)
        {
            m_value = CoreUtils.Clone(value);
            m_typeInfo = TypeInfo.Scalars.DataValue;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="bool"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="bool"/>-array value of the Variant</param>
        public Variant(ArrayOf<bool> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Boolean;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="sbyte"/>-arrat value
        /// </summary>
        /// <param name="value">The <see cref="sbyte"/>-array value of the Variant</param>
        public Variant(ArrayOf<sbyte> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.SByte;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="byte"/>-arrat value
        /// </summary>
        /// <param name="value">The <see cref="byte"/>-array value of the Variant</param>
        public Variant(ArrayOf<byte> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Byte;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="short"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="short"/>-array value of the Variant</param>
        public Variant(ArrayOf<short> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Int16;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="ushort"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="ushort"/>-array value of the Variant</param>
        public Variant(ArrayOf<ushort> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.UInt16;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="int"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="int"/>-array value of the Variant</param>
        public Variant(ArrayOf<int> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Int32;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="Enum"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="Enum"/>-array value of the Variant</param>
        [OverloadResolutionPriority(1)]
        public Variant(ArrayOf<Enum> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Enumeration;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="uint"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="uint"/>-array value of the Variant</param>
        public Variant(ArrayOf<uint> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.UInt32;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="long"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="long"/>-array value of the Variant</param>
        public Variant(ArrayOf<long> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Int64;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="ulong"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="ulong"/>-array value of the Variant</param>
        public Variant(ArrayOf<ulong> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.UInt64;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="float"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="float"/>-array value of the Variant</param>
        public Variant(ArrayOf<float> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Float;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="double"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="double"/>-array value of the Variant</param>
        public Variant(ArrayOf<double> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Double;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="string"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="string"/>-array value of the Variant</param>
        public Variant(ArrayOf<string> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.String;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="DateTime"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="DateTime"/>-array value of the Variant</param>
        public Variant(ArrayOf<DateTime> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.DateTime;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="Uuid"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="Uuid"/>-array value of the Variant</param>
        public Variant(ArrayOf<Uuid> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Guid;
        }

        /// <summary>
        /// Creates a new variant with a 2-d <see cref="ByteString"/>-array value
        /// </summary>
        /// <param name="value">The 2-d <see cref="ByteString"/>-array value of the Variant</param>
        public Variant(ArrayOf<ByteString> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.ByteString;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="XmlElement"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="XmlElement"/>-array value of the Variant</param>
        public Variant(ArrayOf<XmlElement> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.XmlElement;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="NodeId"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="NodeId"/>-array value of the Variant</param>
        public Variant(ArrayOf<NodeId> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.NodeId;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="ExpandedNodeId"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="ExpandedNodeId"/>-array value of the Variant</param>
        public Variant(ArrayOf<ExpandedNodeId> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.ExpandedNodeId;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="StatusCode"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="StatusCode"/>-array value of the Variant</param>
        public Variant(ArrayOf<StatusCode> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.StatusCode;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="QualifiedName"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="QualifiedName"/>-array value of the Variant</param>
        public Variant(ArrayOf<QualifiedName> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.QualifiedName;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="LocalizedText"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="LocalizedText"/>-array value of the Variant</param>
        public Variant(ArrayOf<LocalizedText> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.LocalizedText;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="ExtensionObject"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="ExtensionObject"/>-array value of the Variant</param>
        public Variant(ArrayOf<ExtensionObject> value)
        {
            m_value = CoreUtils.Clone(value);
            m_typeInfo = TypeInfo.Arrays.ExtensionObject;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="DataValue"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="DataValue"/>-array value of the Variant</param>
        public Variant(ArrayOf<DataValue> value)
        {
            m_value = CoreUtils.Clone(value);
            m_typeInfo = TypeInfo.Arrays.DataValue;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="Variant"/>-array value
        /// </summary>
        /// <param name="value">The <see cref="Variant"/>-array value of the Variant</param>
        public Variant(ArrayOf<Variant> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Arrays.Variant;
        }

        /// <summary>
        /// Creates a new variant with a <see cref="bool"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="bool"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<bool> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.Boolean,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="sbyte"/>-arrat value
        /// </summary>
        /// <param name="value">The <see cref="sbyte"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<sbyte> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.SByte,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="byte"/>-arrat value
        /// </summary>
        /// <param name="value">The <see cref="byte"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<byte> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.Byte,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="short"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="short"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<short> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.Int16,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="ushort"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="ushort"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<ushort> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.UInt16,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="int"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="int"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<int> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.Int32,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="Enum"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="Enum"/>-matrix value of the Variant</param>
        [OverloadResolutionPriority(1)]
        public Variant(MatrixOf<Enum> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.Enumeration,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="uint"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="uint"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<uint> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.UInt32,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="long"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="long"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<long> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.Int64,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="ulong"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="ulong"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<ulong> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.UInt64,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="float"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="float"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<float> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.Float,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="double"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="double"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<double> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.Double,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="string"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="string"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<string> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.String,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="DateTime"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="DateTime"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<DateTime> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.DateTime,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="Uuid"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="Uuid"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<Uuid> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.Guid,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a 2-d <see cref="ByteString"/>-matrix value
        /// </summary>
        /// <param name="value">The 2-d <see cref="ByteString"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<ByteString> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.ByteString,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="XmlElement"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="XmlElement"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<XmlElement> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.XmlElement,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="NodeId"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="NodeId"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<NodeId> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.NodeId,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="ExpandedNodeId"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="ExpandedNodeId"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<ExpandedNodeId> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.ExpandedNodeId,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="StatusCode"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="StatusCode"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<StatusCode> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.StatusCode,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="QualifiedName"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="QualifiedName"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<QualifiedName> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.QualifiedName,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="LocalizedText"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="LocalizedText"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<LocalizedText> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.LocalizedText,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="ExtensionObject"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="ExtensionObject"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<ExtensionObject> value)
        {
            m_value = CoreUtils.Clone(value);
            m_typeInfo = TypeInfo.Create(
                BuiltInType.ExtensionObject,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="DataValue"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="DataValue"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<DataValue> value)
        {
            m_value = CoreUtils.Clone(value);
            m_typeInfo = TypeInfo.Create(
                BuiltInType.DataValue,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Creates a new variant with a <see cref="Variant"/>-matrix value
        /// </summary>
        /// <param name="value">The <see cref="Variant"/>-matrix value of the Variant</param>
        public Variant(MatrixOf<Variant> value)
        {
            m_value = value;
            m_typeInfo = TypeInfo.Create(
                BuiltInType.Variant,
                value.Dimensions.Length);
        }

        /// <summary>
        /// Constructs a Variant
        /// </summary>
        /// <param name="value">The value to store.</param>
        /// <param name="typeInfo">The type information for the value.</param>
        [JsonConstructor]
        public Variant(object value, TypeInfo typeInfo)
        {
            VariantHelper.TryCastFrom(value, out Variant variant);
            this = variant;
            m_typeInfo = typeInfo;
        }

        /// <summary>
        /// Creates a new variant instance while specifying the value.
        /// </summary>
        /// <param name="value">The value to encode within the variant</param>
        [OverloadResolutionPriority(0)]
        //[Obsolete("Use TryGet pattern to access values or AsBoxedObject.")]
        public Variant(object value)
        {
            VariantHelper.TryCastFromWithReflectionFallback(value, out Variant variant);
            this = variant;
        }

        /// <summary>
        /// Creates a new variant instance from legacy Matrix
        /// </summary>
        /// <param name="value"></param>
        //[Obsolete("Use MatrixOf<T> instead of Matrix.")]
        public Variant(Matrix value)
        {
            VariantHelper.TryCastFrom(value, out Variant variant);
            this = variant;
        }

        /// <summary>
        /// Private constructor for internal use.
        /// </summary>
        private Variant(Union union, TypeInfo typeInfo, object value = null)
        {
            m_union = union;
            m_value = value;
            m_typeInfo = typeInfo;
        }

        /// <summary>
        /// Box the value stored in the Variant as object
        /// </summary>
        /// <returns></returns>
        public object AsBoxedObject()
        {
            return AsBoxedObject(false);
        }

        /// <summary>
        /// An constant containing a null Variant structure.
        /// </summary>
        public static readonly Variant Null;

        /// <summary>
        /// Returns if the Variant is a Null value.
        /// </summary>
        [JsonIgnore]
        public bool IsNull => TypeInfo.IsUnknown;

        /// <summary>
        /// The value stored -as <see cref="object"/>- within the
        /// Variant object. All arrays and matrices are returned
        /// as <see cref="Array"/>.
        /// </summary>
        [JsonIgnore]
        //[Obsolete("Use TryGet pattern to access values or AsBoxedObject.")]
        public object Value => AsBoxedObject(returnLegacyTypes: true);

        /// <summary>
        /// The type information for the matrix.
        /// </summary>
        [JsonPropertyName("TypeInfo")]
#pragma warning disable RCS1085 // Use auto-implemented property
        public TypeInfo TypeInfo => m_typeInfo;
#pragma warning restore RCS1085 // Use auto-implemented property

        [JsonPropertyName("Value")]
        internal object Raw => AsBoxedObject(returnLegacyTypes: true);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            if (IsNull)
            {
                return 0;
            }
            if (TypeInfo.IsScalar)
            {
                return TypeInfo.BuiltInType switch
                {
                    BuiltInType.Null => 0,
                    BuiltInType.Boolean or
                    BuiltInType.SByte or
                    BuiltInType.Byte or
                    BuiltInType.Int16 or
                    BuiltInType.UInt16 or
                    BuiltInType.Int32 or
                    BuiltInType.UInt32 or
                    BuiltInType.DateTime or
                    BuiltInType.StatusCode or
                    BuiltInType.Float => m_union.Int32,
#if NET8_0_OR_GREATER
                    BuiltInType.Enumeration or
#endif
                    BuiltInType.Int64 or
                    BuiltInType.UInt64 or
                    BuiltInType.Double => m_union.UInt64.GetHashCode(),
                    _ => m_value?.GetHashCode() ?? 0
                };
            }
            if (TypeInfo.IsArray)
            {
                return TypeInfo.BuiltInType switch
                {
                    BuiltInType.Boolean => SequenceEqualityComparer<bool>.Default
                        .GetHashCode(m_value as bool[]),
                    BuiltInType.SByte => SequenceEqualityComparer<sbyte>.Default
                        .GetHashCode(m_value as sbyte[]),
                    BuiltInType.Byte => SequenceEqualityComparer<byte>.Default
                        .GetHashCode(m_value as byte[]),
                    BuiltInType.Int16 => SequenceEqualityComparer<short>.Default
                        .GetHashCode(m_value as short[]),
                    BuiltInType.UInt16 => SequenceEqualityComparer<ushort>.Default
                        .GetHashCode(m_value as ushort[]),
                    BuiltInType.Int32 => SequenceEqualityComparer<int>.Default
                        .GetHashCode(m_value as int[]),
                    BuiltInType.UInt32 => SequenceEqualityComparer<uint>.Default
                        .GetHashCode(m_value as uint[]),
                    BuiltInType.Int64 => SequenceEqualityComparer<long>.Default
                        .GetHashCode(m_value as long[]),
                    BuiltInType.UInt64 => SequenceEqualityComparer<ulong>.Default
                        .GetHashCode(m_value as ulong[]),
                    BuiltInType.Float => SequenceEqualityComparer<float>.Default
                        .GetHashCode(m_value as float[]),
                    BuiltInType.Double => SequenceEqualityComparer<double>.Default
                        .GetHashCode(m_value as double[]),
                    BuiltInType.DateTime => DateTimeArrayComparer.Default
                        .GetHashCode(m_value as DateTime[]),
                    BuiltInType.StatusCode => ArrayEqualityComparer<StatusCode>.Default
                        .GetHashCode(m_value as StatusCode[]),
                    BuiltInType.Guid => SequenceEqualityComparer<Uuid>.Default
                        .GetHashCode(m_value as Uuid[]),
                    BuiltInType.XmlElement => ArrayEqualityComparer<XmlElement>.Default
                        .GetHashCode(m_value as XmlElement[]),
                    BuiltInType.String => ArrayEqualityComparer<string>.Default
                        .GetHashCode(m_value as string[]),
                    BuiltInType.NodeId => ArrayEqualityComparer<NodeId>.Default
                        .GetHashCode(m_value as NodeId[]),
                    BuiltInType.ExpandedNodeId => ArrayEqualityComparer<ExpandedNodeId>.Default
                        .GetHashCode(m_value as ExpandedNodeId[]),
                    BuiltInType.QualifiedName => ArrayEqualityComparer<QualifiedName>.Default
                        .GetHashCode(m_value as QualifiedName[]),
                    BuiltInType.LocalizedText => ArrayEqualityComparer<LocalizedText>.Default
                        .GetHashCode(m_value as LocalizedText[]),
                    BuiltInType.ExtensionObject => ArrayEqualityComparer<ExtensionObject>.Default
                        .GetHashCode(m_value as ExtensionObject[]),
                    BuiltInType.DataValue => ArrayEqualityComparer<DataValue>.Default
                        .GetHashCode(m_value as DataValue[]),
                    BuiltInType.DiagnosticInfo => ArrayEqualityComparer<DiagnosticInfo>
                        .Default.GetHashCode(m_value as DiagnosticInfo[]),
                    BuiltInType.Variant => ArrayEqualityComparer<Variant>.Default
                        .GetHashCode(m_value as Variant[]),
                    BuiltInType.ByteString => ArrayEqualityComparer<ByteString>.Default
                        .GetHashCode(m_value as ByteString[]),
                    _ => 0
                };
            }
            return m_value?.GetHashCode() ?? 0;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return ToString(null, CultureInfo.InvariantCulture);
        }

        /// <inheritdoc/>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                return ToStringCore(formatProvider);
            }

            throw new FormatException(
                CoreUtils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Converts the variant to a bool value or returns the default.
        /// </summary>
        public bool GetBoolean(bool defaultValue = default)
        {
            return TryGet(out bool v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a sbyte value or returns the default.
        /// </summary>
        public sbyte GetSByte(sbyte defaultValue = default)
        {
            return TryGet(out sbyte v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a byte value or returns the default.
        /// </summary>
        public byte GetByte(byte defaultValue = default)
        {
            return TryGet(out byte v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a short value or returns the default.
        /// </summary>
        public short GetInt16(short defaultValue = default)
        {
            return TryGet(out short v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a ushort value or returns the default.
        /// </summary>
        public ushort GetUInt16(ushort defaultValue = default)
        {
            return TryGet(out ushort v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a int value or returns the default.
        /// </summary>
        public int GetInt32(int defaultValue = default)
        {
            return TryGet(out int v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a enum value or returns the default.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public T GetEnumeration<T>(T defaultValue = default) where T : Enum
        {
            return TryGet(out T v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a structure of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public T GetStructure<T>(
            T defaultValue = default,
            IServiceMessageContext context = null) where T : IEncodeable
        {
            return TryGet(out T v, context) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a uint value or returns the default.
        /// </summary>
        public uint GetUInt32(uint defaultValue = default)
        {
            return TryGet(out uint v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a long value or returns the default.
        /// </summary>
        public long GetInt64(long defaultValue = default)
        {
            return TryGet(out long v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a ulong value or returns the default.
        /// </summary>
        public ulong GetUInt64(ulong defaultValue = default)
        {
            return TryGet(out ulong v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a float value or returns the default.
        /// </summary>
        public float GetFloat(float defaultValue = default)
        {
            return TryGet(out float v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a double value or returns the default.
        /// </summary>
        public double GetDouble(double defaultValue = default)
        {
            return TryGet(out double v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a string value or returns the default.
        /// </summary>
        public string GetString(string defaultValue = default)
        {
            return TryGet(out string v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a DateTime value or returns the default.
        /// </summary>
        public DateTime GetDateTime(DateTime defaultValue = default)
        {
            return TryGet(out DateTime v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a Uuid value or returns the default.
        /// </summary>
        public Uuid GetGuid(Uuid defaultValue = default)
        {
            return TryGet(out Uuid v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a ByteString value or returns the default.
        /// </summary>
        public ByteString GetByteString(ByteString defaultValue = default)
        {
            return TryGet(out ByteString v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a XmlElement value or returns the default.
        /// </summary>
        public XmlElement GetXmlElement(XmlElement defaultValue = default)
        {
            return TryGet(out XmlElement v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a NodeId value or returns the default.
        /// </summary>
        public NodeId GetNodeId(NodeId defaultValue = default)
        {
            return TryGet(out NodeId v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a ExpandedNodeId value or returns the default.
        /// </summary>
        public ExpandedNodeId GetExpandedNodeId(ExpandedNodeId defaultValue = default)
        {
            return TryGet(out ExpandedNodeId v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a StatusCode value or returns the default.
        /// </summary>
        public StatusCode GetStatusCode(StatusCode defaultValue = default)
        {
            return TryGet(out StatusCode v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a QualifiedName value or returns the default.
        /// </summary>
        public QualifiedName GetQualifiedName(QualifiedName defaultValue = default)
        {
            return TryGet(out QualifiedName v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a LocalizedText value or returns the default.
        /// </summary>
        public LocalizedText GetLocalizedText(LocalizedText defaultValue = default)
        {
            return TryGet(out LocalizedText v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a ExtensionObject value or returns the default.
        /// </summary>
        public ExtensionObject GetExtensionObject(ExtensionObject defaultValue = default)
        {
            return TryGet(out ExtensionObject v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a DataValue value or returns the default.
        /// </summary>
        public DataValue GetDataValue(DataValue defaultValue = default)
        {
            return TryGet(out DataValue v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a bool-array value or returns the default.
        /// </summary>
        public ArrayOf<bool> GetBooleanArray(ArrayOf<bool> defaultValue = default)
        {
            return TryGet(out ArrayOf<bool> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a sbyte-array value or returns the default.
        /// </summary>
        public ArrayOf<sbyte> GetSByteArray(ArrayOf<sbyte> defaultValue = default)
        {
            return TryGet(out ArrayOf<sbyte> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a byte-array value or returns the default.
        /// </summary>
        public ArrayOf<byte> GetByteArray(ArrayOf<byte> defaultValue = default)
        {
            return TryGet(out ArrayOf<byte> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a short-array value or returns the default.
        /// </summary>
        public ArrayOf<short> GetInt16Array(ArrayOf<short> defaultValue = default)
        {
            return TryGet(out ArrayOf<short> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a ushort-array value or returns the default.
        /// </summary>
        public ArrayOf<ushort> GetUInt16Array(ArrayOf<ushort> defaultValue = default)
        {
            return TryGet(out ArrayOf<ushort> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a int-array value or returns the default.
        /// </summary>
        public ArrayOf<int> GetInt32Array(ArrayOf<int> defaultValue = default)
        {
            return TryGet(out ArrayOf<int> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a enum array value or returns the default.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public ArrayOf<T> GetEnumerationArray<T>(ArrayOf<T> defaultValue = default) where T : Enum
        {
            return TryGet(out ArrayOf<T> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a structure of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public ArrayOf<T> GetStructureArray<T>(
            ArrayOf<T> defaultValue = default,
            IServiceMessageContext context = null) where T : IEncodeable
        {
            return TryGet(out ArrayOf<T> v, context) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a uint-array value or returns the default.
        /// </summary>
        public ArrayOf<uint> GetUInt32Array(ArrayOf<uint> defaultValue = default)
        {
            return TryGet(out ArrayOf<uint> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a long-array value or returns the default.
        /// </summary>
        public ArrayOf<long> GetInt64Array(ArrayOf<long> defaultValue = default)
        {
            return TryGet(out ArrayOf<long> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a ulong-array value or returns the default.
        /// </summary>
        public ArrayOf<ulong> GetUInt64Array(ArrayOf<ulong> defaultValue = default)
        {
            return TryGet(out ArrayOf<ulong> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a float-array value or returns the default.
        /// </summary>
        public ArrayOf<float> GetFloatArray(ArrayOf<float> defaultValue = default)
        {
            return TryGet(out ArrayOf<float> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a double-array value or returns the default.
        /// </summary>
        public ArrayOf<double> GetDoubleArray(ArrayOf<double> defaultValue = default)
        {
            return TryGet(out ArrayOf<double> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a string []value or returns the default.
        /// </summary>
        public ArrayOf<string> GetStringArray(ArrayOf<string> defaultValue = default)
        {
            return TryGet(out ArrayOf<string> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a DateTime-array value or returns the default.
        /// </summary>
        public ArrayOf<DateTime> GetDateTimeArray(ArrayOf<DateTime> defaultValue = default)
        {
            return TryGet(out ArrayOf<DateTime> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a Uuid-array value or returns the default.
        /// </summary>
        public ArrayOf<Uuid> GetGuidArray(ArrayOf<Uuid> defaultValue = default)
        {
            return TryGet(out ArrayOf<Uuid> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a byte[]-array value or returns the default.
        /// </summary>
        public ArrayOf<ByteString> GetByteStringArray(ArrayOf<ByteString> defaultValue = default)
        {
            return TryGet(out ArrayOf<ByteString> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a XmlElement-array value or returns the default.
        /// </summary>
        public ArrayOf<XmlElement> GetXmlElementArray(ArrayOf<XmlElement> defaultValue = default)
        {
            return TryGet(out ArrayOf<XmlElement> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a NodeId-array value or returns the default.
        /// </summary>
        public ArrayOf<NodeId> GetNodeIdArray(ArrayOf<NodeId> defaultValue = default)
        {
            return TryGet(out ArrayOf<NodeId> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a ExpandedNodeId-array value or returns the default.
        /// </summary>
        public ArrayOf<ExpandedNodeId> GetExpandedNodeIdArray(ArrayOf<ExpandedNodeId> defaultValue = default)
        {
            return TryGet(out ArrayOf<ExpandedNodeId> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a StatusCode-array value or returns the default.
        /// </summary>
        public ArrayOf<StatusCode> GetStatusCodeArray(ArrayOf<StatusCode> defaultValue = default)
        {
            return TryGet(out ArrayOf<StatusCode> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a QualifiedName-array value or returns the default.
        /// </summary>
        public ArrayOf<QualifiedName> GetQualifiedNameArray(ArrayOf<QualifiedName> defaultValue = default)
        {
            return TryGet(out ArrayOf<QualifiedName> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a LocalizedText-array value or returns the default.
        /// </summary>
        public ArrayOf<LocalizedText> GetLocalizedTextArray(ArrayOf<LocalizedText> defaultValue = default)
        {
            return TryGet(out ArrayOf<LocalizedText> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a ExtensionObject-array value or returns the default.
        /// </summary>
        public ArrayOf<ExtensionObject> GetExtensionObjectArray(ArrayOf<ExtensionObject> defaultValue = default)
        {
            return TryGet(out ArrayOf<ExtensionObject> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a DataValue-array value or returns the default.
        /// </summary>
        public ArrayOf<DataValue> GetDataValueArray(ArrayOf<DataValue> defaultValue = default)
        {
            return TryGet(out ArrayOf<DataValue> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a Variant-array value or returns the default.
        /// </summary>
        public ArrayOf<Variant> GetVariantArray(ArrayOf<Variant> defaultValue = default)
        {
            return TryGet(out ArrayOf<Variant> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a bool-matrix value or returns the default.
        /// </summary>
        public MatrixOf<bool> GetBooleanMatrix(MatrixOf<bool> defaultValue = default)
        {
            return TryGet(out MatrixOf<bool> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a sbyte-matrix value or returns the default.
        /// </summary>
        public MatrixOf<sbyte> GetSByteMatrix(MatrixOf<sbyte> defaultValue = default)
        {
            return TryGet(out MatrixOf<sbyte> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a byte-matrix value or returns the default.
        /// </summary>
        public MatrixOf<byte> GetByteMatrix(MatrixOf<byte> defaultValue = default)
        {
            return TryGet(out MatrixOf<byte> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a short-matrix value or returns the default.
        /// </summary>
        public MatrixOf<short> GetInt16Matrix(MatrixOf<short> defaultValue = default)
        {
            return TryGet(out MatrixOf<short> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a ushort-matrix value or returns the default.
        /// </summary>
        public MatrixOf<ushort> GetUInt16Matrix(MatrixOf<ushort> defaultValue = default)
        {
            return TryGet(out MatrixOf<ushort> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a int-matrix value or returns the default.
        /// </summary>
        public MatrixOf<int> GetInt32Matrix(MatrixOf<int> defaultValue = default)
        {
            return TryGet(out MatrixOf<int> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a enum array value or returns the default.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public MatrixOf<T> GetEnumerationMatrix<T>(MatrixOf<T> defaultValue = default) where T : Enum
        {
            return TryGet(out MatrixOf<T> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a structure of type T
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public MatrixOf<T> GetStructureMatrix<T>(
            MatrixOf<T> defaultValue = default,
            IServiceMessageContext context = null) where T : IEncodeable
        {
            return TryGet(out MatrixOf<T> v, context) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a uint-matrix value or returns the default.
        /// </summary>
        public MatrixOf<uint> GetUInt32Matrix(MatrixOf<uint> defaultValue = default)
        {
            return TryGet(out MatrixOf<uint> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a long-matrix value or returns the default.
        /// </summary>
        public MatrixOf<long> GetInt64Matrix(MatrixOf<long> defaultValue = default)
        {
            return TryGet(out MatrixOf<long> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a ulong-matrix value or returns the default.
        /// </summary>
        public MatrixOf<ulong> GetUInt64Matrix(MatrixOf<ulong> defaultValue = default)
        {
            return TryGet(out MatrixOf<ulong> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a float-matrix value or returns the default.
        /// </summary>
        public MatrixOf<float> GetFloatMatrix(MatrixOf<float> defaultValue = default)
        {
            return TryGet(out MatrixOf<float> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a double-matrix value or returns the default.
        /// </summary>
        public MatrixOf<double> GetDoubleMatrix(MatrixOf<double> defaultValue = default)
        {
            return TryGet(out MatrixOf<double> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a string []value or returns the default.
        /// </summary>
        public MatrixOf<string> GetStringMatrix(MatrixOf<string> defaultValue = default)
        {
            return TryGet(out MatrixOf<string> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a DateTime-matrix value or returns the default.
        /// </summary>
        public MatrixOf<DateTime> GetDateTimeMatrix(MatrixOf<DateTime> defaultValue = default)
        {
            return TryGet(out MatrixOf<DateTime> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a Uuid-matrix value or returns the default.
        /// </summary>
        public MatrixOf<Uuid> GetGuidMatrix(MatrixOf<Uuid> defaultValue = default)
        {
            return TryGet(out MatrixOf<Uuid> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a byte[]-matrix value or returns the default.
        /// </summary>
        public MatrixOf<ByteString> GetByteStringMatrix(MatrixOf<ByteString> defaultValue = default)
        {
            return TryGet(out MatrixOf<ByteString> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a XmlElement-matrix value or returns the default.
        /// </summary>
        public MatrixOf<XmlElement> GetXmlElementMatrix(MatrixOf<XmlElement> defaultValue = default)
        {
            return TryGet(out MatrixOf<XmlElement> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a NodeId-matrix value or returns the default.
        /// </summary>
        public MatrixOf<NodeId> GetNodeIdMatrix(MatrixOf<NodeId> defaultValue = default)
        {
            return TryGet(out MatrixOf<NodeId> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a ExpandedNodeId-matrix value or returns the default.
        /// </summary>
        public MatrixOf<ExpandedNodeId> GetExpandedNodeIdMatrix(MatrixOf<ExpandedNodeId> defaultValue = default)
        {
            return TryGet(out MatrixOf<ExpandedNodeId> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a StatusCode-matrix value or returns the default.
        /// </summary>
        public MatrixOf<StatusCode> GetStatusCodeMatrix(MatrixOf<StatusCode> defaultValue = default)
        {
            return TryGet(out MatrixOf<StatusCode> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a QualifiedName-matrix value or returns the default.
        /// </summary>
        public MatrixOf<QualifiedName> GetQualifiedNameMatrix(MatrixOf<QualifiedName> defaultValue = default)
        {
            return TryGet(out MatrixOf<QualifiedName> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a LocalizedText-matrix value or returns the default.
        /// </summary>
        public MatrixOf<LocalizedText> GetLocalizedTextMatrix(MatrixOf<LocalizedText> defaultValue = default)
        {
            return TryGet(out MatrixOf<LocalizedText> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a ExtensionObject-matrix value or returns the default.
        /// </summary>
        public MatrixOf<ExtensionObject> GetExtensionObjectMatrix(MatrixOf<ExtensionObject> defaultValue = default)
        {
            return TryGet(out MatrixOf<ExtensionObject> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a DataValue-matrix value or returns the default.
        /// </summary>
        public MatrixOf<DataValue> GetDataValueMatrix(MatrixOf<DataValue> defaultValue = default)
        {
            return TryGet(out MatrixOf<DataValue> v) ? v : defaultValue;
        }

        /// <summary>
        /// Converts the variant to a Variant-matrix value or returns the default.
        /// </summary>
        public MatrixOf<Variant> GetVariantMatrix(MatrixOf<Variant> defaultValue = default)
        {
            return TryGet(out MatrixOf<Variant> v) ? v : defaultValue;
        }

        /// <summary>
        /// Try convert the variant to a <see cref="bool"/> value.
        /// </summary>
        /// <param name="value">The <see cref="bool"/> value to get
        /// </param>
        public bool TryGet(out bool value)
        {
            return TryGetScalar(in m_union.Boolean, out value, BuiltInType.Boolean);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="sbyte"/> value.
        /// </summary>
        /// <param name="value">The <see cref="sbyte"/> value to get
        /// </param>
        public bool TryGet(out sbyte value)
        {
            return TryGetScalar(in m_union.SByte, out value, BuiltInType.SByte);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="byte"/> value.
        /// </summary>
        /// <param name="value">The <see cref="byte"/> value to get
        /// </param>
        public bool TryGet(out byte value)
        {
            return TryGetScalar(in m_union.Byte, out value, BuiltInType.Byte);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="short"/> value.
        /// </summary>
        /// <param name="value">The <see cref="short"/> value to get
        /// </param>
        public bool TryGet(out short value)
        {
            return TryGetScalar(in m_union.Int16, out value, BuiltInType.Int16);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="ushort"/> value.
        /// </summary>
        /// <param name="value">The <see cref="ushort"/> value to get
        /// </param>
        public bool TryGet(out ushort value)
        {
            return TryGetScalar(in m_union.UInt16, out value, BuiltInType.UInt16);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="int"/> value.
        /// </summary>
        /// <param name="value">The <see cref="int"/> value to get
        /// </param>
        public bool TryGet(out int value)
        {
            return
                TryGetScalar(in m_union.Int32, out value, BuiltInType.Int32) ||
#if NET8_0_OR_GREATER
                TryGetScalar(in m_union.Int32, out value, BuiltInType.Enumeration);
#else
                TryGetScalar(out value, BuiltInType.Enumeration);
#endif
        }

        /// <summary>
        /// Try get a structure value from the Variant. There is no overload
        /// resolution on generic types so we need to name it differently than
        /// the scalar TryGet.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The structure value to get.</param>
        /// <param name="context">The context to use when decoding the structure.
        /// </param>
        public bool TryGet<T>(out T value, IServiceMessageContext context)
            where T : IEncodeable
        {
            if (TryGet(out ExtensionObject v) &&
                v.TryGetEncodeable(out value, context))
            {
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Try get a structure value from the Variant. There is no overload
        /// resolution on generic types so we need to name it differently than
        /// the scalar TryGet.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The structure value to get
        /// </param>
        public bool TryGetStructure<T>(out T value) where T : IEncodeable
        {
            return TryGet(out value, null);
        }

        /// <summary>
        /// Get a enumeration value from the Variant.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The enumeration value to get
        /// </param>
        public bool TryGet<T>(out T value) where T : Enum
        {
#if NET8_0_OR_GREATER
            // On .net we convert between the base type of the
            // enum and the enum type using reinterpret casting
            if (TypeInfo.IsScalar &&
                TypeInfo.BuiltInType is
                BuiltInType.Enumeration or
                BuiltInType.Int32)
            {
                switch (Unsafe.SizeOf<T>())
                {
                    case sizeof(byte):
                        byte b = m_union.Byte;
                        value = Unsafe.As<byte, T>(ref b);
                        return true;
                    case sizeof(ushort):
                        ushort u16 = m_union.UInt16;
                        value = Unsafe.As<ushort, T>(ref u16);
                        return true;
                    case sizeof(ulong):
                        ulong u64 = m_union.UInt64;
                        value = Unsafe.As<ulong, T>(ref u64);
                        return true;
                    case sizeof(uint):
                        uint u32 = m_union.UInt32;
                        value = Unsafe.As<uint, T>(ref u32);
                        return true;
                }
            }
            value = default;
            return false;
#else
            // On net framework we always box. However, we still need
            // to account for variant being initialized via int32.
            if (TryGetScalar(out value, BuiltInType.Enumeration))
            {
                return true;
            }
            if (TryGet(out int int32Value))
            {
                value = EnumHelper.Int32ToEnum<T>(int32Value);
                return true;
            }
            value = default;
            return false;
#endif
        }

        /// <summary>
        /// Try convert the variant to a <see cref="uint"/> value.
        /// </summary>
        /// <param name="value">The <see cref="uint"/> value to get
        /// </param>
        public bool TryGet(out uint value)
        {
            return
                TryGetScalar(in m_union.UInt32, out value, BuiltInType.UInt32) ||
                TryGetScalar(in m_union.UInt32, out value, BuiltInType.StatusCode);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="long"/> value.
        /// </summary>
        /// <param name="value">The <see cref="long"/> value to get
        /// </param>
        public bool TryGet(out long value)
        {
            return TryGetScalar(in m_union.Int64, out value, BuiltInType.Int64);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="ulong"/> value.
        /// </summary>
        /// <param name="value">The <see cref="ulong"/> value to get
        /// </param>
        public bool TryGet(out ulong value)
        {
            return TryGetScalar(in m_union.UInt64, out value, BuiltInType.UInt64);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="float"/> value.
        /// </summary>
        /// <param name="value">The <see cref="float"/> value to get
        /// </param>
        public bool TryGet(out float value)
        {
            return TryGetScalar(in m_union.Float, out value, BuiltInType.Float);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="double"/> value.
        /// </summary>
        /// <param name="value">The <see cref="double"/> value to get
        /// </param>
        public bool TryGet(out double value)
        {
            return TryGetScalar(in m_union.Double, out value, BuiltInType.Double);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="string"/> value.
        /// </summary>
        /// <param name="value">The <see cref="string"/> value to get
        /// </param>
        public bool TryGet(out string value)
        {
            return TryGetScalar(out value, BuiltInType.String);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="DateTime"/> value.
        /// </summary>
        /// <param name="value">The <see cref="DateTime"/> value to get
        /// </param>
        public bool TryGet(out DateTime value)
        {
            return TryGetScalar(in m_union.DateTime, out value, BuiltInType.DateTime);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="Uuid"/> value.
        /// </summary>
        /// <param name="value">The <see cref="Uuid"/> value to get
        /// </param>
        public bool TryGet(out Uuid value)
        {
            return TryGetScalar(out value, BuiltInType.Guid);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="ByteString"/> value.
        /// </summary>
        /// <param name="value">The <see cref="ByteString"/>-value to get
        /// </param>
        public bool TryGet(out ByteString value)
        {
            if (TryGetScalar(out value, BuiltInType.ByteString))
            {
                return true;
            }
            if (TryGetArray(out ArrayOf<byte> byteArray, BuiltInType.Byte))
            {
                value = byteArray.ToByteString();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Try convert the variant to a <see cref="XmlElement"/> value.
        /// </summary>
        /// <param name="value">The <see cref="XmlElement"/> value to get
        /// </param>
        public bool TryGet(out XmlElement value)
        {
            return TryGetScalar(out value, BuiltInType.XmlElement);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="NodeId"/> value.
        /// </summary>
        /// <param name="value">The <see cref="NodeId"/> value to get
        /// </param>
        public bool TryGet(out NodeId value)
        {
            return TryGetScalar(out value, BuiltInType.NodeId);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="ExpandedNodeId"/> value.
        /// </summary>
        /// <param name="value">The <see cref="ExpandedNodeId"/> value to
        /// get </param>
        public bool TryGet(out ExpandedNodeId value)
        {
            return TryGetScalar(out value, BuiltInType.ExpandedNodeId);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="StatusCode"/> value.
        /// </summary>
        /// <param name="value">The <see cref="StatusCode"/> value to get
        /// </param>
        public bool TryGet(out StatusCode value)
        {
            if (TypeInfo.IsScalar &&
                TypeInfo.BuiltInType is BuiltInType.StatusCode or BuiltInType.UInt32)
            {
                value = m_value is string s ?
                    new StatusCode(m_union.UInt32, s) :
                    new StatusCode(m_union.UInt32);
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Try convert the variant to a <see cref="QualifiedName"/> value.
        /// </summary>
        /// <param name="value">The <see cref="QualifiedName"/> value to get
        /// </param>
        public bool TryGet(out QualifiedName value)
        {
            return TryGetScalar(out value, BuiltInType.QualifiedName);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="LocalizedText"/> value.
        /// </summary>
        /// <param name="value">The <see cref="LocalizedText"/> value to get
        /// </param>
        public bool TryGet(out LocalizedText value)
        {
            return TryGetScalar(out value, BuiltInType.LocalizedText);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="ExtensionObject"/> value.
        /// </summary>
        /// <param name="value">The <see cref="ExtensionObject"/> value to get
        /// </param>
        public bool TryGet(out ExtensionObject value)
        {
            return TryGetScalar(out value, BuiltInType.ExtensionObject);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="DataValue"/> value.
        /// </summary>
        /// <param name="value">The <see cref="DataValue"/> value to get
        /// </param>
        public bool TryGet(out DataValue value)
        {
            return TryGetScalar(out value, BuiltInType.DataValue);
        }

        /// <summary>
        /// Convert to array of type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        private bool TryGetScalar<T>(
            scoped ref readonly T data,
            out T value,
            BuiltInType builtInType,
            T defaultValue = default)
        {
            bool success = TypeInfo.BuiltInType == builtInType && TypeInfo.IsScalar;
            value = success ? data : defaultValue;
            return success;
        }

        /// <summary>
        /// Try get scalar value
        /// </summary>
        /// <typeparam name="T"></typeparam>
        private bool TryGetScalar<T>(out T value, BuiltInType expectedType)
        {
            if (TypeInfo.BuiltInType != expectedType || !TypeInfo.IsScalar)
            {
                // But it could be convertable from one to the other, ie change type will work.
                if (!IsConvertible(TypeInfo, new TypeInfo(expectedType, TypeInfo.ValueRank)))
                {
                    value = default;
                    return false;
                }
            }
            else if (m_value is T variable)
            {
                value = variable;
                return true;
            }
            if (m_value == null)
            {
                value = default;
                return true;
            }
            try
            {
                value = (T)Convert.ChangeType(m_value, typeof(T), CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Try convert the variant to a <see cref="bool"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="bool"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<bool> value)
        {
            return TryGetArray(out value, BuiltInType.Boolean);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="sbyte"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="sbyte"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<sbyte> value)
        {
            return TryGetArray(out value, BuiltInType.SByte);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="byte"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="byte"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<byte> value)
        {
            if (TryGetArray(out value, BuiltInType.Byte))
            {
                return true;
            }
            if (TryGetScalar(out ByteString byteString, BuiltInType.ByteString))
            {
                value = byteString.ToArray();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Try convert the variant to a <see cref="short"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="short"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<short> value)
        {
            return TryGetArray(out value, BuiltInType.Int16);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="ushort"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="ushort"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<ushort> value)
        {
            return TryGetArray(out value, BuiltInType.UInt16);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="int"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="int"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<int> value)
        {
            return
                TryGetArray(out value, BuiltInType.Int32) ||
                TryGetArray(out value, BuiltInType.Enumeration);
        }

        /// <summary>
        /// Try get a structure value from the Variant. There is no overload
        /// resolution on generic types so we need to name it differently than
        /// the scalar TryGet.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The structure value to get.</param>
        /// <param name="context">The context to use when decoding the structure.
        /// </param>
        public bool TryGet<T>(out ArrayOf<T> value, IServiceMessageContext context)
            where T : IEncodeable
        {
            if (!TryGet(out ArrayOf<ExtensionObject> v))
            {
                value = default;
                return false;
            }
            var buffer = new T[v.Count];
            for (int ii = 0; ii < v.Count; ii++)
            {
                if (!v.Span[ii].TryGetEncodeable(out buffer[ii], context))
                {
                    value = default;
                    return false;
                }
            }
            value = buffer;
            return true;
        }

        /// <summary>
        /// Try get a structure value from the Variant. There is no overload
        /// resolution on generic types so we need to name it differently than
        /// the scalar TryGet.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The structure value to get</param>
        public bool TryGetStructure<T>(out ArrayOf<T> value) where T : IEncodeable
        {
            return TryGet(out value, null);
        }

        /// <summary>
        /// Get a enumeration value from the Variant.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The value to get</param>
        public bool TryGet<T>(out ArrayOf<T> value) where T : Enum
        {
            // All enum values are stored as integer arrays with type lost
            if (TryGetArray(out ArrayOf<int> int32Values, BuiltInType.Enumeration) ||
                TryGetArray(out int32Values, BuiltInType.Int32))
            {
                value = int32Values.ConvertAll(EnumHelper.Int32ToEnum<T>);
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Try convert the variant to a <see cref="uint"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="uint"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<uint> value)
        {
            return TryGetArray(out value, BuiltInType.UInt32);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="long"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="long"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<long> value)
        {
            return TryGetArray(out value, BuiltInType.Int64);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="ulong"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="ulong"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<ulong> value)
        {
            return TryGetArray(out value, BuiltInType.UInt64);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="float"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="float"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<float> value)
        {
            return TryGetArray(out value, BuiltInType.Float);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="double"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="double"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<double> value)
        {
            return TryGetArray(out value, BuiltInType.Double);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="string"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="string"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<string> value)
        {
            return TryGetArray(out value, BuiltInType.String);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="DateTime"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="DateTime"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<DateTime> value)
        {
            return TryGetArray(out value, BuiltInType.DateTime);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="Uuid"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="Uuid"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<Uuid> value)
        {
            return TryGetArray(out value, BuiltInType.Guid);
        }

        /// <summary>
        /// Try convert the variant to a 2-d <see cref="byte"/>-array value.
        /// </summary>
        /// <param name="value">The 2-d <see cref="byte"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<ByteString> value)
        {
            return TryGetArray(out value, BuiltInType.ByteString);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="XmlElement"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="XmlElement"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<XmlElement> value)
        {
            return TryGetArray(out value, BuiltInType.XmlElement);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="NodeId"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="NodeId"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<NodeId> value)
        {
            return TryGetArray(out value, BuiltInType.NodeId);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="ExpandedNodeId"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="ExpandedNodeId"/>-array value to
        /// get </param>
        public bool TryGet(out ArrayOf<ExpandedNodeId> value)
        {
            return TryGetArray(out value, BuiltInType.ExpandedNodeId);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="StatusCode"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="StatusCode"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<StatusCode> value)
        {
            return TryGetArray(out value, BuiltInType.StatusCode);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="QualifiedName"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="QualifiedName"/>-array value to
        /// get </param>
        public bool TryGet(out ArrayOf<QualifiedName> value)
        {
            return TryGetArray(out value, BuiltInType.QualifiedName);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="LocalizedText"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="LocalizedText"/>-array value to
        /// get </param>
        public bool TryGet(out ArrayOf<LocalizedText> value)
        {
            return TryGetArray(out value, BuiltInType.LocalizedText);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="ExtensionObject"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="ExtensionObject"/>-array value to
        /// get </param>
        public bool TryGet(out ArrayOf<ExtensionObject> value)
        {
            return TryGetArray(out value, BuiltInType.ExtensionObject);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="DataValue"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="DataValue"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<DataValue> value)
        {
            return TryGetArray(out value, BuiltInType.DataValue);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="Variant"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="Variant"/>-array value to get
        /// </param>
        public bool TryGet(out ArrayOf<Variant> value)
        {
            return TryGetArray(out value, BuiltInType.Variant);
        }

        /// <summary>
        /// Try get array of specified type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public bool TryGetArray<T>(out ArrayOf<T> value, BuiltInType expectedType)
        {
            if (TypeInfo.BuiltInType == expectedType)
            {
                if (m_value is ArrayOf<T> array)
                {
                    value = array;
                    return true;
                }

                if (m_value is MatrixOf<T> matrix && matrix.Dimensions.Length == 1)
                {
                    value = matrix.ToArrayOf();
                    return true;
                }
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Try convert the variant to a <see cref="bool"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="bool"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<bool> value)
        {
            return TryGetMatrix(out value, BuiltInType.Boolean);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="sbyte"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="sbyte"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<sbyte> value)
        {
            return TryGetMatrix(out value, BuiltInType.SByte);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="byte"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="byte"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<byte> value)
        {
            return TryGetMatrix(out value, BuiltInType.Byte);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="short"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="short"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<short> value)
        {
            return TryGetMatrix(out value, BuiltInType.Int16);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="ushort"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="ushort"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<ushort> value)
        {
            return TryGetMatrix(out value, BuiltInType.UInt16);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="int"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="int"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<int> value)
        {
            return
                TryGetMatrix(out value, BuiltInType.Int32) ||
                TryGetMatrix(out value, BuiltInType.Enumeration);
        }

        /// <summary>
        /// Try get a structure value from the Variant. There is no overload
        /// resolution on generic types so we need to name it differently than
        /// the scalar TryGet.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The structure value to get.</param>
        /// <param name="context">The context to use when decoding the structure.
        /// </param>
        public bool TryGet<T>(out MatrixOf<T> value, IServiceMessageContext context)
            where T : IEncodeable
        {
            if (!TryGet(out MatrixOf<ExtensionObject> v))
            {
                value = default;
                return false;
            }
            var buffer = new T[v.Count];
            for (int ii = 0; ii < v.Count; ii++)
            {
                if (!v.Span[ii].TryGetEncodeable(out buffer[ii], context))
                {
                    value = default;
                    return false;
                }
            }
            value = ArrayOf.Create(buffer).ToMatrix(v.Dimensions);
            return true;
        }

        /// <summary>
        /// Try get a structure value from the Variant. There is no overload
        /// resolution on generic types so we need to name it differently than
        /// the scalar TryGet.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The structure value to get</param>
        public bool TryGetStructure<T>(out MatrixOf<T> value) where T : IEncodeable
        {
            return TryGet(out value, null);
        }

        /// <summary>
        /// Get a enumeration value from the Variant.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The value to get</param>
        public bool TryGet<T>(out MatrixOf<T> value) where T : Enum
        {
            // All enum values are stored as integer matrices with type lost
            if (TryGetMatrix(out MatrixOf<int> int32Values, BuiltInType.Enumeration) ||
                TryGetMatrix(out int32Values, BuiltInType.Int32))
            {
                value = int32Values.ConvertAll(EnumHelper.Int32ToEnum<T>);
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Try convert the variant to a <see cref="uint"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="uint"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<uint> value)
        {
            return TryGetMatrix(out value, BuiltInType.UInt32);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="long"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="long"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<long> value)
        {
            return TryGetMatrix(out value, BuiltInType.Int64);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="ulong"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="ulong"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<ulong> value)
        {
            return TryGetMatrix(out value, BuiltInType.UInt64);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="float"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="float"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<float> value)
        {
            return TryGetMatrix(out value, BuiltInType.Float);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="double"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="double"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<double> value)
        {
            return TryGetMatrix(out value, BuiltInType.Double);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="string"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="string"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<string> value)
        {
            return TryGetMatrix(out value, BuiltInType.String);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="DateTime"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="DateTime"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<DateTime> value)
        {
            return TryGetMatrix(out value, BuiltInType.DateTime);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="Uuid"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="Uuid"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<Uuid> value)
        {
            return TryGetMatrix(out value, BuiltInType.Guid);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="ByteString"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="ByteString"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<ByteString> value)
        {
            return TryGetMatrix(out value, BuiltInType.ByteString);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="XmlElement"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="XmlElement"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<XmlElement> value)
        {
            return TryGetMatrix(out value, BuiltInType.XmlElement);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="NodeId"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="NodeId"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<NodeId> value)
        {
            return TryGetMatrix(out value, BuiltInType.NodeId);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="ExpandedNodeId"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="ExpandedNodeId"/>-matrix value to
        /// get </param>
        public bool TryGet(out MatrixOf<ExpandedNodeId> value)
        {
            return TryGetMatrix(out value, BuiltInType.ExpandedNodeId);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="StatusCode"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="StatusCode"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<StatusCode> value)
        {
            return TryGetMatrix(out value, BuiltInType.StatusCode);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="QualifiedName"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="QualifiedName"/>-matrix value to
        /// get </param>
        public bool TryGet(out MatrixOf<QualifiedName> value)
        {
            return TryGetMatrix(out value, BuiltInType.QualifiedName);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="LocalizedText"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="LocalizedText"/>-matrix value to
        /// get </param>
        public bool TryGet(out MatrixOf<LocalizedText> value)
        {
            return TryGetMatrix(out value, BuiltInType.LocalizedText);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="ExtensionObject"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="ExtensionObject"/>-matrix value to
        /// get </param>
        public bool TryGet(out MatrixOf<ExtensionObject> value)
        {
            return TryGetMatrix(out value, BuiltInType.ExtensionObject);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="DataValue"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="DataValue"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<DataValue> value)
        {
            return TryGetMatrix(out value, BuiltInType.DataValue);
        }

        /// <summary>
        /// Try convert the variant to a <see cref="Variant"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="Variant"/>-matrix value to get
        /// </param>
        public bool TryGet(out MatrixOf<Variant> value)
        {
            return TryGetMatrix(out value, BuiltInType.Variant);
        }

        /// <summary>
        /// Try get matrix of type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public bool TryGetMatrix<T>(out MatrixOf<T> value, BuiltInType expectedType)
        {
            if (TypeInfo.BuiltInType == expectedType)
            {
                if (m_value is MatrixOf<T> matrix)
                {
                    value = matrix;
                    return true;
                }
                if (TryGetArray(out ArrayOf<T> array, expectedType))
                {
                    value = array.ToMatrix();
                    return true;
                }
            }
            value = default;
            return false;
        }

        /// <inheritdoc/>
        IVariantOf<bool> IVariantOf<bool>.WithValue(bool value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<sbyte> IVariantOf<sbyte>.WithValue(sbyte value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<byte> IVariantOf<byte>.WithValue(byte value)
        {
            return new Variant(value);
        }

        /// <inheritdoc/>
        IVariantOf<short> IVariantOf<short>.WithValue(short value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ushort> IVariantOf<ushort>.WithValue(ushort value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<int> IVariantOf<int>.WithValue(int value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<uint> IVariantOf<uint>.WithValue(uint value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<long> IVariantOf<long>.WithValue(long value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ulong> IVariantOf<ulong>.WithValue(ulong value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<float> IVariantOf<float>.WithValue(float value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<double> IVariantOf<double>.WithValue(double value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<string> IVariantOf<string>.WithValue(string value)
        {
            return value is null ? default : new Variant(value);
        }

        /// <inheritdoc/>
        IVariantOf<DateTime> IVariantOf<DateTime>.WithValue(DateTime value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<Uuid> IVariantOf<Uuid>.WithValue(Uuid value)
        {
            return new Variant(value);
        }

        /// <inheritdoc/>
        IVariantOf<ByteString> IVariantOf<ByteString>.WithValue(ByteString value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<XmlElement> IVariantOf<XmlElement>.WithValue(XmlElement value)
        {
            return new Variant(value);
        }

        /// <inheritdoc/>
        IVariantOf<NodeId> IVariantOf<NodeId>.WithValue(NodeId value)
        {
            return new Variant(value);
        }

        /// <inheritdoc/>
        IVariantOf<ExpandedNodeId> IVariantOf<ExpandedNodeId>.WithValue(
            ExpandedNodeId value)
        {
            return new Variant(value);
        }

        /// <inheritdoc/>
        IVariantOf<StatusCode> IVariantOf<StatusCode>.WithValue(
            StatusCode value)
        {
            return new Variant(value);
        }

        /// <inheritdoc/>
        IVariantOf<QualifiedName> IVariantOf<QualifiedName>.WithValue(
            QualifiedName value)
        {
            return new Variant(value);
        }

        /// <inheritdoc/>
        IVariantOf<LocalizedText> IVariantOf<LocalizedText>.WithValue(
            LocalizedText value)
        {
            return new Variant(value);
        }

        /// <inheritdoc/>
        IVariantOf<ExtensionObject> IVariantOf<ExtensionObject>.WithValue(
            ExtensionObject value)
        {
            return new Variant(value);
        }

        /// <inheritdoc/>
        IVariantOf<DataValue> IVariantOf<DataValue>.WithValue(DataValue value)
        {
            return value is null ? default : new Variant(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<bool>> IVariantOf<ArrayOf<bool>>.WithValue(
            ArrayOf<bool> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<sbyte>> IVariantOf<ArrayOf<sbyte>>.WithValue(
            ArrayOf<sbyte> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<byte>> IVariantOf<ArrayOf<byte>>.WithValue(
            ArrayOf<byte> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<short>> IVariantOf<ArrayOf<short>>.WithValue(
            ArrayOf<short> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<ushort>> IVariantOf<ArrayOf<ushort>>.WithValue(
            ArrayOf<ushort> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<int>> IVariantOf<ArrayOf<int>>.WithValue(
            ArrayOf<int> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<uint>> IVariantOf<ArrayOf<uint>>.WithValue(
            ArrayOf<uint> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<long>> IVariantOf<ArrayOf<long>>.WithValue(
            ArrayOf<long> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<ulong>> IVariantOf<ArrayOf<ulong>>.WithValue(
            ArrayOf<ulong> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<float>> IVariantOf<ArrayOf<float>>.WithValue(
            ArrayOf<float> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<double>> IVariantOf<ArrayOf<double>>.WithValue(
            ArrayOf<double> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<string>> IVariantOf<ArrayOf<string>>.WithValue(
            ArrayOf<string> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<DateTime>> IVariantOf<ArrayOf<DateTime>>.WithValue(
            ArrayOf<DateTime> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<Uuid>> IVariantOf<ArrayOf<Uuid>>.WithValue(
            ArrayOf<Uuid> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<ByteString>> IVariantOf<ArrayOf<ByteString>>.WithValue(
            ArrayOf<ByteString> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<XmlElement>> IVariantOf<ArrayOf<XmlElement>>.WithValue(
            ArrayOf<XmlElement> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<NodeId>> IVariantOf<ArrayOf<NodeId>>.WithValue(
            ArrayOf<NodeId> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<ExpandedNodeId>> IVariantOf<ArrayOf<ExpandedNodeId>>.WithValue(
            ArrayOf<ExpandedNodeId> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<StatusCode>> IVariantOf<ArrayOf<StatusCode>>.WithValue(
            ArrayOf<StatusCode> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<QualifiedName>> IVariantOf<ArrayOf<QualifiedName>>.WithValue(
            ArrayOf<QualifiedName> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<LocalizedText>> IVariantOf<ArrayOf<LocalizedText>>.WithValue(
            ArrayOf<LocalizedText> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<ExtensionObject>> IVariantOf<ArrayOf<ExtensionObject>>.WithValue(
            ArrayOf<ExtensionObject> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<DataValue>> IVariantOf<ArrayOf<DataValue>>.WithValue(
            ArrayOf<DataValue> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<ArrayOf<Variant>> IVariantOf<ArrayOf<Variant>>.WithValue(
            ArrayOf<Variant> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<bool>> IVariantOf<MatrixOf<bool>>.WithValue(
            MatrixOf<bool> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<sbyte>> IVariantOf<MatrixOf<sbyte>>.WithValue(
            MatrixOf<sbyte> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<byte>> IVariantOf<MatrixOf<byte>>.WithValue(
            MatrixOf<byte> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<short>> IVariantOf<MatrixOf<short>>.WithValue(
            MatrixOf<short> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<ushort>> IVariantOf<MatrixOf<ushort>>.WithValue(
            MatrixOf<ushort> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<int>> IVariantOf<MatrixOf<int>>.WithValue(
            MatrixOf<int> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<uint>> IVariantOf<MatrixOf<uint>>.WithValue(
            MatrixOf<uint> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<long>> IVariantOf<MatrixOf<long>>.WithValue(
            MatrixOf<long> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<ulong>> IVariantOf<MatrixOf<ulong>>.WithValue(
            MatrixOf<ulong> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<float>> IVariantOf<MatrixOf<float>>.WithValue(
            MatrixOf<float> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<double>> IVariantOf<MatrixOf<double>>.WithValue(
            MatrixOf<double> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<string>> IVariantOf<MatrixOf<string>>.WithValue(
            MatrixOf<string> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<DateTime>> IVariantOf<MatrixOf<DateTime>>.WithValue(
            MatrixOf<DateTime> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<Uuid>> IVariantOf<MatrixOf<Uuid>>.WithValue(
            MatrixOf<Uuid> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<ByteString>> IVariantOf<MatrixOf<ByteString>>.WithValue(
            MatrixOf<ByteString> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<XmlElement>> IVariantOf<MatrixOf<XmlElement>>.WithValue(
            MatrixOf<XmlElement> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<NodeId>> IVariantOf<MatrixOf<NodeId>>.WithValue(
            MatrixOf<NodeId> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<ExpandedNodeId>> IVariantOf<MatrixOf<ExpandedNodeId>>.WithValue(
            MatrixOf<ExpandedNodeId> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<StatusCode>> IVariantOf<MatrixOf<StatusCode>>.WithValue(
            MatrixOf<StatusCode> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<QualifiedName>> IVariantOf<MatrixOf<QualifiedName>>.WithValue(
            MatrixOf<QualifiedName> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<LocalizedText>> IVariantOf<MatrixOf<LocalizedText>>.WithValue(
            MatrixOf<LocalizedText> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<ExtensionObject>> IVariantOf<MatrixOf<ExtensionObject>>.WithValue(
            MatrixOf<ExtensionObject> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<DataValue>> IVariantOf<MatrixOf<DataValue>>.WithValue(
            MatrixOf<DataValue> value)
        {
            return From(value);
        }

        /// <inheritdoc/>
        IVariantOf<MatrixOf<Variant>> IVariantOf<MatrixOf<Variant>>.WithValue(
            MatrixOf<Variant> value)
        {
            return From(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="bool"/> value.
        /// </summary>
        /// <param name="value">The <see cref="bool"/> value to set
        /// this Variant to</param>
        public static Variant From(bool value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="sbyte"/> value.
        /// </summary>
        /// <param name="value">The <see cref="sbyte"/> value to set
        /// this Variant to</param>
        public static Variant From(sbyte value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="byte"/> value.
        /// </summary>
        /// <param name="value">The <see cref="byte"/> value to set
        /// this Variant to</param>
        public static Variant From(byte value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="short"/> value.
        /// </summary>
        /// <param name="value">The <see cref="short"/> value to set
        /// this Variant to</param>
        public static Variant From(short value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="ushort"/> value.
        /// </summary>
        /// <param name="value">The <see cref="ushort"/> value to set
        /// this Variant to</param>
        public static Variant From(ushort value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="int"/> value.
        /// </summary>
        /// <param name="value">The <see cref="int"/> value to set
        /// this Variant to</param>
        public static Variant From(int value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a Enum value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The Enum value to set
        /// this Variant to</param>
        /// <exception cref="ServiceResultException"></exception>
        public static Variant From<T>(T value) where T : Enum
        {
#if NET8_0_OR_GREATER
            Union data = default;
            switch (Unsafe.SizeOf<T>())
            {
                case sizeof(byte):
                    data.Byte = Unsafe.As<T, byte>(ref value);
                    break;
                case sizeof(ushort):
                    data.UInt16 = Unsafe.As<T, ushort>(ref value);
                    break;
                case sizeof(ulong):
                    data.UInt64 = Unsafe.As<T, ulong>(ref value);
                    break;
                case sizeof(uint):
                    data.UInt32 = Unsafe.As<T, uint>(ref value);
                    break;
                default:
                    throw ServiceResultException.Unexpected(
                        "Bad enum type {0} with size: {1}",
                        typeof(T).Name, Unsafe.SizeOf<T>());
            }
            return new Variant(data, TypeInfo.Scalars.Enumeration, typeof(T));
#else
            return new Variant(default, TypeInfo.Scalars.Enumeration, value);
#endif
        }

        /// <summary>
        /// Create a Variant from a <see cref="uint"/> value.
        /// </summary>
        /// <param name="value">The <see cref="uint"/> value to set
        /// this Variant to</param>
        public static Variant From(uint value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="long"/> value.
        /// </summary>
        /// <param name="value">The <see cref="long"/> value to set
        /// this Variant to</param>
        public static Variant From(long value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="ulong"/> value.
        /// </summary>
        /// <param name="value">The <see cref="ulong"/> value to set
        /// this Variant to</param>
        public static Variant From(ulong value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="float"/> value.
        /// </summary>
        /// <param name="value">The <see cref="float"/> value to set
        /// this Variant to</param>
        public static Variant From(float value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="double"/> value.
        /// </summary>
        /// <param name="value">The <see cref="double"/> value to set
        /// this Variant to</param>
        public static Variant From(double value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="string"/> value.
        /// </summary>
        /// <param name="value">The <see cref="string"/> value to set
        /// this Variant to</param>
        public static Variant From(string value)
        {
            return value is null ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="DateTime"/> value.
        /// </summary>
        /// <param name="value">The <see cref="DateTime"/> value to set
        /// this Variant to</param>
        public static Variant From(DateTime value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="Uuid"/> value.
        /// </summary>
        /// <param name="value">The <see cref="Uuid"/> value to set
        /// this Variant to</param>
        public static Variant From(Uuid value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="ByteString"/> value.
        /// </summary>
        /// <param name="value">The <see cref="ByteString"/> value to set
        /// this Variant to</param>
        public static Variant From(ByteString value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="XmlElement"/> value.
        /// </summary>
        /// <param name="value">The <see cref="XmlElement"/> value to set
        /// this Variant to</param>
        public static Variant From(XmlElement value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="NodeId"/> value.
        /// </summary>
        /// <param name="value">The <see cref="NodeId"/> value to set
        /// this Variant to</param>
        public static Variant From(NodeId value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="ExpandedNodeId"/> value.
        /// </summary>
        /// <param name="value">The <see cref="ExpandedNodeId"/> value to
        /// set this Variant to</param>
        public static Variant From(ExpandedNodeId value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="StatusCode"/> value.
        /// </summary>
        /// <param name="value">The <see cref="StatusCode"/> value to set
        /// this Variant to</param>
        public static Variant From(StatusCode value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="QualifiedName"/> value.
        /// </summary>
        /// <param name="value">The <see cref="QualifiedName"/> value to set
        /// this Variant to</param>
        public static Variant From(QualifiedName value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="LocalizedText"/> value.
        /// </summary>
        /// <param name="value">The <see cref="LocalizedText"/> value to set
        /// this Variant to</param>
        public static Variant From(LocalizedText value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="ExtensionObject"/> value.
        /// </summary>
        /// <param name="value">The <see cref="ExtensionObject"/> value to set
        /// this Variant to</param>
        public static Variant From(ExtensionObject value)
        {
            return new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="IEncodeable"/> value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The <see cref="IEncodeable"/> value to set
        /// this Variant to</param>
        /// <param name="copy">Make a copy of the encodeable</param>
        public static Variant FromStructure<T>(T value, bool copy = false)
            where T : IEncodeable
        {
            return value is null ? default : new Variant(new ExtensionObject(value, copy));
        }

        /// <summary>
        /// Create a Variant from a <see cref="DataValue"/> value.
        /// </summary>
        /// <param name="value">The <see cref="DataValue"/> value to set
        /// this Variant to</param>
        public static Variant From(DataValue value)
        {
            return value is null ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="bool"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="bool"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<bool> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="sbyte"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="sbyte"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<sbyte> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="byte"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="byte"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<byte> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="short"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="short"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<short> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="ushort"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="ushort"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<ushort> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="int"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="int"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<int> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a Enum-array value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The Enum-array value to set
        /// this Variant to</param>
        public static Variant From<T>(ArrayOf<T> value) where T : Enum
        {
            // All enum arrays are stored as int32 arrays, so we
            // need to convert them to int32 if needed.
            return value.IsNull ? default : new Variant(
                default,
                TypeInfo.Arrays.Enumeration,
                value.ConvertAll(e => Convert.ToInt32(e, CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Create a Variant from a Enum-array value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The Enum-array value to set
        /// this Variant to</param>
        public static Variant From<T>(T[] value) where T : Enum
        {
            return From(value.ToArrayOf());
        }

        /// <summary>
        /// Create a Variant from a <see cref="uint"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="uint"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<uint> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="long"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="long"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<long> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="ulong"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="ulong"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<ulong> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="float"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="float"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<float> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="double"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="double"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<double> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="string"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="string"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<string> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="DateTime"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="DateTime"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<DateTime> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="Uuid"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="Uuid"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<Uuid> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="ByteString"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="ByteString"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<ByteString> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="XmlElement"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="XmlElement"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<XmlElement> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="NodeId"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="NodeId"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<NodeId> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="ExpandedNodeId"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="ExpandedNodeId"/>-array value to
        /// set this Variant to</param>
        public static Variant From(ArrayOf<ExpandedNodeId> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="StatusCode"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="StatusCode"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<StatusCode> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="QualifiedName"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="QualifiedName"/>-array value to
        /// set this Variant to</param>
        public static Variant From(ArrayOf<QualifiedName> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="LocalizedText"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="LocalizedText"/>-array value to
        /// set this Variant to</param>
        public static Variant From(ArrayOf<LocalizedText> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="ExtensionObject"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="ExtensionObject"/>-array value to
        /// set this Variant to</param>
        public static Variant From(ArrayOf<ExtensionObject> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="IEncodeable"/>-array value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The <see cref="IEncodeable"/>-array value to set
        /// this Variant to</param>
        /// <param name="copy">Make a copy of the encodeable</param>
        public static Variant FromStructure<T>(ArrayOf<T> value, bool copy = false)
            where T : IEncodeable
        {
            return value.IsNull ? default :
                new Variant(value.ConvertAll(b => new ExtensionObject(b, copy)));
        }

        /// <summary>
        /// Create a Variant from a <see cref="DataValue"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="DataValue"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<DataValue> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="Variant"/>-array value.
        /// </summary>
        /// <param name="value">The <see cref="Variant"/>-array value to set
        /// this Variant to</param>
        public static Variant From(ArrayOf<Variant> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="bool"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="bool"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<bool> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="sbyte"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="sbyte"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<sbyte> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="byte"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="byte"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<byte> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="short"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="short"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<short> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="ushort"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="ushort"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<ushort> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="int"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="int"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<int> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a Enum-matrix value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The Enum-matrix value to set
        /// this Variant to</param>
        public static Variant From<T>(MatrixOf<T> value) where T : Enum
        {
            // All enum matrices are stored as int32 matrices, so we
            // need to convert them to int32
            return value.IsNull ? default : new Variant(
                default,
                TypeInfo.Create(BuiltInType.Enumeration, value.Dimensions.Length),
                value.ConvertAll(e => Convert.ToInt32(e, CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Create a Variant from a <see cref="uint"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="uint"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<uint> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="long"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="long"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<long> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="ulong"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="ulong"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<ulong> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="float"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="float"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<float> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="double"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="double"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<double> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="string"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="string"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<string> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="DateTime"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="DateTime"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<DateTime> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="Uuid"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="Uuid"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<Uuid> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="ByteString"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="ByteString"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<ByteString> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="XmlElement"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="XmlElement"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<XmlElement> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="NodeId"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="NodeId"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<NodeId> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="ExpandedNodeId"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="ExpandedNodeId"/>-matrix value to
        /// set this Variant to</param>
        public static Variant From(MatrixOf<ExpandedNodeId> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="StatusCode"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="StatusCode"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<StatusCode> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="QualifiedName"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="QualifiedName"/>-matrix value to
        /// set this Variant to</param>
        public static Variant From(MatrixOf<QualifiedName> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="LocalizedText"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="LocalizedText"/>-matrix value to
        /// set this Variant to</param>
        public static Variant From(MatrixOf<LocalizedText> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="ExtensionObject"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="ExtensionObject"/>-matrix value to
        /// set this Variant to</param>
        public static Variant From(MatrixOf<ExtensionObject> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="IEncodeable"/>-matrix value.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="value">The <see cref="IEncodeable"/>-matrix value to set
        /// this Variant to</param>
        /// <param name="copy">Make a copy of the encodeable</param>
        public static Variant FromStructure<T>(MatrixOf<T> value, bool copy = false)
            where T : IEncodeable
        {
            return value.IsNull ? default :
                new Variant(value.ConvertAll(b => new ExtensionObject(b, copy)));
        }

        /// <summary>
        /// Create a Variant from a <see cref="DataValue"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="DataValue"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<DataValue> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Create a Variant from a <see cref="Variant"/>-matrix value.
        /// </summary>
        /// <param name="value">The <see cref="Variant"/>-matrix value to set
        /// this Variant to</param>
        public static Variant From(MatrixOf<Variant> value)
        {
            return value.IsNull ? default : new Variant(value);
        }

        /// <summary>
        /// Converts a bool value to a Variant object.
        /// </summary>
        public static implicit operator Variant(bool value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a sbyte value to a Variant object.
        /// </summary>
        public static implicit operator Variant(sbyte value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a byte value to a Variant object.
        /// </summary>
        public static implicit operator Variant(byte value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a short value to a Variant object.
        /// </summary>
        public static implicit operator Variant(short value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a ushort value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ushort value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a int value to a Variant object.
        /// </summary>
        public static implicit operator Variant(int value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a uint value to a Variant object.
        /// </summary>
        public static implicit operator Variant(uint value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a long value to a Variant object.
        /// </summary>
        public static implicit operator Variant(long value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a ulong value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ulong value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a float value to a Variant object.
        /// </summary>
        public static implicit operator Variant(float value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a double value to a Variant object.
        /// </summary>
        public static implicit operator Variant(double value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a string value to a Variant object.
        /// </summary>
        public static implicit operator Variant(string value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a DateTime value to a Variant object.
        /// </summary>
        public static implicit operator Variant(DateTime value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a Uuid value to a Variant object.
        /// </summary>
        public static implicit operator Variant(Uuid value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a ByteString value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ByteString value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a XmlElement value to a Variant object.
        /// </summary>
        public static implicit operator Variant(XmlElement value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a NodeId value to a Variant object.
        /// </summary>
        public static implicit operator Variant(NodeId value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a ExpandedNodeId value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ExpandedNodeId value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a StatusCode value to a Variant object.
        /// </summary>
        public static implicit operator Variant(StatusCode value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a QualifiedName value to a Variant object.
        /// </summary>
        public static implicit operator Variant(QualifiedName value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a LocalizedText value to a Variant object.
        /// </summary>
        public static implicit operator Variant(LocalizedText value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a ExtensionObject value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ExtensionObject value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a DataValue value to a Variant object.
        /// </summary>
        public static implicit operator Variant(DataValue value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a bool-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<bool> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a sbyte-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<sbyte> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a byte-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<byte> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a short-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<short> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a ushort-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<ushort> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a int-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<int> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a uint-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<uint> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a long-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<long> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a ulong-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<ulong> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a float-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<float> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a double-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<double> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a string-array to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<string> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a DateTime-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<DateTime> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a Uuid-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<Uuid> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a ByteString-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<ByteString> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a XmlElement-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<XmlElement> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a NodeId-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<NodeId> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a ExpandedNodeId-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<ExpandedNodeId> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a StatusCode-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<StatusCode> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a QualifiedName-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<QualifiedName> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a LocalizedText-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<LocalizedText> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a ExtensionObject-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<ExtensionObject> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a DataValue-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<DataValue> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a Variant-array value to a Variant object.
        /// </summary>
        public static implicit operator Variant(ArrayOf<Variant> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a bool-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<bool> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a sbyte-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<sbyte> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a byte-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<byte> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a short-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<short> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a ushort-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<ushort> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a int-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<int> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a uint-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<uint> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a long-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<long> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a ulong-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<ulong> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a float-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<float> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a double-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<double> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a string []value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<string> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a DateTime-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<DateTime> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a Uuid-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<Uuid> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a ByteString-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<ByteString> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a XmlElement-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<XmlElement> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a NodeId-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<NodeId> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a ExpandedNodeId-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<ExpandedNodeId> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a StatusCode-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<StatusCode> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a QualifiedName-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<QualifiedName> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a LocalizedText-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<LocalizedText> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a ExtensionObject-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<ExtensionObject> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a DataValue-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<DataValue> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a Variant-matrix value to a Variant object.
        /// </summary>
        public static implicit operator Variant(MatrixOf<Variant> value)
        {
            return From(value);
        }

        /// <summary>
        /// Converts a bool value to a Variant object.
        /// </summary>
        public static explicit operator bool(Variant value)
        {
            return value.TryGet(out bool v) ? v : throw CannotCast<bool>();
        }

        /// <summary>
        /// Converts a sbyte value to a Variant object.
        /// </summary>
        public static explicit operator sbyte(Variant value)
        {
            return value.TryGet(out sbyte v) ? v : throw CannotCast<sbyte>();
        }

        /// <summary>
        /// Converts a byte value to a Variant object.
        /// </summary>
        public static explicit operator byte(Variant value)
        {
            return value.TryGet(out byte v) ? v : throw CannotCast<byte>();
        }

        /// <summary>
        /// Converts a short value to a Variant object.
        /// </summary>
        public static explicit operator short(Variant value)
        {
            return value.TryGet(out short v) ? v : throw CannotCast<short>();
        }

        /// <summary>
        /// Converts a ushort value to a Variant object.
        /// </summary>
        public static explicit operator ushort(Variant value)
        {
            return value.TryGet(out ushort v) ? v : throw CannotCast<ushort>();
        }

        /// <summary>
        /// Converts a int value to a Variant object.
        /// </summary>
        public static explicit operator int(Variant value)
        {
            return value.TryGet(out int v) ? v : throw CannotCast<int>();
        }

        /// <summary>
        /// Converts a uint value to a Variant object.
        /// </summary>
        public static explicit operator uint(Variant value)
        {
            return value.TryGet(out uint v) ? v : throw CannotCast<uint>();
        }

        /// <summary>
        /// Converts a long value to a Variant object.
        /// </summary>
        public static explicit operator long(Variant value)
        {
            return value.TryGet(out long v) ? v : throw CannotCast<long>();
        }

        /// <summary>
        /// Converts a ulong value to a Variant object.
        /// </summary>
        public static explicit operator ulong(Variant value)
        {
            return value.TryGet(out ulong v) ? v : throw CannotCast<ulong>();
        }

        /// <summary>
        /// Converts a float value to a Variant object.
        /// </summary>
        public static explicit operator float(Variant value)
        {
            return value.TryGet(out float v) ? v : throw CannotCast<float>();
        }

        /// <summary>
        /// Converts a double value to a Variant object.
        /// </summary>
        public static explicit operator double(Variant value)
        {
            return value.TryGet(out double v) ? v : throw CannotCast<double>();
        }

        /// <summary>
        /// Converts a string value to a Variant object.
        /// </summary>
        public static explicit operator string(Variant value)
        {
            return value.TryGet(out string v) ? v : throw CannotCast<string>();
        }

        /// <summary>
        /// Converts a DateTime value to a Variant object.
        /// </summary>
        public static explicit operator DateTime(Variant value)
        {
            return value.TryGet(out DateTime v) ? v : throw CannotCast<DateTime>();
        }

        /// <summary>
        /// Converts a Uuid value to a Variant object.
        /// </summary>
        public static explicit operator Uuid(Variant value)
        {
            return value.TryGet(out Uuid v) ? v : throw CannotCast<Uuid>();
        }

        /// <summary>
        /// Converts a ByteString value to a Variant object.
        /// </summary>
        public static explicit operator ByteString(Variant value)
        {
            return value.TryGet(out ByteString v) ? v : throw CannotCast<ByteString>();
        }

        /// <summary>
        /// Converts a XmlElement value to a Variant object.
        /// </summary>
        public static explicit operator XmlElement(Variant value)
        {
            return value.TryGet(out XmlElement v) ? v : throw CannotCast<XmlElement>();
        }

        /// <summary>
        /// Converts a NodeId value to a Variant object.
        /// </summary>
        public static explicit operator NodeId(Variant value)
        {
            return value.TryGet(out NodeId v) ? v : throw CannotCast<NodeId>();
        }

        /// <summary>
        /// Converts a ExpandedNodeId value to a Variant object.
        /// </summary>
        public static explicit operator ExpandedNodeId(Variant value)
        {
            return value.TryGet(out ExpandedNodeId v) ? v : throw CannotCast<ExpandedNodeId>();
        }

        /// <summary>
        /// Converts a StatusCode value to a Variant object.
        /// </summary>
        public static explicit operator StatusCode(Variant value)
        {
            return value.TryGet(out StatusCode v) ? v : throw CannotCast<StatusCode>();
        }

        /// <summary>
        /// Converts a QualifiedName value to a Variant object.
        /// </summary>
        public static explicit operator QualifiedName(Variant value)
        {
            return value.TryGet(out QualifiedName v) ? v : throw CannotCast<QualifiedName>();
        }

        /// <summary>
        /// Converts a LocalizedText value to a Variant object.
        /// </summary>
        public static explicit operator LocalizedText(Variant value)
        {
            return value.TryGet(out LocalizedText v) ? v : throw CannotCast<LocalizedText>();
        }

        /// <summary>
        /// Converts a ExtensionObject value to a Variant object.
        /// </summary>
        public static explicit operator ExtensionObject(Variant value)
        {
            return value.TryGet(out ExtensionObject v) ? v : throw CannotCast<ExtensionObject>();
        }

        /// <summary>
        /// Converts a DataValue value to a Variant object.
        /// </summary>
        public static explicit operator DataValue(Variant value)
        {
            return value.TryGet(out DataValue v) ? v : throw CannotCast<DataValue>();
        }

        /// <summary>
        /// Converts a Variant to a bool-array value.
        /// </summary>
        public static explicit operator ArrayOf<bool>(Variant value)
        {
            return value.TryGet(out ArrayOf<bool> v) ? v : throw CannotCast<ArrayOf<bool>>();
        }

        /// <summary>
        /// Converts a Variant to a sbyte-array value.
        /// </summary>
        public static explicit operator ArrayOf<sbyte>(Variant value)
        {
            return value.TryGet(out ArrayOf<sbyte> v) ? v : throw CannotCast<ArrayOf<sbyte>>();
        }

        /// <summary>
        /// Converts a Variant to a byte-array value.
        /// </summary>
        public static explicit operator ArrayOf<byte>(Variant value)
        {
            return value.TryGet(out ArrayOf<byte> v) ? v : throw CannotCast<ArrayOf<byte>>();
        }

        /// <summary>
        /// Converts a Variant to a short-array value.
        /// </summary>
        public static explicit operator ArrayOf<short>(Variant value)
        {
            return value.TryGet(out ArrayOf<short> v) ? v : throw CannotCast<ArrayOf<short>>();
        }

        /// <summary>
        /// Converts a Variant to a ushort-array value.
        /// </summary>
        public static explicit operator ArrayOf<ushort>(Variant value)
        {
            return value.TryGet(out ArrayOf<ushort> v) ? v : throw CannotCast<ArrayOf<ushort>>();
        }

        /// <summary>
        /// Converts a Variant to a int-array value.
        /// </summary>
        public static explicit operator ArrayOf<int>(Variant value)
        {
            return value.TryGet(out ArrayOf<int> v) ? v : throw CannotCast<ArrayOf<int>>();
        }

        /// <summary>
        /// Converts a Variant to a uint-array value.
        /// </summary>
        public static explicit operator ArrayOf<uint>(Variant value)
        {
            return value.TryGet(out ArrayOf<uint> v) ? v : throw CannotCast<ArrayOf<uint>>();
        }

        /// <summary>
        /// Converts a Variant to a long-array value.
        /// </summary>
        public static explicit operator ArrayOf<long>(Variant value)
        {
            return value.TryGet(out ArrayOf<long> v) ? v : throw CannotCast<ArrayOf<long>>();
        }

        /// <summary>
        /// Converts a Variant to a ulong-array value.
        /// </summary>
        public static explicit operator ArrayOf<ulong>(Variant value)
        {
            return value.TryGet(out ArrayOf<ulong> v) ? v : throw CannotCast<ArrayOf<ulong>>();
        }

        /// <summary>
        /// Converts a Variant to a float-array value.
        /// </summary>
        public static explicit operator ArrayOf<float>(Variant value)
        {
            return value.TryGet(out ArrayOf<float> v) ? v : throw CannotCast<ArrayOf<float>>();
        }

        /// <summary>
        /// Converts a Variant to a double-array value.
        /// </summary>
        public static explicit operator ArrayOf<double>(Variant value)
        {
            return value.TryGet(out ArrayOf<double> v) ? v : throw CannotCast<ArrayOf<double>>();
        }

        /// <summary>
        /// Converts a Variant to a string-array value.
        /// </summary>
        public static explicit operator ArrayOf<string>(Variant value)
        {
            return value.TryGet(out ArrayOf<string> v) ? v : throw CannotCast<ArrayOf<string>>();
        }

        /// <summary>
        /// Converts a Variant to a DateTime-array value.
        /// </summary>
        public static explicit operator ArrayOf<DateTime>(Variant value)
        {
            return value.TryGet(out ArrayOf<DateTime> v) ? v : throw CannotCast<ArrayOf<DateTime>>();
        }

        /// <summary>
        /// Converts a Variant to a Uuid-array value.
        /// </summary>
        public static explicit operator ArrayOf<Uuid>(Variant value)
        {
            return value.TryGet(out ArrayOf<Uuid> v) ? v : throw CannotCast<ArrayOf<Uuid>>();
        }

        /// <summary>
        /// Converts a Variant to a ByteString-array value.
        /// </summary>
        public static explicit operator ArrayOf<ByteString>(Variant value)
        {
            return value.TryGet(out ArrayOf<ByteString> v) ? v : throw CannotCast<ArrayOf<ByteString>>();
        }

        /// <summary>
        /// Converts a Variant to a XmlElement-array value.
        /// </summary>
        public static explicit operator ArrayOf<XmlElement>(Variant value)
        {
            return value.TryGet(out ArrayOf<XmlElement> v) ? v : throw CannotCast<ArrayOf<XmlElement>>();
        }

        /// <summary>
        /// Converts a Variant to a NodeId-array value.
        /// </summary>
        public static explicit operator ArrayOf<NodeId>(Variant value)
        {
            return value.TryGet(out ArrayOf<NodeId> v) ? v : throw CannotCast<ArrayOf<NodeId>>();
        }

        /// <summary>
        /// Converts a Variant to a ExpandedNodeId-array value.
        /// </summary>
        public static explicit operator ArrayOf<ExpandedNodeId>(Variant value)
        {
            return value.TryGet(out ArrayOf<ExpandedNodeId> v) ? v : throw CannotCast<ArrayOf<ExpandedNodeId>>();
        }

        /// <summary>
        /// Converts a Variant to a StatusCode-array value.
        /// </summary>
        public static explicit operator ArrayOf<StatusCode>(Variant value)
        {
            return value.TryGet(out ArrayOf<StatusCode> v) ? v : throw CannotCast<ArrayOf<StatusCode>>();
        }

        /// <summary>
        /// Converts a Variant to a QualifiedName-array value.
        /// </summary>
        public static explicit operator ArrayOf<QualifiedName>(Variant value)
        {
            return value.TryGet(out ArrayOf<QualifiedName> v) ? v : throw CannotCast<ArrayOf<QualifiedName>>();
        }

        /// <summary>
        /// Converts a Variant to a LocalizedText-array value.
        /// </summary>
        public static explicit operator ArrayOf<LocalizedText>(Variant value)
        {
            return value.TryGet(out ArrayOf<LocalizedText> v) ? v : throw CannotCast<ArrayOf<LocalizedText>>();
        }

        /// <summary>
        /// Converts a Variant to a ExtensionObject-array value.
        /// </summary>
        public static explicit operator ArrayOf<ExtensionObject>(Variant value)
        {
            return value.TryGet(out ArrayOf<ExtensionObject> v) ? v : throw CannotCast<ArrayOf<ExtensionObject>>();
        }

        /// <summary>
        /// Converts a Variant to a DataValue-array value.
        /// </summary>
        public static explicit operator ArrayOf<DataValue>(Variant value)
        {
            return value.TryGet(out ArrayOf<DataValue> v) ? v : throw CannotCast<ArrayOf<DataValue>>();
        }

        /// <summary>
        /// Converts a Variant to a Variant-array value.
        /// </summary>
        public static explicit operator ArrayOf<Variant>(Variant value)
        {
            return value.TryGet(out ArrayOf<Variant> v) ? v : throw CannotCast<ArrayOf<Variant>>();
        }

        /// <summary>
        /// Converts a Variant to a bool-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<bool>(Variant value)
        {
            return value.TryGet(out MatrixOf<bool> v) ? v : throw CannotCast<MatrixOf<bool>>();
        }

        /// <summary>
        /// Converts a Variant to a sbyte-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<sbyte>(Variant value)
        {
            return value.TryGet(out MatrixOf<sbyte> v) ? v : throw CannotCast<MatrixOf<sbyte>>();
        }

        /// <summary>
        /// Converts a Variant to a byte-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<byte>(Variant value)
        {
            return value.TryGet(out MatrixOf<byte> v) ? v : throw CannotCast<MatrixOf<byte>>();
        }

        /// <summary>
        /// Converts a Variant to a short-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<short>(Variant value)
        {
            return value.TryGet(out MatrixOf<short> v) ? v : throw CannotCast<MatrixOf<short>>();
        }

        /// <summary>
        /// Converts a Variant to a ushort-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<ushort>(Variant value)
        {
            return value.TryGet(out MatrixOf<ushort> v) ? v : throw CannotCast<MatrixOf<ushort>>();
        }

        /// <summary>
        /// Converts a Variant to a int-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<int>(Variant value)
        {
            return value.TryGet(out MatrixOf<int> v) ? v : throw CannotCast<MatrixOf<int>>();
        }

        /// <summary>
        /// Converts a Variant to a uint-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<uint>(Variant value)
        {
            return value.TryGet(out MatrixOf<uint> v) ? v : throw CannotCast<MatrixOf<uint>>();
        }

        /// <summary>
        /// Converts a Variant to a long-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<long>(Variant value)
        {
            return value.TryGet(out MatrixOf<long> v) ? v : throw CannotCast<MatrixOf<long>>();
        }

        /// <summary>
        /// Converts a Variant to a ulong-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<ulong>(Variant value)
        {
            return value.TryGet(out MatrixOf<ulong> v) ? v : throw CannotCast<MatrixOf<ulong>>();
        }

        /// <summary>
        /// Converts a Variant to a float-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<float>(Variant value)
        {
            return value.TryGet(out MatrixOf<float> v) ? v : throw CannotCast<MatrixOf<float>>();
        }

        /// <summary>
        /// Converts a Variant to a double-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<double>(Variant value)
        {
            return value.TryGet(out MatrixOf<double> v) ? v : throw CannotCast<MatrixOf<double>>();
        }

        /// <summary>
        /// Converts a Variant to a string-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<string>(Variant value)
        {
            return value.TryGet(out MatrixOf<string> v) ? v : throw CannotCast<MatrixOf<string>>();
        }

        /// <summary>
        /// Converts a Variant to a DateTime-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<DateTime>(Variant value)
        {
            return value.TryGet(out MatrixOf<DateTime> v) ? v : throw CannotCast<MatrixOf<DateTime>>();
        }

        /// <summary>
        /// Converts a Variant to a Uuid-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<Uuid>(Variant value)
        {
            return value.TryGet(out MatrixOf<Uuid> v) ? v : throw CannotCast<MatrixOf<Uuid>>();
        }

        /// <summary>
        /// Converts a Variant to a ByteString-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<ByteString>(Variant value)
        {
            return value.TryGet(out MatrixOf<ByteString> v) ? v : throw CannotCast<MatrixOf<ByteString>>();
        }

        /// <summary>
        /// Converts a Variant to a XmlElement-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<XmlElement>(Variant value)
        {
            return value.TryGet(out MatrixOf<XmlElement> v) ? v : throw CannotCast<MatrixOf<XmlElement>>();
        }

        /// <summary>
        /// Converts a Variant to a NodeId-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<NodeId>(Variant value)
        {
            return value.TryGet(out MatrixOf<NodeId> v) ? v : throw CannotCast<MatrixOf<NodeId>>();
        }

        /// <summary>
        /// Converts a Variant to a ExpandedNodeId-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<ExpandedNodeId>(Variant value)
        {
            return value.TryGet(out MatrixOf<ExpandedNodeId> v) ? v : throw CannotCast<MatrixOf<ExpandedNodeId>>();
        }

        /// <summary>
        /// Converts a Variant to a StatusCode-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<StatusCode>(Variant value)
        {
            return value.TryGet(out MatrixOf<StatusCode> v) ? v : throw CannotCast<MatrixOf<StatusCode>>();
        }

        /// <summary>
        /// Converts a Variant to a QualifiedName-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<QualifiedName>(Variant value)
        {
            return value.TryGet(out MatrixOf<QualifiedName> v) ? v : throw CannotCast<MatrixOf<QualifiedName>>();
        }

        /// <summary>
        /// Converts a Variant to a LocalizedText-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<LocalizedText>(Variant value)
        {
            return value.TryGet(out MatrixOf<LocalizedText> v) ? v : throw CannotCast<MatrixOf<LocalizedText>>();
        }

        /// <summary>
        /// Converts a Variant to a ExtensionObject-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<ExtensionObject>(Variant value)
        {
            return value.TryGet(out MatrixOf<ExtensionObject> v) ? v : throw CannotCast<MatrixOf<ExtensionObject>>();
        }

        /// <summary>
        /// Converts a Variant to a DataValue-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<DataValue>(Variant value)
        {
            return value.TryGet(out MatrixOf<DataValue> v) ? v : throw CannotCast<MatrixOf<DataValue>>();
        }

        /// <summary>
        /// Converts a Variant to a Variant-matrix value.
        /// </summary>
        public static explicit operator MatrixOf<Variant>(Variant value)
        {
            return value.TryGet(out MatrixOf<Variant> v) ? v : throw CannotCast<MatrixOf<Variant>>();
        }

        /// <summary>
        /// Converts to a Variant with Boolean
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToBoolean()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.Boolean:
                    return this;
                case BuiltInType.SByte:
                    return Convert.ToBoolean(GetSByte());
                case BuiltInType.Byte:
                    return Convert.ToBoolean(GetByte());
                case BuiltInType.Int16:
                    return Convert.ToBoolean(GetInt16());
                case BuiltInType.UInt16:
                    return Convert.ToBoolean(GetUInt16());
                case BuiltInType.Int32:
                    return Convert.ToBoolean(GetInt32());
                case BuiltInType.UInt32:
                    return Convert.ToBoolean(GetUInt32());
                case BuiltInType.Int64:
                    return Convert.ToBoolean(GetInt64());
                case BuiltInType.UInt64:
                    return Convert.ToBoolean(GetUInt64());
                case BuiltInType.Float:
                    return Convert.ToBoolean(GetFloat());
                case BuiltInType.Double:
                    return Convert.ToBoolean(GetDouble());
                case BuiltInType.String:
                    return XmlConvert.ToBoolean(GetString());
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with SByte
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToSByte()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.SByte:
                    return this;
                case BuiltInType.Boolean:
                    return Convert.ToSByte(GetBoolean());
                case BuiltInType.Byte:
                    return Convert.ToSByte(GetByte());
                case BuiltInType.Int16:
                    return Convert.ToSByte(GetInt16());
                case BuiltInType.UInt16:
                    return Convert.ToSByte(GetUInt16());
                case BuiltInType.Int32:
                    return Convert.ToSByte(GetInt32());
                case BuiltInType.UInt32:
                    return Convert.ToSByte(GetUInt32());
                case BuiltInType.Int64:
                    return Convert.ToSByte(GetInt64());
                case BuiltInType.UInt64:
                    return Convert.ToSByte(GetUInt64());
                case BuiltInType.Float:
                    return Convert.ToSByte(GetFloat());
                case BuiltInType.Double:
                    return Convert.ToSByte(GetDouble());
                case BuiltInType.String:
                    return XmlConvert.ToSByte(GetString());
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with Byte
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToByte()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.Byte:
                    return this;
                case BuiltInType.Boolean:
                    return Convert.ToByte(GetBoolean());
                case BuiltInType.SByte:
                    return Convert.ToByte(GetSByte());
                case BuiltInType.Int16:
                    return Convert.ToByte(GetInt16());
                case BuiltInType.UInt16:
                    return Convert.ToByte(GetUInt16());
                case BuiltInType.Int32:
                    return Convert.ToByte(GetInt32());
                case BuiltInType.UInt32:
                    return Convert.ToByte(GetUInt32());
                case BuiltInType.Int64:
                    return Convert.ToByte(GetInt64());
                case BuiltInType.UInt64:
                    return Convert.ToByte(GetUInt64());
                case BuiltInType.Float:
                    return Convert.ToByte(GetFloat());
                case BuiltInType.Double:
                    return Convert.ToByte(GetDouble());
                case BuiltInType.String:
                    return XmlConvert.ToByte(GetString());
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with Int16
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToInt16()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.Int16:
                    return this;
                case BuiltInType.Boolean:
                    return Convert.ToInt16(GetBoolean());
                case BuiltInType.SByte:
                    return Convert.ToInt16(GetSByte());
                case BuiltInType.Byte:
                    return Convert.ToInt16(GetByte());
                case BuiltInType.UInt16:
                    return Convert.ToInt16(GetUInt16());
                case BuiltInType.Int32:
                    return Convert.ToInt16(GetInt32());
                case BuiltInType.UInt32:
                    return Convert.ToInt16(GetUInt32());
                case BuiltInType.Int64:
                    return Convert.ToInt16(GetInt64());
                case BuiltInType.UInt64:
                    return Convert.ToInt16(GetUInt64());
                case BuiltInType.Float:
                    return Convert.ToInt16(GetFloat());
                case BuiltInType.Double:
                    return Convert.ToInt16(GetDouble());
                case BuiltInType.String:
                    return XmlConvert.ToInt16(GetString());
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with UInt16
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToUInt16()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.UInt16:
                    return this;
                case BuiltInType.Boolean:
                    return Convert.ToUInt16(GetBoolean());
                case BuiltInType.SByte:
                    return Convert.ToUInt16(GetSByte());
                case BuiltInType.Byte:
                    return Convert.ToUInt16(GetByte());
                case BuiltInType.Int16:
                    return Convert.ToUInt16(GetInt16());
                case BuiltInType.Int32:
                    return Convert.ToUInt16(GetInt32());
                case BuiltInType.UInt32:
                    return Convert.ToUInt16(GetUInt32());
                case BuiltInType.Int64:
                    return Convert.ToUInt16(GetInt64());
                case BuiltInType.UInt64:
                    return Convert.ToUInt16(GetUInt64());
                case BuiltInType.Float:
                    return Convert.ToUInt16(GetFloat());
                case BuiltInType.Double:
                    return Convert.ToUInt16(GetDouble());
                case BuiltInType.String:
                    return XmlConvert.ToUInt16(GetString());
                case BuiltInType.StatusCode:
                    StatusCode code = GetStatusCode();
                    return (ushort)(code.CodeBits >> 16);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with Int32
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToInt32()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.Int32:
                    return this;
                case BuiltInType.Boolean:
                    return Convert.ToInt32(GetBoolean());
                case BuiltInType.SByte:
                    return Convert.ToInt32(GetSByte());
                case BuiltInType.Byte:
                    return Convert.ToInt32(GetByte());
                case BuiltInType.Int16:
                    return Convert.ToInt32(GetInt16());
                case BuiltInType.UInt16:
                    return Convert.ToInt32(GetUInt16());
                case BuiltInType.UInt32:
                    return Convert.ToInt32(GetUInt32());
                case BuiltInType.Int64:
                    return Convert.ToInt32(GetInt64());
                case BuiltInType.UInt64:
                    return Convert.ToInt32(GetUInt64());
                case BuiltInType.Float:
                    return Convert.ToInt32(GetFloat());
                case BuiltInType.Double:
                    return Convert.ToInt32(GetDouble());
                case BuiltInType.String:
                    return XmlConvert.ToInt32(GetString());
                case BuiltInType.StatusCode:
                    return Convert.ToInt32(GetStatusCode().Code);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with UInt32
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToUInt32()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.UInt32:
                    return this;
                case BuiltInType.Boolean:
                    return Convert.ToUInt32(GetBoolean());
                case BuiltInType.SByte:
                    return Convert.ToUInt32(GetSByte());
                case BuiltInType.Byte:
                    return Convert.ToUInt32(GetByte());
                case BuiltInType.Int16:
                    return Convert.ToUInt32(GetInt16());
                case BuiltInType.UInt16:
                    return Convert.ToUInt32(GetUInt16());
                case BuiltInType.Int32:
                    return Convert.ToUInt32(GetInt32());
                case BuiltInType.Int64:
                    return Convert.ToUInt32(GetInt64());
                case BuiltInType.UInt64:
                    return Convert.ToUInt32(GetUInt64());
                case BuiltInType.Float:
                    return Convert.ToUInt32(GetFloat());
                case BuiltInType.Double:
                    return Convert.ToUInt32(GetDouble());
                case BuiltInType.String:
                    return XmlConvert.ToUInt32(GetString());
                case BuiltInType.StatusCode:
                    return Convert.ToUInt32(GetStatusCode().Code);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with Int64
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToInt64()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.Int64:
                    return this;
                case BuiltInType.Boolean:
                    return Convert.ToInt64(GetBoolean());
                case BuiltInType.SByte:
                    return Convert.ToInt64(GetSByte());
                case BuiltInType.Byte:
                    return Convert.ToInt64(GetByte());
                case BuiltInType.Int16:
                    return Convert.ToInt64(GetInt16());
                case BuiltInType.UInt16:
                    return Convert.ToInt64(GetUInt16());
                case BuiltInType.Int32:
                    return Convert.ToInt64(GetInt32());
                case BuiltInType.UInt32:
                    return Convert.ToInt64(GetUInt32());
                case BuiltInType.UInt64:
                    return Convert.ToInt64(GetUInt64());
                case BuiltInType.Float:
                    return Convert.ToInt64(GetFloat());
                case BuiltInType.Double:
                    return Convert.ToInt64(GetDouble());
                case BuiltInType.String:
                    return XmlConvert.ToInt64(GetString());
                case BuiltInType.StatusCode:
                    return Convert.ToInt64(GetStatusCode().Code);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with UInt64
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToUInt64()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.UInt64:
                    return this;
                case BuiltInType.Boolean:
                    return Convert.ToUInt64(GetBoolean());
                case BuiltInType.SByte:
                    return Convert.ToUInt64(GetSByte());
                case BuiltInType.Byte:
                    return Convert.ToUInt64(GetByte());
                case BuiltInType.Int16:
                    return Convert.ToUInt64(GetInt16());
                case BuiltInType.UInt16:
                    return Convert.ToUInt64(GetUInt16());
                case BuiltInType.Int32:
                    return Convert.ToUInt64(GetInt32());
                case BuiltInType.UInt32:
                    return Convert.ToUInt64(GetUInt32());
                case BuiltInType.Int64:
                    return Convert.ToUInt64(GetInt64());
                case BuiltInType.Float:
                    return Convert.ToUInt64(GetFloat());
                case BuiltInType.Double:
                    return Convert.ToUInt64(GetDouble());
                case BuiltInType.String:
                    return XmlConvert.ToUInt64(GetString());
                case BuiltInType.StatusCode:
                    return Convert.ToUInt64(GetStatusCode().Code);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with Float
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToFloat()
        {
            switch (TypeInfo.BuiltInType)
            {
                // TODO: BitConverter.Int32BitsToSingle
                case BuiltInType.Float:
                    return this;
                case BuiltInType.Boolean:
                    return Convert.ToSingle(GetBoolean());
                case BuiltInType.SByte:
                    return Convert.ToSingle(GetSByte());
                case BuiltInType.Byte:
                    return Convert.ToSingle(GetByte());
                case BuiltInType.Int16:
                    return Convert.ToSingle(GetInt16());
                case BuiltInType.UInt16:
                    return Convert.ToSingle(GetUInt16());
                case BuiltInType.Int32:
                    return Convert.ToSingle(GetInt32());
                case BuiltInType.UInt32:
                    return Convert.ToSingle(GetUInt32());
                case BuiltInType.Int64:
                    return Convert.ToSingle(GetInt64());
                case BuiltInType.UInt64:
                    return Convert.ToSingle(GetUInt64());
                case BuiltInType.Double:
                    return Convert.ToSingle(GetDouble());
                case BuiltInType.String:
                    return XmlConvert.ToSingle(GetString());
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with Double
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToDouble()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.Double:
                    return this;
                case BuiltInType.Boolean:
                    return Convert.ToDouble(GetBoolean());
                case BuiltInType.SByte:
                    return Convert.ToDouble(GetSByte());
                case BuiltInType.Byte:
                    return Convert.ToDouble(GetByte());
                case BuiltInType.Int16:
                    return Convert.ToDouble(GetInt16());
                case BuiltInType.UInt16:
                    return Convert.ToDouble(GetUInt16());
                case BuiltInType.Int32:
                    return Convert.ToDouble(GetInt32());
                case BuiltInType.UInt32:
                    return Convert.ToDouble(GetUInt32());
                case BuiltInType.Int64:
                    return Convert.ToDouble(GetInt64());
                case BuiltInType.UInt64:
                    return Convert.ToDouble(GetUInt64());
                case BuiltInType.Float:
                    return Convert.ToDouble(GetFloat());
                case BuiltInType.String:
                    return XmlConvert.ToDouble(GetString());
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with String
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToString()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.String:
                    return this;
                case BuiltInType.Boolean:
                    return XmlConvert.ToString(GetBoolean());
                case BuiltInType.SByte:
                    return XmlConvert.ToString(GetSByte());
                case BuiltInType.Byte:
                    return XmlConvert.ToString(GetByte());
                case BuiltInType.Int16:
                    return XmlConvert.ToString(GetInt16());
                case BuiltInType.UInt16:
                    return XmlConvert.ToString(GetUInt16());
                case BuiltInType.Int32:
                    return XmlConvert.ToString(GetInt32());
                case BuiltInType.UInt32:
                    return XmlConvert.ToString(GetUInt32());
                case BuiltInType.Int64:
                    return XmlConvert.ToString(GetInt64());
                case BuiltInType.UInt64:
                    return XmlConvert.ToString(GetUInt64());
                case BuiltInType.Float:
                    return XmlConvert.ToString(GetFloat());
                case BuiltInType.Double:
                    return XmlConvert.ToString(GetDouble());
                case BuiltInType.DateTime:
                    return XmlConvert.ToString(GetDateTime(), XmlDateTimeSerializationMode.Unspecified);
                case BuiltInType.Guid:
                    return GetGuid().ToString();
                case BuiltInType.NodeId:
                    return GetNodeId().ToString();
                case BuiltInType.ExpandedNodeId:
                    return GetExpandedNodeId().ToString();
                case BuiltInType.LocalizedText:
                    return GetLocalizedText().Text;
                case BuiltInType.QualifiedName:
                    return GetQualifiedName().ToString();
                case BuiltInType.XmlElement:
                    return GetXmlElement().OuterXml;
                case BuiltInType.StatusCode:
                    return GetStatusCode().Code.ToString(CultureInfo.InvariantCulture);
                case BuiltInType.ExtensionObject:
                    return GetExtensionObject().ToString();
                case BuiltInType.Null:
                    return default;
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with DateTime
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToDateTime()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.DateTime:
                    return this;
                case BuiltInType.String:
                    return XmlConvert.ToDateTime(GetString(), XmlDateTimeSerializationMode.Unspecified);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with Guid
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToGuid()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.Guid:
                    return this;
                case BuiltInType.String:
                    return Uuid.Parse(GetString());
                case BuiltInType.ByteString:
                    return new Uuid(GetByteString());
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with ByteString
        /// </summary>
        /// <exception cref="FormatException"></exception>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToByteString()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.ByteString:
                    return this;
                case BuiltInType.String:
                    string text = GetString();
                    if (text == null)
                    {
                        return new Variant(ByteString.Empty);
                    }
                    return ByteString.FromHexString(text);
                case BuiltInType.Guid:
                    return ByteString.From(GetGuid().ToByteArray());
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with XmlElement
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToXmlElement()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.XmlElement:
                    return this;
                case BuiltInType.String:
                    return XmlElement.From(GetString());
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with NodeId
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToNodeId()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.NodeId:
                    return this;
                case BuiltInType.ExpandedNodeId:
                    return (NodeId)GetExpandedNodeId();
                case BuiltInType.String:
                    return NodeId.Parse(GetString());
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with ExpandedNodeId
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToExpandedNodeId()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.ExpandedNodeId:
                    return this;
                case BuiltInType.NodeId:
                    return (ExpandedNodeId)GetNodeId();
                case BuiltInType.String:
                    return ExpandedNodeId.Parse(GetString());
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with StatusCode
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToStatusCode()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.StatusCode:
                    return this;
                case BuiltInType.UInt16:
                    uint code = Convert.ToUInt32(GetUInt16(), CultureInfo.InvariantCulture);
                    code <<= 16;
                    return (StatusCode)code;
                case BuiltInType.Int32:
                    return (StatusCode)Convert.ToUInt32(GetInt32(), CultureInfo.InvariantCulture);
                case BuiltInType.UInt32:
                    return (StatusCode)GetUInt32();
                case BuiltInType.Int64:
                    return (StatusCode)Convert.ToUInt32(GetInt64(), CultureInfo.InvariantCulture);
                case BuiltInType.UInt64:
                    return (StatusCode)Convert.ToUInt32(GetUInt64());
                case BuiltInType.String:
                    string text = GetString();
                    if (text == null)
                    {
                        return StatusCodes.Good;
                    }
                    text = text.Trim();
                    if (text.StartsWith("0x", StringComparison.Ordinal))
                    {
                        return (StatusCode)Convert.ToUInt32(text[2..], 16);
                    }
                    return (StatusCode)Convert.ToUInt32(text, CultureInfo.InvariantCulture);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with QualifiedName
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToQualifiedName()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.QualifiedName:
                    return this;
                case BuiltInType.String:
                    return QualifiedName.Parse(GetString());
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts to a Variant with LocalizedText
        /// </summary>
        /// <exception cref="InvalidCastException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertToLocalizedText()
        {
            switch (TypeInfo.BuiltInType)
            {
                case BuiltInType.LocalizedText:
                    return this;
                case BuiltInType.String:
                    return new LocalizedText(GetString());
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    // conversion not supported.
                    throw new InvalidCastException();
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected BuiltInType {TypeInfo.BuiltInType}");
            }
        }

        /// <summary>
        /// Converts a variant to a Variant with a specified target type. Throws if
        /// conversion is not possible.
        /// </summary>
        /// <param name="targetType">Type of the target.</param>
        /// <returns>Returns a converted variant.</returns>
        /// <exception cref="InvalidCastException">if impossible to cast.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public Variant ConvertTo(BuiltInType targetType)
        {
            if (TypeInfo.BuiltInType == BuiltInType.Null)
            {
                return default;
            }
            if (TypeInfo.BuiltInType == targetType)
            {
                return this;
            }
            if (TypeInfo.IsScalar)
            {
                switch (targetType)
                {
                    case BuiltInType.Boolean:
                        return ConvertToBoolean();
                    case BuiltInType.SByte:
                        return ConvertToSByte();
                    case BuiltInType.Byte:
                        return ConvertToByte();
                    case BuiltInType.Int16:
                        return ConvertToInt16();
                    case BuiltInType.UInt16:
                        return ConvertToUInt16();
                    case BuiltInType.Int32:
                        return ConvertToInt32();
                    case BuiltInType.UInt32:
                        return ConvertToUInt32();
                    case BuiltInType.Int64:
                        return ConvertToInt64();
                    case BuiltInType.UInt64:
                        return ConvertToUInt64();
                    case BuiltInType.Float:
                        return ConvertToFloat();
                    case BuiltInType.Double:
                        return ConvertToDouble();
                    case BuiltInType.String:
                        return ConvertToString();
                    case BuiltInType.DateTime:
                        return ConvertToDateTime();
                    case BuiltInType.Guid:
                        return ConvertToGuid();
                    case BuiltInType.ByteString:
                        return ConvertToByteString();
                    case BuiltInType.NodeId:
                        return ConvertToNodeId();
                    case BuiltInType.ExpandedNodeId:
                        return ConvertToExpandedNodeId();
                    case BuiltInType.StatusCode:
                        return ConvertToStatusCode();
                    case BuiltInType.QualifiedName:
                        return ConvertToQualifiedName();
                    case BuiltInType.LocalizedText:
                        return ConvertToLocalizedText();
                    case BuiltInType.Variant:
                        return this;
                    case BuiltInType.Number:
                        return ConvertToDouble();
                    case BuiltInType.Integer:
                        return ConvertToInt64();
                    case BuiltInType.UInteger:
                        return ConvertToUInt64();
                    case BuiltInType.Enumeration:
                        return ConvertToInt32();
                    case BuiltInType.XmlElement:
                        return ConvertToXmlElement();
                    case BuiltInType.Null:
                    case BuiltInType.ExtensionObject:
                    case BuiltInType.DataValue:
                    case BuiltInType.DiagnosticInfo:
                        throw new InvalidCastException();
                    default:
                        throw ServiceResultException.Unexpected(
                            $"Unexpected BuiltInType {targetType}");
                }
            }
            if (TypeInfo.IsArray)
            {
                return Collapse(Expand().ToArrayOf(v => v.ConvertTo(targetType)));
            }
            throw new InvalidCastException();
        }

        /// <inheritdoc/>
        public bool Equals(bool value)
        {
            return TryGet(out bool v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(sbyte value)
        {
            return TryGet(out sbyte v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(byte value)
        {
            return TryGet(out byte v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(short value)
        {
            return TryGet(out short v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ushort value)
        {
            return TryGet(out ushort v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(int value)
        {
            return TryGet(out int v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(Enum value)
        {
            return TryGet(out int v) &&
                v == Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        /// <inheritdoc/>
        public bool Equals(uint value)
        {
            return TryGet(out uint v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(long value)
        {
            return TryGet(out long v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ulong value)
        {
            return TryGet(out ulong v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(float value)
        {
            return TryGet(out float v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(double value)
        {
            return TryGet(out double v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(string value)
        {
            return TryGet(out string v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(DateTime value)
        {
            return TryGet(out DateTime v) &&
                DateTimeComparer.Default.Equals(v, value);
        }

        /// <inheritdoc/>
        public bool Equals(Uuid value)
        {
            return TryGet(out Uuid v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ByteString value)
        {
            return TryGet(out ByteString v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(XmlElement value)
        {
            return TryGet(out XmlElement v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(NodeId value)
        {
            return TryGet(out NodeId v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ExpandedNodeId value)
        {
            return TryGet(out ExpandedNodeId v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(StatusCode value)
        {
            return TryGet(out StatusCode v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(QualifiedName value)
        {
            return TryGet(out QualifiedName v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(LocalizedText value)
        {
            return TryGet(out LocalizedText v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ExtensionObject value)
        {
            return TryGet(out ExtensionObject v) &&
                EqualityComparer<ExtensionObject>.Default.Equals(v, value);
        }

        /// <inheritdoc/>
        public bool Equals(DataValue value)
        {
            return TryGet(out DataValue v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<bool> value)
        {
            return TryGet(out ArrayOf<bool> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<sbyte> value)
        {
            return TryGet(out ArrayOf<sbyte> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<byte> value)
        {
            return TryGet(out ArrayOf<byte> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<short> value)
        {
            return TryGet(out ArrayOf<short> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<ushort> value)
        {
            return TryGet(out ArrayOf<ushort> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<int> value)
        {
            return TryGet(out ArrayOf<int> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<Enum> value)
        {
            return TryGet(out ArrayOf<int> v) &&
                value.ConvertAll(e => Convert.ToInt32(e, CultureInfo.InvariantCulture)) == v;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<uint> value)
        {
            return TryGet(out ArrayOf<uint> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<long> value)
        {
            return TryGet(out ArrayOf<long> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<ulong> value)
        {
            return TryGet(out ArrayOf<ulong> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<float> value)
        {
            return TryGet(out ArrayOf<float> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<double> value)
        {
            return TryGet(out ArrayOf<double> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<string> value)
        {
            return TryGet(out ArrayOf<string> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<DateTime> value)
        {
            return TryGet(out ArrayOf<DateTime> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<Uuid> value)
        {
            return TryGet(out ArrayOf<Uuid> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<ByteString> value)
        {
            return TryGet(out ArrayOf<ByteString> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<XmlElement> value)
        {
            return TryGet(out ArrayOf<XmlElement> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<NodeId> value)
        {
            return TryGet(out ArrayOf<NodeId> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<ExpandedNodeId> value)
        {
            return TryGet(out ArrayOf<ExpandedNodeId> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<StatusCode> value)
        {
            return TryGet(out ArrayOf<StatusCode> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<QualifiedName> value)
        {
            return TryGet(out ArrayOf<QualifiedName> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<LocalizedText> value)
        {
            return TryGet(out ArrayOf<LocalizedText> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<ExtensionObject> value)
        {
            return TryGet(out ArrayOf<ExtensionObject> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<DataValue> value)
        {
            return TryGet(out ArrayOf<DataValue> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(ArrayOf<Variant> value)
        {
            return TryGet(out ArrayOf<Variant> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<bool> value)
        {
            return TryGet(out MatrixOf<bool> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<sbyte> value)
        {
            return TryGet(out MatrixOf<sbyte> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<byte> value)
        {
            return TryGet(out MatrixOf<byte> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<short> value)
        {
            return TryGet(out MatrixOf<short> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<ushort> value)
        {
            return TryGet(out MatrixOf<ushort> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<int> value)
        {
            return TryGet(out MatrixOf<int> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<Enum> value)
        {
            return TryGet(out MatrixOf<int> v) &&
                value.ConvertAll(e => Convert.ToInt32(e, CultureInfo.InvariantCulture)) == v;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<uint> value)
        {
            return TryGet(out MatrixOf<uint> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<long> value)
        {
            return TryGet(out MatrixOf<long> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<ulong> value)
        {
            return TryGet(out MatrixOf<ulong> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<float> value)
        {
            return TryGet(out MatrixOf<float> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<double> value)
        {
            return TryGet(out MatrixOf<double> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<string> value)
        {
            return TryGet(out MatrixOf<string> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<DateTime> value)
        {
            return TryGet(out MatrixOf<DateTime> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<Uuid> value)
        {
            return TryGet(out MatrixOf<Uuid> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<ByteString> value)
        {
            return TryGet(out MatrixOf<ByteString> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<XmlElement> value)
        {
            return TryGet(out MatrixOf<XmlElement> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<NodeId> value)
        {
            return TryGet(out MatrixOf<NodeId> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<ExpandedNodeId> value)
        {
            return TryGet(out MatrixOf<ExpandedNodeId> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<StatusCode> value)
        {
            return TryGet(out MatrixOf<StatusCode> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<QualifiedName> value)
        {
            return TryGet(out MatrixOf<QualifiedName> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<LocalizedText> value)
        {
            return TryGet(out MatrixOf<LocalizedText> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<ExtensionObject> value)
        {
            return TryGet(out MatrixOf<ExtensionObject> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<DataValue> value)
        {
            return TryGet(out MatrixOf<DataValue> v) && v == value;
        }

        /// <inheritdoc/>
        public bool Equals(MatrixOf<Variant> value)
        {
            return TryGet(out MatrixOf<Variant> v) && v == value;
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, Variant b)
        {
            return a.Equals(b);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, Variant b)
        {
            return !a.Equals(b);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, bool value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, bool value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, sbyte value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, sbyte value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, byte value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, byte value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, short value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, short value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ushort value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ushort value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, int value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, int value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, Enum value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, Enum value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, uint value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, uint value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, long value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, long value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ulong value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ulong value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, float value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, float value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, double value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, double value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, string value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, string value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, DateTime value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, DateTime value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, Uuid value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, Uuid value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ByteString value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ByteString value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, XmlElement value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, XmlElement value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, NodeId value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, NodeId value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ExpandedNodeId value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ExpandedNodeId value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, StatusCode value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, StatusCode value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, QualifiedName value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, QualifiedName value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, LocalizedText value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, LocalizedText value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ExtensionObject value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ExtensionObject value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, DataValue value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, DataValue value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<bool> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<bool> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<sbyte> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<sbyte> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<byte> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<byte> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<short> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<short> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<ushort> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<ushort> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<int> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<int> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<Enum> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<Enum> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<uint> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<uint> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<long> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<long> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<ulong> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<ulong> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<float> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<float> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<double> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<double> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<string> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<string> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<DateTime> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<DateTime> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<Uuid> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<Uuid> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<ByteString> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<ByteString> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<XmlElement> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<XmlElement> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<NodeId> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<NodeId> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<ExpandedNodeId> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<ExpandedNodeId> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<StatusCode> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<StatusCode> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<QualifiedName> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<QualifiedName> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<LocalizedText> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<LocalizedText> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<ExtensionObject> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<ExtensionObject> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<DataValue> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<DataValue> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, ArrayOf<Variant> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, ArrayOf<Variant> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<bool> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<bool> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<sbyte> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<sbyte> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<byte> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<byte> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<short> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<short> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<ushort> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<ushort> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<int> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<int> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<Enum> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<Enum> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<uint> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<uint> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<long> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<long> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<ulong> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<ulong> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<float> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<float> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<double> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<double> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<string> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<string> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<DateTime> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<DateTime> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<Uuid> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<Uuid> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<ByteString> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<ByteString> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<XmlElement> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<XmlElement> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<NodeId> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<NodeId> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<ExpandedNodeId> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<ExpandedNodeId> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<StatusCode> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<StatusCode> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<QualifiedName> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<QualifiedName> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<LocalizedText> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<LocalizedText> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<ExtensionObject> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<ExtensionObject> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<DataValue> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<DataValue> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator ==(Variant a, MatrixOf<Variant> value)
        {
            return a.Equals(value);
        }

        /// <inheritdoc/>
        public static bool operator !=(Variant a, MatrixOf<Variant> value)
        {
            return !a.Equals(value);
        }

        /// <inheritdoc/>
        public bool Equals(Variant other)
        {
            if (IsNull && other.IsNull)
            {
                return true;
            }

            // Ensure we compare against a null variant correctly below.
            TypeInfo ourTypeInfo = IsNull ? other.TypeInfo : TypeInfo;
            TypeInfo otherTypeInfo = other.IsNull ? ourTypeInfo : other.TypeInfo;

            if ((ourTypeInfo.ValueRank != otherTypeInfo.ValueRank ||
                ourTypeInfo.BuiltInType != otherTypeInfo.BuiltInType) &&
                !IsConvertible(ourTypeInfo, otherTypeInfo))
            {
                return false;
            }
            if (ourTypeInfo.IsScalar)
            {
                switch (ourTypeInfo.BuiltInType)
                {
                    case BuiltInType.Null:
                        return otherTypeInfo.BuiltInType == BuiltInType.Null;
                    case BuiltInType.Boolean:
                        return Equals(other.GetBoolean());
                    case BuiltInType.SByte:
                        return Equals(other.GetSByte());
                    case BuiltInType.Byte:
                        return Equals(other.GetByte());
                    case BuiltInType.Int16:
                        return Equals(other.GetInt16());
                    case BuiltInType.UInt16:
                        return Equals(other.GetUInt16());
                    case BuiltInType.Enumeration:
                    case BuiltInType.Int32:
                        return Equals(other.GetInt32());
                    case BuiltInType.UInt32:
                        return Equals(other.GetUInt32());
                    case BuiltInType.Int64:
                        return Equals(other.GetInt64());
                    case BuiltInType.UInt64:
                        return Equals(other.GetUInt64());
                    case BuiltInType.Float:
                        return Equals(other.GetFloat());
                    case BuiltInType.Double:
                        return Equals(other.GetDouble());
                    case BuiltInType.String:
                        return Equals(other.GetString());
                    case BuiltInType.DateTime:
                        return Equals(other.GetDateTime());
                    case BuiltInType.Guid:
                        return Equals(other.GetGuid());
                    case BuiltInType.ByteString:
                        return Equals(other.GetByteString());
                    case BuiltInType.XmlElement:
                        return Equals(other.GetXmlElement());
                    case BuiltInType.NodeId:
                        return Equals(other.GetNodeId());
                    case BuiltInType.ExpandedNodeId:
                        return Equals(other.GetExpandedNodeId());
                    case BuiltInType.StatusCode:
                        return Equals(other.GetStatusCode());
                    case BuiltInType.QualifiedName:
                        return Equals(other.GetQualifiedName());
                    case BuiltInType.LocalizedText:
                        return Equals(other.GetLocalizedText());
                    case BuiltInType.ExtensionObject:
                        return Equals(other.GetExtensionObject());
                    case BuiltInType.DataValue:
                        return Equals(other.GetDataValue());
                    default:
                        Debug.Fail("Unexpected Built in type.");
                        return false;
                }
            }
            else if (ourTypeInfo.IsArray)
            {
                switch (ourTypeInfo.BuiltInType)
                {
                    case BuiltInType.Null:
                        return other.IsNull;
                    case BuiltInType.Boolean:
                        return Equals(other.GetBooleanArray());
                    case BuiltInType.SByte:
                        return Equals(other.GetSByteArray());
                    case BuiltInType.Byte:
                        return Equals(other.GetByteArray());
                    case BuiltInType.Int16:
                        return Equals(other.GetInt16Array());
                    case BuiltInType.UInt16:
                        return Equals(other.GetUInt16Array());
                    case BuiltInType.Enumeration:
                    case BuiltInType.Int32:
                        return Equals(other.GetInt32Array());
                    case BuiltInType.UInt32:
                        return Equals(other.GetUInt32Array());
                    case BuiltInType.Int64:
                        return Equals(other.GetInt64Array());
                    case BuiltInType.UInt64:
                        return Equals(other.GetUInt64Array());
                    case BuiltInType.Float:
                        return Equals(other.GetFloatArray());
                    case BuiltInType.Double:
                        return Equals(other.GetDoubleArray());
                    case BuiltInType.String:
                        return Equals(other.GetStringArray());
                    case BuiltInType.DateTime:
                        return Equals(other.GetDateTimeArray());
                    case BuiltInType.Guid:
                        return Equals(other.GetGuidArray());
                    case BuiltInType.ByteString:
                        return Equals(other.GetByteStringArray());
                    case BuiltInType.XmlElement:
                        return Equals(other.GetXmlElementArray());
                    case BuiltInType.NodeId:
                        return Equals(other.GetNodeIdArray());
                    case BuiltInType.ExpandedNodeId:
                        return Equals(other.GetExpandedNodeIdArray());
                    case BuiltInType.StatusCode:
                        return Equals(other.GetStatusCodeArray());
                    case BuiltInType.QualifiedName:
                        return Equals(other.GetQualifiedNameArray());
                    case BuiltInType.LocalizedText:
                        return Equals(other.GetLocalizedTextArray());
                    case BuiltInType.ExtensionObject:
                        return Equals(other.GetExtensionObjectArray());
                    case BuiltInType.DataValue:
                        return Equals(other.GetDataValueArray());
                    case BuiltInType.Variant:
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                        return Equals(other.GetVariantArray());
                    default:
                        Debug.Fail("Unexpected Built in type.");
                        return false;
                }
            }
            else if (ourTypeInfo.IsMatrix)
            {
                switch (ourTypeInfo.BuiltInType)
                {
                    case BuiltInType.Null:
                        return other.IsNull;
                    case BuiltInType.Boolean:
                        return Equals(other.GetBooleanMatrix());
                    case BuiltInType.SByte:
                        return Equals(other.GetSByteMatrix());
                    case BuiltInType.Byte:
                        return Equals(other.GetByteMatrix());
                    case BuiltInType.Int16:
                        return Equals(other.GetInt16Matrix());
                    case BuiltInType.UInt16:
                        return Equals(other.GetUInt16Matrix());
                    case BuiltInType.Enumeration:
                    case BuiltInType.Int32:
                        return Equals(other.GetInt32Matrix());
                    case BuiltInType.UInt32:
                        return Equals(other.GetUInt32Matrix());
                    case BuiltInType.Int64:
                        return Equals(other.GetInt64Matrix());
                    case BuiltInType.UInt64:
                        return Equals(other.GetUInt64Matrix());
                    case BuiltInType.Float:
                        return Equals(other.GetFloatMatrix());
                    case BuiltInType.Double:
                        return Equals(other.GetDoubleMatrix());
                    case BuiltInType.String:
                        return Equals(other.GetStringMatrix());
                    case BuiltInType.DateTime:
                        return Equals(other.GetDateTimeMatrix());
                    case BuiltInType.Guid:
                        return Equals(other.GetGuidMatrix());
                    case BuiltInType.ByteString:
                        return Equals(other.GetByteStringMatrix());
                    case BuiltInType.XmlElement:
                        return Equals(other.GetXmlElementMatrix());
                    case BuiltInType.NodeId:
                        return Equals(other.GetNodeIdMatrix());
                    case BuiltInType.ExpandedNodeId:
                        return Equals(other.GetExpandedNodeIdMatrix());
                    case BuiltInType.StatusCode:
                        return Equals(other.GetStatusCodeMatrix());
                    case BuiltInType.QualifiedName:
                        return Equals(other.GetQualifiedNameMatrix());
                    case BuiltInType.LocalizedText:
                        return Equals(other.GetLocalizedTextMatrix());
                    case BuiltInType.ExtensionObject:
                        return Equals(other.GetExtensionObjectMatrix());
                    case BuiltInType.DataValue:
                        return Equals(other.GetDataValueMatrix());
                    case BuiltInType.Variant:
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                        return Equals(other.GetVariantMatrix());
                    default:
                        Debug.Fail("Unexpected Built in type.");
                        return false;
                }
            }
            else
            {
                return false;
            }
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            // Sign before uns
            return obj switch
            {
                null => IsNull,
                Variant v => Equals(v),
                bool v => Equals(v),
                sbyte v => Equals(v),
                byte v => Equals(v),
                ushort v => Equals(v),
                short v => Equals(v),
                uint v => Equals(v),
                int v => Equals(v),
                ulong v => Equals(v),
                long v => Equals(v),
                float v => Equals(v),
                double v => Equals(v),
                string v => Equals(v),
                Enum v => Equals(v),
                ByteString v => Equals(v),
                DateTime v => Equals(v),
                Uuid v => Equals(v),
                XmlElement v => Equals(v),
                NodeId v => Equals(v),
                ExpandedNodeId v => Equals(v),
                StatusCode v => Equals(v),
                QualifiedName v => Equals(v),
                LocalizedText v => Equals(v),
                ExtensionObject v => Equals(v),
                DataValue v => Equals(v),
                ArrayOf<sbyte> v => Equals(v),
                ArrayOf<byte> v => Equals(v),
                ArrayOf<ushort> v => Equals(v),
                ArrayOf<short> v => Equals(v),
                ArrayOf<uint> v => Equals(v),
                ArrayOf<int> v => Equals(v),
                ArrayOf<ulong> v => Equals(v),
                ArrayOf<long> v => Equals(v),
                ArrayOf<bool> v => Equals(v),
                ArrayOf<float> v => Equals(v),
                ArrayOf<double> v => Equals(v),
                ArrayOf<string> v => Equals(v),
                ArrayOf<Enum> v => Equals(v),
                ArrayOf<DateTime> v => Equals(v),
                ArrayOf<Uuid> v => Equals(v),
                ArrayOf<ByteString> v => Equals(v),
                ArrayOf<XmlElement> v => Equals(v),
                ArrayOf<NodeId> v => Equals(v),
                ArrayOf<ExpandedNodeId> v => Equals(v),
                ArrayOf<StatusCode> v => Equals(v),
                ArrayOf<QualifiedName> v => Equals(v),
                ArrayOf<LocalizedText> v => Equals(v),
                ArrayOf<ExtensionObject> v => Equals(v),
                ArrayOf<DataValue> v => Equals(v),
                ArrayOf<Variant> v => Equals(v),
                MatrixOf<sbyte> v => Equals(v),
                MatrixOf<byte> v => Equals(v),
                MatrixOf<ushort> v => Equals(v),
                MatrixOf<short> v => Equals(v),
                MatrixOf<uint> v => Equals(v),
                MatrixOf<int> v => Equals(v),
                MatrixOf<ulong> v => Equals(v),
                MatrixOf<long> v => Equals(v),
                MatrixOf<bool> v => Equals(v),
                MatrixOf<float> v => Equals(v),
                MatrixOf<double> v => Equals(v),
                MatrixOf<string> v => Equals(v),
                MatrixOf<Enum> v => Equals(v),
                MatrixOf<DateTime> v => Equals(v),
                MatrixOf<Uuid> v => Equals(v),
                MatrixOf<ByteString> v => Equals(v),
                MatrixOf<XmlElement> v => Equals(v),
                MatrixOf<NodeId> v => Equals(v),
                MatrixOf<ExpandedNodeId> v => Equals(v),
                MatrixOf<StatusCode> v => Equals(v),
                MatrixOf<QualifiedName> v => Equals(v),
                MatrixOf<LocalizedText> v => Equals(v),
                MatrixOf<ExtensionObject> v => Equals(v),
                MatrixOf<DataValue> v => Equals(v),
                MatrixOf<Variant> v => Equals(v),
                _ => false
            };
        }

        /// <summary>
        /// Convert to a variant from an xml stream. Used during initialization
        /// of values from string values.
        /// </summary>
        /// <remarks>
        /// This is an internal API and subject to change, but it is not marked
        /// experimental because it is used by generated code.
        /// </remarks>
        /// <param name="istrm">The variant value xml as utf8 character stream
        /// </param>
        /// <param name="context">message context</param>
        /// <returns></returns>
        public static Variant FromXml(Stream istrm, ISystemContext context)
        {
            return FromXml(istrm, context.AsMessageContext());
        }

        /// <summary>
        /// Convert to a variant from an xml stream. Used during initialization
        /// of values from string values.
        /// </summary>
        /// <remarks>
        /// This is an internal API and subject to change, but it is not marked
        /// experimental because it is used by generated code.
        /// </remarks>
        /// <param name="istrm">The variant value xml as utf8 character stream
        /// </param>
        /// <param name="context">message context</param>
        /// <returns></returns>
        public static Variant FromXml(Stream istrm, IServiceMessageContext context)
        {
            using var reader = XmlReader.Create(istrm, CoreUtils.DefaultXmlReaderSettings());
            using var decoder = new XmlDecoder(reader, context);
            return decoder.ReadVariantContents();
        }

        /// <summary>
        /// Lifts all elements contained in this variant into individual variants
        /// with the same type info. If the variant is scalar returns only the
        /// variant.
        /// </summary>
        /// <returns></returns>
        internal ArrayOf<Variant> Expand()
        {
            if (TypeInfo.IsScalar)
            {
                return [this];
            }
            if (TypeInfo.IsArray)
            {
                switch (TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        return GetBooleanArray().ConvertAll(v => new Variant(v));
                    case BuiltInType.SByte:
                        return GetSByteArray().ConvertAll(v => new Variant(v));
                    case BuiltInType.Byte:
                        return GetByteArray().ConvertAll(v => new Variant(v));
                    case BuiltInType.Int16:
                        return GetInt16Array().ConvertAll(v => new Variant(v));
                    case BuiltInType.UInt16:
                        return GetUInt16Array().ConvertAll(v => new Variant(v));
                    case BuiltInType.Int32:
                        return GetInt32Array().ConvertAll(v => new Variant(v));
                    case BuiltInType.UInt32:
                        return GetUInt32Array().ConvertAll(v => new Variant(v));
                    case BuiltInType.Int64:
                        return GetInt64Array().ConvertAll(v => new Variant(v));
                    case BuiltInType.UInt64:
                        return GetUInt64Array().ConvertAll(v => new Variant(v));
                    case BuiltInType.Float:
                        return GetFloatArray().ConvertAll(v => new Variant(v));
                    case BuiltInType.Double:
                        return GetDoubleArray().ConvertAll(v => new Variant(v));
                    case BuiltInType.String:
                        return GetStringArray().ConvertAll(v => new Variant(v));
                    case BuiltInType.DateTime:
                        return GetDateTimeArray().ConvertAll(v => new Variant(v));
                    case BuiltInType.Guid:
                        return GetGuidArray().ConvertAll(v => new Variant(v));
                    case BuiltInType.ByteString:
                        return GetByteStringArray().ConvertAll(v => new Variant(v));
                    case BuiltInType.XmlElement:
                        return GetXmlElementArray().ConvertAll(v => new Variant(v));
                    case BuiltInType.NodeId:
                        return GetNodeIdArray().ConvertAll(v => new Variant(v));
                    case BuiltInType.ExpandedNodeId:
                        return GetExpandedNodeIdArray().ConvertAll(v => new Variant(v));
                    case BuiltInType.StatusCode:
                        return GetStatusCodeArray().ConvertAll(v => new Variant(v));
                    case BuiltInType.QualifiedName:
                        return GetQualifiedNameArray().ConvertAll(v => new Variant(v));
                    case BuiltInType.LocalizedText:
                        return GetLocalizedTextArray().ConvertAll(v => new Variant(v));
                    case BuiltInType.ExtensionObject:
                        return GetExtensionObjectArray().ConvertAll(v => new Variant(v));
                    case BuiltInType.DataValue:
                        return GetDataValueArray().ConvertAll(v => new Variant(v));
                    case BuiltInType.DiagnosticInfo:
                        return [];
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                    case BuiltInType.Enumeration:
                    case BuiltInType.Variant:
                        return GetVariantArray();
                }
            }
            // TODO: Enumerate matrix
            return [];
        }

        /// <summary>
        /// Collapses a list of variants into a variant Type info array. If the
        /// type infos of the individual items are not the same, collapses to a
        /// variant of variants.
        /// </summary>
        /// <returns></returns>
        internal static Variant Collapse(ArrayOf<Variant> items)
        {
            if (items.Count == 0)
            {
                return default;
            }
            if (items.Count == 1)
            {
                return items.Span[0];
            }
            TypeInfo typeInfo = items.Span[0].TypeInfo;
            if (!items.ToArray().All(v => v.TypeInfo == typeInfo))
            {
                // Variant of variants
                return new Variant(items);
            }
            if (typeInfo.IsScalar)
            {
                switch (typeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        return new Variant(items.ConvertAll(v => v.GetBoolean()));
                    case BuiltInType.SByte:
                        return new Variant(items.ConvertAll(v => v.GetSByte()));
                    case BuiltInType.Byte:
                        return new Variant(items.ConvertAll(v => v.GetByte()));
                    case BuiltInType.Int16:
                        return new Variant(items.ConvertAll(v => v.GetInt16()));
                    case BuiltInType.UInt16:
                        return new Variant(items.ConvertAll(v => v.GetUInt16()));
                    case BuiltInType.Int32:
                        return new Variant(items.ConvertAll(v => v.GetInt32()));
                    case BuiltInType.UInt32:
                        return new Variant(items.ConvertAll(v => v.GetUInt32()));
                    case BuiltInType.Int64:
                        return new Variant(items.ConvertAll(v => v.GetInt64()));
                    case BuiltInType.UInt64:
                        return new Variant(items.ConvertAll(v => v.GetUInt64()));
                    case BuiltInType.Float:
                        return new Variant(items.ConvertAll(v => v.GetFloat()));
                    case BuiltInType.Double:
                        return new Variant(items.ConvertAll(v => v.GetDouble()));
                    case BuiltInType.String:
                        return new Variant(items.ConvertAll(v => v.GetString()));
                    case BuiltInType.DateTime:
                        return new Variant(items.ConvertAll(v => v.GetDateTime()));
                    case BuiltInType.Guid:
                        return new Variant(items.ConvertAll(v => v.GetGuid()));
                    case BuiltInType.ByteString:
                        return new Variant(items.ConvertAll(v => v.GetByteString()));
                    case BuiltInType.XmlElement:
                        return new Variant(items.ConvertAll(v => v.GetXmlElement()));
                    case BuiltInType.NodeId:
                        return new Variant(items.ConvertAll(v => v.GetNodeId()));
                    case BuiltInType.ExpandedNodeId:
                        return new Variant(items.ConvertAll(v => v.GetExpandedNodeId()));
                    case BuiltInType.StatusCode:
                        return new Variant(items.ConvertAll(v => v.GetStatusCode()));
                    case BuiltInType.QualifiedName:
                        return new Variant(items.ConvertAll(v => v.GetQualifiedName()));
                    case BuiltInType.LocalizedText:
                        return new Variant(items.ConvertAll(v => v.GetLocalizedText()));
                    case BuiltInType.ExtensionObject:
                        return new Variant(items.ConvertAll(v => v.GetExtensionObject()));
                    case BuiltInType.DataValue:
                        return new Variant(items.ConvertAll(v => v.GetDataValue()));
                    case BuiltInType.DiagnosticInfo:
                        return default;
                    case BuiltInType.Number:
                    case BuiltInType.Integer:
                    case BuiltInType.UInteger:
                    case BuiltInType.Enumeration:
                    case BuiltInType.Variant:
                        return new Variant(items);
                }
            }
            if (typeInfo.IsArray)
            {
                // TODO: Collapse each individual one first
            }
            // TODO: Collapse matrix
            return new Variant(items);
        }

        /// <summary>
        /// Format the internal value
        /// </summary>
        private string ToStringCore(IFormatProvider provider)
        {
            if (TypeInfo.IsScalar)
            {
                // Handle built-in type value-types without another check
                switch (TypeInfo.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        return m_union.Boolean.ToString();
                    case BuiltInType.SByte:
                        return m_union.SByte.ToString(provider);
                    case BuiltInType.Byte:
                        return m_union.Byte.ToString(provider);
                    case BuiltInType.Int16:
                        return m_union.Int16.ToString(provider);
                    case BuiltInType.UInt16:
                        return m_union.UInt16.ToString(provider);
                    case BuiltInType.Int32:
                        return m_union.Int32.ToString(provider);
                    case BuiltInType.UInt32:
                        return m_union.UInt32.ToString(provider);
                    case BuiltInType.Int64:
                        return m_union.Int64.ToString(provider);
                    case BuiltInType.UInt64:
                        return m_union.UInt64.ToString(provider);
                    case BuiltInType.Float:
                        return m_union.Float.ToString(provider);
                    case BuiltInType.Double:
                        return m_union.Double.ToString(provider);
                    case BuiltInType.DateTime:
                        return m_union.DateTime.ToString(provider);
                    case BuiltInType.StatusCode:
                        return (m_value is string s ?
                            new StatusCode(m_union.UInt32, s) :
                            new StatusCode(m_union.UInt32)).ToString(null, provider);
#if NET8_0_OR_GREATER
                    case BuiltInType.Enumeration:
                        if (m_value is Type enumType)
                        {
                            Type type = enumType.GetEnumUnderlyingType();
                            if (type == typeof(int) || type == typeof(uint))
                            {
                                return Enum.ToObject(enumType, m_union.Int32).ToString();
                            }
                            if (type == typeof(byte) || type == typeof(sbyte))
                            {
                                return Enum.ToObject(enumType, m_union.Byte).ToString();
                            }
                            if (type == typeof(short) || type == typeof(ushort))
                            {
                                return Enum.ToObject(enumType, m_union.Int16).ToString();
                            }
                            if (type == typeof(long) || type == typeof(ulong))
                            {
                                return Enum.ToObject(enumType, m_union.Int64).ToString();
                            }
                        }
                        return m_union.Int32.ToString(provider);
#endif
                }
            }
            if (m_value is IFormattable f)
            {
                return f.ToString(null, provider);
            }
            return m_value?.ToString() ?? "<null>";
        }

        /// <summary>
        /// Create a Variant from a Enum value with type information.
        /// </summary>
        /// <param name="value">The Enum value to set this Variant to</param>
        /// <param name="enumType">The Enum type</param>
        /// <exception cref="ServiceResultException"></exception>
        internal static Variant FromEnumeration(object value, Type enumType)
        {
#if NET8_0_OR_GREATER
            Union data = default;
            var underlyingSize = Marshal.SizeOf(Enum.GetUnderlyingType(enumType));
            switch (Enum.GetUnderlyingType(enumType))
            {
                case Type t when t == typeof(int):
                    data.Int32 = (int)value;
                    break;
                case Type t when t == typeof(sbyte):
                    data.SByte = (sbyte)value;
                    break;
                case Type t when t == typeof(short):
                    data.Int16 = (short)value;
                    break;
                case Type t when t == typeof(long):
                    data.Int64 = (long)value;
                    break;
                case Type t when t == typeof(byte):
                    data.Byte = (byte)value;
                    break;
                case Type t when t == typeof(ushort):
                    data.UInt16 = (ushort)value;
                    break;
                case Type t when t == typeof(ulong):
                    data.UInt64 = (ulong)value;
                    break;
                case Type t when t == typeof(uint):
                    data.UInt32 = (uint)value;
                    break;
                default:
                    throw ServiceResultException.Unexpected(
                        "Bad enum type {0} with size: {1}",
                        enumType.Name, underlyingSize);
            }
            return new Variant(data, TypeInfo.Scalars.Enumeration, enumType);
#else
            return new Variant(default, TypeInfo.Scalars.Enumeration, value);
#endif
        }

        /// <summary>
        /// Box the value stored in the Variant as object
        /// </summary>
        /// <returns></returns>
        public object AsBoxedObject(bool returnLegacyTypes) // TODO: Make private
        {
            if (TypeInfo.IsUnknown)
            {
                return m_value;
            }
            if (TypeInfo.IsScalar)
            {
                // Handle built-in type value-types without another check
                switch (TypeInfo.BuiltInType)
                {
                    case BuiltInType.NodeId:
                        return m_value is NodeId v ? v : default;
                    case BuiltInType.ExpandedNodeId:
                        return m_value is ExpandedNodeId e ? e : default;
                    case BuiltInType.LocalizedText:
                        return m_value is LocalizedText l ? l : default;
                    case BuiltInType.QualifiedName:
                        return m_value is QualifiedName q ? q : default;
                    case BuiltInType.ExtensionObject:
                        return m_value is ExtensionObject o ? o : default;
                    case BuiltInType.Boolean:
                        return m_union.Boolean;
                    case BuiltInType.SByte:
                        return m_union.SByte;
                    case BuiltInType.Byte:
                        return m_union.Byte;
                    case BuiltInType.Int16:
                        return m_union.Int16;
                    case BuiltInType.UInt16:
                        return m_union.UInt16;
                    case BuiltInType.Int32:
                        return m_union.Int32;
                    case BuiltInType.UInt32:
                        return m_union.UInt32;
                    case BuiltInType.Int64:
                        return m_union.Int64;
                    case BuiltInType.UInt64:
                        return m_union.UInt64;
                    case BuiltInType.Float:
                        return m_union.Float;
                    case BuiltInType.Double:
                        return m_union.Double;
                    case BuiltInType.DateTime:
                        return m_union.DateTime;
                    case BuiltInType.StatusCode:
                        return m_value is string s ?
                            new StatusCode(m_union.UInt32, s) :
                            new StatusCode(m_union.UInt32);
#if NET8_0_OR_GREATER
                    case BuiltInType.Enumeration:
                        if (m_value is Type enumType)
                        {
                            Type type = enumType.GetEnumUnderlyingType();
                            if (type == typeof(int) || type == typeof(uint))
                            {
                                return Enum.ToObject(enumType, m_union.Int32);
                            }
                            if (type == typeof(byte) || type == typeof(sbyte))
                            {
                                return Enum.ToObject(enumType, m_union.Byte);
                            }
                            if (type == typeof(short) || type == typeof(ushort))
                            {
                                return Enum.ToObject(enumType, m_union.Int16);
                            }
                            if (type == typeof(long) || type == typeof(ulong))
                            {
                                return Enum.ToObject(enumType, m_union.Int64);
                            }
                        }
                        return m_union.Int32;
#endif
                }
            }
            if (returnLegacyTypes)
            {
                if (m_value is IConvertableToMatrix convertibleMatrix)
                {
                    return convertibleMatrix.ToMatrix(m_typeInfo.BuiltInType);
                }

                if (m_value is IConvertableToArray convertible)
                {
                    return convertible.ToArray();
                }
            }
            return m_value;
        }

        /// <summary>
        /// Returns true if for sake of variant these type infos are equivalent
        /// </summary>
        /// <param name="typeInfo1"></param>
        /// <param name="typeInfo2"></param>
        /// <returns></returns>
        private static bool IsConvertible(TypeInfo typeInfo1, TypeInfo typeInfo2)
        {
            // Cooerce Enumeration and Int32
            if (typeInfo1.ValueRank == typeInfo2.ValueRank &&
                IsEnumeration(typeInfo1) &&
                IsEnumeration(typeInfo2))
            {
                return true;
            }
            // ByteString is the same as Array of bytes
            if (IsByteString(typeInfo1) &&
                IsByteString(typeInfo2))
            {
                return true;
            }

            return false;

            static bool IsByteString(TypeInfo typeInfo)
            {
                return
                   (typeInfo.BuiltInType == BuiltInType.Byte && typeInfo.IsArray) ||
                   (typeInfo.BuiltInType == BuiltInType.ByteString && typeInfo.IsScalar);
            }

            static bool IsEnumeration(TypeInfo typeInfo) =>
                typeInfo.BuiltInType is BuiltInType.Int32 or BuiltInType.Enumeration;
        }

        /// <summary>
        /// Helper to create a InvalidCastException for type T.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        private static InvalidCastException CannotCast<T>()
        {
            return new InvalidCastException(
                CoreUtils.Format(
                    "Cannot convert Variant to {0}.",
                    typeof(T).Name));
        }

        bool IVariantOf<sbyte>.TryGet(out sbyte value)
        {
            throw new NotImplementedException();
        }

        [StructLayout(LayoutKind.Explicit, Size = 8)]
        internal struct Union
        {
            [FieldOffset(0)]
            public bool Boolean;

            [FieldOffset(0)]
            public sbyte SByte;

            [FieldOffset(0)]
            public byte Byte;

            [FieldOffset(0)]
            public short Int16;

            [FieldOffset(0)]
            public ushort UInt16;

            [FieldOffset(0)]
            public int Int32;

            [FieldOffset(0)]
            public uint UInt32;

            [FieldOffset(0)]
            public long Int64;

            [FieldOffset(0)]
            public ulong UInt64;

            [FieldOffset(0)]
            public float Float;

            [FieldOffset(0)]
            public double Double;

            [FieldOffset(0)]
            public DateTime DateTime;

            /// <summary>
            /// In case of array offset into it
            /// </summary>
            [FieldOffset(0)]
            public int Index;

            /// <summary>
            /// In case of array length from offset
            /// </summary>
            [FieldOffset(4)]
            public int Length;
        }

#pragma warning disable IDE0032 // Use auto property
        private readonly object m_value;
        private readonly Union m_union;
        private readonly TypeInfo m_typeInfo;
#pragma warning restore IDE0032 // Use auto property
    }

    /// <summary>
    /// VariantOf type T interface
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IVariantOf<T>
    {
        /// <summary>
        /// Get type T from variant
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        bool TryGet(out T value);

        /// <summary>
        /// Set value in variant and return new variant value
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        IVariantOf<T> WithValue(T value);
    }

    /// <summary>
    /// Helper to allow data contract serialization of Variant
    /// </summary>
    [DataContract(
        Name = "Variant",
        Namespace = Namespaces.OpcUaXsd)]
    public class SerializableVariant :
        ISurrogateFor<Variant>,
        IEquatable<Variant>,
        IEquatable<SerializableVariant>
    {
        /// <inheritdoc/>
        public SerializableVariant()
        {
            Value = default;
        }

        /// <inheritdoc/>
        public SerializableVariant(Variant value)
        {
            Value = value;
        }

        /// <inheritdoc/>
        public Variant Value { get; private set; }

        /// <inheritdoc/>
        public object GetValue()
        {
            return Value;
        }

        /// <summary>
        /// The value stored within the Variant object.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        [DataMember(Name = "Value", Order = 1)]
        internal System.Xml.XmlElement XmlEncodedValue
        {
            get
            {
                // check for null.
                if (Value.IsNull)
                {
                    return null;
                }

                // create encoder.
                using var encoder = new XmlEncoder(AmbientMessageContext.CurrentContext);
                // write value.
                encoder.WriteVariantValue(null, Value);

                // create document from encoder.
                var document = new XmlDocument();
                document.LoadInnerXml(encoder.CloseAndReturnText());

                // return element.
                return document.DocumentElement;
            }
            set
            {
                // check for null values.
                if (value == null)
                {
                    Value = Variant.Null;
                    return;
                }

                // create decoder.
                using var decoder = new XmlDecoder(value, AmbientMessageContext.CurrentContext);
                try
                {
                    // read value.
                    Value = decoder.ReadVariantContents();
                }
                catch (Exception e)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadDecodingError,
                        e,
                        "Error decoding Variant value.");
                }
                finally
                {
                    // close decoder.
                    decoder.Close();
                }
            }
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj switch
            {
                SerializableVariant s => Equals(s),
                Variant n => Equals(n),
                _ => Value.Equals(obj)
            };
        }

        /// <inheritdoc/>
        public bool Equals(Variant obj)
        {
            return Value.Equals(obj);
        }

        /// <inheritdoc/>
        public bool Equals(SerializableVariant obj)
        {
            return Value.Equals(obj?.Value ?? default);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <inheritdoc/>
        public static bool operator ==(
            SerializableVariant left,
            SerializableVariant right)
        {
            return EqualityComparer<SerializableVariant>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(
            SerializableVariant left,
            SerializableVariant right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static bool operator ==(
            SerializableVariant left,
            Variant right)
        {
            return EqualityComparer<SerializableVariant>.Default.Equals(left, right);
        }

        /// <inheritdoc/>
        public static bool operator !=(
            SerializableVariant left,
            Variant right)
        {
            return !(left == right);
        }

        /// <inheritdoc/>
        public static implicit operator SerializableVariant(Variant value)
        {
            return new SerializableVariant(value);
        }

        /// <inheritdoc/>
        public static implicit operator Variant(SerializableVariant value)
        {
            return value.Value;
        }

        /// <inheritdoc/>
        public static explicit operator System.Xml.XmlElement(SerializableVariant value)
        {
            return value.XmlEncodedValue;
        }

        /// <inheritdoc/>
        public static explicit operator SerializableVariant(System.Xml.XmlElement value)
        {
            return new SerializableVariant { XmlEncodedValue = value };
        }
    }
}
