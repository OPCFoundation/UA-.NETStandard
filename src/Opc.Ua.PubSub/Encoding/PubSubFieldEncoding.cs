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
