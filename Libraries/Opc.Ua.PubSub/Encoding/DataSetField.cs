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
    /// Single field within a <see cref="PubSubDataSetMessage"/>. Carries
    /// the field name, its Variant value, per-field status code (for
    /// DataValue field encoding), source timestamp, and the chosen
    /// field encoding so encoders can round-trip without consulting
    /// metadata for the encoding-mode bit alone.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/5.3.2">
    /// Part 14 §5.3.2 DataSetMessage</see>. A
    /// <see cref="StatusCode"/> of <see cref="StatusCodes.Good"/> with
    /// <see cref="PubSubFieldEncoding.Variant"/> or
    /// <see cref="PubSubFieldEncoding.RawData"/> indicates the encoder
    /// omitted the explicit DataValue wrapper.
    /// </remarks>
    public sealed record DataSetField
    {
        /// <summary>
        /// Field name as declared in the DataSetMetaData. Empty when the
        /// field is anonymous in RawData encoding.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Field value carried as a <see cref="Variant"/>; the inner
        /// Built-In type matches the metadata declaration.
        /// </summary>
        public Variant Value { get; init; }

        /// <summary>
        /// Per-field status code; meaningful only for
        /// <see cref="PubSubFieldEncoding.DataValue"/> encoding. Defaults
        /// to <see cref="StatusCodes.Good"/>.
        /// </summary>
        public StatusCode StatusCode { get; init; } = (StatusCode)StatusCodes.Good;

        /// <summary>
        /// Per-field source timestamp; meaningful only for
        /// <see cref="PubSubFieldEncoding.DataValue"/> encoding.
        /// </summary>
        public DateTimeUtc SourceTimestamp { get; init; }

        /// <summary>
        /// Field encoding chosen by the producing writer.
        /// </summary>
        public PubSubFieldEncoding Encoding { get; init; }
    }
}
