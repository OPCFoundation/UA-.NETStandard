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
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

/// <summary>
/// Reads sequence using sequence reader
/// </summary>
public ref struct BinaryReader : IReader
{
    /// <summary>
    /// Get last error
    /// </summary>
    public ServiceResult LastError { get; private set; }

    /// <summary>
    /// Get or set the position
    /// </summary>
    public long Position
    {
        readonly get => _reader.Consumed;
        set
        {
            if (value > _reader.Length)
            {
                _reader.AdvanceToEnd();
            }
            else if (value > _reader.Consumed)
            {
                _reader.Advance(value - _reader.Consumed);
            }
            else if (value <= 0)
            {
                _reader.Rewind(_reader.Consumed);
            }
            else if (value < _reader.Consumed)
            {
                _reader.Rewind(_reader.Consumed - value);
            }
        }
    }

    /// <summary>
    /// To allow testing big endianess - DO NOT USE
    /// </summary>
    internal bool IsLittleEndian
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        readonly get => _reader.IsLittleEndian;
        set => _reader.IsLittleEndian = value;
    }

    /// <summary>
    /// Creates a decoder that reads from a memory buffer.
    /// </summary>
    public BinaryReader(in ReadOnlySequence<byte> sequence,
        ICodecContext context)
    {
        _nestingLevel = 0;
        _context = context;
        _reader = new SequenceReader(sequence);
        LastError = ServiceResult.Good;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadBoolean(ReadOnlySpan<byte> field, out bool value)
        => TryReadBoolean(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadBooleanValues(ReadOnlySpan<byte> field,
        out ArrayOf<bool> values)
        => TryReadBooleanValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadByte(ReadOnlySpan<byte> field, out byte value)
        => TryReadByte(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadByteValues(ReadOnlySpan<byte> field,
        out ArrayOf<byte> values)
        => TryReadByteValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadByteString(ReadOnlySpan<byte> field, out ByteString value)
        => TryReadByteString(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadByteStringValues(ReadOnlySpan<byte> field,
        out ArrayOf<ByteString> values)
        => TryReadByteStringValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadDataValue(ReadOnlySpan<byte> field, out DataValue value)
        => TryReadDataValue(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadDataValueValues(ReadOnlySpan<byte> field,
        out ArrayOf<DataValue> values)
        => TryReadDataValueValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadDateTime(ReadOnlySpan<byte> field, out DateTimeUtc value)
        => TryReadDateTime(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadDateTimeValues(ReadOnlySpan<byte> field,
        out ArrayOf<DateTimeUtc> values)
        => TryReadDateTimeValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadDiagnosticInfo(ReadOnlySpan<byte> field,
        out DiagnosticInfo value)
        => TryReadDiagnosticInfo(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadDiagnosticInfoValues(ReadOnlySpan<byte> field,
        out ArrayOf<DiagnosticInfo> values)
        => TryReadDiagnosticInfoValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadDouble(ReadOnlySpan<byte> field, out double value)
        => TryReadDouble(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadDoubleValues(ReadOnlySpan<byte> field,
        out ArrayOf<double> values) => TryReadDoubleValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadEnumeration<T>(ReadOnlySpan<byte> field, out T value)
        where T : IEnumeration<T>
        => TryReadEnumeration(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadEnumerationValues<T>(ReadOnlySpan<byte> field,
        out ArrayOf<T> values) where T : IEnumeration<T>
        => TryReadEnumerationValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadExpandedNodeId(ReadOnlySpan<byte> field,
        out ExpandedNodeId value)
        => TryReadExpandedNodeId(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadExpandedNodeIdValues(ReadOnlySpan<byte> field,
        out ArrayOf<ExpandedNodeId> values)
        => TryReadExpandedNodeIdValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadExtensionObject(ReadOnlySpan<byte> field,
        out ExtensionObject value)
        => TryReadExtensionObject(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadExtensionObjectValues(ReadOnlySpan<byte> field,
        out ArrayOf<ExtensionObject> values)
        => TryReadExtensionObjectValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadFloat(ReadOnlySpan<byte> field, out float value)
        => TryReadFloat(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadFloatValues(ReadOnlySpan<byte> field,
        out ArrayOf<float> values)
        => TryReadFloatValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadGuid(ReadOnlySpan<byte> field, out Guid value)
        => TryReadGuid(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadGuidValues(ReadOnlySpan<byte> field,
        out ArrayOf<Guid> values)
        => TryReadGuidValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadInt16(ReadOnlySpan<byte> field, out short value)
        => TryReadInt16(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadInt16Values(ReadOnlySpan<byte> field,
        out ArrayOf<short> values)
        => TryReadInt16Values(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadInt32(ReadOnlySpan<byte> field, out int value)
        => TryReadInt32(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadInt32Values(ReadOnlySpan<byte> field,
        out ArrayOf<int> values)
        => TryReadInt32Values(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadInt64(ReadOnlySpan<byte> field, out long value)
        => TryReadInt64(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadInt64Values(ReadOnlySpan<byte> field,
        out ArrayOf<long> values)
        => TryReadInt64Values(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadLocalizedText(ReadOnlySpan<byte> field,
        out LocalizedText value)
        => TryReadLocalizedText(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadLocalizedTextValues(ReadOnlySpan<byte> field,
        out ArrayOf<LocalizedText> values)
        => TryReadLocalizedTextValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadNodeId(ReadOnlySpan<byte> field, out NodeId value)
        => TryReadNodeId(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadNodeIdValues(ReadOnlySpan<byte> field,
        out ArrayOf<NodeId> values)
        => TryReadNodeIdValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadQualifiedName(ReadOnlySpan<byte> field,
        out QualifiedName value)
        => TryReadQualifiedName(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadQualifiedNameValues(ReadOnlySpan<byte> field,
        out ArrayOf<QualifiedName> values)
        => TryReadQualifiedNameValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadSByte(ReadOnlySpan<byte> field, out sbyte value)
        => TryReadSByte(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadSByteValues(ReadOnlySpan<byte> field,
        out ArrayOf<sbyte> values)
        => TryReadSByteValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadStatusCode(ReadOnlySpan<byte> field, out StatusCode value)
        => TryReadStatusCode(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadStatusCodeValues(ReadOnlySpan<byte> field,
        out ArrayOf<StatusCode> values)
        => TryReadStatusCodeValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadString(ReadOnlySpan<byte> field, out Utf8String value)
        => TryReadString(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadStringValues(ReadOnlySpan<byte> field,
        out ArrayOf<Utf8String> values)
        => TryReadStringValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadStructure<T>(ReadOnlySpan<byte> field, out T value)
        where T : IStructure<T>
        => TryReadStructure(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadStructureAsExtensionObject<T>(ReadOnlySpan<byte> field,
        out T value) where T : IStructure<T>
        => TryReadStructureAsExtensionObject(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadStructureAsExtensionObjectValues<T>(ReadOnlySpan<byte> field,
        out ArrayOf<T> values) where T : IStructure<T>
        => TryReadStructureAsExtensionObjectValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadStructureValues<T>(ReadOnlySpan<byte> field,
        out ArrayOf<T> values) where T : IStructure<T>
        => TryReadStructureValues(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadUInt16(ReadOnlySpan<byte> field, out ushort value)
        => TryReadUInt16(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadUInt16Values(ReadOnlySpan<byte> field,
        out ArrayOf<ushort> values)
        => TryReadUInt16Values(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadUInt32(ReadOnlySpan<byte> field, out uint value)
        => TryReadUInt32(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadUInt32Values(ReadOnlySpan<byte> field,
        out ArrayOf<uint> values)
        => TryReadUInt32Values(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadUInt64(ReadOnlySpan<byte> field, out ulong value)
        => TryReadUInt64(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadUInt64Values(ReadOnlySpan<byte> field,
        out ArrayOf<ulong> values)
        => TryReadUInt64Values(out values);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadVariant(ReadOnlySpan<byte> field, out Variant value)
        => TryReadVariant(out value);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReadVariantValues(ReadOnlySpan<byte> field,
        out ArrayOf<Variant> values)
        => TryReadVariantValues(out values);

    /// <summary>
    /// Read using decoder
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="decoder"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    internal bool TryReadStructureWithDecoder<T>(IDecoder decoder, out T value)
        where T : IStructure<T>
    {
        // Part 6 5.2.6: Structures do not have a null value. If an encoder is
        // written in a programming language that allows structures to have null
        // values, then the encoder shall create a new instance with DefaultValues
        // for all fields and serialize that. Encoders shall not generate an
        // encoding error in this situation.

        CheckNestingLimitsExceeded();
        _nestingLevel++;
        try
        {
            if (!decoder.TryRead(ref this, out value, out var failedToConvert))
            {
                return false;
            }
            if (failedToConvert)
            {
                LastError = _context.SetLastError(StatusCodes.BadDecodingError,
                    $"Failed to convert decoded type to {typeof(T).Name}.");
            }
            return true;
        }
        finally
        {
            _nestingLevel--;
        }
    }

    /// <summary>
    /// Read message. Messages are Structures encoded as sequence
    /// of bytes prefixed by the NodeId of for the OPC UA Binary
    /// DataTypeEncoding defined for the Message.
    /// </summary>
    /// <param name="maxMessageSize"></param>
    /// <param name="message"></param>
    /// <returns></returns>
    public bool TryReadMessage<T>(uint maxMessageSize,
        [NotNullWhen(true)] out T? message) where T : Message, IStructure<T>
    {
        if (!TryReadNodeId(out var typeId))
        {
            message = default;
            return false;
        }
        if (!T.TypeInfo.TryGet(typeId, out var typeInfo) ||
            !typeInfo.TryGetDecoder(typeId, out var decoder))
        {
            LastError = _context.SetLastError(StatusCodes.BadDecodingError,
                $"Type info for type {typeof(T).Name} not found");
            message = default;
            return false;
        }
        return TryReadMessage(decoder, maxMessageSize, out message);
    }

    /// <summary>
    /// Read message using the decoder. The type identifier has already been
    /// read and is part of the binary decoder provided.
    /// </summary>
    /// <param name="decoder"></param>
    /// <param name="maxMessageSize"></param>
    /// <param name="message"></param>
    /// <returns></returns>
    public bool TryReadMessage<T>(IDecoder decoder, uint maxMessageSize,
        [NotNullWhen(true)] out T? message) where T : Message, IStructure<T>
    {
        Debug.Assert(_nestingLevel == 0, "Expected to be called at the root");
        if (maxMessageSize != 0 && _reader.Remaining > maxMessageSize)
        {
            LastError = _context.SetLastError(StatusCodes.BadEncodingLimitsExceeded,
                $"{_reader.Remaining} bytes > max = {maxMessageSize}");
            message = default;
            return false;
        }
        if (!decoder.TryRead(ref this, out T o, out var failedToConvert))
        {
            message = default;
            return false;
        }
        Debug.Assert(_nestingLevel == 0, "Expected to be back at root");
        if (failedToConvert)
        {
            LastError = _context.SetLastError(StatusCodes.BadDecodingError,
                $"Failed to convert decoded type to {typeof(T).Name}.");
        }
        message = o;
        return true;
    }

    /// <summary>
    /// Check nesting levels exceeded
    /// </summary>
    private void CheckNestingLimitsExceeded()
    {
        // check the nesting level for avoiding a stack overflow.
        if (_nestingLevel >= _context.Limits.MaxEncodingNestingLevels)
        {
            LastError = _context.SetLastError(StatusCodes.BadEncodingLimitsExceeded,
                $"Maximum nesting level {_context.Limits.MaxEncodingNestingLevels} exceeded.");
            throw new ServiceResultException(LastError);
        }
    }

    /// <summary>
    /// Try get dimensions
    /// </summary>
    /// <param name="encodingByte"></param>
    /// <param name="dimensions"></param>
    /// <returns></returns>
    private bool TryReadArrayDimensions(byte encodingByte, out int[]? dimensions)
    {
        if ((encodingByte & 0x40) == 0)
        {
            dimensions = null;
            return true;
        }
        // multi-dimensional
        if (TryReadInt32Values(out var dims))
        {
            switch (dims.Count)
            {
                case 0:
                    LastError = _context.SetLastError(StatusCodes.BadDecodingError,
                        "ArrayDimensions not specified when ArrayDimensions " +
                        "decoder bit was set in Variant object's decoder byte.");
                    break;
                case 1:
                    // https://reference.opcfoundation.org/Core/Part6/v105/docs/5.2.5
                    LastError = _context.SetLastError(StatusCodes.BadDecodingError,
                        "The number of dimensions shall be at least 2.");
                    break;
                default:
                    dimensions = dims.Memory.ToArray();
                    return true;
            }
        }
        dimensions = default;
        return false;
    }

    /// <summary>
    /// Read array length
    /// </summary>
    /// <returns></returns>
    internal bool TryReadArrayLength(out int length)
    {
        if (!TryReadInt32(out length))
        {
            return false;
        }
        if (length == -1)
        {
            length = 0;
            return true;
        }
        if (length < 0 || length > _context.Limits.MaxArrayLength)
        {
            LastError = _context.SetLastError(StatusCodes.BadEncodingLimitsExceeded,
                $"Array length {(uint)length} > max = {_context.Limits.MaxArrayLength}");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Try read boolean
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadBoolean(out bool value)
    {
        if (TryReadByte(out var b))
        {
            value = b != 0;
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Read boolean values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadBooleanValues(out ArrayOf<bool> values)
    {
#if !SLOW_BOOL
        if (TryReadArrayLength(out var length))
        {
            var result = new bool[length];
            if (TryRead(MemoryMarshal.AsBytes(result.AsSpan())))
            {
                values = result;
                return true;
            }
        }
#else
        if (TryReadArrayLength(out var length) &&
            length <= Remaining)
        {
            var result = new bool[length];
            for (var index = 0; index < length; index++)
            {
                var success = TryReadByte(out var b);
                System.Diagnostics.Debug.Assert(success);
                result[index] = b != 0;
            }
            values = result;
            return true;
        }
#endif
        values = [];
        return false;
    }

    /// <summary>
    /// Try read byte
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryReadByte(out byte value)
        => _reader.TryRead(out value);

    /// <summary>
    /// Read byte string
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadByteString(out ByteString value)
    {
        if (!TryReadByteStringLength(out var length))
        {
            value = ByteString.Empty;
            return false;
        }
        if (length <= 0)
        {
            value = ByteString.Empty;
            return true;
        }
        if (!_reader.TryReadExact(length, out var sequence))
        {
            value = ByteString.Empty;
            return false;
        }
        value = new ByteString(sequence);
        return true;
    }

    /// <summary>
    /// Try read string length
    /// </summary>
    /// <param name="length"></param>
    /// <returns></returns>
    internal bool TryReadStringLength(out int length)
    {
        if (!TryReadInt32(out length))
        {
            return false;
        }

        if (length == -1)
        {
            length = 0;
            return true;
        }
        else if (length < 0 || length > _context.Limits.MaxStringLength)
        {
            LastError = _context.SetLastError(StatusCodes.BadEncodingLimitsExceeded,
                $"String length {(uint)length} > max = {_context.Limits.MaxStringLength}");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Read byte string length
    /// </summary>
    /// <returns></returns>
    internal bool TryReadByteStringLength(out int length)
    {
        if (!TryReadInt32(out length))
        {
            return false;
        }

        if (length == -1)
        {
            length = 0;
            return true;
        }
        if (length < 0 || length > _context.Limits.MaxByteStringLength)
        {
            LastError = _context.SetLastError(StatusCodes.BadEncodingLimitsExceeded,
                $"{(uint)length} bytes > max = {_context.Limits.MaxByteStringLength}");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Try read array of byte string
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadByteStringValues(out ArrayOf<ByteString> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new ByteString[length];
            for (var index = 0; index < length; index++)
            {
                if (!TryReadByteString(out var b))
                {
                    values = [];
                    return false;
                }
                result[index] = b;
            }
            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Try read byte values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadByteValues(out ArrayOf<byte> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new byte[length];
            if (TryRead(result.AsSpan()))
            {
                values = result;
                return true;
            }
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Try read data value
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadDataValue(out DataValue value)
    {
        while (TryReadByte(out var encodingByte))
        {
            if (encodingByte == 0)
            {
                value = DataValue.Null;
                return true;
            }

            Variant variant;
            StatusCode code;
            DateTimeUtc sourceTimestamp;
            ushort sourcePico;
            DateTimeUtc serverTimestamp;
            ushort serverPico;

            // read the fields of the DataValue structure.
            if ((encodingByte & 0x01) == 0)
            {
                variant = Variant.Null;
            }
            else if (!TryReadVariant(out variant))
            {
                break;
            }
            if ((encodingByte & 0x02) == 0)
            {
                code = StatusCodes.Good;
            }
            else if (!TryReadStatusCode(out code))
            {
                break;
            }
            if ((encodingByte & 0x04) == 0)
            {
                sourceTimestamp = DateTimeUtc.MinValue;
            }
            else if (!TryReadDateTime(out sourceTimestamp))
            {
                break;
            }
            if ((encodingByte & 0x10) == 0)
            {
                sourcePico = 0;
            }
            else if (!TryReadUInt16(out sourcePico))
            {
                break;
            }
            if ((encodingByte & 0x08) == 0)
            {
                serverTimestamp = DateTimeUtc.MinValue;
            }
            else if (!TryReadDateTime(out serverTimestamp))
            {
                break;
            }
            if ((encodingByte & 0x20) == 0)
            {
                serverPico = 0;
            }
            else if (!TryReadUInt16(out serverPico))
            {
                break;
            }
            value = new DataValue(variant, code, sourceTimestamp,
                sourcePico, serverTimestamp, serverPico);
            return true;
        }
        value = DataValue.Null;
        return false;
    }

    /// <summary>
    /// Try read array of data value values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadDataValueValues(out ArrayOf<DataValue> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new DataValue[length];
            for (var index = 0; index < length; index++)
            {
                if (!TryReadDataValue(out var b))
                {
                    values = [];
                    return false;
                }
                result[index] = b;
            }
            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Read date time
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadDateTime(out DateTimeUtc value)
    {
        if (TryReadInt64(out var ticks))
        {
            value = ticks;
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Try read array of timestamps
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadDateTimeValues(out ArrayOf<DateTimeUtc> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new DateTimeUtc[length];
            if (IsLittleEndian)
            {
                var span = MemoryMarshal.AsBytes(result.AsSpan());
                if (TryRead(span))
                {
                    values = result;
                    return true;
                }
            }
            for (var index = 0; index < length; index++)
            {
                if (!TryReadDateTime(out var b))
                {
                    values = [];
                    return false;
                }
                result[index] = b;
            }
            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Read diagnostic
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadDiagnosticInfo(out DiagnosticInfo value)
    {
        CheckNestingLimitsExceeded();
        _nestingLevel++;
        try
        {
            while (TryReadByte(out var encodingByte))
            {
                if (encodingByte == 0)
                {
                    value = DiagnosticInfo.Null;
                    return true;
                }

                value = new DiagnosticInfo();

                // read the fields of the diagnostic info structure.
                if ((encodingByte & 0x1) != 0)
                {
                    if (!TryReadInt32(out var symbolicId))
                    {
                        break;
                    }
                    value.SymbolicId = symbolicId;
                }
                if ((encodingByte & 0x2) != 0)
                {
                    if (!TryReadInt32(out var nsIndex))
                    {
                        break;
                    }
                    value.NamespaceUri = nsIndex;
                }
                if ((encodingByte & 0x8) != 0)
                {
                    if (!TryReadInt32(out var locale))
                    {
                        break;
                    }
                    value.Locale = locale;
                }
                if ((encodingByte & 0x4) != 0)
                {
                    if (!TryReadInt32(out var localizedText))
                    {
                        break;
                    }
                    value.LocalizedText = localizedText;
                }
                if ((encodingByte & 0x10) != 0)
                {
                    if (!TryReadString(out var additionalInfo))
                    {
                        break;
                    }
                    value.AdditionalInfo = additionalInfo;
                }
                if ((encodingByte & 0x20) != 0)
                {
                    if (!TryReadStatusCode(out var statusCode))
                    {
                        break;
                    }
                    value.InnerStatusCode = statusCode;
                }
                if ((encodingByte & 0x40) != 0)
                {
                    if (!TryReadDiagnosticInfo(out var inner))
                    {
                        break;
                    }
                    value.InnerDiagnosticInfo = inner;
                }
                return true;
            }
            value = DiagnosticInfo.Null;
            return false;
        }
        finally
        {
            _nestingLevel--;
        }
    }

    /// <summary>
    /// Try read array of diagnostic infos
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadDiagnosticInfoValues(out ArrayOf<DiagnosticInfo> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new DiagnosticInfo[length];
            for (var index = 0; index < length; index++)
            {
                if (!TryReadDiagnosticInfo(out var b))
                {
                    values = [];
                    return false;
                }
                result[index] = b;
            }
            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Try read double
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadDouble(out double value)
    {
        if (TryReadInt64(out var b))
        {
            value = BitConverter.Int64BitsToDouble(b);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Try read array of double values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadDoubleValues(out ArrayOf<double> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new double[length];
            if (IsLittleEndian)
            {
                var span = MemoryMarshal.AsBytes(result.AsSpan());
                if (!TryRead(span))
                {
                    values = [];
                    return false;
                }
            }
            else
            {
                for (var index = 0; index < length; index++)
                {
                    if (!TryReadDouble(out var b))
                    {
                        values = [];
                        return false;
                    }
                    result[index] = b;
                }
            }

            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Try read enumerated
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadEnumeration<T>(out T value) where T : IEnumeration<T>
    {
        var success = TryReadInt32(out var enumValue);
        value = T.From(enumValue);
        return success;
    }

    /// <summary>
    /// Try read enumerated values
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadEnumerationValues<T>(out ArrayOf<T> values) where T : IEnumeration<T>
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new T[length];
#if FALSE
            if (IsLittleEndian)
            {
                var span = MemoryMarshal.AsBytes(result.AsSpan());
                if (!TryReadInto(span))
                {
                    values = [];
                    return false;
                }
            }
            else
#endif
            for (var index = 0; index < length; index++)
            {
                if (!TryReadEnumeration<T>(out var b))
                {
                    values = [];
                    return false;
                }
                result[index] = b;
            }
            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Reads the expanded node id information
    /// </summary>
    /// <param name="encodingByte"></param>
    /// <param name="namespaceUri"></param>
    /// <param name="serverIndex"></param>
    /// <returns></returns>
    private bool TryReadExpandedInfo(byte encodingByte, out Utf8String namespaceUri,
        out uint serverIndex)
    {
        namespaceUri = Utf8String.Empty;
        serverIndex = 0u;
        if ((encodingByte & 0x80) != 0 && !TryReadString(out namespaceUri))
        {
            return false;
        }
        if ((encodingByte & 0x40) != 0 && !TryReadUInt32(out serverIndex))
        {
            return false;
        }
        return true;
    }

    /// <inheritdoc/>
    private bool TryReadExpandedNodeId(out ExpandedNodeId value)
    {
        if (!TryReadByte(out var encodingByte))
        {
            value = ExpandedNodeId.Null;
            return false;
        }
        switch (encodingByte & 0x3F)
        {
            case 0x0:
                if (TryReadByte(out var byteId))
                {
                    if ((encodingByte & 0xc0) == 0)
                    {
                        value = new ExpandedNodeId(byteId, 0);
                        return true;
                    }
                    if (TryReadExpandedInfo(encodingByte, out var namespaceUri, out var serverIndex))
                    {
                        value = new ExpandedNodeId(byteId, 0, namespaceUri, serverIndex);
                        return true;
                    }
                }
                break;
            case 0x1:
                if (TryReadByte(out var nsIndex) &&
                    TryReadUInt16(out var shortId))
                {
                    if ((encodingByte & 0xc0) == 0)
                    {
                        value = new ExpandedNodeId(shortId, nsIndex);
                        return true;
                    }
                    if (TryReadExpandedInfo(encodingByte, out var namespaceUri, out var serverIndex))
                    {
                        value = new ExpandedNodeId(shortId, nsIndex, namespaceUri, serverIndex);
                        return true;
                    }
                }
                break;
            case 0x2:
                if (TryReadUInt16(out var nsIndex2) &&
                    TryReadUInt32(out var id))
                {
                    if ((encodingByte & 0xc0) == 0)
                    {
                        value = new ExpandedNodeId(id, nsIndex2);
                        return true;
                    }
                    if (TryReadExpandedInfo(encodingByte, out var namespaceUri, out var serverIndex))
                    {
                        value = new ExpandedNodeId(id, nsIndex2, namespaceUri, serverIndex);
                        return true;
                    }
                }
                break;
            case 0x3:
                if (TryReadUInt16(out nsIndex2) &&
                    TryReadString(out var stringId))
                {
                    if ((encodingByte & 0xc0) == 0)
                    {
                        value = new ExpandedNodeId(stringId, nsIndex2);
                        return true;
                    }
                    if (TryReadExpandedInfo(encodingByte, out var namespaceUri, out var serverIndex))
                    {
                        value = new ExpandedNodeId(stringId, nsIndex2, namespaceUri, serverIndex);
                        return true;
                    }
                }
                break;
            case 0x4:
                if (TryReadUInt16(out nsIndex2) &&
                    TryReadGuid(out var guidId))
                {
                    if ((encodingByte & 0xc0) == 0)
                    {
                        value = new ExpandedNodeId(guidId, nsIndex2);
                        return true;
                    }
                    if (TryReadExpandedInfo(encodingByte, out var namespaceUri, out var serverIndex))
                    {
                        value = new ExpandedNodeId(guidId, nsIndex2, namespaceUri, serverIndex);
                        return true;
                    }
                }
                break;
            case 0x5:
                if (TryReadUInt16(out nsIndex2) &&
                    TryReadByteString(out var bytes))
                {
                    if ((encodingByte & 0xc0) == 0)
                    {
                        value = new ExpandedNodeId(bytes, nsIndex2);
                        return true;
                    }
                    if (TryReadExpandedInfo(encodingByte, out var namespaceUri, out var serverIndex))
                    {
                        value = new ExpandedNodeId(bytes, nsIndex2, namespaceUri, serverIndex);
                        return true;
                    }
                }
                break;
            default:
                LastError = _context.SetLastError(StatusCodes.BadDecodingError,
                    $"Invald decoder byte ({encodingByte}) for NodeId.");
                break;
        }
        value = ExpandedNodeId.Null;
        return false;
    }

    /// <summary>
    /// Try read array of expanded node ids
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadExpandedNodeIdValues(out ArrayOf<ExpandedNodeId> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new ExpandedNodeId[length];
            for (var index = 0; index < length; index++)
            {
                if (!TryReadExpandedNodeId(out var b))
                {
                    values = [];
                    return false;
                }
                result[index] = b;
            }
            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Try read extension object
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadExtensionObject(out ExtensionObject value)
    {
        if (!TryReadNodeId(out var typeId) ||
            !TryReadByte(out var encodingType))
        {
            value = ExtensionObject.Null;
            return false;
        }

        CheckNestingLimitsExceeded();
        _nestingLevel++;
        try
        {
            switch (encodingType)
            {
                case 1: // bytestring
                    // Read length
                    if (!TryReadByteStringLength(out var length) ||
                        !_reader.TryReadExact(length, out var sequence))
                    {
                        value = ExtensionObject.Null;
                        return false;
                    }
                    value = ExtensionObject.From(in typeId, new ByteString(sequence));
                    return true;
                case 2: // xml
                    if (!TryReadXmlElement(out var xmlElement))
                    {
                        value = ExtensionObject.Null;
                        return false;
                    }
                    value = ExtensionObject.From(in typeId, in xmlElement);
                    return true;
                case 3: // json
                    if (TryReadUtf16String(out var jsonString))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(jsonString);
                            var jsonElement = doc.RootElement.Clone();
                            value = ExtensionObject.From(in typeId, in jsonElement);
                            return true;
                        }
                        catch (JsonException)
                        {
                            value = ExtensionObject.Null;
                            return true;
                        }
                    }
                    value = ExtensionObject.Null;
                    return false;
                default:
                    // Empty body
                    value = ExtensionObject.Null;
                    return true;
            }
        }
        finally
        {
            _nestingLevel--;
        }
    }

    /// <summary>
    /// Read extension objects
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadExtensionObjectValues(out ArrayOf<ExtensionObject> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new ExtensionObject[length];
            for (var index = 0; index < length; index++)
            {
                if (!TryReadExtensionObject(out var b))
                {
                    values = [];
                    return false;
                }
                result[index] = b;
            }
            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Try read float
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadFloat(out float value)
    {
        if (TryReadInt32(out var b))
        {
            value = BitConverter.Int32BitsToSingle(b);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Try read array of float values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadFloatValues(out ArrayOf<float> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new float[length];
            if (IsLittleEndian)
            {
                var span = MemoryMarshal.AsBytes(result.AsSpan());
                if (!TryRead(span))
                {
                    values = [];
                    return false;
                }
            }
            else
            {
                for (var index = 0; index < length; index++)
                {
                    if (!TryReadFloat(out var b))
                    {
                        values = [];
                        return false;
                    }
                    result[index] = b;
                }
            }

            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Read guid
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadGuid(out Guid value)
    {
        scoped Span<byte> temp = stackalloc byte[16];
        if (TryRead(temp))
        {
            value = new Guid(temp);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Try read array of guids
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadGuidValues(out ArrayOf<Guid> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new Guid[length];
            if (IsLittleEndian)
            {
                var span = MemoryMarshal.AsBytes(result.AsSpan());
                if (!TryRead(span))
                {
                    values = [];
                    return false;
                }
            }
            else
            {
                for (var index = 0; index < length; index++)
                {
                    if (!TryReadGuid(out var b))
                    {
                        values = [];
                        return false;
                    }
                    result[index] = b;
                }
            }

            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Try read int16
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryReadInt16(out short value)
        => _reader.TryReadLittleEndian(out value);

    /// <summary>
    /// Try read array of shorts
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadInt16Values(out ArrayOf<short> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new short[length];
            if (IsLittleEndian)
            {
                var span = MemoryMarshal.AsBytes(result.AsSpan());
                if (!TryRead(span))
                {
                    values = [];
                    return false;
                }
            }
            else
            {
                for (var index = 0; index < length; index++)
                {
                    if (!TryReadInt16(out var b))
                    {
                        values = [];
                        return false;
                    }
                    result[index] = b;
                }
            }

            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Try read integer
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryReadInt32(out int value)
        => _reader.TryReadLittleEndian(out value);

    /// <summary>
    /// Try read array of ints
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadInt32Values(out ArrayOf<int> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new int[length];
            if (IsLittleEndian)
            {
                var span = MemoryMarshal.AsBytes(result.AsSpan());
                if (!TryRead(span))
                {
                    values = [];
                    return false;
                }
            }
            else
            {
                for (var index = 0; index < length; index++)
                {
                    if (!TryReadInt32(out var b))
                    {
                        values = [];
                        return false;
                    }
                    result[index] = b;
                }
            }

            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Try read long
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryReadInt64(out long value)
        => _reader.TryReadLittleEndian(out value);

    /// <summary>
    /// Try read array of longs
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadInt64Values(out ArrayOf<long> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new long[length];
            if (IsLittleEndian)
            {
                var span = MemoryMarshal.AsBytes(result.AsSpan());
                if (!TryRead(span))
                {
                    values = [];
                    return false;
                }
            }
            else
            {
                for (var index = 0; index < length; index++)
                {
                    if (!TryReadInt64(out var b))
                    {
                        values = [];
                        return false;
                    }
                    result[index] = b;
                }
            }

            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Read json element
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    internal bool TryReadJsonElement(out JsonElement value)
    {
        if (!TryReadUtf16String(out var jsonString))
        {
            value = default;
            return false;
        }
        try
        {
            using var doc = JsonDocument.Parse(jsonString);
            value = doc.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            value = default;
            return true;
        }
    }

    /// <summary>
    /// Read localized text
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadLocalizedText(out LocalizedText value)
    {
        while (TryReadByte(out var encodingByte))
        {
            if (encodingByte == 0)
            {
                value = LocalizedText.Null;
                return true;
            }

            Utf8String locale;
            if ((encodingByte & 0x1) == 0)
            {
                locale = Utf8String.Empty;
            }
            else if (!TryReadString(out locale))
            {
                break;
            }
            Utf8String text;
            if ((encodingByte & 0x2) == 0)
            {
                text = Utf8String.Empty;
            }
            else if (!TryReadString(out text))
            {
                break;
            }
            value = LocalizedText.From(text, locale);
            return true;
        }
        value = LocalizedText.Null;
        return false;
    }

    /// <summary>
    /// Try read array of localized text values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadLocalizedTextValues(out ArrayOf<LocalizedText> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new LocalizedText[length];
            for (var index = 0; index < length; index++)
            {
                if (!TryReadLocalizedText(out var b))
                {
                    values = [];
                    return false;
                }
                result[index] = b;
            }
            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Try read node id
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadNodeId(out NodeId value)
    {
        if (!TryReadByte(out var encodingByte))
        {
            value = NodeId.Null;
            return false;
        }
        switch (encodingByte & 0x3f)
        {
            case 0x0:
                if (TryReadByte(out var byteId))
                {
                    value = new NodeId(byteId, 0);
                    return true;
                }
                break;
            case 0x1:
                if (TryReadByte(out var nsIndex) &&
                    TryReadUInt16(out var shortId))
                {
                    value = new NodeId(shortId, nsIndex);
                    return true;
                }
                break;
            case 0x2:
                if (TryReadUInt16(out var nsIndex2) &&
                    TryReadUInt32(out var id))
                {
                    value = new NodeId(id, nsIndex2);
                    return true;
                }
                break;
            case 0x3:
                if (TryReadUInt16(out nsIndex2) &&
                    TryReadString(out var stringId))
                {
                    value = new NodeId(stringId, nsIndex2);
                    return true;
                }
                break;
            case 0x4:
                if (TryReadUInt16(out nsIndex2) &&
                    TryReadGuid(out var guidId))
                {
                    value = new NodeId(guidId, nsIndex2);
                    return true;
                }
                break;
            case 0x5:
                if (TryReadUInt16(out nsIndex2) &&
                    TryReadByteString(out var bytes))
                {
                    value = new NodeId(bytes, nsIndex2);
                    return true;
                }
                break;
            default:
                LastError = _context.SetLastError(StatusCodes.BadDecodingError,
                    $"Invald decoder byte ({encodingByte}) for NodeId.");
                break;
        }
        value = NodeId.Null;
        return false;
    }

    /// <summary>
    /// Try read array of node ids
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadNodeIdValues(out ArrayOf<NodeId> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new NodeId[length];
            for (var index = 0; index < length; index++)
            {
                if (!TryReadNodeId(out var b))
                {
                    values = [];
                    return false;
                }
                result[index] = b;
            }
            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Read qualified name
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadQualifiedName(out QualifiedName value)
    {
        if (TryReadUInt16(out var nsIndex) &&
            TryReadString(out var name))
        {
            value = QualifiedName.From(name, nsIndex);
            return true;
        }
        value = QualifiedName.Null;
        return false;
    }

    /// <summary>
    /// Try read array of qualified name values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadQualifiedNameValues(out ArrayOf<QualifiedName> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new QualifiedName[length];
            for (var index = 0; index < length; index++)
            {
                if (!TryReadQualifiedName(out var b))
                {
                    values = [];
                    return false;
                }
                result[index] = b;
            }
            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Try read signed byte
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadSByte(out sbyte value)
    {
        if (TryReadByte(out var b))
        {
            value = (sbyte)b;
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Try read short values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadSByteValues(out ArrayOf<sbyte> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new sbyte[length];
            if (TryRead(MemoryMarshal.AsBytes(result.AsSpan())))
            {
                values = result;
                return true;
            }
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Read status code
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadStatusCode(out StatusCode value)
    {
        if (TryReadUInt32(out var code))
        {
            value = code;
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Try read array of status codes
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadStatusCodeValues(out ArrayOf<StatusCode> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new StatusCode[length];
            if (IsLittleEndian)
            {
                var span = MemoryMarshal.AsBytes(result.AsSpan());
                if (!TryRead(span))
                {
                    values = [];
                    return false;
                }
            }
            else
            {
                for (var index = 0; index < length; index++)
                {
                    if (!TryReadStatusCode(out var b))
                    {
                        values = [];
                        return false;
                    }
                    result[index] = b;
                }
            }

            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Try read Utf8 string
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadString([NotNullWhen(true)] out Utf8String value)
    {
        if (!TryReadStringLength(out var length))
        {
            value = default;
            return false;
        }
        if (length <= 0)
        {
            value = Utf8String.Empty;
            return true;
        }
        if (!_reader.TryReadExact(length, out var sequence))
        {
            value = default;
            return false;
        }
        value = Utf8String.From(in sequence);
        return true;
    }

    /// <summary>
    /// Try read array of utf8 string values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadStringValues(out ArrayOf<Utf8String> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new Utf8String[length];
            for (var index = 0; index < length; index++)
            {
                if (!TryReadString(out var b))
                {
                    values = [];
                    return false;
                }
                result[index] = b;
            }
            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Read structure
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadStructure<T>(out T value) where T : IStructure<T>
    {
        if (!T.TypeInfo.TryGetDecoder(NodeId.Null, out var decoder))
        {
            LastError = _context.SetLastError(StatusCodes.BadDecodingError,
                $"Could not get decoder for type {typeof(T).Name}");
            value = T.Null;
            return false;
        }
        return TryReadStructureWithDecoder(decoder, out value);
    }

    /// <summary>
    /// Read structure as extension object
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadStructureAsExtensionObject<T>(out T value) where T : IStructure<T>
    {
        if (!TryReadNodeId(out var typeId) ||
            !TryReadByte(out var encodingType))
        {
            value = T.Null;
            return false;
        }

        CheckNestingLimitsExceeded();
        _nestingLevel++;
        try
        {
            switch (encodingType)
            {
                case 1: // binary
                    if (!TryReadByteStringLength(out var length))
                    {
                        value = T.Null;
                        return false;
                    }
                    // save the current position.
                    var start = Position;

                    if (!T.TypeInfo.TryGet(typeId, out var typeInfo) ||
                        !typeInfo.TryGetDecoder(typeId, out var decoder))
                    {
                        LastError = _context.SetLastError(StatusCodes.BadDecodingError,
                            $"Cannot decode unknown type {typeId} extension object.");
                        value = T.Null;
                        return false;
                    }
                    var success = decoder.TryRead(ref this, out value, out _);

                    // skip any unread data.
                    Position = start + length;
                    return success;
                case 2: // xml
                    if (!TryReadString(out var xml) ||
                        !Codec.TryDecodeFromUtf8Xml<T>(xml, _context, out value))
                    {
                        value = T.Null;
                        return false;
                    }
                    return true;
                case 3: // json
                    if (!TryReadString(out var json) ||
                        !Codec.TryDecodeFromUtf8Json<T>(json, _context, out value))
                    {
                        value = T.Null;
                        return false;
                    }
                    return true;
                default: // Empty body
                    value = T.Null;
                    return true;
            }
        }
        finally
        {
            _nestingLevel--;
        }
    }

    /// <summary>
    /// Read structure values with extension object decoder
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadStructureAsExtensionObjectValues<T>(
        out ArrayOf<T> values) where T : IStructure<T>
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new T[length];
            for (var index = 0; index < length; index++)
            {
                if (!TryReadStructureAsExtensionObject<T>(out var b))
                {
                    values = [];
                    return false;
                }
                result[index] = b;
            }
            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Read structure values
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadStructureValues<T>(out ArrayOf<T> values) where T : IStructure<T>
    {
        if (!T.TypeInfo.TryGetDecoder(NodeId.Null, out var decoder))
        {
            LastError = _context.SetLastError(StatusCodes.BadDecodingError,
               $"Could not get decoder for type {typeof(T).Name}");
            values = [];
            return false;
        }
        return TryReadStructureValuesWithDecoder(decoder, out values);
    }

    /// <summary>
    /// Read structure values with decoder
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="decoder"></param>
    /// <param name="values"></param>
    /// <returns></returns>
    internal bool TryReadStructureValuesWithDecoder<T>(IDecoder decoder,
        out ArrayOf<T> values) where T : IStructure<T>
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new T[length];
            for (var index = 0; index < length; index++)
            {
                if (!decoder.TryRead(ref this, out T value, out var failedToConvert))
                {
                    values = [];
                    return false;
                }
                if (failedToConvert)
                {
                    LastError = _context.SetLastError(StatusCodes.BadDecodingError,
                        $"Failed to convert decoded type to {typeof(T).Name}.");
                }
                result[index] = value;
            }
            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Try read unsigned short
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadUInt16(out ushort value)
    {
        if (TryReadInt16(out var b))
        {
            value = (ushort)b;
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Try read array of shorts
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadUInt16Values(out ArrayOf<ushort> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new ushort[length];
            if (IsLittleEndian)
            {
                var span = MemoryMarshal.AsBytes(result.AsSpan());
                if (!TryRead(span))
                {
                    values = [];
                    return false;
                }
            }
            else
            {
                for (var index = 0; index < length; index++)
                {
                    if (!TryReadUInt16(out var b))
                    {
                        values = [];
                        return false;
                    }
                    result[index] = b;
                }
            }
            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Try read unsigned int
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadUInt32(out uint value)
    {
        if (TryReadInt32(out var b))
        {
            value = (uint)b;
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Try read array of unsigned ints
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadUInt32Values(out ArrayOf<uint> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new uint[length];
            if (IsLittleEndian)
            {
                var span = MemoryMarshal.AsBytes(result.AsSpan());
                if (!TryRead(span))
                {
                    values = [];
                    return false;
                }
            }
            else
            {
                for (var index = 0; index < length; index++)
                {
                    if (!TryReadUInt32(out var b))
                    {
                        values = [];
                        return false;
                    }
                    result[index] = b;
                }
            }

            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Try read unsigned long
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadUInt64(out ulong value)
    {
        if (TryReadInt64(out var b))
        {
            value = (ulong)b;
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Try read array of unsigned longs
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadUInt64Values(out ArrayOf<ulong> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new ulong[length];
            if (IsLittleEndian)
            {
                var span = MemoryMarshal.AsBytes(result.AsSpan());
                if (!TryRead(span))
                {
                    values = [];
                    return false;
                }
            }
            else
            {
                for (var index = 0; index < length; index++)
                {
                    if (!TryReadUInt64(out var b))
                    {
                        values = [];
                        return false;
                    }
                    result[index] = b;
                }
            }
            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Try read string
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    internal bool TryReadUtf16String(out string value)
    {
        if (!TryReadString(out var utf8String))
        {
            value = string.Empty;
            return false;
        }
        value = utf8String.ToString();
        return true;
    }

    /// <summary>
    /// Try read array of string values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    internal bool TryReadUtf16StringValues(out ArrayOf<string> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new string[length];
            for (var index = 0; index < length; index++)
            {
                if (!TryReadUtf16String(out var b))
                {
                    values = [];
                    return false;
                }
                result[index] = b;
            }
            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Try read variant
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadVariant(out Variant value)
    {
        CheckNestingLimitsExceeded();
        _nestingLevel++;
        try
        {
            if (!TryReadByte(out var encodingByte))
            {
                value = Variant.Null;
                return false;
            }
            if ((encodingByte & 0x80) != 0)
            {
                int[]? dimensions;
                var builtInType = (BuiltInType)(encodingByte & 0x3f);
                switch (builtInType)
                {
                    case BuiltInType.Boolean:
                        if (!TryReadBooleanValues(out var booleans) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(booleans, dimensions);
                        return true;
                    case BuiltInType.SByte:
                        if (!TryReadSByteValues(out var sbytes) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(sbytes, dimensions);
                        return true;
                    case BuiltInType.Byte:
                        if (!TryReadByteValues(out var byteValues) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(byteValues, dimensions);
                        return true;
                    case BuiltInType.Int16:
                        if (!TryReadInt16Values(out var shortValues) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(shortValues, dimensions);
                        return true;
                    case BuiltInType.UInt16:
                        if (!TryReadUInt16Values(out var unsignedShorts) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(unsignedShorts, dimensions);
                        return true;
                    case BuiltInType.Int32:
                        if (!TryReadInt32Values(out var integerValues) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(integerValues, dimensions);
                        return true;
                    case BuiltInType.UInt32:
                        if (!TryReadUInt32Values(out var unsignedIntegers) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(unsignedIntegers, dimensions);
                        return true;
                    case BuiltInType.Int64:
                        if (!TryReadInt64Values(out var longValues) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(longValues, dimensions);
                        return true;
                    case BuiltInType.UInt64:
                        if (!TryReadUInt64Values(out var unsignedLongs) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(unsignedLongs, dimensions);
                        return true;
                    case BuiltInType.Float:
                        if (!TryReadFloatValues(out var floatValues) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(floatValues, dimensions);
                        return true;
                    case BuiltInType.Double:
                        if (!TryReadDoubleValues(out var doubleValues) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(doubleValues, dimensions);
                        return true;
                    case BuiltInType.String:
                        if (!TryReadStringValues(out var strings) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(strings, dimensions);
                        return true;
                    case BuiltInType.DateTime:
                        if (!TryReadDateTimeValues(out var dateTimes) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(dateTimes, dimensions);
                        return true;
                    case BuiltInType.Guid:
                        if (!TryReadGuidValues(out var guids) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(guids, dimensions);
                        return true;
                    case BuiltInType.ByteString:
                        if (!TryReadByteStringValues(out var byteStrings) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(byteStrings, dimensions);
                        return true;
                    case BuiltInType.XmlElement:
                        if (!TryReadXmlElementValues(out var xmlElements) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(xmlElements, dimensions);
                        return true;
                    case BuiltInType.NodeId:
                        if (!TryReadNodeIdValues(out var nodeIds) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(nodeIds, dimensions);
                        return true;
                    case BuiltInType.ExpandedNodeId:
                        if (!TryReadExpandedNodeIdValues(out var expandedNodeIds) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(expandedNodeIds, dimensions);
                        return true;
                    case BuiltInType.StatusCode:
                        if (!TryReadStatusCodeValues(out var statusCodes) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(statusCodes, dimensions);
                        return true;
                    case BuiltInType.QualifiedName:
                        if (!TryReadQualifiedNameValues(out var qualifiedNames) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(qualifiedNames, dimensions);
                        return true;
                    case BuiltInType.LocalizedText:
                        if (!TryReadLocalizedTextValues(out var localizedTexts) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(localizedTexts, dimensions);
                        return true;
                    case BuiltInType.ExtensionObject:
                        if (!TryReadExtensionObjectValues(out var extensionObjects) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(extensionObjects, dimensions);
                        return true;
                    case BuiltInType.DataValue:
                        if (!TryReadDataValueValues(out var dataValues) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(dataValues, dimensions);
                        return true;
                    case BuiltInType.Variant:
                        if (!TryReadVariantValues(out var variants) ||
                            !TryReadArrayDimensions(encodingByte, out dimensions))
                        {
                            break;
                        }
                        value = new Variant(variants, dimensions);
                        return true;
                    case BuiltInType.DiagnosticInfo:
                        LastError = _context.SetLastError(StatusCodes.BadDecodingError,
                            "Diagnostic info is not allowed in variant object.");
                        value = Variant.Null;
                        return false;
                    default:
                        if ((byte)builtInType <= 31)
                        {
                            goto case BuiltInType.ByteString;
                        }
                        LastError = _context.SetLastError(StatusCodes.BadDecodingError,
                            $"Cannot decode unknown type with BuiltInType: {builtInType}.");
                        value = Variant.Null;
                        return false;
                }
            }
            else
            {
                // Read scalar
                var builtInType = (BuiltInType)encodingByte;
                switch (builtInType)
                {
                    case BuiltInType.Null:
                        value = Variant.Null;
                        return true;
                    case BuiltInType.Boolean:
                        if (!TryReadBoolean(out var boolean))
                        {
                            break;
                        }
                        value = new Variant(boolean);
                        return true;
                    case BuiltInType.SByte:
                        if (!TryReadSByte(out var signedByte))
                        {
                            break;
                        }
                        value = new Variant(signedByte);
                        return true;
                    case BuiltInType.Byte:
                        if (!TryReadByte(out var byteValue))
                        {
                            break;
                        }
                        value = new Variant(byteValue);
                        return true;
                    case BuiltInType.Int16:
                        if (!TryReadInt16(out var shortValue))
                        {
                            break;
                        }
                        value = new Variant(shortValue);
                        return true;
                    case BuiltInType.UInt16:
                        if (!TryReadUInt16(out var unsignedShort))
                        {
                            break;
                        }
                        value = new Variant(unsignedShort);
                        return true;
                    case BuiltInType.Int32:
                        if (!TryReadInt32(out var integerValue))
                        {
                            break;
                        }
                        value = new Variant(integerValue);
                        return true;
                    case BuiltInType.UInt32:
                        if (!TryReadUInt32(out var unsignedInteger))
                        {
                            break;
                        }
                        value = new Variant(unsignedInteger);
                        return true;
                    case BuiltInType.Int64:
                        if (!TryReadInt64(out var longValue))
                        {
                            break;
                        }
                        value = new Variant(longValue);
                        return true;
                    case BuiltInType.UInt64:
                        if (!TryReadUInt64(out var unsignedLong))
                        {
                            break;
                        }
                        value = new Variant(unsignedLong);
                        return true;
                    case BuiltInType.Float:
                        if (!TryReadFloat(out var floatValue))
                        {
                            break;
                        }
                        value = new Variant(floatValue);
                        return true;
                    case BuiltInType.Double:
                        if (!TryReadDouble(out var doubleValue))
                        {
                            break;
                        }
                        value = new Variant(doubleValue);
                        return true;
                    case BuiltInType.String:
                        if (!TryReadString(out var stringValue))
                        {
                            break;
                        }
                        value = new Variant(stringValue);
                        return true;
                    case BuiltInType.DateTime:
                        if (!TryReadDateTime(out var dateTime))
                        {
                            break;
                        }
                        value = new Variant(dateTime);
                        return true;
                    case BuiltInType.Guid:
                        if (!TryReadGuid(out var guid))
                        {
                            break;
                        }
                        value = new Variant(guid);
                        return true;
                    case BuiltInType.ByteString:
                        if (!TryReadByteString(out var byteString))
                        {
                            break;
                        }
                        value = new Variant(byteString);
                        return true;
                    case BuiltInType.XmlElement:
                        if (!TryReadXmlElement(out var xmlElement))
                        {
                            break;
                        }
                        value = new Variant(xmlElement);
                        return true;
                    case BuiltInType.NodeId:
                        if (!TryReadNodeId(out var nodeId))
                        {
                            break;
                        }
                        value = new Variant(nodeId);
                        return true;
                    case BuiltInType.ExpandedNodeId:
                        if (!TryReadExpandedNodeId(out var expandedNodeId))
                        {
                            break;
                        }
                        value = new Variant(expandedNodeId);
                        return true;
                    case BuiltInType.StatusCode:
                        if (!TryReadStatusCode(out var statusCode))
                        {
                            break;
                        }
                        value = new Variant(statusCode);
                        return true;
                    case BuiltInType.QualifiedName:
                        if (!TryReadQualifiedName(out var qualifiedName))
                        {
                            break;
                        }
                        value = new Variant(qualifiedName);
                        return true;
                    case BuiltInType.LocalizedText:
                        if (!TryReadLocalizedText(out var localizedText))
                        {
                            break;
                        }
                        value = new Variant(localizedText);
                        return true;
                    case BuiltInType.ExtensionObject:
                        if (!TryReadExtensionObject(out var extensionObject))
                        {
                            break;
                        }
                        value = new Variant(extensionObject);
                        return true;
                    case BuiltInType.DataValue:
                        if (!TryReadDataValue(out var dataValue))
                        {
                            break;
                        }
                        value = new Variant(dataValue);
                        return true;
                    case BuiltInType.DiagnosticInfo:
                        LastError = _context.SetLastError(StatusCodes.BadDecodingError,
                            "Diagnostic info is not allowed in variant object.");
                        value = Variant.Null;
                        return false;
                    case BuiltInType.Variant:
                        LastError = _context.SetLastError(StatusCodes.BadDecodingError,
                            "Variant is not allowed in variant object.");
                        value = Variant.Null;
                        return false;
                    default:
                        if ((byte)builtInType <= 31)
                        {
                            goto case BuiltInType.ByteString;
                        }
                        LastError = _context.SetLastError(StatusCodes.BadDecodingError,
                            $"Cannot decode unknown type ({encodingByte}) in Variant.");
                        value = Variant.Null;
                        return false;
                }
            }
            value = Variant.Null;
            return false;
        }
        finally
        {
            _nestingLevel--;
        }
    }

    /// <summary>
    /// Try read array of variant values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryReadVariantValues(out ArrayOf<Variant> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new Variant[length];
            for (var index = 0; index < length; index++)
            {
                if (!TryReadVariant(out var b))
                {
                    values = [];
                    return false;
                }
                result[index] = b;
            }
            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Read xml element
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    internal bool TryReadXmlElement(out XmlElement value)
    {
        if (!TryReadString(out var xmlString))
        {
            value = XmlElement.Empty;
            return false;
        }
        value = new XmlElement(xmlString);
        return true;
    }

    /// <summary>
    /// Try read array of xml elements
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    internal bool TryReadXmlElementValues(out ArrayOf<XmlElement> values)
    {
        if (TryReadArrayLength(out var length))
        {
            var result = new XmlElement[length];
            for (var index = 0; index < length; index++)
            {
                if (!TryReadXmlElement(out var b))
                {
                    values = [];
                    return false;
                }
                result[index] = b;
            }
            values = result;
            return true;
        }
        values = [];
        return false;
    }

    /// <summary>
    /// Read into destination
    /// </summary>
    /// <param name="destination"></param>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool TryRead(scoped Span<byte> destination)
    {
        if (!_reader.TryCopyTo(destination))
        {
            return false;
        }
        _reader.Advance(destination.Length);
        return true;
    }

    private readonly ICodecContext _context;
    private int _nestingLevel;
    private SequenceReader _reader;
}

/// <summary>
/// Efficient binary reader on span memory for transport layer.
/// </summary>
internal ref struct SpanReader
{
    /// <summary>
    /// Get or set the position
    /// </summary>
    public int Position { readonly get; set; }

    /// <summary>
    /// Create span writer
    /// </summary>
    /// <param name="span"></param>
    public SpanReader(ReadOnlySpan<byte> span)
    {
        Position = 0;
        _span = span;
    }

    public bool TryReadByte(out byte value)
    {
        if (_span.Length < Position + 1)
        {
            value = default;
            return false;
        }
        value = _span[Position++];
        return true;
    }

    public bool TryReadUInt16(out ushort value)
    {
        if (_span.Length < Position + 2)
        {
            value = default;
            return false;
        }
        var success = BinaryPrimitives.TryReadUInt16LittleEndian(
            _span.Slice(Position, 2), out value);
        Position += 2;
        return success;
    }

    public bool TryReadUInt32(out uint value)
    {
        if (_span.Length < Position + 4)
        {
            value = default;
            return false;
        }
        var success = BinaryPrimitives.TryReadUInt32LittleEndian(
            _span.Slice(Position, 4), out value);
        Position += 4;
        return success;
    }

    private readonly ReadOnlySpan<byte> _span;
}
