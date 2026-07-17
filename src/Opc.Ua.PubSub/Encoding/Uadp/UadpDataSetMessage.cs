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

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// UADP concrete <see cref="PubSubDataSetMessage"/>. Adds the UADP
    /// per-DataSetMessage header bits (field encoding, configured
    /// size, optional picoseconds).
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.4">
    /// Part 14 §7.2.4.5.4 — UADP DataSetMessage Header</see>. The
    /// <see cref="ContentMask"/> selects which optional fields are
    /// emitted; <see cref="FieldEncoding"/> selects between the three
    /// field-encoding bit patterns of Table 162.
    /// </remarks>
    public sealed record UadpDataSetMessage : PubSubDataSetMessage
    {
        /// <summary>
        /// Mask of optional DataSetMessage header fields to emit on
        /// encode and require on decode.
        /// </summary>
        public UadpDataSetMessageContentMask ContentMask { get; init; }

        /// <summary>
        /// Per-DataSetMessage fractional-second component, populated
        /// when <see cref="UadpDataSetMessageContentMask.PicoSeconds"/>
        /// is enabled.
        /// </summary>
        public ushort PicoSeconds { get; init; }

        /// <summary>
        /// When non-zero, fixes the encoded payload size to the
        /// specified byte count via trailing-zero padding (RawData
        /// encoding only). Used by deterministic transports that
        /// require constant-size messages per
        /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.4">
        /// Part 14 §7.2.4.5.4</see>.
        /// </summary>
        public uint ConfiguredSize { get; init; }

        /// <summary>
        /// Field-encoding selector. Drives the two field-encoding bits
        /// of DataSetFlags1 (Variant / RawData / DataValue).
        /// </summary>
        public PubSubFieldEncoding FieldEncoding { get; init; } = PubSubFieldEncoding.Variant;

        /// <summary>
        /// Per-field content mask honoured when
        /// <see cref="FieldEncoding"/> is
        /// <see cref="PubSubFieldEncoding.DataValue"/>. The encoder
        /// suppresses any <c>DataValue</c> member whose corresponding
        /// bit is not set; the decoder populates the matching
        /// <see cref="DataSetField"/> properties only for set bits.
        /// </summary>
        /// <remarks>
        /// Implements the per-field selector of
        /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.3.1.3">
        /// Part 14 §6.3.1.3 DataSetFieldContentMask</see>. The default
        /// <see cref="DataSetFieldContentMask.None"/> preserves
        /// pre-Phase-15 behaviour (all four <c>DataValue</c> members
        /// emitted unconditionally).
        /// </remarks>
        public DataSetFieldContentMask FieldContentMask { get; init; }
            = DataSetFieldContentMask.None;
    }
}
