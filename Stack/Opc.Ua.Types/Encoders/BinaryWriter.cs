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

#nullable enable
namespace Opc.Ua.Encoders;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>
/// Write data to a buffer writer using UA binary encoder
/// </summary>
public ref struct BinaryWriter<TWriter> : IWriter, ISizer, IDisposable
    where TWriter : IBufferWriter<byte>
{
    /// <summary>
    /// Returns the amount of bytes written so far but not flushed
    /// to the writer and committed.
    /// </summary>
    public int BytesPending { get; private set; }

    /// <summary>
    /// Returns the amount of bytes committed to the writer so far.
    /// </summary>
    public long BytesCommitted { get; private set; }

    /// <summary>
    /// Get last error
    /// </summary>
    public ServiceResult LastError { get; private set; }

    /// <summary>
    /// Test big endian writing
    /// </summary>
    internal bool IsLittleEndian { get; set; } = BitConverter.IsLittleEndian;

    /// <summary>
    /// Creates an encoder that writes to the buffer writer.
    /// </summary>
    public BinaryWriter(TWriter writer, ICodecContext context)
    {
        _writer = writer;
        _context = context;
        _nestingLevel = 0;
        _memory = Memory<byte>.Empty;
        LastError = ServiceResult.Good;
        BytesPending = 0;
        BytesCommitted = 0;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBoolean(ReadOnlySpan<byte> field, bool value)
        => WriteBoolean(value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBooleanValues(ReadOnlySpan<byte> field,
        in ArrayOf<bool> values) => WriteBooleanValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(ReadOnlySpan<byte> field, byte value)
        => WriteByte(value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByteValues(ReadOnlySpan<byte> field,
        in ArrayOf<byte> values) => WriteByteValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByteString(ReadOnlySpan<byte> field, in ByteString value)
        => WriteByteString(in value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByteStringValues(ReadOnlySpan<byte> field,
       in ArrayOf<ByteString> values) => WriteByteStringValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDataValue(ReadOnlySpan<byte> field, in DataValue value)
        => WriteDataValue(in value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDataValueValues(ReadOnlySpan<byte> field,
        in ArrayOf<DataValue> values) => WriteDataValueValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDateTime(ReadOnlySpan<byte> field, DateTimeUtc value)
        => WriteDateTime(value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDateTimeValues(ReadOnlySpan<byte> field,
        in ArrayOf<DateTimeUtc> values) => WriteDateTimeValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDiagnosticInfo(ReadOnlySpan<byte> field, in DiagnosticInfo value)
        => WriteDiagnosticInfo(in value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDiagnosticInfoValues(ReadOnlySpan<byte> field,
        in ArrayOf<DiagnosticInfo> values) => WriteDiagnosticInfoValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDouble(ReadOnlySpan<byte> field, double value) => WriteDouble(value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteDoubleValues(ReadOnlySpan<byte> field,
        in ArrayOf<double> values) => WriteDoubleValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteEnumeration<T>(ReadOnlySpan<byte> field, T value)
        where T : IEnumeration<T> => WriteEnumeration(in value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteEnumerationValues<T>(ReadOnlySpan<byte> field,
        in ArrayOf<T> values) where T : IEnumeration<T> => WriteEnumerationValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteExpandedNodeId(ReadOnlySpan<byte> field, in ExpandedNodeId value)
        => WriteExpandedNodeId(in value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteExpandedNodeIdValues(ReadOnlySpan<byte> field,
        in ArrayOf<ExpandedNodeId> values) => WriteExpandedNodeIdValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteExtensionObject(ReadOnlySpan<byte> field, in ExtensionObject value)
        => WriteExtensionObject(in value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteExtensionObjectValues(ReadOnlySpan<byte> field,
        in ArrayOf<ExtensionObject> values) => WriteExtensionObjectValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFloat(ReadOnlySpan<byte> field, float value)
        => WriteFloat(value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteFloatValues(ReadOnlySpan<byte> field,
        in ArrayOf<float> values) => WriteFloatValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteGuid(ReadOnlySpan<byte> field, in Guid value)
        => WriteGuid(in value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteGuidValues(ReadOnlySpan<byte> field,
        in ArrayOf<Guid> values) => WriteGuidValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt16(ReadOnlySpan<byte> field, short value)
        => WriteInt16(value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt16Values(ReadOnlySpan<byte> field,
        in ArrayOf<short> values) => WriteInt16Values(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt32(ReadOnlySpan<byte> field, int value)
        => WriteInt32(value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt32Values(ReadOnlySpan<byte> field,
        in ArrayOf<int> values) => WriteInt32Values(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt64(ReadOnlySpan<byte> field, long value)
        => WriteInt64(value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteInt64Values(ReadOnlySpan<byte> field,
        in ArrayOf<long> values) => WriteInt64Values(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLocalizedText(ReadOnlySpan<byte> field, in LocalizedText value)
        => WriteLocalizedText(in value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteLocalizedTextValues(ReadOnlySpan<byte> field,
        in ArrayOf<LocalizedText> values) => WriteLocalizedTextValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteNodeId(ReadOnlySpan<byte> field, in NodeId value)
        => WriteNodeId(in value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteNodeIdValues(ReadOnlySpan<byte> field,
        in ArrayOf<NodeId> values) => WriteNodeIdValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteQualifiedName(ReadOnlySpan<byte> field, in QualifiedName value)
        => WriteQualifiedName(in value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteQualifiedNameValues(ReadOnlySpan<byte> field,
        in ArrayOf<QualifiedName> values) => WriteQualifiedNameValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSByte(ReadOnlySpan<byte> field, sbyte value)
        => WriteSByte(value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteSByteValues(ReadOnlySpan<byte> field,
        in ArrayOf<sbyte> values) => WriteSByteValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteStatusCode(ReadOnlySpan<byte> field, StatusCode value)
        => WriteStatusCode(value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteStatusCodeValues(ReadOnlySpan<byte> field,
        in ArrayOf<StatusCode> values) => WriteStatusCodeValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteString(ReadOnlySpan<byte> field, in Utf8String value)
        => WriteString(in value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteStringValues(ReadOnlySpan<byte> field,
        in ArrayOf<Utf8String> values) => WriteStringValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteStructure<T>(ReadOnlySpan<byte> field, in T value)
        where T : IStructure<T> => WriteStructure(in value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteStructureValues<T>(ReadOnlySpan<byte> field,
        in ArrayOf<T> values) where T : IStructure<T>
        => WriteStructureValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteStructureAsExtensionObject<T>(ReadOnlySpan<byte> field, in T value)
        where T : IStructure<T>
        => WriteStructureAsExtensionObject(in value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteStructureAsExtensionObjectValues<T>(ReadOnlySpan<byte> field,
        in ArrayOf<T> values) where T : IStructure<T>
        => WriteStructureAsExtensionObjectValues(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt16(ReadOnlySpan<byte> field, ushort value)
        => WriteUInt16(value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt16Values(ReadOnlySpan<byte> field,
        in ArrayOf<ushort> values) => WriteUInt16Values(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32(ReadOnlySpan<byte> field, uint value)
        => WriteUInt32(value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt32Values(ReadOnlySpan<byte> field,
        in ArrayOf<uint> values) => WriteUInt32Values(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt64(ReadOnlySpan<byte> field, ulong value)
        => WriteUInt64(value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUInt64Values(ReadOnlySpan<byte> field,
        in ArrayOf<ulong> values) => WriteUInt64Values(in values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteVariant(ReadOnlySpan<byte> field, in Variant value)
        => WriteVariant(in value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteVariantValues(ReadOnlySpan<byte> field,
        in ArrayOf<Variant> values) => WriteVariantValues(in values);

    /// <inheritdoc/>
    public void Dispose() => Flush();

    /// <summary>
    /// Write messages. Messages are Structures encoded as sequence
    /// of bytes prefixed by the NodeId of for the OPC UA Binary
    /// DataTypeEncoding defined for the Message.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="maxMessageSize"></param>
    public void WriteMessage<T>(T value, uint maxMessageSize)
        where T : Message, IStructure<T>
    {
        if (value.GetTypeInfo() is not IBinaryEncoding encoding)
        {
            LastError = _context.SetLastError(StatusCodes.BadEncodingError,
                $"Type {typeof(T).Name} does not support binary encoding");
            return;
        }
        if (maxMessageSize != 0)
        {
            var length = value.GetSize(ref this);
            if (length + GetSizeOfNodeId(encoding.EncodingId) > maxMessageSize)
            {
                LastError = _context.SetLastError(StatusCodes.BadTcpMessageTooLarge,
                    $"Message exceeds max message size of {maxMessageSize}");
                return;
            }
        }
        WriteNodeId(encoding.EncodingId);
        WriteStructure(in value);
    }

    /// <summary>
    /// Flush writer
    /// </summary>
    /// <exception cref="ServiceResultException"></exception>
    public void Flush()
    {
        if (BytesPending > 0)
        {
            _writer.Advance(BytesPending);
            BytesCommitted += BytesPending;
            BytesPending = 0;
        }
    }

    /// <summary>
    /// Write boolean
    /// </summary>
    /// <param name="value"></param>
    private void WriteBoolean(bool value)
        => WriteByte((byte)(value ? 0x1 : 0x0));

    /// <summary>
    /// Write signed byte
    /// </summary>
    /// <param name="value"></param>
    private void WriteSByte(sbyte value)
        => WriteByte((byte)value);

    /// <summary>
    /// Write byte
    /// </summary>
    /// <param name="value"></param>
    private void WriteByte(byte value)
    {
        Ensure(1, out var output);
        output[0] = value;
        Advance(1);
    }

    /// <summary>
    /// Write short
    /// </summary>
    /// <param name="value"></param>
    private void WriteInt16(short value)
    {
        Ensure(2, out var output);
        if (!IsLittleEndian)
        {
            var tmp = BinaryPrimitives.ReverseEndianness(value);
            MemoryMarshal.Write(output, in tmp);
        }
        else
        {
            MemoryMarshal.Write(output, in value);
        }
        Advance(2);
    }

    /// <summary>
    /// Write unsigned short
    /// </summary>
    /// <param name="value"></param>
    private void WriteUInt16(ushort value)
    {
        Ensure(2, out var output);
        if (!IsLittleEndian)
        {
            var tmp = BinaryPrimitives.ReverseEndianness(value);
            MemoryMarshal.Write(output, in tmp);
        }
        else
        {
            MemoryMarshal.Write(output, in value);
        }
        Advance(2);
    }

    /// <summary>
    /// Write integer
    /// </summary>
    /// <param name="value"></param>
    private void WriteInt32(int value)
    {
        Ensure(4, out var output);
        if (!IsLittleEndian)
        {
            var tmp = BinaryPrimitives.ReverseEndianness(value);
            MemoryMarshal.Write(output, in tmp);
        }
        else
        {
            MemoryMarshal.Write(output, in value);
        }
        Advance(4);
    }

    /// <summary>
    /// Write unsigned integer
    /// </summary>
    /// <param name="value"></param>
    private void WriteUInt32(uint value)
    {
        Ensure(4, out var output);
        if (!IsLittleEndian)
        {
            var tmp = BinaryPrimitives.ReverseEndianness(value);
            MemoryMarshal.Write(output, in tmp);
        }
        else
        {
            MemoryMarshal.Write(output, in value);
        }
        Advance(4);
    }

    /// <summary>
    /// Write long
    /// </summary>
    /// <param name="value"></param>
    private void WriteInt64(long value)
    {
        Ensure(8, out var output);
        if (!IsLittleEndian)
        {
            var tmp = BinaryPrimitives.ReverseEndianness(value);
            MemoryMarshal.Write(output, in tmp);
        }
        else
        {
            MemoryMarshal.Write(output, in value);
        }
        Advance(8);
    }

    /// <summary>
    /// Write unsigned long
    /// </summary>
    /// <param name="value"></param>
    private void WriteUInt64(ulong value)
    {
        Ensure(8, out var output);
        if (!IsLittleEndian)
        {
            var tmp = BinaryPrimitives.ReverseEndianness(value);
            MemoryMarshal.Write(output, in tmp);
        }
        else
        {
            MemoryMarshal.Write(output, in value);
        }
        Advance(8);
    }

    /// <summary>
    /// Write float
    /// </summary>
    /// <param name="value"></param>
    private void WriteFloat(float value)
    {
        Ensure(4, out var output);
        if (!IsLittleEndian)
        {
            var tmp = BinaryPrimitives.ReverseEndianness(BitConverter.SingleToInt32Bits(value));
            MemoryMarshal.Write(output, in tmp);
        }
        else
        {
            MemoryMarshal.Write(output, in value);
        }
        Advance(4);
    }

    /// <summary>
    /// Write double
    /// </summary>
    /// <param name="value"></param>
    private void WriteDouble(double value)
    {
        Ensure(8, out var output);
        if (!IsLittleEndian)
        {
            var tmp = BinaryPrimitives.ReverseEndianness(BitConverter.DoubleToInt64Bits(value));
            MemoryMarshal.Write(output, in tmp);
        }
        else
        {
            MemoryMarshal.Write(output, in value);
        }
        Advance(8);
    }

    /// <summary>
    /// Write date time
    /// </summary>
    /// <param name="value"></param>
    private void WriteDateTime(DateTimeUtc value) => WriteInt64(value.Value);

    /// <summary>
    /// Write guid
    /// </summary>
    /// <param name="value"></param>
    private void WriteGuid(in Guid value)
    {
        Ensure(16, out var output);
        value.TryWriteBytes(output);
        Advance(16);
    }

    /// <summary>
    /// Write status code
    /// </summary>
    /// <param name="value"></param>
    private void WriteStatusCode(StatusCode value)
        => WriteUInt32(value.Code);

    /// <summary>
    /// Write enumeration
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="value"></param>
    /// <exception cref="ArgumentNullException"></exception>
    private void WriteEnumeration<T>(in T value) where T : IEnumeration<T>
        => WriteInt32(value.Value);

    /// <summary>
    /// Write xml element
    /// </summary>
    /// <param name="value"></param>
    private void WriteXmlElement(in XmlElement value)
    {
        if (value.IsEmpty)
        {
            WriteInt32(-1);
            return;
        }
        WriteString(in value.AsUtf8String());
    }

    /// <summary>
    /// Get size of the xml element
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public int GetSizeOfXmlElement(in XmlElement value)
    {
        if (value.IsEmpty)
        {
            return 4;
        }
        return GetSizeOfString(value.OuterXml);
    }

    /// <summary>
    /// Write string
    /// </summary>
    /// <param name="value"></param>
    public void WriteString(string value)
        => WriteString(new Utf8String(value));

    /// <summary>
    /// Get size of the string
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public int GetSizeOfString(string value)
        => GetSizeOfString(new Utf8String(value));

    /// <summary>
    /// Write string
    /// </summary>
    /// <param name="value"></param>
    private void WriteString(in Utf8String value)
    {
        var count = value.ByteCount;
        if (count == 0 || IsStringLengthEncodingLimitsExceeded(count))
        {
            WriteInt32(-1);
            return;
        }
        WriteInt32(count);
        if (value.IsMemoryRef())
        {
            BitCopy(value.AsMemory().Span);
            return;
        }
        BitCopy(value.ToByteString().Span);
    }

    /// <summary>
    /// Get size of the string
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public int GetSizeOfString(in Utf8String value)
    {
        var count = value.ByteCount;
        if (IsStringLengthEncodingLimitsExceeded(count))
        {
            return 4;
        }
        return 4 + count;
    }

    /// <summary>
    /// Write byte string
    /// </summary>
    /// <param name="value"></param>
    private void WriteByteString(in ByteString value)
    {
        if (value.IsEmpty || IsByteStringLengthEncodingLimitsExceeded(value.Length))
        {
            WriteInt32(-1);
            return;
        }
        WriteInt32(value.Length);
        BitCopy(value.Span);
    }

    /// <summary>
    /// Get size of byte string
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public int GetSizeOfByteString(in ByteString value)
    {
        if (IsByteStringLengthEncodingLimitsExceeded(value.Length))
        {
            return 4;
        }
        return 4 + value.Length;
    }

    /// <summary>
    /// Write node id
    /// </summary>
    /// <param name="value"></param>
    private void WriteNodeId(in NodeId value)
    {
        // write a null node id.
        if (value.IsNull)
        {
            WriteUInt16(0);
            return;
        }
        // write the encoding.
        var encoding = value.Encoding;
        WriteByte(encoding);
        // write the node id body.
        switch (0x3f & encoding)
        {
            case 0x0:
                WriteByte((byte)value.AsNumeric());
                break;
            case 0x1:
                WriteByte((byte)value.NamespaceIndex);
                WriteUInt16((ushort)value.AsNumeric());
                break;
            case 0x2:
                WriteUInt16(value.NamespaceIndex);
                WriteUInt32(value.AsNumeric());
                break;
            case 0x3:
                WriteUInt16(value.NamespaceIndex);
                WriteString(in value.AsUtf8String());
                break;
            case 0x4:
                WriteUInt16(value.NamespaceIndex);
                WriteGuid(in value.AsGuid());
                break;
            default:
                Debug.Assert((0x3f & encoding) == 0x5);
                WriteUInt16(value.NamespaceIndex);
                WriteByteString(in value.AsByteString());
                break;
        }
    }

    /// <summary>
    /// Get size of node id
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public int GetSizeOfNodeId(in NodeId value)
    {
        if (value.IsNull)
        {
            return 2;
        }
        return 1 + (0x3f & value.Encoding) switch
        {
            0x0 => 1,
            0x1 => 3,
            0x2 => 6,
            0x3 => 2 + GetSizeOfString(in value.AsUtf8String()),
            0x4 => 2 + 16,
            _ => 2 + GetSizeOfByteString(in value.AsByteString()),
        };
    }

    /// <summary>
    /// Write expanded node id
    /// </summary>
    /// <param name="value"></param>
    private void WriteExpandedNodeId(in ExpandedNodeId value)
    {
        // write a null node id.
        if (value.IsNull)
        {
            WriteUInt16(0);
            return;
        }

        var encoding = value.Encoding;

        // add the bit indicating a uri string is encoded as well.
        if (value.HasNamespaceUri)
        {
            encoding |= 0x80;
        }

        // add the bit indicating a server index.
        if (value.ServerIndex > 0)
        {
            encoding |= 0x40;
        }

        // write the encoding.
        WriteByte(encoding);

        // write the node id.
        switch (0x3f & value.Encoding)
        {
            case 0x0:
                WriteByte((byte)value.AsNumeric());
                break;
            case 0x1:
                WriteByte((byte)value.NamespaceIndex);
                WriteUInt16((ushort)value.AsNumeric());
                break;
            case 0x2:
                WriteUInt16(value.NamespaceIndex);
                WriteUInt32(value.AsNumeric());
                break;
            case 0x3:
                WriteUInt16(value.NamespaceIndex);
                WriteString(in value.AsUtf8String());
                break;
            case 0x4:
                WriteUInt16(value.NamespaceIndex);
                WriteGuid(in value.AsGuid());
                break;
            default:
                Debug.Assert((0x3f & value.Encoding) == 0x5);
                WriteUInt16(value.NamespaceIndex);
                WriteByteString(in value.AsByteString());
                break;
        }

        // write the namespace uri.
        if ((encoding & 0x80) != 0)
        {
            WriteString(value.NamespaceUri);
        }

        // write the server index.
        if ((encoding & 0x40) != 0)
        {
            WriteUInt32(value.ServerIndex);
        }
    }

    /// <summary>
    /// Get size of expanded node id
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public int GetSizeOfExpandedNodeId(in ExpandedNodeId value)
    {
        if (value.IsNull)
        {
            return 2;
        }
        // get the node encoding.
        var size = 1 + (0x3f & value.Encoding) switch
        {
            0x0 => 1,
            0x1 => 3,
            0x2 => 6,
            0x3 => 2 + GetSizeOfString(in value.AsUtf8String()),
            0x4 => 2 + 16,
            _ => 2 + GetSizeOfByteString(in value.AsByteString()),
        };
        if (value.HasNamespaceUri)
        {
            size += GetSizeOfString(value.NamespaceUri);
        }
        if (value.ServerIndex > 0)
        {
            size += 4;
        }
        return size;
    }

    /// <summary>
    /// Write diagnostic info
    /// </summary>
    /// <param name="value"></param>
    private void WriteDiagnosticInfo(in DiagnosticInfo value)
    {
        // check for null.
        if (DiagnosticInfo.IsNull(value) || IsNestingLevelsExceeded())
        {
            WriteByte(0);
            return;
        }

        _nestingLevel++;

        // calculate the encoding.
        byte encoding = 0;

        if (value.SymbolicId >= 0)
        {
            encoding |= 0x01;
        }

        if (value.NamespaceUri >= 0)
        {
            encoding |= 0x02;
        }

        if (value.Locale >= 0)
        {
            encoding |= 0x08;
        }

        if (value.LocalizedText >= 0)
        {
            encoding |= 0x04;
        }

        if (!value.AdditionalInfo.IsNullOrEmpty)
        {
            encoding |= 0x10;
        }

        if (value.InnerStatusCode != StatusCodes.Good)
        {
            encoding |= 0x20;
        }

        if (!DiagnosticInfo.IsNull(value.InnerDiagnosticInfo))
        {
            encoding |= 0x40;
        }

        // write the encoding.
        WriteByte(encoding);

        // write the fields of the diagnostic info structure.
        if ((encoding & 0x01) != 0)
        {
            WriteInt32(value.SymbolicId);
        }

        if ((encoding & 0x02) != 0)
        {
            WriteInt32(value.NamespaceUri);
        }

        if ((encoding & 0x08) != 0)
        {
            WriteInt32(value.Locale);
        }

        if ((encoding & 0x04) != 0)
        {
            WriteInt32(value.LocalizedText);
        }

        if ((encoding & 0x10) != 0)
        {
            WriteString(value.AdditionalInfo);
        }

        if ((encoding & 0x20) != 0)
        {
            WriteStatusCode(value.InnerStatusCode);
        }

        if ((encoding & 0x40) != 0)
        {
            WriteDiagnosticInfo(value.InnerDiagnosticInfo!);
        }
        _nestingLevel--;
    }

    /// <summary>
    /// Get size of diagnostic info
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public int GetSizeOfDiagnosticInfo(DiagnosticInfo value)
    {
        // check for null.
        var size = 1;
        if (DiagnosticInfo.IsNull(value) || IsNestingLevelsExceeded())
        {
            return size;
        }

        _nestingLevel++;

        if (value.SymbolicId >= 0)
        {
            size += 4;
        }
        if (value.NamespaceUri >= 0)
        {
            size += 4;
        }
        if (value.Locale >= 0)
        {
            size += 4;
        }
        if (value.LocalizedText >= 0)
        {
            size += 4;
        }
        if (!value.AdditionalInfo.IsNullOrEmpty)
        {
            size += GetSizeOfString(value.AdditionalInfo);
        }
        if (value.InnerStatusCode != StatusCodes.Good)
        {
            size += 4;
        }
        if (!DiagnosticInfo.IsNull(value.InnerDiagnosticInfo))
        {
            size += GetSizeOfDiagnosticInfo(value.InnerDiagnosticInfo);
        }

        _nestingLevel--;
        return size;
    }

    /// <summary>
    /// Write qualfied name
    /// </summary>
    /// <param name="value"></param>
    private void WriteQualifiedName(in QualifiedName value)
    {
        WriteUInt16(value.NamespaceIndex);
        WriteString(value.Name);
    }

    /// <summary>
    /// Get size of qualified name
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public int GetSizeOfQualifiedName(in QualifiedName value)
        => 2 + GetSizeOfString(value.Name);

    /// <summary>
    /// Write localized text
    /// </summary>
    /// <param name="value"></param>
    private void WriteLocalizedText(in LocalizedText value)
    {
        // check for null.
        if (value.IsNull)
        {
            WriteByte(0);
            return;
        }

        // calculate the encoding.
        byte encoding = 0;

        if (!value.Locale.IsNullOrEmpty)
        {
            encoding |= 0x1;
        }

        if (!value.Text.IsNullOrEmpty)
        {
            encoding |= 0x2;
        }

        // write the encoding.
        WriteByte(encoding);

        // write the fields.
        if ((encoding & 0x1) != 0)
        {
            WriteString(value.Locale);
        }

        if ((encoding & 0x2) != 0)
        {
            WriteString(value.Text);
        }
    }

    /// <summary>
    /// Get the size of the localized text
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public int GetSizeOfLocalizedText(in LocalizedText value)
    {
        // check for null.
        var size = 1;
        if (value.IsNull)
        {
            return size;
        }
        if (!value.Locale.IsNullOrEmpty)
        {
            size += GetSizeOfString(value.Locale);
        }
        if (!value.Text.IsNullOrEmpty)
        {
            size += GetSizeOfString(value.Text);
        }
        return size;
    }

    /// <summary>
    /// Write data value
    /// </summary>
    /// <param name="value"></param>
    private void WriteDataValue(in DataValue value)
    {
        // check for null.
        if (value.IsNull)
        {
            WriteByte(0);
            return;
        }

        // calculate the encoding.
        byte encoding = 0;

        ref readonly var variant = ref value.AsVariant();
        if (!variant.IsNull)
        {
            encoding |= 0x01;
        }

        if (value.StatusCode != StatusCodes.Good)
        {
            encoding |= 0x02;
        }

        if (!value.SourceTimestamp.IsNullOrEmpty)
        {
            encoding |= 0x04;
        }

        if (value.SourcePicoseconds != 0)
        {
            encoding |= 0x10;
        }

        if (!value.ServerTimestamp.IsNullOrEmpty)
        {
            encoding |= 0x08;
        }

        if (value.ServerPicoseconds != 0)
        {
            encoding |= 0x20;
        }

        WriteByte(encoding);

        // write the fields of the data value structure.
        if ((encoding & 0x01) != 0)
        {
            WriteVariant(in variant);
        }

        if ((encoding & 0x02) != 0)
        {
            WriteStatusCode(value.StatusCode);
        }

        if ((encoding & 0x04) != 0)
        {
            WriteDateTime(value.SourceTimestamp);
        }

        if ((encoding & 0x10) != 0)
        {
            WriteUInt16(value.SourcePicoseconds);
        }

        if ((encoding & 0x08) != 0)
        {
            WriteDateTime(value.ServerTimestamp);
        }

        if ((encoding & 0x20) != 0)
        {
            WriteUInt16(value.ServerPicoseconds);
        }
    }

    /// <summary>
    /// Get size of data value
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public int GetSizeOfDataValue(in DataValue value)
    {
        var size = 1;
        if (value.IsNull)
        {
            return size;
        }

        if (!value.Value.IsNull)
        {
            size += GetSizeOfVariant(value.Value);
        }

        if (value.StatusCode != StatusCodes.Good)
        {
            size += 4;
        }

        if (!value.SourceTimestamp.IsNullOrEmpty)
        {
            size += 8;
        }

        if (value.SourcePicoseconds != 0)
        {
            size += 2;
        }

        if (!value.ServerTimestamp.IsNullOrEmpty)
        {
            size += 8;
        }

        if (value.ServerPicoseconds != 0)
        {
            size += 2;
        }
        return size;
    }

    /// <summary>
    /// Write extension object
    /// </summary>
    /// <param name="value"></param>
    private void WriteExtensionObject(in ExtensionObject value)
    {
        // check for null.
        if (value.IsNull)
        {
            WriteNodeId(default);
            WriteByte(0); // none
            return;
        }

        // TODO: Try Convert from binary and xml to json

        WriteNodeId(value.TypeId);
        WriteByte((byte)value.Encoding);
        switch (value.Encoding)
        {
            case EncodingType.Binary:
                WriteByteString(in value.AsByteString());
                break;
            case EncodingType.Json:
            case EncodingType.Xml:
                WriteString(in value.AsUtf8String());
                break;
        }
    }

    /// <summary>
    /// Get size of extension object
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public int GetSizeOfExtensionObject(in ExtensionObject value)
    {
        if (value.IsNull)
        {
            return 1 + 2;
        }

        // TODO: Try Convert from binary and xml to json

        var size = 1 + GetSizeOfNodeId(value.TypeId);
        switch (value.Encoding)
        {
            case EncodingType.Binary:
                size += GetSizeOfByteString(in value.AsByteString());
                break;
            case EncodingType.Json:
            case EncodingType.Xml:
                size += GetSizeOfString(in value.AsUtf8String());
                break;
        }
        return size;
    }

    /// <summary>
    /// Get size of extension object structure
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public int GetSizeOfStructureAsExtensionObject<T>(in T value) where T : IStructure<T>
    {
        if (T.IsNull(value))
        {
            return 1 + 2;
        }
        if (value.GetTypeInfo() is not IBinaryEncoding encoding)
        {
            LastError = _context.SetLastError(StatusCodes.BadEncodingError,
                $"Type {typeof(T).Name} does not support binary encoding");
            return 0;
        }
        var size = 1 + GetSizeOfNodeId(encoding.EncodingId);
        return size + 4 + GetSizeOfStructure(in value);
    }

    /// <summary>
    /// Write structure as extension object
    /// </summary>
    /// <param name="value"></param>
    private void WriteStructureAsExtensionObject<T>(in T value)
        where T : IStructure<T>
    {
        if (T.IsNull(value))
        {
            WriteNodeId(default);
            WriteByte(0);
            return;
        }
        if (value.GetTypeInfo() is not IBinaryEncoding encoding)
        {
            LastError = _context.SetLastError(StatusCodes.BadEncodingError,
                $"Type {typeof(T).Name} does not support binary encoding");
            return;
        }
        WriteNodeId(encoding.EncodingId);
        WriteByte(Convert.ToByte(EncodingType.Binary,
            CultureInfo.InvariantCulture));
        WriteInt32(value.GetSize(ref this));
        WriteStructure<T>(in value);
    }

    /// <summary>
    /// Write structure using a given encoder
    /// </summary>
    /// <param name="value"></param>
    internal void WriteStructure<T>(in T value) where T : IStructure<T>
    {
        // Part 6 5.2.6: Structures do not have a null value. If an encoder is
        // written in a programming language that allows structures to have null
        // values, then the encoder shall create a new instance with DefaultValues
        // for all fields and serialize that. Encoders shall not generate an
        // encoding error in this situation.

        if (IsNestingLevelsExceeded())
        {
            return;
        }
        _nestingLevel++;
        try
        {
            value.Write(ref this);
        }
        finally
        {
            _nestingLevel--;
        }
    }

    /// <summary>
    /// Get size of structure
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="value"></param>
    /// <returns></returns>
    public int GetSizeOfStructure<T>(in T value) where T : IStructure<T>
    {
        if (IsNestingLevelsExceeded())
        {
            return 0;
        }
        _nestingLevel++;
        try
        {
            return value.GetSize(ref this);
        }
        finally
        {
            _nestingLevel--;
        }
    }

    /// <summary>
    /// Write variant
    /// </summary>
    /// <param name="value"></param>
    private void WriteVariant(in Variant value)
    {
        if (IsNestingLevelsExceeded())
        {
            WriteByte(0);
            return;
        }
        _nestingLevel++;
        try
        {
            // check for null.
            if (value.BuiltInType == BuiltInType.Null)
            {
                WriteByte(0);
                return;
            }

            var encodingByte = (byte)value.BuiltInType;

            if (value.ValueRank < 0)
            {
                WriteByte(encodingByte);

                switch (value.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        WriteBoolean((bool)value);
                        break;
                    case BuiltInType.SByte:
                        WriteSByte((sbyte)value);
                        break;
                    case BuiltInType.Byte:
                        WriteByte((byte)value);
                        break;
                    case BuiltInType.Int16:
                        WriteInt16((short)value);
                        break;
                    case BuiltInType.UInt16:
                        WriteUInt16((ushort)value);
                        break;
                    case BuiltInType.Int32:
                        WriteInt32((int)value);
                        break;
                    case BuiltInType.UInt32:
                        WriteUInt32((uint)value);
                        break;
                    case BuiltInType.Int64:
                        WriteInt64((long)value);
                        break;
                    case BuiltInType.UInt64:
                        WriteUInt64((ulong)value);
                        break;
                    case BuiltInType.Float:
                        WriteFloat((float)value);
                        break;
                    case BuiltInType.Double:
                        WriteDouble((double)value);
                        break;
                    case BuiltInType.String:
                        WriteString(in value.AsUtf8String());
                        break;
                    case BuiltInType.DateTime:
                        WriteDateTime((DateTimeUtc)value);
                        break;
                    case BuiltInType.Guid:
                        WriteGuid(in value.AsGuid());
                        break;
                    case BuiltInType.ByteString:
                        WriteByteString(in value.AsByteString());
                        break;
                    case BuiltInType.XmlElement:
                        WriteXmlElement(in value.AsXmlElement());
                        break;
                    case BuiltInType.NodeId:
                        WriteNodeId(in value.AsNodeId());
                        break;
                    case BuiltInType.ExpandedNodeId:
                        WriteExpandedNodeId(in value.AsExpandedNodeId());
                        break;
                    case BuiltInType.StatusCode:
                        WriteStatusCode((StatusCode)value);
                        break;
                    case BuiltInType.QualifiedName:
                        WriteQualifiedName(in value.AsQualifiedName());
                        break;
                    case BuiltInType.LocalizedText:
                        WriteLocalizedText(in value.AsLocalizedText());
                        break;
                    case BuiltInType.ExtensionObject:
                        WriteExtensionObject(in value.AsExtensionObject());
                        break;
                    case BuiltInType.DataValue:
                        WriteDataValue(in value.AsDataValue());
                        break;
                    default:
                        LastError = _context.SetLastError(StatusCodes.BadEncodingError,
                            $"Unexpected type while encoding a Variant: {value.BuiltInType}");
                        return;
                }
            }
            else if (value.ValueRank >= 0)
            {
                encodingByte |= 0x80; // array

                if (value.ValueRank > 1)
                {
                    encodingByte |= 0x40; // multi dimension
                }

                WriteByte(encodingByte);

                switch (value.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        WriteBooleanValues(in value.AsArrayOf<bool>());
                        break;
                    case BuiltInType.SByte:
                        WriteSByteValues(in value.AsArrayOf<sbyte>());
                        break;
                    case BuiltInType.Byte:
                        WriteByteValues(in value.AsArrayOf<byte>());
                        break;
                    case BuiltInType.Int16:
                        WriteInt16Values(in value.AsArrayOf<short>());
                        break;
                    case BuiltInType.UInt16:
                        WriteUInt16Values(in value.AsArrayOf<ushort>());
                        break;
                    case BuiltInType.Int32:
                        WriteInt32Values(in value.AsArrayOf<int>());
                        break;
                    case BuiltInType.UInt32:
                        WriteUInt32Values(in value.AsArrayOf<uint>());
                        break;
                    case BuiltInType.Int64:
                        WriteInt64Values(in value.AsArrayOf<long>());
                        break;
                    case BuiltInType.UInt64:
                        WriteUInt64Values(in value.AsArrayOf<ulong>());
                        break;
                    case BuiltInType.Float:
                        WriteFloatValues(in value.AsArrayOf<float>());
                        break;
                    case BuiltInType.Double:
                        WriteDoubleValues(in value.AsArrayOf<double>());
                        break;
                    case BuiltInType.String:
                        WriteStringValues(in value.AsArrayOf<Utf8String>());
                        break;
                    case BuiltInType.DateTime:
                        WriteDateTimeValues(in value.AsArrayOf<DateTimeUtc>());
                        break;
                    case BuiltInType.Guid:
                        WriteGuidValues(in value.AsArrayOf<Guid>());
                        break;
                    case BuiltInType.ByteString:
                        WriteByteStringValues(in value.AsArrayOf<ByteString>());
                        break;
                    case BuiltInType.XmlElement:
                        WriteXmlElementValues(in value.AsArrayOf<XmlElement>());
                        break;
                    case BuiltInType.NodeId:
                        WriteNodeIdValues(in value.AsArrayOf<NodeId>());
                        break;
                    case BuiltInType.ExpandedNodeId:
                        WriteExpandedNodeIdValues(in value.AsArrayOf<ExpandedNodeId>());
                        break;
                    case BuiltInType.StatusCode:
                        WriteStatusCodeValues(in value.AsArrayOf<StatusCode>());
                        break;
                    case BuiltInType.QualifiedName:
                        WriteQualifiedNameValues(in value.AsArrayOf<QualifiedName>());
                        break;
                    case BuiltInType.LocalizedText:
                        WriteLocalizedTextValues(in value.AsArrayOf<LocalizedText>());
                        break;
                    case BuiltInType.ExtensionObject:
                        WriteExtensionObjectValues(in value.AsArrayOf<ExtensionObject>());
                        break;
                    case BuiltInType.DataValue:
                        WriteDataValueValues(in value.AsArrayOf<DataValue>());
                        break;
                    case BuiltInType.Variant:
                        WriteVariantValues(in value.AsArrayOf<Variant>());
                        break;
                    default:
                        LastError = _context.SetLastError(StatusCodes.BadEncodingError,
                           $"Unexpected type while encoding a Variant: {value.BuiltInType}");
                        return;
                }

                if (value.ValueRank > 1)
                {
                    // Write dimensions
                    WriteInt32Values(value.Dimensions.ToArrayOf()); // TODO: Optimize
                }
            }
        }
        finally
        {
            _nestingLevel--;
        }
    }

    /// <summary>
    /// Get size of variant
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public int GetSizeOfVariant(in Variant value)
    {
        if (IsNestingLevelsExceeded())
        {
            return 1;
        }
        _nestingLevel++;
        try
        {
            var size = 1; // encoding byte
            // check for null.
            if (value.BuiltInType == BuiltInType.Null)
            {
                return size;
            }
            else if (value.ValueRank < 0)
            {
                switch (value.BuiltInType)
                {
                    case BuiltInType.Boolean:
                    case BuiltInType.SByte:
                    case BuiltInType.Byte:
                        return size + 1;
                    case BuiltInType.Int16:
                    case BuiltInType.UInt16:
                        return size + 2;
                    case BuiltInType.Int32:
                    case BuiltInType.UInt32:
                    case BuiltInType.Float:
                    case BuiltInType.StatusCode:
                        return size + 4;
                    case BuiltInType.Int64:
                    case BuiltInType.UInt64:
                    case BuiltInType.Double:
                    case BuiltInType.DateTime:
                        return size + 8;
                    case BuiltInType.String:
                        return size + GetSizeOfString(in value.AsUtf8String());
                    case BuiltInType.Guid:
                        return size + 16;
                    case BuiltInType.ByteString:
                        return size + GetSizeOfByteString(in value.AsByteString());
                    case BuiltInType.XmlElement:
                        return size + GetSizeOfXmlElement(in value.AsXmlElement());
                    case BuiltInType.NodeId:
                        return size + GetSizeOfNodeId(in value.AsNodeId());
                    case BuiltInType.ExpandedNodeId:
                        return size + GetSizeOfExpandedNodeId(in value.AsExpandedNodeId());
                    case BuiltInType.QualifiedName:
                        return size + GetSizeOfQualifiedName(in value.AsQualifiedName());
                    case BuiltInType.LocalizedText:
                        return size + GetSizeOfLocalizedText(in value.AsLocalizedText());
                    case BuiltInType.ExtensionObject:
                        return size + GetSizeOfExtensionObject(in value.AsExtensionObject());
                    case BuiltInType.DataValue:
                        return size + GetSizeOfDataValue(in value.AsDataValue());
                }
                LastError = _context.SetLastError(StatusCodes.BadEncodingError,
                    $"Unexpected type or value while encoding a Variant: {value.BuiltInType}");
                return size;
            }
            else
            {
                if (value.ValueRank > 1)
                {
                    size += 4 + (value.Dimensions.Length * 4);
                }
                switch (value.BuiltInType)
                {
                    case BuiltInType.Boolean:
                        return size + 4 + (value.AsArrayOf<bool>().Count * 1);
                    case BuiltInType.SByte:
                        return size + 4 + (value.AsArrayOf<sbyte>().Count * 1);
                    case BuiltInType.Byte:
                        return size + 4 + (value.AsArrayOf<byte>().Count * 1);
                    case BuiltInType.Int16:
                        return size + 4 + (value.AsArrayOf<short>().Count * 2);
                    case BuiltInType.UInt16:
                        return size + 4 + (value.AsArrayOf<ushort>().Count * 2);
                    case BuiltInType.Int32:
                        return size + 4 + (value.AsArrayOf<int>().Count * 4);
                    case BuiltInType.UInt32:
                        return size + 4 + (value.AsArrayOf<uint>().Count * 4);
                    case BuiltInType.StatusCode:
                        return size + 4 + (value.AsArrayOf<StatusCode>().Count * 4);
                    case BuiltInType.Int64:
                        return size + 4 + (value.AsArrayOf<long>().Count * 8);
                    case BuiltInType.UInt64:
                        return size + 4 + (value.AsArrayOf<ulong>().Count * 8);
                    case BuiltInType.Float:
                        return size + 4 + (value.AsArrayOf<float>().Count * 4);
                    case BuiltInType.Double:
                        return size + 4 + (value.AsArrayOf<double>().Count * 8);
                    case BuiltInType.DateTime:
                        return size + 4 + (value.AsArrayOf<DateTime>().Count * 8);
                    case BuiltInType.Guid:
                        return size + 4 + (value.AsArrayOf<Guid>().Count * 16);
                    case BuiltInType.String:
                        return size + GetSizeOfStringValues(in value.AsArrayOf<Utf8String>());
                    case BuiltInType.ByteString:
                        return size + GetSizeOfByteStringValues(in value.AsArrayOf<ByteString>());
                    case BuiltInType.XmlElement:
                        return size + GetSizeOfXmlElementValues(in value.AsArrayOf<XmlElement>());
                    case BuiltInType.NodeId:
                        return size + GetSizeOfNodeIdValues(in value.AsArrayOf<NodeId>());
                    case BuiltInType.ExpandedNodeId:
                        return size + GetSizeOfExpandedNodeIdValues(in value.AsArrayOf<ExpandedNodeId>());
                    case BuiltInType.QualifiedName:
                        return size + GetSizeOfQualifiedNameValues(in value.AsArrayOf<QualifiedName>());
                    case BuiltInType.LocalizedText:
                        return size + GetSizeOfLocalizedTextValues(in value.AsArrayOf<LocalizedText>());
                    case BuiltInType.ExtensionObject:
                        return size + GetSizeOfExtensionObjectValues(in value.AsArrayOf<ExtensionObject>());
                    case BuiltInType.DataValue:
                        return size + GetSizeOfDataValueValues(in value.AsArrayOf<DataValue>());
                    case BuiltInType.Variant:
                        return size + GetSizeOfVariantValues(in value.AsArrayOf<Variant>());
                }
                LastError = _context.SetLastError(StatusCodes.BadEncodingError,
                    $"Unexpected type while encoding a Variant: {value.BuiltInType}");
                return size;
            }
        }
        finally
        {
            _nestingLevel--;
        }
    }

    /// <summary>
    /// Write boolean values
    /// </summary>
    /// <param name="values"></param>
    private void WriteBooleanValues(in ArrayOf<bool> values)
    {
        if (WriteArrayLength(in values))
        {
#if !SLOW_BOOL
            BitCopy(values.Span);
#else
            for (var i = 0; i < values.Count; i++)
            {
                WriteBoolean(values.Span[i]);
            }
#endif
        }
    }

    /// <summary>
    /// Write byte values
    /// </summary>
    /// <param name="values"></param>
    private void WriteByteValues(in ArrayOf<byte> values)
    {
        if (WriteArrayLength(in values))
        {
            BitCopy(values.Span);
        }
    }

    /// <summary>
    /// Write signed bytes
    /// </summary>
    /// <param name="values"></param>
    private void WriteSByteValues(in ArrayOf<sbyte> values)
    {
        if (WriteArrayLength(in values))
        {
            BitCopy(values.Span);
        }
    }

    /// <summary>
    /// Write short values
    /// </summary>
    /// <param name="values"></param>
    private void WriteInt16Values(in ArrayOf<short> values)
    {
        if (WriteArrayLength(in values))
        {
            if (IsLittleEndian)
            {
                BitCopy(values.Span);
                return;
            }

            for (var i = 0; i < values.Count; i++)
            {
                WriteInt16(values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write unsigned short values
    /// </summary>
    /// <param name="values"></param>
    private void WriteUInt16Values(in ArrayOf<ushort> values)
    {
        if (WriteArrayLength(in values))
        {
            if (IsLittleEndian)
            {
                BitCopy(values.Span);
                return;
            }

            for (var i = 0; i < values.Count; i++)
            {
                WriteUInt16(values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write integer values
    /// </summary>
    /// <param name="values"></param>
    private void WriteInt32Values(in ArrayOf<int> values)
    {
        if (WriteArrayLength(in values))
        {
            if (IsLittleEndian)
            {
                BitCopy(values.Span);
                return;
            }

            for (var i = 0; i < values.Count; i++)
            {
                WriteInt32(values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write unsigned integer values
    /// </summary>
    /// <param name="values"></param>
    private void WriteUInt32Values(in ArrayOf<uint> values)
    {
        if (WriteArrayLength(in values))
        {
            if (IsLittleEndian)
            {
                BitCopy(values.Span);
                return;
            }

            for (var i = 0; i < values.Count; i++)
            {
                WriteUInt32(values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write long values
    /// </summary>
    /// <param name="values"></param>
    private void WriteInt64Values(in ArrayOf<long> values)
    {
        if (WriteArrayLength(in values))
        {
            if (IsLittleEndian)
            {
                BitCopy(values.Span);
                return;
            }

            for (var i = 0; i < values.Count; i++)
            {
                WriteInt64(values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write unsigned long values
    /// </summary>
    /// <param name="values"></param>
    private void WriteUInt64Values(in ArrayOf<ulong> values)
    {
        if (WriteArrayLength(in values))
        {
            if (IsLittleEndian)
            {
                BitCopy(values.Span);
                return;
            }

            for (var i = 0; i < values.Count; i++)
            {
                WriteUInt64(values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write float values
    /// </summary>
    /// <param name="values"></param>
    private void WriteFloatValues(in ArrayOf<float> values)
    {
        if (WriteArrayLength(in values))
        {
            if (IsLittleEndian)
            {
                BitCopy(values.Span);
                return;
            }

            for (var i = 0; i < values.Count; i++)
            {
                WriteFloat(values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write double values
    /// </summary>
    /// <param name="values"></param>
    private void WriteDoubleValues(in ArrayOf<double> values)
    {
        if (WriteArrayLength(in values))
        {
            if (IsLittleEndian)
            {
                BitCopy(values.Span);
                return;
            }

            for (var i = 0; i < values.Count; i++)
            {
                WriteDouble(values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write string values
    /// </summary>
    /// <param name="values"></param>
    public void WriteStringValues(in ArrayOf<string> values)
    {
        if (WriteArrayLength(in values))
        {
            for (var i = 0; i < values.Count; i++)
            {
                WriteString(values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write string values
    /// </summary>
    /// <param name="values"></param>
    private void WriteStringValues(in ArrayOf<Utf8String> values)
    {
        if (WriteArrayLength(in values))
        {
            for (var i = 0; i < values.Count; i++)
            {
                WriteString(in values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write timestamp values
    /// </summary>
    /// <param name="values"></param>
    private void WriteDateTimeValues(in ArrayOf<DateTimeUtc> values)
    {
        if (WriteArrayLength(in values))
        {
            if (IsLittleEndian)
            {
                BitCopy(values.Span);
                return;
            }
            for (var i = 0; i < values.Count; i++)
            {
                WriteDateTime(values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write guid values
    /// </summary>
    /// <param name="values"></param>
    private void WriteGuidValues(in ArrayOf<Guid> values)
    {
        if (WriteArrayLength(in values))
        {
            if (IsLittleEndian)
            {
                BitCopy(values.Span);
                return;
            }

            for (var i = 0; i < values.Count; i++)
            {
                WriteGuid(in values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write byte string values
    /// </summary>
    /// <param name="values"></param>
    private void WriteByteStringValues(in ArrayOf<ByteString> values)
    {
        if (WriteArrayLength(in values))
        {
            for (var i = 0; i < values.Count; i++)
            {
                WriteByteString(in values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write xml element values
    /// </summary>
    /// <param name="values"></param>
    private void WriteXmlElementValues(in ArrayOf<XmlElement> values)
    {
        if (WriteArrayLength(in values))
        {
            for (var i = 0; i < values.Count; i++)
            {
                WriteXmlElement(in values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write node id values
    /// </summary>
    /// <param name="values"></param>
    private void WriteNodeIdValues(in ArrayOf<NodeId> values)
    {
        if (WriteArrayLength(in values))
        {
            for (var i = 0; i < values.Count; i++)
            {
                WriteNodeId(in values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write expanded node id values
    /// </summary>
    /// <param name="values"></param>
    private void WriteExpandedNodeIdValues(in ArrayOf<ExpandedNodeId> values)
    {
        if (WriteArrayLength(in values))
        {
            for (var i = 0; i < values.Count; i++)
            {
                WriteExpandedNodeId(in values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write status codes
    /// </summary>
    /// <param name="values"></param>
    private void WriteStatusCodeValues(in ArrayOf<StatusCode> values)
    {
        if (WriteArrayLength(in values))
        {
            if (IsLittleEndian)
            {
                BitCopy(values.Span);
                return;
            }

            for (var i = 0; i < values.Count; i++)
            {
                WriteStatusCode(values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write diagnostic info values
    /// </summary>
    /// <param name="values"></param>
    private void WriteDiagnosticInfoValues(in ArrayOf<DiagnosticInfo> values)
    {
        if (WriteArrayLength(in values))
        {
            for (var i = 0; i < values.Count; i++)
            {
                WriteDiagnosticInfo(in values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write qualified names
    /// </summary>
    /// <param name="values"></param>
    private void WriteQualifiedNameValues(in ArrayOf<QualifiedName> values)
    {
        if (WriteArrayLength(in values))
        {
            for (var i = 0; i < values.Count; i++)
            {
                WriteQualifiedName(in values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write localized text values
    /// </summary>
    /// <param name="values"></param>
    private void WriteLocalizedTextValues(in ArrayOf<LocalizedText> values)
    {
        if (WriteArrayLength(in values))
        {
            for (var i = 0; i < values.Count; i++)
            {
                WriteLocalizedText(in values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write variant values
    /// </summary>
    /// <param name="values"></param>
    private void WriteVariantValues(in ArrayOf<Variant> values)
    {
        if (WriteArrayLength(in values))
        {
            for (var i = 0; i < values.Count; i++)
            {
                WriteVariant(in values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write data values
    /// </summary>
    /// <param name="values"></param>
    private void WriteDataValueValues(in ArrayOf<DataValue> values)
    {
        if (WriteArrayLength(in values))
        {
            for (var i = 0; i < values.Count; i++)
            {
                WriteDataValue(in values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write extension object values
    /// </summary>
    /// <param name="values"></param>
    private void WriteExtensionObjectValues(in ArrayOf<ExtensionObject> values)
    {
        if (WriteArrayLength(in values))
        {
            for (var i = 0; i < values.Count; i++)
            {
                WriteExtensionObject(in values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write structures as extension object values
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="values"></param>
    private void WriteStructureAsExtensionObjectValues<T>(in ArrayOf<T> values)
        where T : IStructure<T>
    {
        if (WriteArrayLength(in values))
        {
            for (var i = 0; i < values.Count; i++)
            {
                WriteStructureAsExtensionObject(in values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write structure values
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="values"></param>
    private void WriteStructureValues<T>(in ArrayOf<T> values)
        where T : IStructure<T>
    {
        if (WriteArrayLength(in values))
        {
            for (var i = 0; i < values.Count; i++)
            {
                WriteStructure(in values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Write enumeration values
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="values"></param>
    private void WriteEnumerationValues<T>(in ArrayOf<T> values)
        where T : IEnumeration<T>
    {
        if (WriteArrayLength(in values))
        {
            for (var i = 0; i < values.Count; i++)
            {
                WriteEnumeration(in values.Span[i]);
            }
        }
    }

    /// <summary>
    /// Get size of strings
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    public int GetSizeOfStringValues(in ArrayOf<string> values)
    {
        var size = 4;
        for (var i = 0; i < values.Count; i++)
        {
            size += GetSizeOfString(values.Span[i]);
        }
        return size;
    }

    /// <summary>
    /// Get size of strings
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    public int GetSizeOfStringValues(in ArrayOf<Utf8String> values)
    {
        var size = 4;
        for (var i = 0; i < values.Count; i++)
        {
            size += GetSizeOfString(in values.Span[i]);
        }
        return size;
    }

    /// <summary>
    /// Get size of byte string
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    public int GetSizeOfByteStringValues(in ArrayOf<ByteString> values)
    {
        var size = 4;
        for (var i = 0; i < values.Count; i++)
        {
            size += GetSizeOfByteString(in values.Span[i]);
        }
        return size;
    }

    /// <summary>
    /// Get size of xml elements
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    public int GetSizeOfXmlElementValues(in ArrayOf<XmlElement> values)
    {
        var size = 4;
        for (var i = 0; i < values.Count; i++)
        {
            size += GetSizeOfXmlElement(in values.Span[i]);
        }
        return size;
    }

    /// <summary>
    /// Get size of node ids
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    public int GetSizeOfNodeIdValues(in ArrayOf<NodeId> values)
    {
        var size = 4;
        for (var i = 0; i < values.Count; i++)
        {
            size += GetSizeOfNodeId(in values.Span[i]);
        }
        return size;
    }

    /// <summary>
    /// Get size of expanded node ids
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    public int GetSizeOfExpandedNodeIdValues(in ArrayOf<ExpandedNodeId> values)
    {
        var size = 4;
        for (var i = 0; i < values.Count; i++)
        {
            size += GetSizeOfExpandedNodeId(in values.Span[i]);
        }
        return size;
    }

    /// <summary>
    /// Get size of diagnostic info values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    public int GetSizeOfDiagnosticInfoValues(in ArrayOf<DiagnosticInfo> values)
    {
        var size = 4;
        for (var i = 0; i < values.Count; i++)
        {
            size += GetSizeOfDiagnosticInfo(values.Span[i]);
        }
        return size;
    }

    /// <summary>
    /// Get size of qualified name values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    public int GetSizeOfQualifiedNameValues(in ArrayOf<QualifiedName> values)
    {
        var size = 4;
        for (var i = 0; i < values.Count; i++)
        {
            size += GetSizeOfQualifiedName(in values.Span[i]);
        }
        return size;
    }

    /// <summary>
    /// Get size of localized text objects
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    public int GetSizeOfLocalizedTextValues(in ArrayOf<LocalizedText> values)
    {
        var size = 4;
        for (var i = 0; i < values.Count; i++)
        {
            size += GetSizeOfLocalizedText(in values.Span[i]);
        }
        return size;
    }

    /// <summary>
    /// Get size of variant values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    public int GetSizeOfVariantValues(in ArrayOf<Variant> values)
    {
        var size = 4;
        for (var i = 0; i < values.Count; i++)
        {
            size += GetSizeOfVariant(in values.Span[i]);
        }
        return size;
    }

    /// <summary>
    /// Get size of data values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    public int GetSizeOfDataValueValues(in ArrayOf<DataValue> values)
    {
        var size = 4;
        for (var i = 0; i < values.Count; i++)
        {
            size += GetSizeOfDataValue(in values.Span[i]);
        }
        return size;
    }

    /// <summary>
    /// Get size of the given extension objects
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    public int GetSizeOfExtensionObjectValues(in ArrayOf<ExtensionObject> values)
    {
        var size = 4;
        for (var i = 0; i < values.Count; i++)
        {
            size += GetSizeOfExtensionObject(in values.Span[i]);
        }
        return size;
    }

    /// <summary>
    /// Get size of the given extension object encoded structures
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    public int GetSizeOfStructureAsExtensionObjectValues<T>(in ArrayOf<T> values)
        where T : IStructure<T>
    {
        var size = 4;
        for (var i = 0; i < values.Count; i++)
        {
            size += GetSizeOfStructureAsExtensionObject(in values.Span[i]);
        }
        return size;
    }

    /// <summary>
    /// Get size of the given structures
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="values"></param>
    /// <returns></returns>
    public int GetSizeOfStructureValues<T>(in ArrayOf<T> values)
        where T : IStructure<T>
    {
        var size = 4;
        for (var i = 0; i < values.Count; i++)
        {
            size += GetSizeOfStructure(in values.Span[i]);
        }
        return size;
    }

    /// <summary>
    /// Blit the buffer
    /// </summary>
    /// <param name="value"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void BitCopy<T>(ReadOnlySpan<T> value) where T : unmanaged
    {
        var bytes = value.Length * Unsafe.SizeOf<T>();
        Ensure(bytes, out var output);
        MemoryMarshal.AsBytes<T>(value).CopyTo(output);
        Advance(bytes);
    }

    /// <summary>
    /// Blit the buffer
    /// </summary>
    /// <param name="value"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void BitCopy(ReadOnlySpan<byte> value)
    {
        Ensure(value.Length, out var output);
        value.CopyTo(output);
        Advance(value.Length);
    }

    /// <summary>
    /// Ensure enough memory is available and if not grow
    /// </summary>
    /// <param name="requiredSize"></param>
    /// <param name="output"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Ensure(int requiredSize, out Span<byte> output)
    {
        if (_memory.Length - BytesPending >= requiredSize)
        {
            output = _memory.Span.Slice(BytesPending, requiredSize);
        }
        else
        {
            Grow(requiredSize);
            output = _memory.Span;
        }
    }

    /// <summary>
    /// Write array length
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="values"></param>
    /// <returns>false if array is empty</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool WriteArrayLength<T>(in ArrayOf<T> values)
    {
        if (values.Count > _context.Limits.MaxArrayLength)
        {
            LastError = _context.SetLastError(StatusCodes.BadEncodingLimitsExceeded,
                $"MaxArrayLength {_context.Limits.MaxArrayLength} < {values.Count}");
            WriteInt32(-1);
            return false;
        }

        WriteInt32(values.Count);
        return values.Count > 0;
    }

    /// <summary>
    /// Check byte string
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsByteStringLengthEncodingLimitsExceeded(int length)
    {
        if (length > _context.Limits.MaxByteStringLength)
        {
            LastError = _context.SetLastError(StatusCodes.BadEncodingLimitsExceeded,
                $"{length} bytes > max (= {_context.Limits.MaxByteStringLength})");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Check string
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsStringLengthEncodingLimitsExceeded(int length)
    {
        if (length > _context.Limits.MaxStringLength)
        {
            LastError = _context.SetLastError(StatusCodes.BadEncodingLimitsExceeded,
                $"{length} characters > max (= {_context.Limits.MaxStringLength})");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Check nesting
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsNestingLevelsExceeded()
    {
        // check the nesting level for avoiding a stack overflow.
        if (_nestingLevel >= _context.Limits.MaxEncodingNestingLevels)
        {
            LastError = _context.SetLastError(StatusCodes.BadEncodingLimitsExceeded,
                $"Maximum nesting level of {_context.Limits.MaxEncodingNestingLevels} exceeded.");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Advance the buffer
    /// </summary>
    /// <param name="written"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Advance(int written) => BytesPending += written;

    /// <summary>
    /// Grow the buffer by requesting more data.
    /// </summary>
    /// <param name="requiredSize"></param>
    /// <exception cref="InvalidOperationException"></exception>
    private void Grow(int requiredSize)
    {
        Debug.Assert(requiredSize > 0);
        if (BytesPending > 0)
        {
            _writer.Advance(BytesPending);
            BytesCommitted += BytesPending;
            BytesPending = 0;
        }
        _memory = _writer.GetMemory(requiredSize);
        Debug.Assert(_memory.Length >= requiredSize);
    }

    private readonly TWriter _writer;
    private readonly ICodecContext _context;
    private Memory<byte> _memory;
    private uint _nestingLevel;
}

/// <summary>
/// Efficient binary writer on span memory for transport layer.
/// </summary>
internal ref struct SpanWriter
{
    /// <summary>
    /// Get or set the position
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// Create span writer
    /// </summary>
    /// <param name="span"></param>
    public SpanWriter(Span<byte> span)
    {
        Position = 0;
        _span = span;
    }

    /// <summary>
    /// Writes a boolean to the stream.
    /// </summary>
    public void WriteBoolean(bool value)
        => WriteByte((byte)(value ? 0x1 : 0x0));

    /// <summary>
    /// Writes a sbyte to the stream.
    /// </summary>
    public void WriteSByte(sbyte value)
        => WriteByte((byte)value);

    /// <summary>
    /// Writes a byte to the stream.
    /// </summary>
    public void WriteByte(byte value)
    {
        EnsureRoomToWrite(1);
        _span[Position++] = value;
    }

    /// <summary>
    /// Writes a short to the stream.
    /// </summary>
    public void WriteInt16(short value)
    {
        EnsureRoomToWrite(2);
        BinaryPrimitives.WriteInt16LittleEndian(
            _span.Slice(Position, 2), value);
        Position += 2;
    }

    /// <summary>
    /// Writes a ushort to the stream.
    /// </summary>
    public void WriteUInt16(ushort value)
    {
        EnsureRoomToWrite(2);
        BinaryPrimitives.WriteUInt16LittleEndian(
            _span.Slice(Position, 2), value);
        Position += 2;
    }

    /// <summary>
    /// Writes an int to the stream.
    /// </summary>
    public void WriteInt32(int value)
    {
        EnsureRoomToWrite(4);
        BinaryPrimitives.WriteInt32LittleEndian(
            _span.Slice(Position, 4), value);
        Position += 4;
    }

    /// <summary>
    /// Writes a uint to the stream.
    /// </summary>
    public void WriteUInt32(uint value)
    {
        EnsureRoomToWrite(4);
        BinaryPrimitives.WriteUInt32LittleEndian(
            _span.Slice(Position, 4), value);
        Position += 4;
    }

    /// <summary>
    /// Writes a string to the stream.
    /// </summary>
    public void WriteString(Utf8String value)
    {
        if (value.IsNullOrEmpty)
        {
            WriteInt32(-1);
            return;
        }

        var length = value.ByteCount;
        WriteInt32(length);
        EnsureRoomToWrite(length);
        value.CopyTo(_span.Slice(Position, length));
        Position += length;
    }

    /// <summary>
    /// Writes a byte string to the stream.
    /// </summary>
    public void WriteByteString(ByteString value)
    {
        if (value.IsEmpty)
        {
            WriteInt32(-1);
            return;
        }

        WriteInt32(value.Length);
        EnsureRoomToWrite(value.Length);
        value.CopyTo(_span.Slice(Position, value.Length));
        Position += value.Length;
    }

    /// <summary>
    /// Test whether there is room in the span or otherwise throw
    /// </summary>
    /// <param name="bytes"></param>
    /// <exception cref="ServiceResultException"></exception>
    private readonly void EnsureRoomToWrite(int bytes)
    {
        if (Position + bytes > _span.Length)
        {
            ServiceResultException.Throw(StatusCodes.BadTcpMessageTooLarge);
        }
    }
    private readonly Span<byte> _span;
}
