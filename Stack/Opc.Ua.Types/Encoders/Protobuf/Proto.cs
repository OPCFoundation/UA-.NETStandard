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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// Provides low-level Protobuf wire-format read and write helpers.
    /// </summary>
    internal static class Proto
    {
        /// <summary>
        /// Reserved Protobuf field number carrying a union's switch (discriminator) value. Chosen well
        /// above the positional member range and outside Protobuf's reserved 19000-19999 range so it
        /// never collides with a real field. See ProtobufEncoder.WriteSwitchField.
        /// </summary>
        public const int UnionSwitchField = 60000;

        /// <summary>
        /// Reserved Protobuf field number carrying a structure's optional-field presence mask. See
        /// ProtobufEncoder.WriteEncodingMask.
        /// </summary>
        public const int UnionMaskField = 60001;

        /// <summary>
        /// Writes a Protobuf field tag for the supplied field number and wire type.
        /// </summary>
        /// <param name = "w">The binary writer that receives wire-format bytes.</param>
        /// <param name = "field">The Protobuf field number to write.</param>
        /// <param name = "wire">The Protobuf wire type to write.</param>
        public static void WriteTag(BinaryWriter w, int field, int wire)
        {
            WriteVarint(w, ((ulong)field << 3) | (uint)wire);
        }

        /// <summary>
        /// Writes an unsigned Protobuf varint value.
        /// </summary>
        /// <param name = "w">The binary writer that receives wire-format bytes.</param>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        public static void WriteVarint(BinaryWriter w, ulong v)
        {
            while (v >= 0x80)
            {
                w.Write((byte)(v | 0x80));
                v >>= 7;
            }

            w.Write((byte)v);
        }

        /// <summary>
        /// Writes a signed integer using the Protobuf varint representation used by this reference codec.
        /// </summary>
        /// <param name = "w">The binary writer that receives wire-format bytes.</param>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        public static void WriteSignedVarint(BinaryWriter w, long v)
        {
            WriteVarint(w, unchecked((ulong)v));
        }

        /// <summary>
        /// Writes a 32-bit fixed-width Protobuf value.
        /// </summary>
        /// <param name = "w">The binary writer that receives wire-format bytes.</param>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        public static void WriteFixed32(BinaryWriter w, uint v)
        {
            w.Write(v);
        }

        /// <summary>
        /// Writes a 64-bit fixed-width Protobuf value.
        /// </summary>
        /// <param name = "w">The binary writer that receives wire-format bytes.</param>
        /// <param name = "v">The input required by this experimental codec helper.</param>
        public static void WriteFixed64(BinaryWriter w, ulong v)
        {
            w.Write(v);
        }

        /// <summary>
        /// Writes a length-delimited Protobuf byte sequence.
        /// </summary>
        /// <param name = "w">The binary writer that receives wire-format bytes.</param>
        /// <param name = "b">The input required by this experimental codec helper.</param>
        public static void WriteBytes(BinaryWriter w, ReadOnlySpan<byte> b)
        {
            WriteVarint(w, (ulong)b.Length);
            w.Write(b);
        }

        /// <summary>
        /// Writes a UTF-8 Protobuf string as a length-delimited field value.
        /// </summary>
        /// <param name = "w">The binary writer that receives wire-format bytes.</param>
        /// <param name = "s">The first value index in the Arrow segment.</param>
        public static void WriteString(BinaryWriter w, string s)
        {
            WriteBytes(w, Encoding.UTF8.GetBytes(s));
        }

        /// <summary>
        /// Parses a Protobuf message buffer into positional field records.
        /// </summary>
        /// <param name = "buffer">The encoded payload buffer to decode.</param>
        /// <returns>The parsed Protobuf message.</returns>
        public static ProtoMessage Parse(ReadOnlyMemory<byte> buffer)
        {
            var msg = new ProtoMessage();
            int p = 0;
            var span = buffer.Span;
            while (p < span.Length)
            {
                ulong tag = ReadVarint(span, ref p);
                int field = (int)(tag >> 3);
                int wire = (int)(tag & 7);
                switch (wire)
                {
                    case 0:
                        msg.Fields.Add(new ProtoField(field, wire, ReadVarint(span, ref p), default, 0, 0));
                        break;
                    case 1:
                        if (p + 8 > span.Length)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadDecodingError,
                                "Truncated Protobuf fixed64 value."
                            );
                        }
                        ulong f64 = BinaryPrimitives.ReadUInt64LittleEndian(span[p..]);
                        p += 8;
                        msg.Fields.Add(new ProtoField(field, wire, 0, default, 0, f64));
                        break;
                    case 2:
                        long rawLen = (long)ReadVarint(span, ref p);
                        if (rawLen < 0 || rawLen > span.Length - p)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadDecodingError,
                                "Truncated or invalid Protobuf length-delimited field."
                            );
                        }
                        int len = (int)rawLen;
                        msg.Fields.Add(new ProtoField(field, wire, 0, buffer.Slice(p, len), 0, 0));
                        p += len;
                        break;
                    case 5:
                        if (p + 4 > span.Length)
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadDecodingError,
                                "Truncated Protobuf fixed32 value."
                            );
                        }
                        uint f32 = BinaryPrimitives.ReadUInt32LittleEndian(span[p..]);
                        p += 4;
                        msg.Fields.Add(new ProtoField(field, wire, 0, default, f32, 0));
                        break;
                    default:
                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError,
                            $"Unsupported Protobuf wire type {wire}."
                        );
                }
            }

            return msg;
        }

        /// <summary>
        /// Reads an unsigned Protobuf varint value from a span.
        /// </summary>
        /// <param name = "span">The source span containing encoded bytes.</param>
        /// <param name = "p">The current read offset, updated as bytes are consumed.</param>
        /// <returns>The decoded unsigned varint value.</returns>
        public static ulong ReadVarint(ReadOnlySpan<byte> span, ref int p)
        {
            ulong v = 0;
            int shift = 0;
            while (p < span.Length)
            {
                byte b = span[p++];
                v |= (ulong)(b & 0x7f) << shift;
                if ((b & 0x80) == 0)
                {
                    return v;
                }

                shift += 7;
            }

            throw new ServiceResultException(
                StatusCodes.BadDecodingError,
                "Truncated Protobuf varint."
            );
        }

        /// <summary>
        /// Decodes UTF-8 bytes from a Protobuf length-delimited string value.
        /// </summary>
        /// <param name = "bytes">The byte sequence to encode or decode.</param>
        /// <returns>The decoded UTF-8 string.</returns>
        public static string String(ReadOnlyMemory<byte> bytes)
        {
            return Encoding.UTF8.GetString(bytes.Span);
        }
    }
}
