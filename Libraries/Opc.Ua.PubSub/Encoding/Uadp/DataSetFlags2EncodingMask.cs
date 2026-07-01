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
    /// DataSetFlags2 byte of a UADP DataSetMessage. The low 4 bits
    /// encode the DataSetMessage <c>Type</c> (KeyFrame / DeltaFrame /
    /// Event / KeepAlive); two further bits enable per-message
    /// Timestamp and PicoSeconds fields.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.4">
    /// Part 14 §7.2.4.5.4 — UADP DataSetMessage Header</see>
    /// (Table 163). Only present when
    /// <see cref="DataSetFlags1EncodingMask.DataSetFlags2Enabled"/> is
    /// set in DataSetFlags1.
    /// </remarks>
#pragma warning disable CA1069 // Enums values should not be duplicated — None and KeyFrame both encode the zero
    // nibble; spec mandates KeyFrame as the zero pattern so the duplication is intentional.
#pragma warning disable CA2217 // Do not mark enums with FlagsAttribute — Table 163 uses both single-bit flags AND a
    // bitmask helper (MessageTypeMask = 0x0F); [Flags] reflects the spec semantics.
    [Flags]
    public enum DataSetFlags2EncodingMask : byte
    {
        /// <summary>
        /// No DataSetFlags2 bits set; the DataSetMessage is a KeyFrame
        /// (type value 0) with no per-message timestamp or picoseconds.
        /// </summary>
        None = 0,

        /// <summary>
        /// Bit pattern <c>0000</c> — KeyFrame DataSetMessage.
        /// </summary>
        KeyFrame = 0x00,

        /// <summary>
        /// Bit pattern <c>0001</c> — DeltaFrame DataSetMessage.
        /// </summary>
        DeltaFrame = 0x01,

        /// <summary>
        /// Bit pattern <c>0010</c> — Event DataSetMessage.
        /// </summary>
        Event = 0x02,

        /// <summary>
        /// Bit pattern <c>0011</c> — KeepAlive DataSetMessage.
        /// </summary>
        KeepAlive = 0x03,

        /// <summary>
        /// Mask isolating the low 4 bits which encode the
        /// <see cref="PubSubDataSetMessageType"/> wire value.
        /// </summary>
        MessageTypeMask = 0x0F,

        /// <summary>
        /// Bit 4 — per-message Timestamp enabled (UA <c>DateTime</c>).
        /// </summary>
        TimestampEnabled = 0x10,

        /// <summary>
        /// Bit 5 — per-message PicoSeconds enabled (UA
        /// <c>UInt16</c>).
        /// </summary>
        PicoSecondsEnabled = 0x20
    }
#pragma warning restore CA2217
#pragma warning restore CA1069

    /// <summary>
    /// Helpers for converting the DataSetMessage type nibble between
    /// the on-wire bit pattern and the
    /// <see cref="PubSubDataSetMessageType"/> enum.
    /// </summary>
    public static class DataSetFlags2EncodingMaskExtensions
    {
        /// <summary>
        /// Decodes the <see cref="PubSubDataSetMessageType"/> from the
        /// low 4 bits of the supplied raw byte. Reserved values 4-15
        /// report <see langword="false"/>.
        /// </summary>
        /// <param name="raw">Raw DataSetFlags2 byte from the wire.</param>
        /// <param name="messageType">Decoded message type.</param>
        /// <returns>
        /// <see langword="true"/> when the bits encode a supported
        /// DataSetMessage type; <see langword="false"/> otherwise.
        /// </returns>
        public static bool TryGetMessageType(byte raw, out PubSubDataSetMessageType messageType)
        {
            int bits = raw & (byte)DataSetFlags2EncodingMask.MessageTypeMask;
            switch (bits)
            {
                case 0:
                    messageType = PubSubDataSetMessageType.KeyFrame;
                    return true;
                case 1:
                    messageType = PubSubDataSetMessageType.DeltaFrame;
                    return true;
                case 2:
                    messageType = PubSubDataSetMessageType.Event;
                    return true;
                case 3:
                    messageType = PubSubDataSetMessageType.KeepAlive;
                    return true;
                default:
                    messageType = PubSubDataSetMessageType.KeyFrame;
                    return false;
            }
        }

        /// <summary>
        /// Encodes a <see cref="PubSubDataSetMessageType"/> as the
        /// 4-bit nibble that lives in
        /// <see cref="DataSetFlags2EncodingMask.MessageTypeMask"/>.
        /// </summary>
        /// <param name="messageType">Message type to translate.</param>
        /// <returns>The encoded bit pattern (0..3).</returns>
        public static byte EncodeMessageType(PubSubDataSetMessageType messageType)
        {
            return messageType switch
            {
                PubSubDataSetMessageType.KeyFrame => 0,
                PubSubDataSetMessageType.DeltaFrame => 1,
                PubSubDataSetMessageType.Event => 2,
                PubSubDataSetMessageType.KeepAlive => 3,
                _ => 0
            };
        }
    }
}
