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
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Opc.Ua.SchemaRegistry.Formats
{
    internal sealed class ArrowSchemaWriter : IDisposable
    {
        private ArrowSchemaWriter()
        {
            m_writer = new BinaryWriter(m_stream, Encoding.UTF8, leaveOpen: true);
        }

        public static ByteString Write(ArrowIpcSchemaContentDataType schema)
        {
            using var writer = new ArrowSchemaWriter();
            writer.m_writer.Write(0u);
            int root = writer.Table(
                Scalar(0, 2, schema.MetadataVersion),
                Scalar(1, 1, 1),
                Reference(2, () => writer.Schema(schema)),
                Reference(4, () => writer.Metadata(schema.MessageMetadata)));
            writer.Patch32(0, root);
            writer.Align(8);
            byte[] metadata = writer.m_stream.ToArray();
            using var document = new MemoryStream();
            using (var output = new BinaryWriter(document, Encoding.UTF8, leaveOpen: true))
            {
                output.Write(uint.MaxValue);
                output.Write(metadata.Length);
                output.Write(metadata);
            }
            return ByteString.From(document.ToArray());
        }

        public void Dispose()
        {
            m_writer.Dispose();
            m_stream.Dispose();
        }

        private int Schema(ArrowIpcSchemaContentDataType schema)
        {
            return Table(
                Scalar(0, 2, schema.Endianness),
                Reference(1, () => Fields(schema.Fields)),
                Reference(2, () => Metadata(schema.Metadata)),
                Reference(3, () => Integers(schema.Features)));
        }

        private int Fields(ArrayOf<ArrowIpcFieldDataType> fields)
        {
            Count(fields.Count);
            var values = new List<Func<int>>();
            foreach (ArrowIpcFieldDataType field in fields)
            {
                values.Add(() => Field(field));
            }
            return References(values);
        }

        private int Field(ArrowIpcFieldDataType field)
        {
            var fields = new List<Cell>
            {
                Scalar(1, 1, field.Nullable ? 1 : 0),
                Scalar(2, 1, field.Type.Code),
                Reference(3, () => Type(field.Type)),
                Reference(5, () => Fields(field.Children)),
                Reference(6, () => Metadata(field.Metadata))
            };
            if (field.HasName)
            {
                fields.Add(Reference(0, () => Bytes(ByteString.From(s_utf8.GetBytes(field.Name!)))));
            }
            if (field.Dictionary is not null)
            {
                fields.Add(Reference(4, () => Dictionary(field.Dictionary)));
            }
            return Table(fields.ToArray());
        }

        private int Dictionary(ArrowIpcDictionaryDataType dictionary)
        {
            var fields = new List<Cell>
            {
                Scalar(0, 8, dictionary.Id),
                Scalar(2, 1, dictionary.Ordered ? 1 : 0),
                Scalar(3, 2, dictionary.DictionaryKind)
            };
            if (dictionary.HasIndexType)
            {
                fields.Add(Reference(1, () => Table(
                    Scalar(0, 4, dictionary.IndexBitWidth),
                    Scalar(1, 1, dictionary.IndexIsSigned ? 1 : 0))));
            }
            return Table(fields.ToArray());
        }

        private int Type(ArrowIpcTypeDataType type)
        {
            switch (type.Code)
            {
                case 2:
                    return Table(Scalar(0, 4, type.BitWidth), Scalar(1, 1, type.IsSigned ? 1 : 0));
                case 3:
                    return Table(Scalar(0, 2, type.Precision));
                case 7:
                    return Table(Scalar(0, 4, type.Precision), Scalar(1, 4, type.Scale), Scalar(2, 4, type.BitWidth));
                case 8:
                case 11:
                case 18:
                    return Table(Scalar(0, 2, type.Unit));
                case 9:
                    return Table(Scalar(0, 2, type.Unit), Scalar(1, 4, type.BitWidth));
                case 10:
                    return type.HasTimezone
                        ? Table(Scalar(0, 2, type.Unit),
                            Reference(1, () => Bytes(ByteString.From(s_utf8.GetBytes(type.Timezone!)))))
                        : Table(Scalar(0, 2, type.Unit));
                case 14:
                    return type.HasTypeIds
                        ? Table(Scalar(0, 2, type.Mode), Reference(1, () => Integers(type.TypeIds)))
                        : Table(Scalar(0, 2, type.Mode));
                case 15:
                    return Table(Scalar(0, 4, type.ByteWidth));
                case 16:
                    return Table(Scalar(0, 4, type.ListSize));
                case 17:
                    return Table(Scalar(0, 1, type.KeysSorted ? 1 : 0));
                default:
                    return Table();
            }
        }

        private int Metadata(ArrayOf<ArrowKeyValueDataType> values)
        {
            Count(values.Count);
            var references = new List<Func<int>>();
            foreach (ArrowKeyValueDataType pair in values)
            {
                if (pair is null)
                {
                    throw new ArgumentException("An Arrow metadata entry cannot be null.");
                }
                references.Add(() =>
                {
                    var fields = new List<Cell>();
                    if (!pair.Key.IsNull)
                    {
                        fields.Add(Reference(0, () => Bytes(pair.Key)));
                    }
                    if (!pair.Value.IsNull)
                    {
                        fields.Add(Reference(1, () => Bytes(pair.Value)));
                    }
                    return Table(fields.ToArray());
                });
            }
            return References(references);
        }

        private int Table(params Cell[] cells)
        {
            Array.Sort(cells, (a, b) => a.Ordinal.CompareTo(b.Ordinal));
            int count = cells.Length == 0 ? 0 : cells[^1].Ordinal + 1;
            Align(2);
            int vtable = Position;
            Zeros(4 + count * 2);
            Align(8);
            int table = Position;
            m_writer.Write(table - vtable);
            var slots = new int[cells.Length];
            for (int index = 0; index < cells.Length; index++)
            {
                Cell cell = cells[index];
                Align(cell.Width);
                slots[index] = Position;
                for (int b = 0; b < cell.Width; b++)
                {
                    m_writer.Write(unchecked((byte)(cell.Value >> (b * 8))));
                }
            }
            Patch16(vtable, checked((ushort)(4 + count * 2)));
            Patch16(vtable + 2, checked((ushort)(Position - table)));
            for (int index = 0; index < cells.Length; index++)
            {
                Cell cell = cells[index];
                Patch16(vtable + 4 + cell.Ordinal * 2, checked((ushort)(slots[index] - table)));
                if (cell.Reference is not null)
                {
                    int target = cell.Reference();
                    Patch32(slots[index], target - slots[index]);
                }
            }
            Bound();
            return table;
        }

        private int References(List<Func<int>> values)
        {
            Align(4);
            int position = Position;
            m_writer.Write(values.Count);
            Zeros(checked(values.Count * 4));
            for (int index = 0; index < values.Count; index++)
            {
                int slot = position + 4 + index * 4;
                Patch32(slot, values[index]() - slot);
            }
            Bound();
            return position;
        }

        private int Integers(ArrayOf<int> values)
        {
            Reserve(4L + values.Count * 4L);
            Align(4);
            int position = Position;
            m_writer.Write(values.Count);
            foreach (int value in values)
            {
                m_writer.Write(value);
            }
            Bound();
            return position;
        }

        private int Integers(ArrayOf<long> values)
        {
            Reserve(8L + values.Count * 8L);
            Align(4);
            if ((Position + 4) % 8 != 0)
            {
                Zeros(4);
            }
            int position = Position;
            m_writer.Write(values.Count);
            foreach (long value in values)
            {
                m_writer.Write(value);
            }
            Bound();
            return position;
        }

        private int Bytes(ByteString bytes)
        {
            Reserve(8L + bytes.Length);
            Align(4);
            int position = Position;
            m_writer.Write(bytes.Length);
            m_writer.Write(bytes.ToArray());
            m_writer.Write((byte)0);
            Bound();
            return position;
        }

        private void Align(int alignment)
        {
            while (Position % alignment != 0)
            {
                m_writer.Write((byte)0);
            }
        }

        private void Zeros(int count)
        {
            Reserve(count);
            for (int index = 0; index < count; index++)
            {
                m_writer.Write((byte)0);
            }
            Bound();
        }

        private void Patch16(int offset, ushort value)
        {
            int end = Position;
            m_stream.Position = offset;
            m_writer.Write(value);
            m_stream.Position = end;
        }

        private void Patch32(int offset, int value)
        {
            int end = Position;
            m_stream.Position = offset;
            m_writer.Write(value);
            m_stream.Position = end;
        }

        private void Bound()
        {
            if (m_stream.Length > 16 * 1024 * 1024 - 8)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
        }

        private void Reserve(long size)
        {
            if (size < 0 || m_stream.Length + size > 16 * 1024 * 1024 - 8)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
        }

        private static void Count(int count)
        {
            if (count > 100000)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded);
            }
        }

        private int Position => checked((int)m_stream.Position);

        private static Cell Scalar(int ordinal, int width, long value) => new(ordinal, width, value, null);
        private static Cell Reference(int ordinal, Func<int> reference) => new(ordinal, 4, 0, reference);
        private sealed record Cell(int Ordinal, int Width, long Value, Func<int>? Reference);

        private readonly MemoryStream m_stream = new();
        private readonly BinaryWriter m_writer;
        private static readonly UTF8Encoding s_utf8 = new(false, true);
    }
}
