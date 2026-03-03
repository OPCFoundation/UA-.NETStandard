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
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

/// <summary>
/// Reads reversable encoding using utf8 json reader. This is a fast
/// path reader which requires properties and values in order of how
/// they are written by JsonWriter and respective Json encodings.
/// For a relaxed but slower consumer use JsonParser class.
/// </summary>
public ref struct JsonReader : IReader
{
    /// <summary>
    /// Get or set the position
    /// </summary>
    public readonly int Position => checked((int)_reader.BytesConsumed);

    /// <summary>
    /// Create reader
    /// </summary>
    /// <param name="sequence"></param>
    /// <param name="context"></param>
    public JsonReader(ReadOnlySequence<byte> sequence, ICodecContext? context = null)
    {
        _context = context ?? new CodecContext();
        _length = sequence.Length;
        _reader = new Utf8JsonReader(sequence, false, new JsonReaderState(new JsonReaderOptions
        {
            // Catch in CheckNestingLevel while reading, this is just fail safe
            MaxDepth = checked((int)_context.Limits.MaxEncodingNestingLevels + 1),
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Skip
        }));
        TryReadNext();
        System.Diagnostics.Debug.Assert(_reader.TokenType != JsonTokenType.None);
    }

    /// <inheritdoc/>
    public bool TryReadBoolean(ReadOnlySpan<byte> field, out bool value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = default;
            return false;
        }
        return TryGetBoolean(out value);
    }

    /// <inheritdoc/>
    public bool TryReadBooleanValues(ReadOnlySpan<byte> field,
        out ArrayOf<bool> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetBooleanValues(out values);
    }

    /// <inheritdoc/>
    public bool TryReadByte(ReadOnlySpan<byte> field, out byte value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = default;
            return false;
        }
        return TryGetByte(out value);
    }

    /// <inheritdoc/>
    public bool TryReadByteString(ReadOnlySpan<byte> field, out ByteString value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = ByteString.Empty;
            return false;
        }
        return TryGetByteString(out value);
    }

    /// <inheritdoc/>
    public bool TryReadByteStringValues(ReadOnlySpan<byte> field,
        out ArrayOf<ByteString> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetByteStringValues(out values);
    }

    /// <inheritdoc/>
    public bool TryReadByteValues(ReadOnlySpan<byte> field,
        out ArrayOf<byte> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetByteValues(out values);
    }

    /// <inheritdoc/>
    public bool TryReadDataValue(ReadOnlySpan<byte> field, out DataValue value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = DataValue.Null;
            return false;
        }
        return TryGetDataValue(out value);
    }

    /// <inheritdoc/>
    public bool TryReadDataValueValues(ReadOnlySpan<byte> field,
        out ArrayOf<DataValue> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetDataValueValues(out values);
    }

    /// <inheritdoc/>
    public bool TryReadDateTime(ReadOnlySpan<byte> field, out DateTimeUtc value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = default;
            return false;
        }
        return TryGetDateTime(out value);
    }

    /// <inheritdoc/>
    public bool TryReadDateTimeValues(ReadOnlySpan<byte> field,
        out ArrayOf<DateTimeUtc> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetDateTimeValues(out values);
    }

    /// <inheritdoc/>
    public bool TryReadDiagnosticInfo(ReadOnlySpan<byte> field, out DiagnosticInfo value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = DiagnosticInfo.Null;
            return false;
        }
        return TryGetDiagnosticInfo(out value);
    }

    /// <inheritdoc/>
    public bool TryReadDiagnosticInfoValues(ReadOnlySpan<byte> field,
        out ArrayOf<DiagnosticInfo> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetDiagnosticInfoValues(out values);
    }

    /// <inheritdoc/>
    public bool TryReadDouble(ReadOnlySpan<byte> field, out double value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = default;
            return false;
        }
        return TryGetDouble(out value);
    }

    /// <inheritdoc/>
    public bool TryReadDoubleValues(ReadOnlySpan<byte> field,
        out ArrayOf<double> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetDoubleValues(out values);
    }

    /// <inheritdoc/>
    public bool TryReadEnumeration<T>(ReadOnlySpan<byte> field, out T value) where T : IEnumeration<T>
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = T.DefaultValue;
            return false;
        }
        return TryGetEnumeration<T>(out value);
    }

    /// <inheritdoc/>
    public bool TryReadEnumerationValues<T>(ReadOnlySpan<byte> field,
        out ArrayOf<T> values) where T : IEnumeration<T>
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetEnumerationValues(out values);
    }

    /// <inheritdoc/>
    public bool TryReadExpandedNodeId(ReadOnlySpan<byte> field, out ExpandedNodeId value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = ExpandedNodeId.Null;
            return false;
        }
        return TryGetExpandedNodeId(out value);
    }

    /// <inheritdoc/>
    public bool TryReadExpandedNodeIdValues(ReadOnlySpan<byte> field,
        out ArrayOf<ExpandedNodeId> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetExpandedNodeIdValues(out values);
    }

    /// <inheritdoc/>
    public bool TryReadExtensionObject(ReadOnlySpan<byte> field, out ExtensionObject value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = ExtensionObject.Null;
            return false;
        }
        return TryGetExtensionObject(out value);
    }

    /// <inheritdoc/>
    public bool TryReadExtensionObjectValues(ReadOnlySpan<byte> field,
        out ArrayOf<ExtensionObject> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetExtensionObjectValues(out values);
    }

    /// <inheritdoc/>
    public bool TryReadFloat(ReadOnlySpan<byte> field, out float value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = default;
            return false;
        }
        return TryGetFloat(out value);
    }

    /// <inheritdoc/>
    public bool TryReadFloatValues(ReadOnlySpan<byte> field,
        out ArrayOf<float> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetFloatValues(out values);
    }

    /// <inheritdoc/>
    public bool TryReadGuid(ReadOnlySpan<byte> field, out Guid value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = Guid.Empty;
            return false;
        }
        return TryGetGuid(out value);
    }

    /// <inheritdoc/>
    public bool TryReadGuidValues(ReadOnlySpan<byte> field,
        out ArrayOf<Guid> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetGuidValues(out values);
    }

    /// <inheritdoc/>
    public bool TryReadInt16(ReadOnlySpan<byte> field, out short value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = default;
            return false;
        }
        return TryGetInt16(out value);
    }

    /// <inheritdoc/>
    public bool TryReadInt16Values(ReadOnlySpan<byte> field,
        out ArrayOf<short> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetInt16Values(out values);
    }

    /// <inheritdoc/>
    public bool TryReadInt32(ReadOnlySpan<byte> field, out int value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = default;
            return false;
        }
        return TryGetInt32(out value);
    }

    /// <inheritdoc/>
    public bool TryReadInt32Values(ReadOnlySpan<byte> field,
        out ArrayOf<int> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetInt32Values(out values);
    }

    /// <inheritdoc/>
    public bool TryReadInt64(ReadOnlySpan<byte> field, out long value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = default;
            return false;
        }
        return TryGetInt64(out value);
    }

    /// <inheritdoc/>
    public bool TryReadInt64Values(ReadOnlySpan<byte> field,
        out ArrayOf<long> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetInt64Values(out values);
    }

    /// <inheritdoc/>
    public bool TryReadLocalizedText(ReadOnlySpan<byte> field, out LocalizedText value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = LocalizedText.Null;
            return false;
        }
        return TryGetLocalizedText(out value);
    }

    /// <inheritdoc/>
    public bool TryReadLocalizedTextValues(ReadOnlySpan<byte> field,
        out ArrayOf<LocalizedText> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetLocalizedTextValues(out values);
    }

    /// <summary>
    /// Read message
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public bool TryReadMessage<T>(out T? value) where T : Message, IStructure<T>
    {
        if (_reader.TokenType != JsonTokenType.StartObject ||
            !TryReadNodeId(JsonProperties.TypeId, out var typeId))
        {
            value = T.Null;
            return false;
        }

        if (!T.TypeInfo.TryGet(typeId, out var typeInfo) ||
            !typeInfo.TryGetDecoder(typeId, out var decoder))
        {
            ServiceResultException.Throw(StatusCodes.BadDecodingError,
                $"Could not get decoder for type {typeof(T).Name}");
        }
        else if (TryReadStructure<T>(decoder, JsonProperties.Body, out var body))
        {
            value = body;
            return true;
        }
        value = T.Null;
        return false;
    }

    /// <inheritdoc/>
    public bool TryReadNodeId(ReadOnlySpan<byte> field, out NodeId value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = NodeId.Null;
            return false;
        }
        return TryGetNodeId(out value);
    }

    /// <inheritdoc/>
    public bool TryReadNodeIdValues(ReadOnlySpan<byte> field,
        out ArrayOf<NodeId> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetNodeIdValues(out values);
    }

    /// <inheritdoc/>
    public bool TryReadQualifiedName(ReadOnlySpan<byte> field, out QualifiedName value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = QualifiedName.Null;
            return false;
        }
        return TryGetQualifiedName(out value);
    }

    /// <inheritdoc/>
    public bool TryReadQualifiedNameValues(ReadOnlySpan<byte> field,
        out ArrayOf<QualifiedName> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetQualifiedNameValues(out values);
    }

    /// <inheritdoc/>
    public bool TryReadSByte(ReadOnlySpan<byte> field, out sbyte value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = default;
            return false;
        }
        return TryGetSByte(out value);
    }

    /// <inheritdoc/>
    public bool TryReadSByteValues(ReadOnlySpan<byte> field,
        out ArrayOf<sbyte> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetSByteValues(out values);
    }

    /// <inheritdoc/>
    public bool TryReadStatusCode(ReadOnlySpan<byte> field, out StatusCode value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = default;
            return false;
        }
        return TryGetStatusCode(out value);
    }

    /// <inheritdoc/>
    public bool TryReadStatusCodeValues(ReadOnlySpan<byte> field,
        out ArrayOf<StatusCode> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetStatusCodeValues(out values);
    }

    /// <inheritdoc/>
    public bool TryReadString(ReadOnlySpan<byte> field, out Utf8String value)
    {
        if (!TryReadUtf16String(field, out var stringValue))
        {
            value = Utf8String.Empty;
            return false;
        }
        value = new Utf8String(stringValue);
        return true;
    }

    /// <inheritdoc/>
    public bool TryReadStringValues(ReadOnlySpan<byte> field,
        out ArrayOf<Utf8String> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetStringValues(out values);
    }

    /// <inheritdoc/>
    public bool TryReadStructure<T>(ReadOnlySpan<byte> field, out T value)
        where T : IStructure<T>
    {
        if (!T.TypeInfo.TryGetDecoder(NodeId.Null, out var decoder))
        {
            ServiceResultException.Throw(StatusCodes.BadDecodingError,
                $"Could not get decoder for type {typeof(T).Name}");
        }
        return TryReadStructure(decoder, field, out value);
    }

    /// <inheritdoc/>
    public bool TryReadStructureAsExtensionObject<T>(ReadOnlySpan<byte> field, out T value)
        where T : IStructure<T>
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = T.Null;
            return false;
        }
        return TryGetStructureAsExtensionObject(out value);
    }

    /// <inheritdoc/>
    public bool TryReadStructureAsExtensionObjectValues<T>(ReadOnlySpan<byte> field,
        out ArrayOf<T> values) where T : IStructure<T>
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetStructureAsExtensionObjectValues<T>(out values);
    }

    /// <inheritdoc/>
    public bool TryReadStructureValues<T>(ReadOnlySpan<byte> field,
        out ArrayOf<T> values) where T : IStructure<T>
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        if (!T.TypeInfo.TryGetDecoder(NodeId.Null, out var decoder))
        {
            ServiceResultException.Throw(StatusCodes.BadDecodingError,
                $"Could not get decoder for type {typeof(T).Name}");
        }
        return TryGetStructureValues(decoder, out values);
    }

    /// <inheritdoc/>
    public bool TryReadUInt16(ReadOnlySpan<byte> field, out ushort value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = default;
            return false;
        }
        return TryGetUInt16(out value);
    }

    /// <inheritdoc/>
    public bool TryReadUInt16Values(ReadOnlySpan<byte> field,
        out ArrayOf<ushort> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetUInt16Values(out values);
    }

    /// <inheritdoc/>
    public bool TryReadUInt32(ReadOnlySpan<byte> field, out uint value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = default;
            return false;
        }
        return TryGetUInt32(out value);
    }

    /// <inheritdoc/>
    public bool TryReadUInt32Values(ReadOnlySpan<byte> field,
        out ArrayOf<uint> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetUInt32Values(out values);
    }

    /// <inheritdoc/>
    public bool TryReadUInt64(ReadOnlySpan<byte> field, out ulong value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = default;
            return false;
        }
        return TryGetUInt64(out value);
    }

    /// <inheritdoc/>
    public bool TryReadUInt64Values(ReadOnlySpan<byte> field,
        out ArrayOf<ulong> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetUInt64Values(out values);
    }

    /// <summary>
    /// Try read string
    /// </summary>
    /// <param name="field"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public bool TryReadUtf16String(ReadOnlySpan<byte> field, out string value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = string.Empty;
            return false;
        }
        return TryGetUtf16String(out value);
    }

    /// <inheritdoc/>
    public bool TryReadUtf16StringValues(ReadOnlySpan<byte> field,
        out ArrayOf<string> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetUtf16StringValues(out values);
    }

    /// <inheritdoc/>
    public bool TryReadVariant(ReadOnlySpan<byte> field, out Variant value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = Variant.Null;
            return false;
        }
        return TryGetVariant(out value);
    }

    /// <inheritdoc/>
    public bool TryReadVariantValues(ReadOnlySpan<byte> field,
        out ArrayOf<Variant> values)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            values = [];
            return false;
        }
        return TryGetVariantValues(out values);
    }

    /// <summary>
    /// Get using decoder
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="decoder"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    /// <exception cref="ServiceResultException"></exception>
    internal bool TryGetStructure<T>(IDecoder decoder, out T value)
        where T : IStructure<T>
    {
        CheckNestingLevel();
        switch (_reader.TokenType)
        {
            case JsonTokenType.StartObject:
                if (!decoder.TryRead(ref this, out value, out var failedToConvert))
                {
                    return false;
                }
                if (failedToConvert)
                {
                    ServiceResultException.Throw(StatusCodes.BadDecodingError,
                        $"Failed to convert decoded type to {typeof(T).Name}.");
                }
                return SkipPastEndOfObject();
            default:
                value = T.Null;
                return SkipValue();
        }
    }

    /// <summary>
    /// Read structure values with decoder
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="decoder"></param>
    /// <param name="values"></param>
    /// <returns></returns>
    /// <exception cref="ServiceResultException"></exception>
    private bool TryGetStructureValues<T>(IDecoder decoder,
        out ArrayOf<T> values) where T : IStructure<T>
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<T>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetStructure<T>(decoder, out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try read xml element
    /// </summary>
    /// <param name="field"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadXmlElement(ReadOnlySpan<byte> field, out XmlElement value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = XmlElement.Empty;
            return false;
        }
        return TryGetXmlElement(out value);
    }

    /// <summary>
    /// Try read json element
    /// </summary>
    /// <param name="field"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadJsonElement(ReadOnlySpan<byte> field, out JsonElement value)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = default;
            return false;
        }
        return TryGetJsonElement(out value);
    }

    /// <summary>
    /// Try get boolean
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetBoolean(out bool value)
    {
        value = _reader.TokenType == JsonTokenType.True;
        return SkipValue();
    }

    /// <summary>
    /// Read boolean values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetBooleanValues(out ArrayOf<bool> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<bool>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetBoolean(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get byte
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetByte(out byte value)
    {
        if (_reader.TokenType == JsonTokenType.Number &&
            _reader.TryGetByte(out value))
        {
            return TryReadNext();
        }
        value = default;
        return SkipValue();
    }

    /// <summary>
    /// Try get guid
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetByteString(out ByteString value)
    {
        if (_reader.TokenType == JsonTokenType.String &&
            _reader.TryGetBytesFromBase64(out var bytes))
        {
            value = bytes;
            return TryReadNext();
        }
        value = ByteString.Empty;
        return SkipValue();
    }

    /// <summary>
    /// Try read array of byte string
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetByteStringValues(out ArrayOf<ByteString> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<ByteString>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetByteString(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result.ToArrayOf();
        return TryReadNext();
    }

    /// <summary>
    /// Try read byte values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetByteValues(out ArrayOf<byte> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<byte>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetByte(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get data value
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetDataValue(out DataValue value)
    {
        if (_reader.TokenType != JsonTokenType.StartObject)
        {
            value = DataValue.Null;
            return SkipValue();
        }
        if (TryReadVariant(JsonProperties.Value, out var variantValue) &&
            TryReadStatusCode(JsonProperties.StatusCode, out var statusCode) &&
            TryReadDateTime(JsonProperties.SourceTimestamp, out var sourceTimestamp) &&
            TryReadUInt16(JsonProperties.SourcePicoseconds, out var sourcePicoseconds) &&
            TryReadDateTime(JsonProperties.ServerTimestamp, out var serverTimestamp) &&
            TryReadUInt16(JsonProperties.ServerPicoseconds, out var serverPicoseconds))
        {
            value = new DataValue(variantValue, statusCode, sourceTimestamp,
                sourcePicoseconds, serverTimestamp, serverPicoseconds);
            return SkipPastEndOfObject();
        }
        value = DataValue.Null;
        return false;
    }

    /// <summary>
    /// Try read array of data value values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetDataValueValues(out ArrayOf<DataValue> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<DataValue>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetDataValue(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get date time
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetDateTime(out DateTimeUtc value)
    {
        if (_reader.TokenType != JsonTokenType.String)
        {
            value = default;
            return SkipValue();
        }
        if (_reader.TryGetDateTime(out var dt))
        {
            value = dt;
        }
        else
        {
            value = default;
        }
        return TryReadNext();
    }

    /// <summary>
    /// Try read array of timestamps
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetDateTimeValues(out ArrayOf<DateTimeUtc> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<DateTimeUtc>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetDateTime(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get diagnostic info
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetDiagnosticInfo(out DiagnosticInfo value)
    {
        CheckNestingLevel();
        if (_reader.TokenType != JsonTokenType.StartObject)
        {
            value = DiagnosticInfo.Null;
            return SkipValue();
        }
        if (TryReadInt32(JsonProperties.SymbolicId, out var symbolicId) &&
            TryReadInt32(JsonProperties.NamespaceUri, out var namespaceUri) &&
            TryReadInt32(JsonProperties.Locale, out var locale) &&
            TryReadInt32(JsonProperties.LocalizedText, out var localizedText) &&
            TryReadString(JsonProperties.AdditionalInfo, out var additionalInfo) &&
            TryReadStatusCode(JsonProperties.InnerStatusCode, out var innerStatusCode) &&
            TryReadDiagnosticInfo(JsonProperties.InnerDiagnosticInfo, out var inner))
        {
            value = new DiagnosticInfo
            {
                SymbolicId = symbolicId,
                NamespaceUri = namespaceUri,
                Locale = locale,
                LocalizedText = localizedText,
                AdditionalInfo = additionalInfo,
                InnerStatusCode = innerStatusCode,
                InnerDiagnosticInfo = inner
            };
            return SkipPastEndOfObject();
        }
        value = DiagnosticInfo.Null;
        return false;
    }

    /// <summary>
    /// Try read array of diagnostic infos
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetDiagnosticInfoValues(out ArrayOf<DiagnosticInfo> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<DiagnosticInfo>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetDiagnosticInfo(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get double
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    /// <exception cref="ServiceResultException"></exception>
    private bool TryGetDouble(out double value)
    {
        switch (_reader.TokenType)
        {
            case JsonTokenType.Number:
                var success = _reader.TryGetDouble(out value);
                Debug.Assert(success, "Should be able to always read double from number");
                return TryReadNext();
            case JsonTokenType.String:
                // As per 5.4.2.4, handle special floating point numbers
                if (!TryGetUtf16String(out var stringEncoded))
                {
                    value = default;
                    return false;
                }
                if (double.TryParse(stringEncoded, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out value))
                {
                    return true;
                }
                break;
            default:
                value = default;
                return SkipValue();
        }
        ServiceResultException.Throw(StatusCodes.BadDecodingError,
            $"Expected float or string special float value, but got {_reader.TokenType}.");
        value = default;
        return false;
    }

    /// <summary>
    /// Try read array of double values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetDoubleValues(out ArrayOf<double> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<double>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetDouble(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get enumerated
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="value"></param>
    private bool TryGetEnumeration<T>(out T value) where T : IEnumeration<T>
    {
        if (_reader.TokenType == JsonTokenType.Number &&
            _reader.TryGetInt32(out var intValue))
        {
            value = T.From(intValue);
            return TryReadNext();
        }
        value = T.DefaultValue;
        return SkipValue();
    }

    /// <summary>
    /// Try read enumerated values
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetEnumerationValues<T>(out ArrayOf<T> values)
        where T : IEnumeration<T>
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<T>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetEnumeration<T>(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get expanded node id
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetExpandedNodeId(out ExpandedNodeId value)
    {
        if (_reader.TokenType == JsonTokenType.String)
        {
            if (!TryGetUtf16String(out var stringValue))
            {
                value = ExpandedNodeId.Null;
                return false;
            }

            if (!ExpandedNodeId.TryParse(stringValue, out value, _context.Namespaces))
            {
                value = ExpandedNodeId.Null;
            }
            return true;
        }

        if (_reader.TokenType != JsonTokenType.StartObject)
        {
            value = ExpandedNodeId.Null;
            return SkipValue();
        }

        if (TryReadInt32(JsonProperties.IdType, out var idType))
        {
            ushort ns;
            Utf8String uri;
            uint serverUri;
            switch (idType)
            {
                case 0: // number
                    if (TryReadUInt32(JsonProperties.Id, out var numericId) &&
                        TryReadNamespace(JsonProperties.Namespace, out ns, out uri) &&
                        TryReadServerIndex(JsonProperties.ServerUri, out serverUri))
                    {
                        value = new ExpandedNodeId(numericId, ns, uri, serverUri);
                        return SkipPastEndOfObject();
                    }
                    break;
                case 1: // string
                    if (TryReadString(JsonProperties.Id, out var stringId) &&
                        TryReadNamespace(JsonProperties.Namespace, out ns, out uri) &&
                        TryReadServerIndex(JsonProperties.ServerUri, out serverUri))
                    {
                        value = new ExpandedNodeId(stringId, ns, uri, serverUri);
                        return SkipPastEndOfObject();
                    }
                    break;
                case 2: // guid
                    if (TryReadGuid(JsonProperties.Id, out var guidId) &&
                        TryReadNamespace(JsonProperties.Namespace, out ns, out uri) &&
                        TryReadServerIndex(JsonProperties.ServerUri, out serverUri))
                    {
                        value = new ExpandedNodeId(guidId, ns, uri, serverUri);
                        return SkipPastEndOfObject();
                    }
                    break;
                case 3: // bytes
                    if (TryReadByteString(JsonProperties.Id, out var binaryId) &&
                        TryReadNamespace(JsonProperties.Namespace, out ns, out uri) &&
                        TryReadServerIndex(JsonProperties.ServerUri, out serverUri))
                    {
                        value = new ExpandedNodeId(binaryId, ns, uri, serverUri);
                        return SkipPastEndOfObject();
                    }
                    break;
            }
        }
        value = ExpandedNodeId.Null;
        return false;
    }

    /// <summary>
    /// Try read array of expanded node ids
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetExpandedNodeIdValues(out ArrayOf<ExpandedNodeId> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<ExpandedNodeId>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetExpandedNodeId(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get Extension Object
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetExtensionObject(out ExtensionObject value)
    {
        if (_reader.TokenType != JsonTokenType.StartObject)
        {
            value = ExtensionObject.Null;
            return SkipValue();
        }

        if (TryReadNodeId(JsonProperties.TypeId, out var typeId) &&
            TryReadByte(JsonProperties.Encoding, out var encodingType) &&
            !typeId.IsNull)
        {
            switch (encodingType)
            {
                case 1: // binary
                    if (!TryReadByteString(JsonProperties.Body, out var bytes))
                    {
                        break;
                    }
                    value = ExtensionObject.From(in typeId, in bytes);
                    return SkipPastEndOfObject();
                case 2: // xml
                    if (!TryReadXmlElement(JsonProperties.Body, out var xml))
                    {
                        break;
                    }
                    value = ExtensionObject.From(in typeId, in xml);
                    return SkipPastEndOfObject();
                case 0: // none
                case 3: // json
                    if (!TryReadJsonElement(JsonProperties.Body, out var jsonElement))
                    {
                        break;
                    }
                    value = ExtensionObject.From(in typeId, in jsonElement);
                    return SkipPastEndOfObject();
                default:
                    ServiceResultException.Throw(StatusCodes.BadDecodingError,
                        $"Encountered unknown encoding type {encodingType}.");
                    break;
            }
        }
        value = ExtensionObject.Null;
        return false;
    }

    /// <summary>
    /// Read extension objects
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetExtensionObjectValues(out ArrayOf<ExtensionObject> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<ExtensionObject>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetExtensionObject(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Get float value
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    /// <exception cref="ServiceResultException"></exception>
    private bool TryGetFloat(out float value)
    {
        switch (_reader.TokenType)
        {
            case JsonTokenType.Number:
                var success = _reader.TryGetSingle(out value);
                Debug.Assert(success, "Should not fail to parse number to float");
                return TryReadNext();
            case JsonTokenType.String:
                // As per 5.4.2.4, handle special floating point numbers
                if (!TryGetUtf16String(out var stringEncoded))
                {
                    value = default;
                    return false;
                }
                if (float.TryParse(stringEncoded, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out value))
                {
                    return true;
                }
                break;
            default:
                value = default;
                return SkipValue();
        }
        ServiceResultException.Throw(StatusCodes.BadDecodingError,
            $"Expected float or string special float value, but got {_reader.TokenType}.");
        value = default;
        return false;
    }

    /// <summary>
    /// Try read array of float values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetFloatValues(out ArrayOf<float> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<float>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetFloat(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get guid
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetGuid(out Guid value)
    {
        if (_reader.TokenType != JsonTokenType.String)
        {
            value = Guid.Empty;
            return SkipValue();
        }
        if (!TryGetUtf16String(out var guid))
        {
            value = default;
            return false;
        }
        if (string.IsNullOrEmpty(guid))
        {
            value = Guid.Empty;
            return true;
        }
        if (Guid.TryParse(guid, out value))
        {
            return true;
        }
        ServiceResultException.Throw(StatusCodes.BadDecodingError,
            $"Failed to parse guid value {guid}.");
        value = default;
        return false;
    }

    /// <summary>
    /// Try read array of guids
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetGuidValues(out ArrayOf<Guid> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<Guid>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetGuid(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get signed short
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetInt16(out short value)
    {
        if (_reader.TokenType == JsonTokenType.Number &&
            _reader.TryGetInt16(out value))
        {
            return TryReadNext();
        }
        value = default;
        return SkipValue();
    }

    /// <summary>
    /// Try read array of shorts
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetInt16Values(out ArrayOf<short> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<short>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetInt16(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get signed integer
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetInt32(out int value)
    {
        if (_reader.TokenType == JsonTokenType.Number &&
            _reader.TryGetInt32(out value))
        {
            return TryReadNext();
        }
        value = default;
        return SkipValue();
    }

    /// <summary>
    /// Try read array of ints
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetInt32Values(out ArrayOf<int> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<int>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetInt32(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get signed long
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    /// <exception cref="ServiceResultException"></exception>
    private bool TryGetInt64(out long value)
    {
        switch (_reader.TokenType)
        {
            case JsonTokenType.Number:
                if (_reader.TryGetInt64(out value))
                {
                    return TryReadNext();
                }
                return TryReadNext();
            case JsonTokenType.String:
                // As per 5.4.2.3, formatted as a decimal number encoded as a JSON string
                if (!TryGetUtf16String(out var stringEncoded))
                {
                    value = default;
                    return false;
                }
                if (long.TryParse(stringEncoded, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out value))
                {
                    return true;
                }
                break;
            default:
                value = default;
                return SkipValue();
        }
        ServiceResultException.Throw(StatusCodes.BadDecodingError,
            $"Expected long or string encoded long, but got {_reader.TokenType}.");
        value = default;
        return false;
    }

    /// <summary>
    /// Try read array of longs
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetInt64Values(out ArrayOf<long> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<long>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetInt64(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get json element
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetJsonElement(out JsonElement value)
    {
        var reader = _reader;
        try
        {
            value = JsonElement.ParseValue(ref _reader);
            return TryReadNext();
        }
        catch (JsonException)
        {
            value = default;
            _reader = reader;
            return SkipValue();
        }
    }

    /// <summary>
    /// Try get localized text
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetLocalizedText(out LocalizedText value)
    {
        if (_reader.TokenType != JsonTokenType.StartObject)
        {
            value = LocalizedText.Null;
            return SkipValue();
        }
        if (!TryReadString(JsonProperties.Locale, out var locale) ||
            !TryReadString(JsonProperties.Text, out var text))
        {
            value = LocalizedText.Null;
            return false;
        }
        value = LocalizedText.From(text, locale);
        return SkipPastEndOfObject();
    }

    /// <summary>
    /// Try read array of localized text values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetLocalizedTextValues(out ArrayOf<LocalizedText> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<LocalizedText>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetLocalizedText(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try Get node id
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetNodeId(out NodeId value)
    {
        if (_reader.TokenType == JsonTokenType.String)
        {
            if (!TryGetUtf16String(out var stringValue))
            {
                value = NodeId.Null;
                return false;
            }

            if (!NodeId.TryParse(stringValue, out value, _context.Namespaces))
            {
                value = NodeId.Null;
            }
            return true;
        }

        if (_reader.TokenType != JsonTokenType.StartObject)
        {
            value = NodeId.Null;
            return SkipValue();
        }

        if (TryReadInt32(JsonProperties.IdType, out var idType))
        {
            ushort ns;
            switch (idType)
            {
                case 0: // number
                    if (TryReadUInt32(JsonProperties.Id, out var numericId) &&
                        TryReadUInt16(JsonProperties.Namespace, out ns))
                    {
                        value = new NodeId(numericId, ns);
                        return SkipPastEndOfObject();
                    }
                    break;
                case 1: // string
                    if (TryReadString(JsonProperties.Id, out var stringId) &&
                        TryReadUInt16(JsonProperties.Namespace, out ns))
                    {
                        value = new NodeId(stringId, ns);
                        return SkipPastEndOfObject();
                    }
                    break;
                case 2: // guid
                    if (TryReadGuid(JsonProperties.Id, out var guidId) &&
                        TryReadUInt16(JsonProperties.Namespace, out ns))
                    {
                        value = new NodeId(guidId, ns);
                        return SkipPastEndOfObject();
                    }
                    break;
                case 3: // bytes
                    if (TryReadByteString(JsonProperties.Id, out var binaryId) &&
                        TryReadUInt16(JsonProperties.Namespace, out ns))
                    {
                        value = new NodeId(binaryId, ns);
                        return SkipPastEndOfObject();
                    }
                    break;
            }
        }
        value = NodeId.Null;
        return false;
    }

    /// <summary>
    /// Try read array of node ids
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetNodeIdValues(out ArrayOf<NodeId> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<NodeId>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetNodeId(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get qualified name
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetQualifiedName(out QualifiedName value)
    {
        if (_reader.TokenType != JsonTokenType.StartObject)
        {
            value = QualifiedName.Null;
            return SkipValue();
        }

        if (TryReadString(JsonProperties.Name, out var name) &&
            TryReadUInt16(JsonProperties.Uri, out var uri))
        {
            value = QualifiedName.From(name, uri);
            return SkipPastEndOfObject();
        }
        value = QualifiedName.Null;
        return false;
    }

    /// <summary>
    /// Try read array of qualified name values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetQualifiedNameValues(out ArrayOf<QualifiedName> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<QualifiedName>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetQualifiedName(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get signed byte
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetSByte(out sbyte value)
    {
        if (_reader.TokenType == JsonTokenType.Number &&
            _reader.TryGetSByte(out value))
        {
            return TryReadNext();
        }
        value = default;
        return SkipValue();
    }

    /// <summary>
    /// Try read short values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetSByteValues(out ArrayOf<sbyte> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<sbyte>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetSByte(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Get status code
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetStatusCode(out StatusCode value)
    {
        if (_reader.TokenType == JsonTokenType.Number &&
            TryGetUInt32(out var code))
        {
            value = code;
            return true;
        }
        value = default;
        return SkipValue();
    }

    /// <summary>
    /// Try read array of status codes
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetStatusCodeValues(out ArrayOf<StatusCode> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<StatusCode>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetStatusCode(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Get utf-8 string
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    /// <exception cref="ServiceResultException"></exception>
    private bool TryGetString(out Utf8String value)
    {
        if (!TryGetUtf16String(out var stringValue))
        {
            value = Utf8String.Empty;
            return false;
        }
        value = new Utf8String(stringValue);
        return true;
    }

    /// <summary>
    /// Try read array of utf-8 string values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetStringValues(out ArrayOf<Utf8String> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<Utf8String>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetString(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Read structure from root
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="value"></param>
    /// <returns></returns>
    /// <exception cref="ServiceResultException"></exception>
    internal bool TryGetStructure<T>(out T value) where T : IStructure<T>
    {
        Debug.Assert(_reader.CurrentDepth == 0);
        if (!T.TypeInfo.TryGetDecoder(NodeId.Null, out var decoder))
        {
            ServiceResultException.Throw(StatusCodes.BadDecodingError,
                $"Could not get decoder for type {typeof(T).Name}");
        }
        var eof = !TryGetStructure(decoder, out value);
        Debug.Assert(eof);
        Debug.Assert(IsEndOfReader());
        return _reader.CurrentDepth == 0;
    }

    /// <summary>
    /// Try get structure packed as Extension Object
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetStructureAsExtensionObject<T>(out T value) where T : IStructure<T>
    {
        if (_reader.TokenType != JsonTokenType.StartObject)
        {
            // Create null value
            value = T.Null;
            return SkipValue();
        }
        if (TryReadNodeId(JsonProperties.TypeId, out var typeId) &&
            TryReadByte(JsonProperties.Encoding, out var encodingType) &&
            !typeId.IsNull)
        {
            switch (encodingType)
            {
                case 1: // binary
                    if (!TryReadByteString(JsonProperties.Body, out var bytes) ||
                        !Codec.TryDecodeFromUaBinary(bytes, _context, out value))
                    {
                        break;
                    }
                    return SkipPastEndOfObject();
                case 2: // xml
                    if (!TryReadString(JsonProperties.Body, out var xml) ||
                        !Codec.TryDecodeFromUtf8Xml(xml, _context, out value))
                    {
                        break;
                    }
                    return SkipPastEndOfObject();
                case 0: // none
                case 3: // json
                    if (!T.TypeInfo.TryGet(typeId, out var typeInfo) ||
                        !typeInfo.TryGetDecoder(typeId, out var decoder))
                    {
                        // Fail here
                        ServiceResultException.Throw(StatusCodes.BadDecodingError,
                            $"Could not decode object of type {typeId}.");
                        break;
                    }
                    if (!TryReadStructure(decoder, JsonProperties.Body, out value))
                    {
                        break;
                    }
                    return SkipPastEndOfObject();
                default:
                    ServiceResultException.Throw(StatusCodes.BadDecodingError,
                        $"Encountered unknown encoding type {encodingType}.");
                    break;
            }
        }
        value = T.Null;
        return false;
    }

    /// <summary>
    /// Get structure as extension value
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetStructureAsExtensionObjectValues<T>(out ArrayOf<T> values) where T : IStructure<T>
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<T>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetStructureAsExtensionObject<T>(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get unsigned short
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetUInt16(out ushort value)
    {
        if (_reader.TokenType == JsonTokenType.Number &&
            _reader.TryGetUInt16(out value))
        {
            return TryReadNext();
        }
        value = default;
        return SkipValue();
    }

    /// <summary>
    /// Try read array of shorts
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetUInt16Values(out ArrayOf<ushort> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<ushort>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetUInt16(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get signed integer
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetUInt32(out uint value)
    {
        if (_reader.TokenType == JsonTokenType.Number &&
            _reader.TryGetUInt32(out value))
        {
            return TryReadNext();
        }
        value = default;
        return SkipValue();
    }

    /// <summary>
    /// Try read array of unsigned ints
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetUInt32Values(out ArrayOf<uint> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<uint>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetUInt32(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Get unsigned long
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    /// <exception cref="ServiceResultException"></exception>
    private bool TryGetUInt64(out ulong value)
    {
        switch (_reader.TokenType)
        {
            case JsonTokenType.Number:
                if (_reader.TryGetUInt64(out value))
                {
                    return TryReadNext();
                }
                break;
            case JsonTokenType.String:
                // As per 5.4.2.3, formatted as a decimal number encoded as a JSON string
                if (!TryGetUtf16String(out var stringEncoded))
                {
                    value = default;
                    return false;
                }
                if (ulong.TryParse(stringEncoded, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out value))
                {
                    return true;
                }
                break;
            default:
                value = default;
                return SkipValue();
        }
        ServiceResultException.Throw(StatusCodes.BadDecodingError,
            $"Expected long or string encoded ulong, but got {_reader.TokenType}.");
        value = default;
        return false;
    }

    /// <summary>
    /// Try read array of unsigned longs
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetUInt64Values(out ArrayOf<ulong> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<ulong>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetUInt64(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Get string
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetUtf16String(out string value)
    {
        if (_reader.TokenType != JsonTokenType.String)
        {
            value = string.Empty;
            return SkipValue();
        }
        var length = _reader.HasValueSequence ?
            _reader.ValueSequence.Length : _reader.ValueSpan.Length;
        CheckStringLength(length);
        var str = _reader.GetString();
        Debug.Assert(str != null, "String is not expected to be null");
        value = str;
        return TryReadNext();
    }

    /// <summary>
    /// Try read array of string values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetUtf16StringValues(out ArrayOf<string> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<string>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetUtf16String(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get variant
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetVariant(out Variant value)
    {
        CheckNestingLevel();
        if (_reader.TokenType != JsonTokenType.StartObject)
        {
            value = Variant.Null;
            return SkipValue();
        }
        if (!TryReadByte(JsonProperties.Type, out var type) ||
            !TryReadToObjectPropertyValue(JsonProperties.Body))
        {
            value = Variant.Null;
            return false;
        }

        var builtInType = (BuiltInType)type;
        if (_reader.TokenType == JsonTokenType.StartArray)
        {
            int[]? dimensions;
            switch (builtInType)
            {
                case BuiltInType.Boolean:
                    if (!TryGetBooleanValues(out var booleans) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(booleans, dimensions);
                    break;
                case BuiltInType.SByte:
                    if (!TryGetSByteValues(out var sbytes) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(sbytes, dimensions);
                    break;
                case BuiltInType.Byte:
                    if (!TryGetByteValues(out var bytes) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(bytes, dimensions);
                    break;
                case BuiltInType.Int16:
                    if (!TryGetInt16Values(out var shorts) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(shorts, dimensions);
                    break;
                case BuiltInType.UInt16:
                    if (!TryGetUInt16Values(out var values) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(values, dimensions);
                    break;
                case BuiltInType.Int32:
                    if (!TryGetInt32Values(out var ints) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(ints, dimensions);
                    break;
                case BuiltInType.UInt32:
                    if (!TryGetUInt32Values(out var uints) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(uints, dimensions);
                    break;
                case BuiltInType.Int64:
                    if (!TryGetInt64Values(out var longs) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(longs, dimensions);
                    break;
                case BuiltInType.UInt64:
                    if (!TryGetUInt64Values(out var ulongs) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(ulongs, dimensions);
                    break;
                case BuiltInType.Float:
                    if (!TryGetFloatValues(out var floats) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(floats, dimensions);
                    break;
                case BuiltInType.Double:
                    if (!TryGetDoubleValues(out var doubles) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(doubles, dimensions);
                    break;
                case BuiltInType.String:
                    if (!TryGetStringValues(out var strings) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(strings, dimensions);
                    break;
                case BuiltInType.DateTime:
                    if (!TryGetDateTimeValues(out var dates) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(dates, dimensions);
                    break;
                case BuiltInType.Guid:
                    if (!TryGetGuidValues(out var guids) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(guids, dimensions);
                    break;
                case BuiltInType.ByteString:
                    if (!TryGetByteStringValues(out var bytestrings) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(bytestrings, dimensions);
                    break;
                case BuiltInType.XmlElement:
                    if (!TryGetXmlElementValues(out var xmlelements) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(xmlelements, dimensions);
                    break;
                case BuiltInType.NodeId:
                    if (!TryGetNodeIdValues(out var nodeids) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(nodeids, dimensions);
                    break;
                case BuiltInType.ExpandedNodeId:
                    if (!TryGetExpandedNodeIdValues(out var expanded) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(expanded, dimensions);
                    break;
                case BuiltInType.StatusCode:
                    if (!TryGetStatusCodeValues(out var statusCodes) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(statusCodes, dimensions);
                    break;
                case BuiltInType.QualifiedName:
                    if (!TryGetQualifiedNameValues(out var names) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(names, dimensions);
                    break;
                case BuiltInType.LocalizedText:
                    if (!TryGetLocalizedTextValues(out var localizedTexts) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(localizedTexts, dimensions);
                    break;
                case BuiltInType.ExtensionObject:
                    if (!TryGetExtensionObjectValues(out var objects) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(objects, dimensions);
                    break;
                case BuiltInType.DataValue:
                    if (!TryGetDataValueValues(out var dataValues) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(dataValues, dimensions);
                    break;
                case BuiltInType.Variant:
                    if (!TryGetVariantValues(out var variants) ||
                        !TryReadArrayDimensions(out dimensions))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(variants, dimensions);
                    break;
                case BuiltInType.DiagnosticInfo:
                    ServiceResultException.Throw(StatusCodes.BadDecodingError,
                        "Diagnostic info not allowed in Variant value");
                    value = Variant.From(StatusCodes.BadDecodingError);
                    break;
                default:
                    if ((byte)builtInType <= 31)
                    {
                        goto case BuiltInType.ByteString;
                    }
                    ServiceResultException.Throw(StatusCodes.BadDecodingError,
                        $"Cannot decode unknown type with BuiltInType: {builtInType}.");
                    value = default;
                    break;
            }
        }
        else
        {
            // Read scalar
            switch (builtInType)
            {
                case BuiltInType.Null:
                    value = Variant.Null;
                    break;
                case BuiltInType.Boolean:
                    if (!TryGetBoolean(out var booleanValue))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(booleanValue);
                    break;
                case BuiltInType.SByte:
                    if (!TryGetSByte(out var signedByte))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(signedByte);
                    break;
                case BuiltInType.Byte:
                    if (!TryGetByte(out var unsignedByte))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(unsignedByte);
                    break;
                case BuiltInType.Int16:
                    if (!TryGetInt16(out var signedShort))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(signedShort);
                    break;
                case BuiltInType.UInt16:
                    if (!TryGetUInt16(out var unsignedShort))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(unsignedShort);
                    break;
                case BuiltInType.Int32:
                    if (!TryGetInt32(out var signedInt))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(signedInt);
                    break;
                case BuiltInType.UInt32:
                    if (!TryGetUInt32(out var unsignedInt))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(unsignedInt);
                    break;
                case BuiltInType.Int64:
                    if (!TryGetInt64(out var signedLong))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(signedLong);
                    break;
                case BuiltInType.UInt64:
                    if (!TryGetUInt64(out var unsignedLong))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(unsignedLong);
                    break;
                case BuiltInType.Float:
                    if (!TryGetFloat(out var floatValue))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(floatValue);
                    break;
                case BuiltInType.Double:
                    if (!TryGetDouble(out var doubleValue))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(doubleValue);
                    break;
                case BuiltInType.String:
                    if (!TryGetString(out var stringValue))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(stringValue);
                    break;
                case BuiltInType.DateTime:
                    if (!TryGetDateTime(out var dateTime))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(dateTime);
                    break;
                case BuiltInType.Guid:
                    if (!TryGetGuid(out var guid))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(guid);
                    break;
                case BuiltInType.ByteString:
                    if (!TryGetByteString(out var bytes))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(bytes);
                    break;
                case BuiltInType.XmlElement:
                    if (!TryGetXmlElement(out var xml))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(xml);
                    break;
                case BuiltInType.NodeId:
                    if (!TryGetNodeId(out var nodeId))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(nodeId);
                    break;
                case BuiltInType.ExpandedNodeId:
                    if (!TryGetExpandedNodeId(out var expanded))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(expanded);
                    break;
                case BuiltInType.StatusCode:
                    if (!TryGetStatusCode(out var statusCode))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(statusCode);
                    break;
                case BuiltInType.QualifiedName:
                    if (!TryGetQualifiedName(out var qualified))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(qualified);
                    break;
                case BuiltInType.LocalizedText:
                    if (!TryGetLocalizedText(out var localizedText))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(localizedText);
                    break;
                case BuiltInType.ExtensionObject:
                    if (!TryGetExtensionObject(out var extension))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(extension);
                    break;
                case BuiltInType.DataValue:
                    if (!TryGetDataValue(out var datavalue))
                    {
                        value = Variant.Null;
                        return false;
                    }
                    value = new Variant(datavalue);
                    break;
                case BuiltInType.Variant:
                    ServiceResultException.Throw(StatusCodes.BadDecodingError,
                        "Variant not allowed in Variant value");
                    value = Variant.From(StatusCodes.BadDecodingError);
                    break;
                case BuiltInType.DiagnosticInfo:
                    ServiceResultException.Throw(StatusCodes.BadDecodingError,
                        "Diagnostic info not allowed in Variant value");
                    value = Variant.From(StatusCodes.BadDecodingError);
                    break;
                default:
                    if ((byte)builtInType <= 31)
                    {
                        goto case BuiltInType.ByteString;
                    }
                    ServiceResultException.Throw(StatusCodes.BadDecodingError,
                        "Cannot decode unknown type in Variant object.");
                    value = default;
                    break;
            }
        }
        return SkipPastEndOfObject();
    }

    /// <summary>
    /// Try read array of variant values
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetVariantValues(out ArrayOf<Variant> values)
    {
        if (_reader.TokenType != JsonTokenType.StartArray)
        {
            values = [];
            return SkipValue();
        }
        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<Variant>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetVariant(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Try get xml
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryGetXmlElement(out XmlElement value)
    {
        if (_reader.TokenType == JsonTokenType.String)
        {
            if (!TryGetString(out var xmlString))
            {
                value = XmlElement.Empty;
                return false;
            }
            value = new XmlElement(xmlString);
            return true;
        }
        value = XmlElement.Empty;
        return SkipValue();
    }

    /// <summary>
    /// Try read array of xml elements
    /// </summary>
    /// <param name="values"></param>
    /// <returns></returns>
    private bool TryGetXmlElementValues(out ArrayOf<XmlElement> values)
    {
        Debug.Assert(_reader.TokenType == JsonTokenType.StartArray,
            "Should be in variant value array");

        if (!TryReadNext())
        {
            values = [];
            return false;
        }
        var result = new List<XmlElement>();
        while (NextArrayElement(result.Count))
        {
            if (!TryGetXmlElement(out var b))
            {
                values = [];
                return false;
            }
            result.Add(b);
        }
        values = result;
        return TryReadNext();
    }

    /// <summary>
    /// Read structure using decoder
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="decoder"></param>
    /// <param name="field"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    private bool TryReadStructure<T>(IDecoder decoder, ReadOnlySpan<byte> field,
        out T value) where T : IStructure<T>
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            value = T.Null;
            return false;
        }
        return TryGetStructure<T>(decoder, out value);
    }

    /// <summary>
    /// Try read dimensions
    /// </summary>
    /// <param name="dimensions"></param>
    /// <returns></returns>
    private bool TryReadArrayDimensions(out int[]? dimensions)
    {
        if (!TryReadInt32Values(JsonProperties.ArrayDimensions, out var dims))
        {
            dimensions = default;
            return false;
        }
        if (dims.Count == 1)
        {
            // https://reference.opcfoundation.org/Core/Part6/v105/docs/5.2.5
            ServiceResultException.Throw(StatusCodes.BadDecodingError,
                "The number of dimensions shall be at least 2.");
        }
        dimensions = dims.Count == 0 ? null : dims.Memory.ToArray();
        return true;
    }

    /// <summary>
    /// Try read namespace for node id where the namespace can be either
    /// a uri or a namespace table index even in reversible decoder.
    /// </summary>
    /// <param name="field"></param>
    /// <param name="ns"></param>
    /// <param name="uri"></param>
    /// <returns></returns>
    private bool TryReadNamespace(ReadOnlySpan<byte> field, out ushort ns,
        out Utf8String uri)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            ns = default;
            uri = Utf8String.Empty;
            return false;
        }
        if (_reader.TokenType == JsonTokenType.Number)
        {
            uri = Utf8String.Empty;
            var success = TryGetUInt16(out ns);
            Debug.Assert(success, "Should always be able to parse number");
            // uri = _context.Namespaces.GetNamespaceUri(ns);
            return true;
        }
        else if (_reader.TokenType == JsonTokenType.String)
        {
            if (TryGetString(out uri))
            {
                ns = _context.Namespaces.GetNamespaceIndex(uri);
                return true;
            }
            ns = default;
            return false;
        }
        else
        {
            ns = default;
            uri = Utf8String.Empty;
            return SkipValue();
        }
    }

    /// <summary>
    /// Try read server uri for node id where the server uri can be either
    /// a uri or a server table index even in reversible decoder.
    /// </summary>
    /// <param name="field"></param>
    /// <param name="serverIndex"></param>
    /// <returns></returns>
    private bool TryReadServerIndex(ReadOnlySpan<byte> field, out uint serverIndex)
    {
        if (!TryReadToObjectPropertyValue(field))
        {
            serverIndex = default;
            return false;
        }
        if (_reader.TokenType == JsonTokenType.Number)
        {
            var success = TryGetUInt32(out serverIndex);
            Debug.Assert(success, "Should always be able to parse number");
            return true;
        }
        else if (_reader.TokenType == JsonTokenType.String)
        {
            var success = TryGetString(out var uri);
            Debug.Assert(success, "Should always be able to parse string");
            serverIndex = _context.Namespaces.GetServerIndex(uri);
            return true;
        }
        else
        {
            serverIndex = default;
            return SkipValue();
        }
    }

    /// <summary>
    /// Try read property name
    /// </summary>
    /// <param name="field"></param>
    /// <returns></returns>
    private bool TryReadToObjectPropertyValue(ReadOnlySpan<byte> field)
    {
        // Forward until end of object or to property if we are not there already
        while (_reader.TokenType is not JsonTokenType.PropertyName and not JsonTokenType.EndObject)
        {
            if (!TryReadNext())
            {
                return false;
            }
        }
        if (_reader.TokenType == JsonTokenType.EndObject)
        {
            // Read all properties of current object
            return true;
        }
        if (!_reader.ValueTextEquals(field))
        {
            // Property name is not as expected, must be missing
#if DEBUG
            var property = System.Text.Encoding.UTF8.GetString(_reader.ValueSpan.ToArray());
#endif
            return true;
        }
        // Found, now skip to value
        return TryReadNext();
    }

    /// <summary>
    /// Check nesting level
    /// </summary>
    private readonly void CheckNestingLevel()
    {
        // check the nesting level for avoiding a stack overflow.
        if (_reader.CurrentDepth >= _context.Limits.MaxEncodingNestingLevels)
        {
            ServiceResultException.Throw(StatusCodes.BadEncodingLimitsExceeded,
                $"Maximum nesting level {_context.Limits.MaxEncodingNestingLevels} exceeded.");
        }
    }

    /// <summary>
    /// Check string length
    /// </summary>
    /// <param name="length"></param>
    private readonly void CheckStringLength(long length)
    {
        if (length > _context.Limits.MaxStringLength)
        {
            ServiceResultException.Throw(StatusCodes.BadEncodingLimitsExceeded,
                $"String length {(uint)length} > max = {_context.Limits.MaxStringLength}");
        }
    }

    /// <summary>
    /// Check array length and whether array is complete
    /// </summary>
    /// <param name="length"></param>
    /// <returns></returns>
    private readonly bool NextArrayElement(int length)
    {
        if (_reader.TokenType == JsonTokenType.EndArray)
        {
            return false;
        }
        if (length >= _context.Limits.MaxArrayLength)
        {
            // If we were to add another element we would exceed the limit
            ServiceResultException.Throw(StatusCodes.BadEncodingLimitsExceeded,
                $"Array length {(uint)length + 1} > max = {_context.Limits.MaxArrayLength}");
        }
        return true;
    }

    /// <summary>
    /// Try read next token. Returns false if no more tokens found
    /// or if parsing failed and we are at end of stream. Throws
    /// an exception if parsing failed and we are not at end of stream.
    /// </summary>
    /// <returns></returns>
    private bool TryReadNext()
    {
        try
        {
            if (_reader.Read())
            {
                return true;
            }
        }
        catch (JsonException jre)
        {
            ServiceResultException.Throw(StatusCodes.BadDecodingError,
                "Invalid json encountered while parsing.", jre);
        }
        return false;
    }

    /// <summary>
    /// Try to find if we are at end of stream or it was a real error.
    /// </summary>
    /// <returns></returns>
    private readonly bool IsEndOfReader() => _reader.BytesConsumed == _length;

    /// <summary>
    /// Try flush to end of the array
    /// </summary>
    /// <returns></returns>
    private bool SkipPastEndOfArray()
    {
        // Skip array and any nested array
        var level = 0;
        do
        {
            switch (_reader.TokenType)
            {
                case JsonTokenType.EndArray:
                    level--;
                    break;
                case JsonTokenType.StartArray:
                    level++;
                    break;
            }
            if (!TryReadNext())
            {
                return false;
            }
        }
        while (level > 0);
        return true;
    }

    /// <summary>
    /// Try flush to end of the object
    /// </summary>
    /// <returns></returns>
    private bool SkipPastEndOfObject()
    {
        // Skip object
        var level = _reader.TokenType == JsonTokenType.StartObject ? 0 : 1;
        do
        {
            switch (_reader.TokenType)
            {
                case JsonTokenType.EndObject:
                    level--;
                    break;
                case JsonTokenType.StartObject:
                    level++;
                    break;
            }
            if (!TryReadNext())
            {
                return false;
            }
        }
        while (level > 0);
        return true;
    }

    /// <summary>
    /// Skip value
    /// </summary>
    /// <returns></returns>
    private bool SkipValue()
    {
        switch (_reader.TokenType)
        {
            case JsonTokenType.StartObject:
                return SkipPastEndOfObject();
            case JsonTokenType.StartArray:
                return SkipPastEndOfArray();
            case JsonTokenType.EndObject:
                // We might have read all of the objects properties already but are
                // still in the object.  The object parsing will skip past end itself.
                return true;
            case JsonTokenType.PropertyName:
                // We might be on a property name because the property did not match.
                // This happens when a property was omitted because it was null.
                return true;
            default:
                return TryReadNext();
        }
    }

    private readonly ICodecContext _context;
    private readonly long _length;
    private Utf8JsonReader _reader;
}
