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
    /// First byte of a UADP NetworkMessage. The low nibble carries the
    /// UADP <c>Version</c> (currently <c>1</c>); the high nibble carries
    /// the four boolean flags that select optional header sections.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.2.4">
    /// Part 14 §A.2.2.4 — UADP NetworkMessage Header Layout</see>
    /// (Table 157). Helpers <see cref="UadpFlagsEncodingMaskExtensions.Combine"/>
    /// and <see cref="UadpFlagsEncodingMaskExtensions.Split"/> isolate the
    /// version and flag halves so callers do not bit-twiddle manually.
    /// </remarks>
    [Flags]
    public enum UadpFlagsEncodingMask : byte
    {
        /// <summary>
        /// No flags set; raw byte equals the bare UADP version nibble.
        /// </summary>
        None = 0,

        /// <summary>
        /// Bit 4 — PublisherId enabled. When set, the NetworkMessage header
        /// carries the publisher identity in the type selected by
        /// <see cref="ExtendedFlags1EncodingMask.PublisherIdTypeMask"/>.
        /// </summary>
        PublisherIdEnabled = 0x10,

        /// <summary>
        /// Bit 5 — GroupHeader enabled. When set, the NetworkMessage
        /// header carries the optional <c>GroupFlags</c> /
        /// <c>WriterGroupId</c> / <c>GroupVersion</c> /
        /// <c>NetworkMessageNumber</c> / <c>SequenceNumber</c> fields.
        /// </summary>
        GroupHeaderEnabled = 0x20,

        /// <summary>
        /// Bit 6 — PayloadHeader enabled. When set, the NetworkMessage
        /// payload starts with a <c>Count</c> byte followed by an array
        /// of DataSetWriterIds.
        /// </summary>
        PayloadHeaderEnabled = 0x40,

        /// <summary>
        /// Bit 7 — ExtendedFlags1 enabled. When set, the NetworkMessage
        /// header carries the ExtendedFlags1 byte.
        /// </summary>
        ExtendedFlags1Enabled = 0x80
    }

    /// <summary>
    /// Helpers for splitting and combining the UADP version nibble and
    /// the <see cref="UadpFlagsEncodingMask"/> flag bits stored in the
    /// same byte.
    /// </summary>
    public static class UadpFlagsEncodingMaskExtensions
    {
        /// <summary>
        /// Mask isolating the UADP <c>Version</c> low nibble.
        /// </summary>
        public const byte VersionMask = 0x0F;

        /// <summary>
        /// Mask isolating the <see cref="UadpFlagsEncodingMask"/> high
        /// nibble.
        /// </summary>
        public const byte FlagsMask = 0xF0;

        /// <summary>
        /// Combines a UADP protocol version and a flag set into the
        /// single header byte that lives at offset 0 of every UADP
        /// NetworkMessage.
        /// </summary>
        /// <param name="version">
        /// UADP version nibble (0..15). Values outside the nibble are
        /// truncated to fit.
        /// </param>
        /// <param name="flags">Flag set to combine with the version.</param>
        /// <returns>The combined header byte.</returns>
        public static byte Combine(byte version, UadpFlagsEncodingMask flags)
        {
            return (byte)((version & VersionMask) | ((byte)flags & FlagsMask));
        }

        /// <summary>
        /// Splits the combined UADP version + flag header byte into the
        /// two halves.
        /// </summary>
        /// <param name="raw">The combined header byte.</param>
        /// <returns>
        /// A tuple of <c>(version, flags)</c> with the UADP version in
        /// the low nibble and the flag set in the high nibble.
        /// </returns>
        public static (byte Version, UadpFlagsEncodingMask Flags) Split(byte raw)
        {
            return ((byte)(raw & VersionMask), (UadpFlagsEncodingMask)(raw & FlagsMask));
        }
    }
}
