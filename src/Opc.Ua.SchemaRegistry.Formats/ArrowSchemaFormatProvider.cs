/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Opc.Ua.SchemaRegistry.Formats
{
    /// <summary>
    /// Schema-only Arrow IPC access. Metadata remains ordered binary data, and no
    /// extension registry or record-batch decoder participates in parsing.
    /// </summary>
    public sealed class ArrowSchemaFormatProvider : ISchemaFormatProvider
    {
        /// <inheritdoc/>
        public string Format => "ApacheArrow/1.0";

        /// <inheritdoc/>
        public string ContentType => "application/vnd.apache.arrow.stream";

        /// <inheritdoc/>
        public string SchemaIdAlgorithm => "SHA-256/ApacheArrow";

        /// <inheritdoc/>
        public SchemaContentDataType Parse(ReadOnlySpan<byte> document)
        {
            var reader = new ArrowSchemaReader(document);
            ArrowIpcSchemaContentDataType result = reader.Read();
            Validate(result);
            return result;
        }

        /// <inheritdoc/>
        public ByteString Serialize(SchemaContentDataType content)
        {
            if (content is not ArrowIpcSchemaContentDataType schema ||
                content.GetType() != typeof(ArrowIpcSchemaContentDataType))
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported,
                    "The complete Arrow IPC native view is required for lossless serialization.");
            }
            Validate(schema);
            ByteString bytes = ArrowSchemaWriter.Write(schema);
            var expected = (ArrowIpcSchemaContentDataType)schema.Clone();
            expected.Format = Format;
            if (!expected.IsEqual(Parse(bytes.Span)))
            {
                throw new ArgumentException("Inactive Arrow fields or unsupported values would be lost.",
                    nameof(content));
            }
            return bytes;
        }

        /// <inheritdoc/>
        public ByteString ComputeSchemaId(ReadOnlySpan<byte> document)
        {
            Parse(document);
#if NETFRAMEWORK || NETSTANDARD2_1
            using SHA256 hash = SHA256.Create();
            byte[] fingerprint = hash.ComputeHash(document.ToArray());
#else
            byte[] fingerprint = SHA256.HashData(document);
#endif
            return ByteString.From(fingerprint.AsSpan(0, 8).ToArray());
        }

        /// <inheritdoc/>
        public IEncodeable Select(SchemaContentDataType content, string selector)
        {
            if (selector is null)
            {
                throw new ArgumentNullException(nameof(selector));
            }
            if (content is not ArrowIpcSchemaContentDataType schema)
            {
                throw new ArgumentException("The complete Arrow IPC view is required.", nameof(content));
            }
            Validate(schema);
            ArrowIpcFieldDataType? selected = null;
            foreach (ArrowIpcFieldDataType field in schema.Fields)
            {
                if (field.HasName && string.Equals(field.Name, selector, StringComparison.Ordinal))
                {
                    if (selected is not null)
                    {
                        throw new ArgumentException("An Arrow field-name selection is ambiguous.", nameof(selector));
                    }
                    selected = field;
                }
            }
            return selected is null
                ? throw new ArgumentException("No top-level Arrow field has this name.", nameof(selector))
                : (IEncodeable)selected.Clone();
        }

        internal static void Validate(ArrowIpcSchemaContentDataType schema)
        {
            if (schema.GetType() != typeof(ArrowIpcSchemaContentDataType) ||
                !string.Equals(schema.Format, "ApacheArrow/1.0", StringComparison.OrdinalIgnoreCase) ||
                schema.MetadataVersion is not (3 or 4) || schema.Endianness > 1)
            {
                throw new ArgumentException("Unsupported Arrow IPC format, metadata version or endianness.");
            }
            if (schema.Features.Count > 100000)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            foreach (long feature in schema.Features)
            {
                if (feature is not (0 or 1 or 2))
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported, "Unknown required Arrow feature.");
                }
            }
            int remaining = 100000;
            Metadata(schema.Metadata, ref remaining);
            Metadata(schema.MessageMetadata, ref remaining);
            remaining -= schema.Features.Count;
            foreach (ArrowIpcFieldDataType field in schema.Fields)
            {
                ValidateField(field, 0, ref remaining);
            }
        }

        private static void ValidateField(ArrowIpcFieldDataType field, int depth, ref int remaining)
        {
            if (depth >= 128 || --remaining < 0)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            if (field is null || field.GetType() != typeof(ArrowIpcFieldDataType) ||
                field.Name is null || (!field.HasName && field.Name.Length != 0) ||
                field.Type is null || field.Type.GetType() != typeof(ArrowIpcTypeDataType))
            {
                throw new ArgumentException("Invalid native Arrow field.");
            }
            _ = s_utf8.GetByteCount(field.Name);
            Metadata(field.Metadata, ref remaining);
            ArrowIpcTypeDataType type = field.Type;
            if (type.Code < 1 || type.Code >= TypeNames.Length || type.Kind != TypeNames[type.Code] ||
                type.Timezone is null || (!type.HasTimezone && type.Timezone.Length != 0) ||
                (!type.HasTypeIds && type.TypeIds.Count != 0))
            {
                throw new ArgumentException("Arrow type tag, kind or presence fields disagree.");
            }
            switch (type.Code)
            {
                case 2:
                    Bits(type.BitWidth);
                    break;
                case 3:
                    if (type.Precision is < 0 or > 2)
                    {
                        throw new ArgumentException("Invalid Arrow floating-point precision.");
                    }
                    break;
                case 7:
                    int maximum = type.BitWidth switch { 32 => 9, 64 => 18, 128 => 38, 256 => 76, _ => 0 };
                    if (type.Precision < 1 || type.Precision > maximum)
                    {
                        throw new ArgumentException("Invalid Arrow decimal precision or bit width.");
                    }
                    break;
                case 8:
                    Unit(type.Unit, 1);
                    break;
                case 9:
                    Unit(type.Unit, 3);
                    if (type.BitWidth != (type.Unit < 2 ? 32 : 64))
                    {
                        throw new ArgumentException("Arrow time unit and bit width disagree.");
                    }
                    break;
                case 10:
                case 18:
                    Unit(type.Unit, 3);
                    break;
                case 11:
                    Unit(type.Unit, 2);
                    break;
                case 14:
                    if (type.Mode is < 0 or > 1 || field.Children.Count > 128 ||
                        (type.HasTypeIds && type.TypeIds.Count != field.Children.Count))
                    {
                        throw new ArgumentException("Invalid Arrow union mode or child type IDs.");
                    }
                    var used = new HashSet<int>();
                    foreach (int id in type.TypeIds)
                    {
                        if (id is < 0 or > 127 || !used.Add(id))
                        {
                            throw new ArgumentException("Arrow union IDs must be unique in 0..127.");
                        }
                    }
                    break;
                case 15:
                    if (type.ByteWidth < 0)
                    {
                        throw new ArgumentException("Arrow fixed binary width cannot be negative.");
                    }
                    break;
                case 16:
                    if (type.ListSize < 0)
                    {
                        throw new ArgumentException("Arrow fixed list size cannot be negative.");
                    }
                    break;
            }
            int expected = type.Code switch
            {
                12 or 16 or 17 or 21 or 25 or 26 => 1,
                22 => 2,
                13 or 14 => -1,
                _ => 0
            };
            if (expected >= 0 && field.Children.Count != expected)
            {
                throw new ArgumentException("Arrow type and child count disagree.");
            }
            if (field.Dictionary is ArrowIpcDictionaryDataType dictionary)
            {
                if (dictionary.GetType() != typeof(ArrowIpcDictionaryDataType) || dictionary.DictionaryKind != 0 ||
                    (!dictionary.HasIndexType && (dictionary.IndexBitWidth != 0 || dictionary.IndexIsSigned)))
                {
                    throw new ArgumentException("Invalid dictionary kind or index-type presence.");
                }
                if (dictionary.HasIndexType)
                {
                    Bits(dictionary.IndexBitWidth);
                }
            }
            foreach (ArrowIpcFieldDataType child in field.Children)
            {
                ValidateField(child, depth + 1, ref remaining);
            }
            if (type.Code == 17)
            {
                ArrowIpcFieldDataType entries = field.Children[0];
                if (entries.Nullable || entries.Type.Code != 13 || entries.Children.Count != 2 ||
                    entries.Children[0].Nullable || entries.Dictionary is not null)
                {
                    throw new ArgumentException("An Arrow Map requires a non-null entries struct and non-null keys.");
                }
            }
            if (type.Code == 22)
            {
                ArrowIpcFieldDataType runs = field.Children[0];
                if (runs.Nullable || runs.Type.Code != 2 || !runs.Type.IsSigned ||
                    runs.Type.BitWidth is not (16 or 32 or 64) || runs.Dictionary is not null)
                {
                    throw new ArgumentException("Arrow run ends require non-null signed Int16/Int32/Int64.");
                }
            }
        }

        private static void Bits(int width)
        {
            if (width is not (8 or 16 or 32 or 64))
            {
                throw new ArgumentException("Invalid Arrow integer bit width.");
            }
        }

        private static void Metadata(ArrayOf<ArrowKeyValueDataType> values, ref int remaining)
        {
            remaining -= values.Count;
            if (remaining < 0)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            foreach (ArrowKeyValueDataType value in values)
            {
                if (value is null || value.GetType() != typeof(ArrowKeyValueDataType))
                {
                    throw new ArgumentException("Invalid or unsupported Arrow metadata entry.");
                }
                if (value.Key.Length > 16 * 1024 * 1024 || value.Value.Length > 16 * 1024 * 1024)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
            }
        }

        private static void Unit(int unit, int maximum)
        {
            if (unit < 0 || unit > maximum)
            {
                throw new ArgumentException("Invalid Arrow temporal unit.");
            }
        }

        internal static readonly string[] TypeNames =
        [
            "NONE", "Null", "Int", "FloatingPoint", "Binary", "Utf8", "Bool", "Decimal",
            "Date", "Time", "Timestamp", "Interval", "List", "Struct_", "Union", "FixedSizeBinary",
            "FixedSizeList", "Map", "Duration", "LargeBinary", "LargeUtf8", "LargeList",
            "RunEndEncoded", "BinaryView", "Utf8View", "ListView", "LargeListView"
        ];
        private static readonly UTF8Encoding s_utf8 = new(false, true);
    }

    internal sealed class ArrowSchemaReader
    {
        public ArrowSchemaReader(ReadOnlySpan<byte> document)
        {
            if (document.Length < 8 || document.Length > 16 * 1024 * 1024)
            {
                throw new ArgumentException("Arrow schema document is truncated or exceeds its size bound.");
            }
            int prefix = BinaryPrimitives.ReadUInt32LittleEndian(document) == uint.MaxValue ? 8 : 4;
            int length = BinaryPrimitives.ReadInt32LittleEndian(document.Slice(prefix - 4));
            if (length < 8 || (long)prefix + length != document.Length)
            {
                throw new ArgumentException("An Arrow document contains exactly one complete IPC Schema message.");
            }
            m_bytes = document.Slice(prefix).ToArray();
        }

        public ArrowIpcSchemaContentDataType Read()
        {
            int message = Indirect(0);
            Table(message, 5);
            if (Unsigned8(message, 1) != 1 || Signed64(message, 3) != 0)
            {
                throw new ArgumentException("The IPC message must be a body-less Schema, not a record batch.");
            }
            int schema = Reference(message, 2, required: true);
            Table(schema, 4);
            return new ArrowIpcSchemaContentDataType
            {
                Format = "ApacheArrow/1.0",
                MetadataVersion = checked((uint)Signed16(message, 0)),
                Endianness = checked((uint)Signed16(schema, 0)),
                Features = Int64Vector(schema, 3),
                Fields = Fields(schema, 1, 0),
                Metadata = Metadata(schema, 2),
                MessageMetadata = Metadata(message, 4)
            };
        }

        private ArrayOf<ArrowIpcFieldDataType> Fields(int owner, int ordinal, int depth)
        {
            int vector = Vector(owner, ordinal, 4, out int count);
            var fields = new ArrowIpcFieldDataType[count];
            for (int index = 0; index < count; index++)
            {
                if (depth >= 128)
                {
                    throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
                }
                int field = Indirect(vector + 4 + index * 4);
                Table(field, 7);
                ByteString name = Bytes(field, 0);
                uint code = Unsigned8(field, 2);
                int dictionary = Reference(field, 4);
                fields[index] = new ArrowIpcFieldDataType
                {
                    Name = name.IsNull ? string.Empty : s_utf8.GetString(name.ToArray()),
                    HasName = !name.IsNull,
                    Nullable = Unsigned8(field, 1) != 0,
                    Type = Type(Reference(field, 3, required: true), code),
                    Dictionary = dictionary == 0 ? null! : Dictionary(dictionary),
                    Children = Fields(field, 5, depth + 1),
                    Metadata = Metadata(field, 6)
                };
            }
            return fields;
        }

        private ArrowIpcDictionaryDataType Dictionary(int table)
        {
            Table(table, 4);
            int index = Reference(table, 1);
            if (index != 0)
            {
                Table(index, 2);
            }
            return new ArrowIpcDictionaryDataType
            {
                Id = Signed64(table, 0),
                HasIndexType = index != 0,
                IndexBitWidth = index == 0 ? 0 : Signed32(index, 0),
                IndexIsSigned = index != 0 && Unsigned8(index, 1) != 0,
                Ordered = Unsigned8(table, 2) != 0,
                DictionaryKind = Signed16(table, 3)
            };
        }

        private ArrowIpcTypeDataType Type(int table, uint code)
        {
            if (code < 1 || code >= ArrowSchemaFormatProvider.TypeNames.Length)
            {
                throw new ServiceResultException(StatusCodes.BadNotSupported, "Unknown Arrow physical type tag.");
            }
            int fields = code switch
            {
                2 or 9 or 10 or 14 => 2,
                7 => 3,
                3 or 8 or 11 or 15 or 16 or 17 or 18 => 1,
                _ => 0
            };
            Table(table, fields);
            var type = new ArrowIpcTypeDataType
            {
                Kind = ArrowSchemaFormatProvider.TypeNames[code],
                Code = code,
                Timezone = string.Empty,
                TypeIds = []
            };
            switch (code)
            {
                case 2:
                    type.BitWidth = Signed32(table, 0);
                    type.IsSigned = Unsigned8(table, 1) != 0;
                    break;
                case 3: type.Precision = Signed16(table, 0); break;
                case 7:
                    type.Precision = Signed32(table, 0);
                    type.Scale = Signed32(table, 1);
                    type.BitWidth = Signed32(table, 2, 128);
                    break;
                case 8: type.Unit = Signed16(table, 0, 1); break;
                case 9:
                    type.Unit = Signed16(table, 0, 1);
                    type.BitWidth = Signed32(table, 1, 32);
                    break;
                case 10:
                    type.Unit = Signed16(table, 0);
                    ByteString timezone = Bytes(table, 1);
                    type.HasTimezone = !timezone.IsNull;
                    type.Timezone = timezone.IsNull ? string.Empty : s_utf8.GetString(timezone.ToArray());
                    break;
                case 11: type.Unit = Signed16(table, 0); break;
                case 14:
                    type.Mode = Signed16(table, 0);
                    type.HasTypeIds = Reference(table, 1) != 0;
                    type.TypeIds = Int32Vector(table, 1);
                    break;
                case 15: type.ByteWidth = Signed32(table, 0); break;
                case 16: type.ListSize = Signed32(table, 0); break;
                case 17: type.KeysSorted = Unsigned8(table, 0) != 0; break;
                case 18: type.Unit = Signed16(table, 0, 1); break;
            }
            return type;
        }

        private ArrayOf<ArrowKeyValueDataType> Metadata(int owner, int ordinal)
        {
            int vector = Vector(owner, ordinal, 4, out int count);
            var values = new ArrowKeyValueDataType[count];
            for (int index = 0; index < count; index++)
            {
                int table = Indirect(vector + 4 + index * 4);
                Table(table, 2);
                values[index] = new ArrowKeyValueDataType { Key = Bytes(table, 0), Value = Bytes(table, 1) };
            }
            return values;
        }

        private ArrayOf<int> Int32Vector(int owner, int ordinal)
        {
            int vector = Vector(owner, ordinal, 4, out int count);
            var values = new int[count];
            for (int index = 0; index < count; index++)
            {
                values[index] = I32(vector + 4 + index * 4);
            }
            return values;
        }

        private ArrayOf<long> Int64Vector(int owner, int ordinal)
        {
            int vector = Vector(owner, ordinal, 8, out int count);
            var values = new long[count];
            for (int index = 0; index < count; index++)
            {
                values[index] = BinaryPrimitives.ReadInt64LittleEndian(m_bytes.AsSpan(vector + 4 + index * 8, 8));
            }
            return values;
        }

        private ByteString Bytes(int owner, int ordinal)
        {
            int position = Reference(owner, ordinal);
            if (position == 0)
            {
                return default;
            }
            int length = I32(position);
            Range((long)position + 4, (long)length + 1);
            if (length < 0 || m_bytes[position + 4 + length] != 0)
            {
                throw new ArgumentException("Invalid Arrow FlatBuffers String.");
            }
            m_retainedBytes += length;
            if (m_retainedBytes > 64 * 1024 * 1024)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
            return ByteString.From(m_bytes.AsSpan(position + 4, length).ToArray());
        }

        private int Vector(int owner, int ordinal, int width, out int count)
        {
            int position = Reference(owner, ordinal);
            count = position == 0 ? 0 : I32(position);
            if (count < 0 || count > m_remaining)
            {
                throw new ArgumentException("Invalid or oversized Arrow vector.");
            }
            m_remaining -= count;
            if (position != 0)
            {
                Range((long)position + 4, (long)width * count);
            }
            return position;
        }

        private void Table(int table, int knownFields)
        {
            int vtable = VTable(table);
            int length = U16(vtable);
            for (int offset = 4 + knownFields * 2; offset < length; offset += 2)
            {
                if (U16(vtable + offset) != 0)
                {
                    throw new ServiceResultException(StatusCodes.BadNotSupported,
                        "The Arrow metadata table has an unsupported field.");
                }
            }
        }

        private int VTable(int table)
        {
            Range(table, 4);
            long position = (long)table - I32(table);
            Range(position, 4);
            int vtable = (int)position;
            int length = U16(vtable);
            int objectLength = U16(vtable + 2);
            if (length < 4 || length % 2 != 0 || objectLength < 4)
            {
                throw new ArgumentException("Invalid Arrow metadata vtable.");
            }
            Range(vtable, length);
            Range(table, objectLength);
            return vtable;
        }

        private int Slot(int table, int ordinal, int width)
        {
            int vtable = VTable(table);
            int offset = 4 + ordinal * 2;
            if (offset >= U16(vtable))
            {
                return 0;
            }
            int position = U16(vtable + offset);
            if (position == 0)
            {
                return 0;
            }
            if (position < 4 || position + width > U16(vtable + 2))
            {
                throw new ArgumentException("Arrow metadata field lies outside its table.");
            }
            return table + position;
        }

        private int Reference(int table, int ordinal, bool required = false)
        {
            int slot = Slot(table, ordinal, 4);
            if (slot == 0 || I32(slot) == 0)
            {
                return required ? throw new ArgumentException("A required Arrow table is absent.") : 0;
            }
            return Indirect(slot);
        }

        private int Indirect(int slot)
        {
            Range(slot, 4);
            uint relative = BinaryPrimitives.ReadUInt32LittleEndian(m_bytes.AsSpan(slot, 4));
            long target = slot + (long)relative;
            if (relative < 4)
            {
                throw new ArgumentException("Invalid Arrow relative offset.");
            }
            Range(target, 4);
            return (int)target;
        }

        private int Signed16(int table, int ordinal, int fallback = 0)
        {
            int slot = Slot(table, ordinal, 2);
            return slot == 0 ? fallback : BinaryPrimitives.ReadInt16LittleEndian(m_bytes.AsSpan(slot, 2));
        }
        private int Signed32(int table, int ordinal, int fallback = 0)
        {
            int slot = Slot(table, ordinal, 4);
            return slot == 0 ? fallback : I32(slot);
        }
        private long Signed64(int table, int ordinal)
        {
            int slot = Slot(table, ordinal, 8);
            return slot == 0 ? 0 : BinaryPrimitives.ReadInt64LittleEndian(m_bytes.AsSpan(slot, 8));
        }
        private byte Unsigned8(int table, int ordinal)
        {
            int slot = Slot(table, ordinal, 1);
            return slot == 0 ? (byte)0 : m_bytes[slot];
        }
        private int I32(int offset)
        {
            Range(offset, 4);
            return BinaryPrimitives.ReadInt32LittleEndian(m_bytes.AsSpan(offset, 4));
        }
        private int U16(int offset)
        {
            Range(offset, 2);
            return BinaryPrimitives.ReadUInt16LittleEndian(m_bytes.AsSpan(offset, 2));
        }
        private void Range(long offset, long length)
        {
            if (offset < 0 || length < 0 || offset > m_bytes.Length - length)
            {
                throw new ArgumentException("Arrow metadata points outside the schema document.");
            }
        }

        private readonly byte[] m_bytes;
        private int m_remaining = 100000;
        private long m_retainedBytes;
        private static readonly UTF8Encoding s_utf8 = new(false, true);
    }
}
