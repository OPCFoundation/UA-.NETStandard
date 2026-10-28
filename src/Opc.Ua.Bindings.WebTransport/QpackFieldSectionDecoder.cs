// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;

namespace Opc.Ua.Bindings
{
    /// <summary>
    /// Represents one decoded QPACK field line.
    /// </summary>
    internal readonly record struct QpackHeaderField(string Name, string Value);

    /// <summary>
    /// Decodes bounded QPACK field sections when the dynamic table capacity is zero.
    /// </summary>
    internal static class QpackFieldSectionDecoder
    {
        private static readonly QpackHeaderField[] s_staticTable = CreateStaticTable();

        /// <summary>
        /// Decodes static and literal field lines without modifying connection compression state.
        /// </summary>
        internal static QpackDecodeStatus TryDecode(
            ReadOnlySequence<byte> encoded,
            int maximumEncodedBytes,
            int maximumDecodedBytes,
            int maximumFields,
            out QpackHeaderField[] fields)
        {
            if (maximumEncodedBytes < 0 || maximumDecodedBytes < 0 || maximumFields < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumEncodedBytes));
            }
            if (encoded.Length > maximumEncodedBytes)
            {
                fields = [];
                return QpackDecodeStatus.FieldSectionTooLarge;
            }

            SequenceReader<byte> reader = new(encoded);
            if (!TryReadPrefixedInteger(ref reader, 8, out ulong requiredInsertCount) ||
                !TryReadPrefixedInteger(ref reader, 7, out ulong deltaBase))
            {
                fields = [];
                return QpackDecodeStatus.NeedMoreData;
            }
            if (requiredInsertCount != 0)
            {
                fields = [];
                return QpackDecodeStatus.DynamicTableReference;
            }

            _ = deltaBase;
            List<QpackHeaderField> decoded = new(Math.Min(maximumFields, 16));
            int decodedBytes = 0;
            while (reader.Remaining > 0)
            {
                if (decoded.Count == maximumFields)
                {
                    fields = [];
                    return QpackDecodeStatus.TooManyFields;
                }

                byte first = reader.UnreadSpan[0];
                QpackDecodeStatus status;
                QpackHeaderField field;
                if ((first & 0x80) != 0)
                {
                    status = TryReadIndexed(ref reader, out field);
                }
                else if ((first & 0xc0) == 0x40)
                {
                    status = TryReadLiteralWithNameReference(
                        ref reader,
                        maximumDecodedBytes - decodedBytes - 32,
                        out field);
                }
                else if ((first & 0xe0) == 0x20)
                {
                    status = TryReadLiteralWithLiteralName(
                        ref reader,
                        maximumDecodedBytes - decodedBytes - 32,
                        out field);
                }
                else
                {
                    fields = [];
                    return QpackDecodeStatus.DynamicTableReference;
                }

                if (status != QpackDecodeStatus.Success)
                {
                    fields = [];
                    return status;
                }

                long fieldSize = (long)field.Name.Length + field.Value.Length + 32;
                if (fieldSize > maximumDecodedBytes - decodedBytes)
                {
                    fields = [];
                    return QpackDecodeStatus.FieldSectionTooLarge;
                }

                decodedBytes += (int)fieldSize;
                decoded.Add(field);
            }

            fields = decoded.ToArray();
            return QpackDecodeStatus.Success;
        }

        /// <summary>
        /// Attempts to decode one QPACK prefixed integer without consuming partial input.
        /// </summary>
        internal static bool TryReadPrefixedInteger(
            ref SequenceReader<byte> reader,
            int prefixBits,
            out ulong value)
        {
            if (prefixBits is < 1 or > 8)
            {
                throw new ArgumentOutOfRangeException(nameof(prefixBits));
            }

            SequenceReader<byte> candidate = reader;
            if (!candidate.TryRead(out byte first))
            {
                value = 0;
                return false;
            }

            byte mask = (byte)((1 << prefixBits) - 1);
            ulong decoded = (ulong)(first & mask);
            if (decoded != mask)
            {
                value = decoded;
                reader = candidate;
                return true;
            }

            int shift = 0;
            for (int continuationCount = 0; continuationCount < 10; continuationCount++)
            {
                if (!candidate.TryRead(out byte continuation))
                {
                    value = 0;
                    return false;
                }

                ulong increment = (ulong)(continuation & 0x7f);
                if (shift >= 62 || increment > (Http3VarInt.MaximumValue - decoded) >> shift)
                {
                    value = 0;
                    return false;
                }

                decoded += increment << shift;
                if ((continuation & 0x80) == 0)
                {
                    value = decoded;
                    reader = candidate;
                    return true;
                }

                shift += 7;
            }

            value = 0;
            return false;
        }

        private static QpackDecodeStatus TryReadIndexed(
            ref SequenceReader<byte> reader,
            out QpackHeaderField field)
        {
            SequenceReader<byte> candidate = reader;
            byte first = candidate.UnreadSpan[0];
            bool isStatic = (first & 0x40) != 0;
            if (!TryReadPrefixedInteger(ref candidate, 6, out ulong index))
            {
                field = default;
                return QpackDecodeStatus.NeedMoreData;
            }
            if (!isStatic)
            {
                field = default;
                return QpackDecodeStatus.DynamicTableReference;
            }
            if (index >= (ulong)s_staticTable.Length)
            {
                field = default;
                return QpackDecodeStatus.InvalidStaticIndex;
            }

            reader = candidate;
            field = s_staticTable[(int)index];
            return QpackDecodeStatus.Success;
        }

        private static QpackDecodeStatus TryReadLiteralWithNameReference(
            ref SequenceReader<byte> reader,
            int maximumFieldBytes,
            out QpackHeaderField field)
        {
            SequenceReader<byte> candidate = reader;
            byte first = candidate.UnreadSpan[0];
            bool neverIndexed = (first & 0x20) != 0;
            bool isStatic = (first & 0x10) != 0;
            if (!TryReadPrefixedInteger(ref candidate, 4, out ulong nameIndex))
            {
                field = default;
                return QpackDecodeStatus.NeedMoreData;
            }
            if (!isStatic)
            {
                field = default;
                return QpackDecodeStatus.DynamicTableReference;
            }
            if (nameIndex >= (ulong)s_staticTable.Length)
            {
                field = default;
                return QpackDecodeStatus.InvalidStaticIndex;
            }
            string name = s_staticTable[(int)nameIndex].Name;
            int maximumValueBytes = maximumFieldBytes - Encoding.UTF8.GetByteCount(name);
            if (!TryReadString(
                ref candidate,
                maximumValueBytes,
                out string value,
                out QpackDecodeStatus stringStatus))
            {
                field = default;
                return stringStatus;
            }

            reader = candidate;
            field = new QpackHeaderField(name, value);
            _ = neverIndexed;
            return QpackDecodeStatus.Success;
        }

        private static QpackDecodeStatus TryReadLiteralWithLiteralName(
            ref SequenceReader<byte> reader,
            int maximumFieldBytes,
            out QpackHeaderField field)
        {
            SequenceReader<byte> candidate = reader;
            if (!candidate.TryRead(out byte first))
            {
                field = default;
                return QpackDecodeStatus.NeedMoreData;
            }

            bool neverIndexed = (first & 0x10) != 0;
            bool huffmanName = (first & 0x08) != 0;
            ulong nameLength = (ulong)(first & 0x07);
            if (nameLength == 0x07 &&
                !TryReadIntegerContinuation(ref candidate, nameLength, 0x07, out nameLength))
            {
                field = default;
                return QpackDecodeStatus.NeedMoreData;
            }
            QpackDecodeStatus nameStatus = TryReadStringBytes(
                ref candidate,
                nameLength,
                huffmanName,
                true,
                maximumFieldBytes,
                out string name);
            if (nameStatus != QpackDecodeStatus.Success)
            {
                field = default;
                return nameStatus == QpackDecodeStatus.InvalidFieldValue ?
                    QpackDecodeStatus.InvalidFieldName :
                    nameStatus;
            }
            if (!IsValidFieldName(name))
            {
                field = default;
                return QpackDecodeStatus.InvalidFieldName;
            }
            int maximumValueBytes = maximumFieldBytes - Encoding.UTF8.GetByteCount(name);
            if (!TryReadString(
                ref candidate,
                maximumValueBytes,
                out string value,
                out QpackDecodeStatus stringStatus))
            {
                field = default;
                return stringStatus;
            }

            reader = candidate;
            field = new QpackHeaderField(name, value);
            return QpackDecodeStatus.Success;
        }

        private static bool TryReadString(
            ref SequenceReader<byte> reader,
            int maximumStringBytes,
            out string value,
            out QpackDecodeStatus status)
        {
            SequenceReader<byte> candidate = reader;
            if (!candidate.TryRead(out byte first))
            {
                value = string.Empty;
                status = QpackDecodeStatus.NeedMoreData;
                return false;
            }

            bool huffman = (first & 0x80) != 0;
            if (!TryReadIntegerContinuation(
                ref candidate,
                (ulong)(first & 0x7f),
                0x7f,
                out ulong length))
            {
                value = string.Empty;
                status = QpackDecodeStatus.NeedMoreData;
                return false;
            }
            if (!huffman && length > (ulong)Math.Max(0, maximumStringBytes))
            {
                value = string.Empty;
                status = QpackDecodeStatus.FieldSectionTooLarge;
                return false;
            }
            status = TryReadStringBytes(
                ref candidate,
                length,
                huffman,
                false,
                maximumStringBytes,
                out value);
            if (status != QpackDecodeStatus.Success)
            {
                return false;
            }

            reader = candidate;
            status = QpackDecodeStatus.Success;
            return true;
        }

        private static QpackDecodeStatus TryReadStringBytes(
            ref SequenceReader<byte> reader,
            ulong length,
            bool huffman,
            bool requireAscii,
            int maximumDecodedBytes,
            out string value)
        {
            if (!huffman && length > (ulong)Math.Max(0, maximumDecodedBytes))
            {
                value = string.Empty;
                return QpackDecodeStatus.FieldSectionTooLarge;
            }
            if (maximumDecodedBytes < 0)
            {
                value = string.Empty;
                return QpackDecodeStatus.FieldSectionTooLarge;
            }
            if (length > int.MaxValue || reader.Remaining < (long)length)
            {
                value = string.Empty;
                return QpackDecodeStatus.NeedMoreData;
            }

            byte[] bytes = new byte[(int)length];
            if (!reader.TryCopyTo(bytes))
            {
                value = string.Empty;
                return QpackDecodeStatus.NeedMoreData;
            }
            reader.Advance((long)length);

            if (huffman &&
                !QpackHuffmanDecoder.TryDecode(bytes, maximumDecodedBytes, out bytes))
            {
                value = string.Empty;
                return QpackDecodeStatus.InvalidHuffman;
            }
            if (requireAscii)
            {
                foreach (byte item in bytes)
                {
                    if (item > 0x7f)
                    {
                        value = string.Empty;
                        return QpackDecodeStatus.InvalidFieldValue;
                    }
                }
            }
            else
            {
                foreach (byte item in bytes)
                {
                    if (item is 0 or 0x0a or 0x0d)
                    {
                        value = string.Empty;
                        return QpackDecodeStatus.InvalidFieldValue;
                    }
                }
            }

            value = requireAscii ? Encoding.ASCII.GetString(bytes) : Encoding.Latin1.GetString(bytes);
            return QpackDecodeStatus.Success;
        }

        private static bool TryReadIntegerContinuation(
            ref SequenceReader<byte> reader,
            ulong prefixValue,
            ulong prefixMaximum,
            out ulong value)
        {
            value = prefixValue;
            if (prefixValue < prefixMaximum)
            {
                return true;
            }

            int shift = 0;
            for (int count = 0; count < 10; count++)
            {
                if (!reader.TryRead(out byte next))
                {
                    return false;
                }
                ulong part = (ulong)(next & 0x7f);
                if (shift >= 62 || part > (Http3VarInt.MaximumValue - value) >> shift)
                {
                    return false;
                }
                value += part << shift;
                if ((next & 0x80) == 0)
                {
                    return true;
                }
                shift += 7;
            }
            return false;
        }

        private static bool IsValidFieldName(string name)
        {
            if (name.Length == 0)
            {
                return false;
            }

            int start = name[0] == ':' ? 1 : 0;
            if (start == name.Length)
            {
                return false;
            }
            for (int index = start; index < name.Length; index++)
            {
                char character = name[index];
                if (character is >= 'A' and <= 'Z' ||
                    !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or
                        '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~'))
                {
                    return false;
                }
            }
            return true;
        }

        private static QpackHeaderField[] CreateStaticTable()
        {
            return
            [
                new(":authority", string.Empty),
                new(":path", "/"),
                new("age", "0"),
                new("content-disposition", string.Empty),
                new("content-length", "0"),
                new("cookie", string.Empty),
                new("date", string.Empty),
                new("etag", string.Empty),
                new("if-modified-since", string.Empty),
                new("if-none-match", string.Empty),
                new("last-modified", string.Empty),
                new("link", string.Empty),
                new("location", string.Empty),
                new("referer", string.Empty),
                new("set-cookie", string.Empty),
                new(":method", "CONNECT"),
                new(":method", "DELETE"),
                new(":method", "GET"),
                new(":method", "HEAD"),
                new(":method", "OPTIONS"),
                new(":method", "POST"),
                new(":method", "PUT"),
                new(":scheme", "http"),
                new(":scheme", "https"),
                new(":status", "103"),
                new(":status", "200"),
                new(":status", "304"),
                new(":status", "404"),
                new(":status", "503"),
                new("accept", "*/*"),
                new("accept", "application/dns-message"),
                new("accept-encoding", "gzip, deflate, br"),
                new("accept-ranges", "bytes"),
                new("access-control-allow-headers", "cache-control"),
                new("access-control-allow-headers", "content-type"),
                new("access-control-allow-origin", "*"),
                new("cache-control", "max-age=0"),
                new("cache-control", "max-age=2592000"),
                new("cache-control", "max-age=604800"),
                new("cache-control", "no-cache"),
                new("cache-control", "no-store"),
                new("cache-control", "public, max-age=31536000"),
                new("content-encoding", "br"),
                new("content-encoding", "gzip"),
                new("content-type", "application/dns-message"),
                new("content-type", "application/javascript"),
                new("content-type", "application/json"),
                new("content-type", "application/x-www-form-urlencoded"),
                new("content-type", "image/gif"),
                new("content-type", "image/jpeg"),
                new("content-type", "image/png"),
                new("content-type", "text/css"),
                new("content-type", "text/html; charset=utf-8"),
                new("content-type", "text/plain"),
                new("content-type", "text/plain;charset=utf-8"),
                new("range", "bytes=0-"),
                new("strict-transport-security", "max-age=31536000"),
                new("strict-transport-security", "max-age=31536000; includesubdomains"),
                new("strict-transport-security", "max-age=31536000; includesubdomains; preload"),
                new("vary", "accept-encoding"),
                new("vary", "origin"),
                new("x-content-type-options", "nosniff"),
                new("x-xss-protection", "1; mode=block"),
                new(":status", "100"),
                new(":status", "204"),
                new(":status", "206"),
                new(":status", "302"),
                new(":status", "400"),
                new(":status", "403"),
                new(":status", "421"),
                new(":status", "425"),
                new(":status", "500"),
                new("accept-language", string.Empty),
                new("access-control-allow-credentials", "FALSE"),
                new("access-control-allow-credentials", "TRUE"),
                new("access-control-allow-headers", "*"),
                new("access-control-allow-methods", "get"),
                new("access-control-allow-methods", "get, post, options"),
                new("access-control-allow-methods", "options"),
                new("access-control-expose-headers", "content-length"),
                new("access-control-request-headers", "content-type"),
                new("access-control-request-method", "get"),
                new("access-control-request-method", "post"),
                new("alt-svc", "clear"),
                new("authorization", string.Empty),
                new("content-security-policy", "script-src 'none'; object-src 'none'; base-uri 'none'"),
                new("early-data", "1"),
                new("expect-ct", string.Empty),
                new("forwarded", string.Empty),
                new("if-range", string.Empty),
                new("origin", string.Empty),
                new("purpose", "prefetch"),
                new("server", string.Empty),
                new("timing-allow-origin", "*"),
                new("upgrade-insecure-requests", "1"),
                new("user-agent", string.Empty),
                new("x-forwarded-for", string.Empty),
                new("x-frame-options", "deny"),
                new("x-frame-options", "sameorigin")
            ];
        }
    }

    /// <summary>
    /// Describes the outcome of decoding a QPACK field section.
    /// </summary>
    internal enum QpackDecodeStatus
    {
        /// <summary>
        /// The field section was successfully decoded.
        /// </summary>
        Success,

        /// <summary>
        /// More bytes are required to complete the field section.
        /// </summary>
        NeedMoreData,

        /// <summary>
        /// A dynamic table reference was used although the configured capacity is zero.
        /// </summary>
        DynamicTableReference,

        /// <summary>
        /// A static table index is outside the QPACK static table.
        /// </summary>
        InvalidStaticIndex,

        /// <summary>
        /// A Huffman string was encountered but is not supported by this decoder.
        /// </summary>
        InvalidHuffman,

        /// <summary>
        /// The encoded or decoded field section exceeds its configured bound.
        /// </summary>
        FieldSectionTooLarge,

        /// <summary>
        /// The field count exceeds its configured bound.
        /// </summary>
        TooManyFields,

        /// <summary>
        /// A literal field name is invalid.
        /// </summary>
        InvalidFieldName,

        /// <summary>
        /// A literal field value is invalid.
        /// </summary>
        InvalidFieldValue
    }
}
