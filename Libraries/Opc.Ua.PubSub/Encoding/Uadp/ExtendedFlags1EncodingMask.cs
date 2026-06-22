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

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// ExtendedFlags1 byte of a UADP NetworkMessage. The low 3 bits
    /// (<see cref="PublisherIdTypeMask"/>) select the on-wire encoding
    /// type for the <see cref="PublisherId"/>; the remaining bits enable
    /// optional header sections.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.2.4">
    /// Part 14 §A.2.2.4 — UADP NetworkMessage Header Layout</see>
    /// (Table 154). The PublisherId type bits are: Byte=0,
    /// UInt16=1, UInt32=2, UInt64=3, String=4. Value 5 is reserved.
    /// </remarks>
#pragma warning disable CA2217 // Do not mark enums with FlagsAttribute — Table 158 uses both single-bit flags AND a
                               // bitmask helper (PublisherIdTypeMask = 0x07); [Flags] reflects the spec semantics.
    [Flags]
    public enum ExtendedFlags1EncodingMask : byte
    {
        /// <summary>
        /// No ExtendedFlags1 bits set; PublisherId type defaults to
        /// <see cref="PublisherIdType.Byte"/> (value 0 in the
        /// <see cref="PublisherIdTypeMask"/>).
        /// </summary>
        None = 0,

        /// <summary>
        /// Bits 0-2 mask — selects the on-wire PublisherId type per
        /// Table 159.
        /// </summary>
        PublisherIdTypeMask = 0x07,

        /// <summary>
        /// Bit 3 — DataSetClassId enabled. When set, a
        /// <c>Guid</c>-typed DataSetClassId follows the PublisherId.
        /// </summary>
        DataSetClassIdEnabled = 0x08,

        /// <summary>
        /// Bit 4 — Security enabled. When set, the message carries a
        /// security header / footer (UADP signed and/or encrypted).
        /// </summary>
        SecurityEnabled = 0x10,

        /// <summary>
        /// Bit 5 — Timestamp enabled. When set, the extended
        /// NetworkMessage header carries an OPC UA <c>DateTime</c>
        /// network-wide timestamp.
        /// </summary>
        TimestampEnabled = 0x20,

        /// <summary>
        /// Bit 6 — PicoSeconds enabled. When set, the extended
        /// NetworkMessage header carries a <see cref="ushort"/>
        /// fractional-time field complementing the
        /// <see cref="TimestampEnabled"/> value.
        /// </summary>
        PicoSecondsEnabled = 0x40,

        /// <summary>
        /// Bit 7 — ExtendedFlags2 enabled. When set, the
        /// <see cref="ExtendedFlags2EncodingMask"/> byte follows
        /// <see cref="ExtendedFlags1EncodingMask"/> in the header.
        /// </summary>
        ExtendedFlags2Enabled = 0x80
    }
#pragma warning restore CA2217

    /// <summary>
    /// Helpers for converting between the on-wire UADP PublisherId type
    /// nibble (Part 14 §A.2.2.4 Table 159) and the cross-mapping
    /// <see cref="PublisherIdType"/> enum.
    /// </summary>
    public static class ExtendedFlags1EncodingMaskExtensions
    {
        /// <summary>
        /// Extracts the <see cref="PublisherIdType"/> from the
        /// <see cref="ExtendedFlags1EncodingMask.PublisherIdTypeMask"/>
        /// bits of the raw byte. Returns <see langword="false"/> when
        /// the bit pattern is reserved (values 5, 6 and 7).
        /// </summary>
        /// <param name="raw">Raw ExtendedFlags1 byte from the wire.</param>
        /// <param name="type">Decoded PublisherId type when supported.</param>
        /// <returns>
        /// <see langword="true"/> when the bits encode a supported
        /// PublisherId type; <see langword="false"/> for reserved
        /// values.
        /// </returns>
        public static bool TryGetPublisherIdType(byte raw, out PublisherIdType type)
        {
            int bits = raw & (byte)ExtendedFlags1EncodingMask.PublisherIdTypeMask;
            switch (bits)
            {
                case 0:
                    type = PublisherIdType.Byte;
                    return true;
                case 1:
                    type = PublisherIdType.UInt16;
                    return true;
                case 2:
                    type = PublisherIdType.UInt32;
                    return true;
                case 3:
                    type = PublisherIdType.UInt64;
                    return true;
                case 4:
                    type = PublisherIdType.String;
                    return true;
                default:
                    type = PublisherIdType.Byte;
                    return false;
            }
        }

        /// <summary>
        /// Returns the low-3-bit type indicator that represents the
        /// supplied <see cref="PublisherIdType"/> in the
        /// <see cref="ExtendedFlags1EncodingMask.PublisherIdTypeMask"/>
        /// nibble.
        /// </summary>
        /// <param name="type">PublisherId type to encode.</param>
        /// <returns>The 3-bit encoding (0..4).</returns>
        public static byte EncodePublisherIdType(PublisherIdType type)
        {
            return type switch
            {
                PublisherIdType.Byte => 0,
                PublisherIdType.UInt16 => 1,
                PublisherIdType.UInt32 => 2,
                PublisherIdType.UInt64 => 3,
                PublisherIdType.String => 4,
                PublisherIdType.Guid => throw new InvalidOperationException(
                    "Guid PublisherId is reserved in the UADP mapping; use JSON mapping."),
                _ => 0
            };
        }
    }
}
