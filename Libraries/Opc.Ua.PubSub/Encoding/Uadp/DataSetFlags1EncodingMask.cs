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
    /// DataSetFlags1 byte that prefixes every UADP DataSetMessage. Bits
    /// 1-2 encode the field encoding (Variant / RawData / DataValue);
    /// the remaining bits enable optional per-DataSet fields and the
    /// secondary <see cref="DataSetFlags2EncodingMask"/> byte.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.4">
    /// Part 14 §7.2.4.5.4 — UADP DataSetMessage Header</see>
    /// (Table 162). The decoder rejects DataSetMessages whose
    /// <see cref="MessageIsValid"/> bit is zero.
    /// </remarks>
#pragma warning disable CA1069 // Enums values should not be duplicated — None and FieldEncoding00 both encode "no
                               // bits set"; spec encodes Variant as the zero pattern so the duplication is intentional.
#pragma warning disable CA2217 // Do not mark enums with FlagsAttribute — Table 162 uses both single-bit flags AND a
                               // bitmask helper (FieldEncodingMask = 0x06); [Flags] reflects the spec semantics.
    [Flags]
    public enum DataSetFlags1EncodingMask : byte
    {
        /// <summary>
        /// No DataSetFlags1 bits set. A DataSetMessage with a zero
        /// flags byte is invalid (it lacks <see cref="MessageIsValid"/>).
        /// </summary>
        None = 0,

        /// <summary>
        /// Bit 0 — MessageIsValid. Decoders MUST drop DataSetMessages
        /// without this bit.
        /// </summary>
        MessageIsValid = 0x01,

        /// <summary>
        /// Bits 1-2 = <c>00</c> — fields encoded as UA
        /// <see cref="Variant"/> values.
        /// </summary>
        FieldEncoding00 = 0x00,

        /// <summary>
        /// Bits 1-2 = <c>01</c> — fields encoded as <c>RawData</c>
        /// (the type is taken from
        /// <see cref="FieldMetaData.BuiltInType"/>).
        /// </summary>
        FieldEncoding01 = 0x02,

        /// <summary>
        /// Bits 1-2 = <c>10</c> — fields encoded as
        /// <see cref="DataValue"/>.
        /// </summary>
        FieldEncoding10 = 0x04,

        /// <summary>
        /// Mask isolating the field-encoding bits.
        /// </summary>
        FieldEncodingMask = 0x06,

        /// <summary>
        /// Bit 3 — SequenceNumber enabled (UA <c>UInt16</c>).
        /// </summary>
        SequenceNumberEnabled = 0x08,

        /// <summary>
        /// Bit 4 — Status enabled (UA <c>StatusCode</c>).
        /// </summary>
        StatusEnabled = 0x10,

        /// <summary>
        /// Bit 5 — ConfigurationVersion MajorVersion enabled (UA
        /// <c>UInt32</c>).
        /// </summary>
        MajorVersionEnabled = 0x20,

        /// <summary>
        /// Bit 6 — ConfigurationVersion MinorVersion enabled (UA
        /// <c>UInt32</c>).
        /// </summary>
        MinorVersionEnabled = 0x40,

        /// <summary>
        /// Bit 7 — DataSetFlags2 enabled. When set, the
        /// <see cref="DataSetFlags2EncodingMask"/> byte follows
        /// <see cref="DataSetFlags1EncodingMask"/> in the
        /// DataSetMessage header.
        /// </summary>
        DataSetFlags2Enabled = 0x80
    }
#pragma warning restore CA2217
#pragma warning restore CA1069

    /// <summary>
    /// Helpers for translating the DataSetFlags1 field-encoding bits to
    /// and from the cross-encoding <see cref="PubSubFieldEncoding"/>
    /// enum.
    /// </summary>
    public static class DataSetFlags1EncodingMaskExtensions
    {
        /// <summary>
        /// Returns the <see cref="PubSubFieldEncoding"/> encoded in the
        /// <see cref="DataSetFlags1EncodingMask.FieldEncodingMask"/>
        /// bits of the supplied raw byte. Reserved value <c>11</c>
        /// reports <see langword="false"/>.
        /// </summary>
        /// <param name="raw">Raw DataSetFlags1 byte from the wire.</param>
        /// <param name="encoding">Decoded field encoding when supported.</param>
        /// <returns>
        /// <see langword="true"/> when the bits encode a supported
        /// field encoding; <see langword="false"/> for the reserved
        /// value.
        /// </returns>
        public static bool TryGetFieldEncoding(byte raw, out PubSubFieldEncoding encoding)
        {
            int bits = raw & (byte)DataSetFlags1EncodingMask.FieldEncodingMask;
            switch (bits)
            {
                case 0x00:
                    encoding = PubSubFieldEncoding.Variant;
                    return true;
                case 0x02:
                    encoding = PubSubFieldEncoding.RawData;
                    return true;
                case 0x04:
                    encoding = PubSubFieldEncoding.DataValue;
                    return true;
                default:
                    encoding = PubSubFieldEncoding.Variant;
                    return false;
            }
        }

        /// <summary>
        /// Returns the bit pattern (0x00 / 0x02 / 0x04) that encodes
        /// the supplied <see cref="PubSubFieldEncoding"/> in
        /// <see cref="DataSetFlags1EncodingMask.FieldEncodingMask"/>.
        /// </summary>
        /// <param name="encoding">Field encoding to translate.</param>
        /// <returns>The encoded bit pattern.</returns>
        public static byte EncodeFieldEncoding(PubSubFieldEncoding encoding)
        {
            return encoding switch
            {
                PubSubFieldEncoding.Variant => (byte)DataSetFlags1EncodingMask.FieldEncoding00,
                PubSubFieldEncoding.RawData => (byte)DataSetFlags1EncodingMask.FieldEncoding01,
                PubSubFieldEncoding.DataValue => (byte)DataSetFlags1EncodingMask.FieldEncoding10,
                _ => (byte)DataSetFlags1EncodingMask.FieldEncoding00
            };
        }
    }
}
