/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Per-field encoding selected by the DataSetFlags1 field-encoding
    /// bits of a UADP DataSetMessage. Determines whether a field is
    /// serialised as a plain <see cref="Variant"/>, as raw built-in
    /// bytes, or wrapped in a full <see cref="DataValue"/> envelope
    /// (with status code and timestamps).
    /// </summary>
    /// <remarks>
    /// Implements the field-encoding selector of
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.4">
    /// Part 14 §7.2.4.5.4 DataSetMessage header / DataSetFlags1</see>.
    /// The numeric values match the on-wire bit values so casts between
    /// the enum and the bit-field are lossless.
    /// </remarks>
    public enum PubSubFieldEncoding
    {
        /// <summary>
        /// Variant encoding — each field is written as a full
        /// <see cref="Variant"/> with its built-in type marker.
        /// </summary>
        Variant = 0,

        /// <summary>
        /// RawData encoding — each field is written as the bare
        /// built-in payload bytes; the receiver consults metadata to
        /// recover the type. Required for Annex A.2.1.7 fixed periodic
        /// data layouts.
        /// </summary>
        RawData = 1,

        /// <summary>
        /// DataValue encoding — each field is wrapped in a
        /// <see cref="DataValue"/> envelope carrying value, status code,
        /// source timestamp, and server timestamp.
        /// </summary>
        DataValue = 2
    }
}
