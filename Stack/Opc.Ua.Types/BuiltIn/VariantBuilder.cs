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
using static Opc.Ua.VariantBuilder;

namespace Opc.Ua
{
    /// <summary>
    /// Variant accessor and builder
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IVariantBuilder<T>
    {
        /// <summary>
        /// Get type T from variant
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        T GetValue(Variant value);

        /// <summary>
        /// Set value in variant and return Variant.From value
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        Variant WithValue(T value);
    }

    /// <summary>
    /// Veriant builder
    /// </summary>
#pragma warning disable CA1815 // Override equals and operator equals on value types
    public readonly struct VariantBuilder :
#pragma warning restore CA1815 // Override equals and operator equals on value types
        IVariantBuilder<bool>,
        IVariantBuilder<sbyte>,
        IVariantBuilder<byte>,
        IVariantBuilder<short>,
        IVariantBuilder<ushort>,
        IVariantBuilder<int>,
        IVariantBuilder<uint>,
        IVariantBuilder<long>,
        IVariantBuilder<ulong>,
        IVariantBuilder<float>,
        IVariantBuilder<double>,
        IVariantBuilder<string>,
        IVariantBuilder<DateTime>,
        IVariantBuilder<Uuid>,
        IVariantBuilder<ByteString>,
        IVariantBuilder<XmlElement>,
        IVariantBuilder<NodeId>,
        IVariantBuilder<ExpandedNodeId>,
        IVariantBuilder<StatusCode>,
        IVariantBuilder<QualifiedName>,
        IVariantBuilder<LocalizedText>,
        IVariantBuilder<ExtensionObject>,
        IVariantBuilder<DataValue>,
        IVariantBuilder<ArrayOf<bool>>,
        IVariantBuilder<ArrayOf<sbyte>>,
        IVariantBuilder<ArrayOf<byte>>,
        IVariantBuilder<ArrayOf<short>>,
        IVariantBuilder<ArrayOf<ushort>>,
        IVariantBuilder<ArrayOf<int>>,
        IVariantBuilder<ArrayOf<uint>>,
        IVariantBuilder<ArrayOf<long>>,
        IVariantBuilder<ArrayOf<ulong>>,
        IVariantBuilder<ArrayOf<float>>,
        IVariantBuilder<ArrayOf<double>>,
        IVariantBuilder<ArrayOf<string>>,
        IVariantBuilder<ArrayOf<DateTime>>,
        IVariantBuilder<ArrayOf<Uuid>>,
        IVariantBuilder<ArrayOf<ByteString>>,
        IVariantBuilder<ArrayOf<XmlElement>>,
        IVariantBuilder<ArrayOf<NodeId>>,
        IVariantBuilder<ArrayOf<ExpandedNodeId>>,
        IVariantBuilder<ArrayOf<StatusCode>>,
        IVariantBuilder<ArrayOf<QualifiedName>>,
        IVariantBuilder<ArrayOf<LocalizedText>>,
        IVariantBuilder<ArrayOf<ExtensionObject>>,
        IVariantBuilder<ArrayOf<DataValue>>,
        IVariantBuilder<ArrayOf<Variant>>,
        IVariantBuilder<MatrixOf<bool>>,
        IVariantBuilder<MatrixOf<sbyte>>,
        IVariantBuilder<MatrixOf<byte>>,
        IVariantBuilder<MatrixOf<short>>,
        IVariantBuilder<MatrixOf<ushort>>,
        IVariantBuilder<MatrixOf<int>>,
        IVariantBuilder<MatrixOf<uint>>,
        IVariantBuilder<MatrixOf<long>>,
        IVariantBuilder<MatrixOf<ulong>>,
        IVariantBuilder<MatrixOf<float>>,
        IVariantBuilder<MatrixOf<double>>,
        IVariantBuilder<MatrixOf<string>>,
        IVariantBuilder<MatrixOf<DateTime>>,
        IVariantBuilder<MatrixOf<Uuid>>,
        IVariantBuilder<MatrixOf<ByteString>>,
        IVariantBuilder<MatrixOf<XmlElement>>,
        IVariantBuilder<MatrixOf<NodeId>>,
        IVariantBuilder<MatrixOf<ExpandedNodeId>>,
        IVariantBuilder<MatrixOf<StatusCode>>,
        IVariantBuilder<MatrixOf<QualifiedName>>,
        IVariantBuilder<MatrixOf<LocalizedText>>,
        IVariantBuilder<MatrixOf<ExtensionObject>>,
        IVariantBuilder<MatrixOf<DataValue>>,
        IVariantBuilder<MatrixOf<Variant>>
   {
        /// <inheritdoc/>
        Variant IVariantBuilder<bool>.WithValue(bool value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<sbyte>.WithValue(sbyte value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<byte>.WithValue(byte value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<short>.WithValue(short value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ushort>.WithValue(ushort value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<int>.WithValue(int value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<uint>.WithValue(uint value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<long>.WithValue(long value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ulong>.WithValue(ulong value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<float>.WithValue(float value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<double>.WithValue(double value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<string>.WithValue(string value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<DateTime>.WithValue(DateTime value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<Uuid>.WithValue(Uuid value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ByteString>.WithValue(ByteString value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<XmlElement>.WithValue(XmlElement value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<NodeId>.WithValue(NodeId value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ExpandedNodeId>.WithValue(
            ExpandedNodeId value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<StatusCode>.WithValue(
            StatusCode value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<QualifiedName>.WithValue(
            QualifiedName value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<LocalizedText>.WithValue(
            LocalizedText value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ExtensionObject>.WithValue(
            ExtensionObject value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<DataValue>.WithValue(DataValue value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<bool>>.WithValue(
            ArrayOf<bool> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<sbyte>>.WithValue(
            ArrayOf<sbyte> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<byte>>.WithValue(
            ArrayOf<byte> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<short>>.WithValue(
            ArrayOf<short> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<ushort>>.WithValue(
            ArrayOf<ushort> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<int>>.WithValue(
            ArrayOf<int> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<uint>>.WithValue(
            ArrayOf<uint> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<long>>.WithValue(
            ArrayOf<long> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<ulong>>.WithValue(
            ArrayOf<ulong> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<float>>.WithValue(
            ArrayOf<float> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<double>>.WithValue(
            ArrayOf<double> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<string>>.WithValue(
            ArrayOf<string> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<DateTime>>.WithValue(
            ArrayOf<DateTime> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<Uuid>>.WithValue(
            ArrayOf<Uuid> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<ByteString>>.WithValue(
            ArrayOf<ByteString> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<XmlElement>>.WithValue(
            ArrayOf<XmlElement> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<NodeId>>.WithValue(
            ArrayOf<NodeId> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<ExpandedNodeId>>.WithValue(
            ArrayOf<ExpandedNodeId> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<StatusCode>>.WithValue(
            ArrayOf<StatusCode> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<QualifiedName>>.WithValue(
            ArrayOf<QualifiedName> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<LocalizedText>>.WithValue(
            ArrayOf<LocalizedText> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<ExtensionObject>>.WithValue(
            ArrayOf<ExtensionObject> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<DataValue>>.WithValue(
            ArrayOf<DataValue> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<Variant>>.WithValue(
            ArrayOf<Variant> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<bool>>.WithValue(
            MatrixOf<bool> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<sbyte>>.WithValue(
            MatrixOf<sbyte> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<byte>>.WithValue(
            MatrixOf<byte> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<short>>.WithValue(
            MatrixOf<short> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<ushort>>.WithValue(
            MatrixOf<ushort> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<int>>.WithValue(
            MatrixOf<int> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<uint>>.WithValue(
            MatrixOf<uint> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<long>>.WithValue(
            MatrixOf<long> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<ulong>>.WithValue(
            MatrixOf<ulong> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<float>>.WithValue(
            MatrixOf<float> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<double>>.WithValue(
            MatrixOf<double> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<string>>.WithValue(
            MatrixOf<string> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<DateTime>>.WithValue(
            MatrixOf<DateTime> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<Uuid>>.WithValue(
            MatrixOf<Uuid> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<ByteString>>.WithValue(
            MatrixOf<ByteString> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<XmlElement>>.WithValue(
            MatrixOf<XmlElement> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<NodeId>>.WithValue(
            MatrixOf<NodeId> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<ExpandedNodeId>>.WithValue(
            MatrixOf<ExpandedNodeId> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<StatusCode>>.WithValue(
            MatrixOf<StatusCode> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<QualifiedName>>.WithValue(
            MatrixOf<QualifiedName> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<LocalizedText>>.WithValue(
            MatrixOf<LocalizedText> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<ExtensionObject>>.WithValue(
            MatrixOf<ExtensionObject> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<DataValue>>.WithValue(
            MatrixOf<DataValue> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<Variant>>.WithValue(
            MatrixOf<Variant> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        bool IVariantBuilder<bool>.GetValue(Variant value)
        {
            return value.GetBoolean();
        }

        /// <inheritdoc/>
        sbyte IVariantBuilder<sbyte>.GetValue(Variant value)
        {
            return value.GetSByte();
        }

        /// <inheritdoc/>
        byte IVariantBuilder<byte>.GetValue(Variant value)
        {
            return value.GetByte();
        }

        /// <inheritdoc/>
        short IVariantBuilder<short>.GetValue(Variant value)
        {
            return value.GetInt16();
        }

        /// <inheritdoc/>
        ushort IVariantBuilder<ushort>.GetValue(Variant value)
        {
            return value.GetUInt16();
        }

        /// <inheritdoc/>
        int IVariantBuilder<int>.GetValue(Variant value)
        {
            return value.GetInt32();
        }

        /// <inheritdoc/>
        uint IVariantBuilder<uint>.GetValue(Variant value)
        {
            return value.GetUInt32();
        }

        /// <inheritdoc/>
        long IVariantBuilder<long>.GetValue(Variant value)
        {
            return value.GetInt64();
        }

        /// <inheritdoc/>
        ulong IVariantBuilder<ulong>.GetValue(Variant value)
        {
            return value.GetUInt64();
        }

        /// <inheritdoc/>
        float IVariantBuilder<float>.GetValue(Variant value)
        {
            return value.GetFloat();
        }

        /// <inheritdoc/>
        double IVariantBuilder<double>.GetValue(Variant value)
        {
            return value.GetDouble();
        }

        /// <inheritdoc/>
        string IVariantBuilder<string>.GetValue(Variant value)
        {
            return value.GetString();
        }

        /// <inheritdoc/>
        DateTime IVariantBuilder<DateTime>.GetValue(Variant value)
        {
            return value.GetDateTime();
        }

        /// <inheritdoc/>
        Uuid IVariantBuilder<Uuid>.GetValue(Variant value)
        {
            return value.GetGuid();
        }

        /// <inheritdoc/>
        ByteString IVariantBuilder<ByteString>.GetValue(Variant value)
        {
            return value.GetByteString();
        }

        /// <inheritdoc/>
        XmlElement IVariantBuilder<XmlElement>.GetValue(Variant value)
        {
            return value.GetXmlElement();
        }

        /// <inheritdoc/>
        NodeId IVariantBuilder<NodeId>.GetValue(Variant value)
        {
            return value.GetNodeId();
        }

        /// <inheritdoc/>
        ExpandedNodeId IVariantBuilder<ExpandedNodeId>.GetValue(
            Variant value)
        {
            return value.GetExpandedNodeId();
        }

        /// <inheritdoc/>
        StatusCode IVariantBuilder<StatusCode>.GetValue(Variant value)
        {
            return value.GetStatusCode();
        }

        /// <inheritdoc/>
        QualifiedName IVariantBuilder<QualifiedName>.GetValue(Variant value)
        {
            return value.GetQualifiedName();
        }

        /// <inheritdoc/>
        LocalizedText IVariantBuilder<LocalizedText>.GetValue(Variant value)
        {
            return value.GetLocalizedText();
        }

        /// <inheritdoc/>
        ExtensionObject IVariantBuilder<ExtensionObject>.GetValue(Variant value)
        {
            return value.GetExtensionObject();
        }

        /// <inheritdoc/>
        DataValue IVariantBuilder<DataValue>.GetValue(Variant value)
        {
            return value.GetDataValue();
        }

        /// <inheritdoc/>
        ArrayOf<bool> IVariantBuilder<ArrayOf<bool>>.GetValue(Variant value)
        {
            return value.GetBooleanArray();
        }

        /// <inheritdoc/>
        ArrayOf<sbyte> IVariantBuilder<ArrayOf<sbyte>>.GetValue(Variant value)
        {
            return value.GetSByteArray();
        }

        /// <inheritdoc/>
        ArrayOf<byte> IVariantBuilder<ArrayOf<byte>>.GetValue(Variant value)
        {
            return value.GetByteArray();
        }

        /// <inheritdoc/>
        ArrayOf<short> IVariantBuilder<ArrayOf<short>>.GetValue(Variant value)
        {
            return value.GetInt16Array();
        }

        /// <inheritdoc/>
        ArrayOf<ushort> IVariantBuilder<ArrayOf<ushort>>.GetValue(Variant value)
        {
            return value.GetUInt16Array();
        }

        /// <inheritdoc/>
        ArrayOf<int> IVariantBuilder<ArrayOf<int>>.GetValue(Variant value)
        {
            return value.GetInt32Array();
        }

        /// <inheritdoc/>
        ArrayOf<uint> IVariantBuilder<ArrayOf<uint>>.GetValue(Variant value)
        {
            return value.GetUInt32Array();
        }

        /// <inheritdoc/>
        ArrayOf<long> IVariantBuilder<ArrayOf<long>>.GetValue(Variant value)
        {
            return value.GetInt64Array();
        }

        /// <inheritdoc/>
        ArrayOf<ulong> IVariantBuilder<ArrayOf<ulong>>.GetValue(Variant value)
        {
            return value.GetUInt64Array();
        }

        /// <inheritdoc/>
        ArrayOf<float> IVariantBuilder<ArrayOf<float>>.GetValue(Variant value)
        {
            return value.GetFloatArray();
        }

        /// <inheritdoc/>
        ArrayOf<double> IVariantBuilder<ArrayOf<double>>.GetValue(Variant value)
        {
            return value.GetDoubleArray();
        }

        /// <inheritdoc/>
        ArrayOf<string> IVariantBuilder<ArrayOf<string>>.GetValue(Variant value)
        {
            return value.GetStringArray();
        }

        /// <inheritdoc/>
        ArrayOf<DateTime> IVariantBuilder<ArrayOf<DateTime>>.GetValue(Variant value)
        {
            return value.GetDateTimeArray();
        }

        /// <inheritdoc/>
        ArrayOf<Uuid> IVariantBuilder<ArrayOf<Uuid>>.GetValue(Variant value)
        {
            return value.GetGuidArray();
        }

        /// <inheritdoc/>
        ArrayOf<ByteString> IVariantBuilder<ArrayOf<ByteString>>.GetValue(
            Variant value)
        {
            return value.GetByteStringArray();
        }

        /// <inheritdoc/>
        ArrayOf<XmlElement> IVariantBuilder<ArrayOf<XmlElement>>.GetValue(
            Variant value)
        {
            return value.GetXmlElementArray();
        }

        /// <inheritdoc/>
        ArrayOf<NodeId> IVariantBuilder<ArrayOf<NodeId>>.GetValue(Variant value)
        {
            return value.GetNodeIdArray();
        }

        /// <inheritdoc/>
        ArrayOf<ExpandedNodeId> IVariantBuilder<ArrayOf<ExpandedNodeId>>.GetValue(
            Variant value)
        {
            return value.GetExpandedNodeIdArray();
        }

        /// <inheritdoc/>
        ArrayOf<StatusCode> IVariantBuilder<ArrayOf<StatusCode>>.GetValue(
            Variant value)
        {
            return value.GetStatusCodeArray();
        }

        /// <inheritdoc/>
        ArrayOf<QualifiedName> IVariantBuilder<ArrayOf<QualifiedName>>.GetValue(
            Variant value)
        {
            return value.GetQualifiedNameArray();
        }

        /// <inheritdoc/>
        ArrayOf<LocalizedText> IVariantBuilder<ArrayOf<LocalizedText>>.GetValue(
            Variant value)
        {
            return value.GetLocalizedTextArray();
        }

        /// <inheritdoc/>
        ArrayOf<ExtensionObject> IVariantBuilder<ArrayOf<ExtensionObject>>.GetValue(
            Variant value)
        {
            return value.GetExtensionObjectArray();
        }

        /// <inheritdoc/>
        ArrayOf<DataValue> IVariantBuilder<ArrayOf<DataValue>>.GetValue(
            Variant value)
        {
            return value.GetDataValueArray();
        }

        /// <inheritdoc/>
        ArrayOf<Variant> IVariantBuilder<ArrayOf<Variant>>.GetValue(
            Variant value)
        {
            return value.GetVariantArray();
        }

        /// <inheritdoc/>
        MatrixOf<bool> IVariantBuilder<MatrixOf<bool>>.GetValue(Variant value)
        {
            return value.GetBooleanMatrix();
        }

        /// <inheritdoc/>
        MatrixOf<sbyte> IVariantBuilder<MatrixOf<sbyte>>.GetValue(Variant value)
        {
            return value.GetSByteMatrix();
        }

        /// <inheritdoc/>
        MatrixOf<byte> IVariantBuilder<MatrixOf<byte>>.GetValue(Variant value)
        {
            return value.GetByteMatrix();
        }

        /// <inheritdoc/>
        MatrixOf<short> IVariantBuilder<MatrixOf<short>>.GetValue(Variant value)
        {
            return value.GetInt16Matrix();
        }

        /// <inheritdoc/>
        MatrixOf<ushort> IVariantBuilder<MatrixOf<ushort>>.GetValue(Variant value)
        {
            return value.GetUInt16Matrix();
        }

        /// <inheritdoc/>
        MatrixOf<int> IVariantBuilder<MatrixOf<int>>.GetValue(Variant value)
        {
            return value.GetInt32Matrix();
        }

        /// <inheritdoc/>
        MatrixOf<uint> IVariantBuilder<MatrixOf<uint>>.GetValue(Variant value)
        {
            return value.GetUInt32Matrix();
        }

        /// <inheritdoc/>
        MatrixOf<long> IVariantBuilder<MatrixOf<long>>.GetValue(Variant value)
        {
            return value.GetInt64Matrix();
        }

        /// <inheritdoc/>
        MatrixOf<ulong> IVariantBuilder<MatrixOf<ulong>>.GetValue(Variant value)
        {
            return value.GetUInt64Matrix();
        }

        /// <inheritdoc/>
        MatrixOf<float> IVariantBuilder<MatrixOf<float>>.GetValue(Variant value)
        {
            return value.GetFloatMatrix();
        }

        /// <inheritdoc/>
        MatrixOf<double> IVariantBuilder<MatrixOf<double>>.GetValue(Variant value)
        {
            return value.GetDoubleMatrix();
        }

        /// <inheritdoc/>
        MatrixOf<string> IVariantBuilder<MatrixOf<string>>.GetValue(Variant value)
        {
            return value.GetStringMatrix();
        }

        /// <inheritdoc/>
        MatrixOf<DateTime> IVariantBuilder<MatrixOf<DateTime>>.GetValue(Variant value)
        {
            return value.GetDateTimeMatrix();
        }

        /// <inheritdoc/>
        MatrixOf<Uuid> IVariantBuilder<MatrixOf<Uuid>>.GetValue(Variant value)
        {
            return value.GetGuidMatrix();
        }

        /// <inheritdoc/>
        MatrixOf<ByteString> IVariantBuilder<MatrixOf<ByteString>>.GetValue(
            Variant value)
        {
            return value.GetByteStringMatrix();
        }

        /// <inheritdoc/>
        MatrixOf<XmlElement> IVariantBuilder<MatrixOf<XmlElement>>.GetValue(
            Variant value)
        {
            return value.GetXmlElementMatrix();
        }

        /// <inheritdoc/>
        MatrixOf<NodeId> IVariantBuilder<MatrixOf<NodeId>>.GetValue(Variant value)
        {
            return value.GetNodeIdMatrix();
        }

        /// <inheritdoc/>
        MatrixOf<ExpandedNodeId> IVariantBuilder<MatrixOf<ExpandedNodeId>>.GetValue(
            Variant value)
        {
            return value.GetExpandedNodeIdMatrix();
        }

        /// <inheritdoc/>
        MatrixOf<StatusCode> IVariantBuilder<MatrixOf<StatusCode>>.GetValue(
            Variant value)
        {
            return value.GetStatusCodeMatrix();
        }

        /// <inheritdoc/>
        MatrixOf<QualifiedName> IVariantBuilder<MatrixOf<QualifiedName>>.GetValue(
            Variant value)
        {
            return value.GetQualifiedNameMatrix();
        }

        /// <inheritdoc/>
        MatrixOf<LocalizedText> IVariantBuilder<MatrixOf<LocalizedText>>.GetValue(
            Variant value)
        {
            return value.GetLocalizedTextMatrix();
        }

        /// <inheritdoc/>
        MatrixOf<ExtensionObject> IVariantBuilder<MatrixOf<ExtensionObject>>.GetValue(
            Variant value)
        {
            return value.GetExtensionObjectMatrix();
        }

        /// <inheritdoc/>
        MatrixOf<DataValue> IVariantBuilder<MatrixOf<DataValue>>.GetValue(
            Variant value)
        {
            return value.GetDataValueMatrix();
        }

        /// <inheritdoc/>
        MatrixOf<Variant> IVariantBuilder<MatrixOf<Variant>>.GetValue(
            Variant value)
        {
            return value.GetVariantMatrix();
        }
    }

    /// <inheritdoc/>
#pragma warning disable CA1815 // Override equals and operator equals on value types
    public readonly struct EnumerationBuilder<T> :
#pragma warning restore CA1815 // Override equals and operator equals on value types
        IVariantBuilder<T>,
        IVariantBuilder<ArrayOf<T>>,
        IVariantBuilder<MatrixOf<T>>
        where T : struct, Enum
    {
        /// <inheritdoc/>
        T IVariantBuilder<T>.GetValue(Variant value)
        {
            return value.GetEnumeration<T>();
        }

        /// <inheritdoc/>
        ArrayOf<T> IVariantBuilder<ArrayOf<T>>.GetValue(Variant value)
        {
            return value.GetEnumerationArray<T>();
        }

        /// <inheritdoc/>
        MatrixOf<T> IVariantBuilder<MatrixOf<T>>.GetValue(Variant value)
        {
            return value.GetEnumerationMatrix<T>();
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<T>.WithValue(T value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<T>>.WithValue(ArrayOf<T> value)
        {
            return Variant.From(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<T>>.WithValue(MatrixOf<T> value)
        {
            return Variant.From(value);
        }
    }

    /// <inheritdoc/>
#pragma warning disable CA1815 // Override equals and operator equals on value types
    public readonly struct StructureBuilder<T> :
#pragma warning restore CA1815 // Override equals and operator equals on value types
        IVariantBuilder<T>,
        IVariantBuilder<ArrayOf<T>>,
        IVariantBuilder<MatrixOf<T>>
        where T : IEncodeable
    {
        /// <inheritdoc/>
        T IVariantBuilder<T>.GetValue(Variant value)
        {
            return value.GetStructure<T>();
        }

        /// <inheritdoc/>
        ArrayOf<T> IVariantBuilder<ArrayOf<T>>.GetValue(Variant value)
        {
            return value.GetStructureArray<T>();
        }

        /// <inheritdoc/>
        MatrixOf<T> IVariantBuilder<MatrixOf<T>>.GetValue(Variant value)
        {
            return value.GetStructureMatrix<T>();
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<T>.WithValue(T value)
        {
            return Variant.FromStructure(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<ArrayOf<T>>.WithValue(ArrayOf<T> value)
        {
            return Variant.FromStructure(value);
        }

        /// <inheritdoc/>
        Variant IVariantBuilder<MatrixOf<T>>.WithValue(MatrixOf<T> value)
        {
            return Variant.FromStructure(value);
        }
    }
}
