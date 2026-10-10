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

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Produces the canonical uniqueness key of a StructuredHistoryData
    /// entry (Part 11 §6.8.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ordinary raw history is unique by <c>SourceTimestamp</c>.
    /// Structured history is not: a variable may hold several structures
    /// with the same source timestamp, and the OPC UA specification
    /// leaves the definition of "the same entry" to the structure type.
    /// A key selector is the seam that supplies that definition to a
    /// historian provider.
    /// </para>
    /// <para>
    /// The returned key is combined with the source timestamp into a
    /// <see cref="HistoricalValueKey"/>. Insert, replace, update and
    /// remove all identify entries through that composite key, so a
    /// selector must be a pure, stable function of the uniqueness
    /// fields — the same structure content must always map to the same
    /// bytes, and two structures that differ in any uniqueness field
    /// must map to different bytes.
    /// </para>
    /// <para>
    /// Changing a uniqueness field of a stored entry therefore changes
    /// its identity. Replacing such an entry fails with
    /// <see cref="StatusCodes.BadNoEntryExists"/>; the client removes the
    /// old entry and inserts the new one instead.
    /// </para>
    /// </remarks>
    public interface IHistorianStructuredDataKeySelector
    {
        /// <summary>
        /// The structure fields that make an entry unique, in the order
        /// they contribute to the key. <c>SourceTimestamp</c> is always
        /// part of the identity and is therefore listed first by
        /// convention. Servers surface these names to explain why an
        /// update was rejected and to document the archive layout.
        /// </summary>
        ArrayOf<QualifiedName> UniquenessFields { get; }

        /// <summary>
        /// Builds the canonical uniqueness key for a structured value.
        /// The key covers every field of <see cref="UniquenessFields"/>
        /// except the source timestamp, which the caller combines with
        /// the returned bytes.
        /// </summary>
        /// <param name="value">The structured value being stored.</param>
        /// <param name="uniquenessKey">
        /// The canonical key; <see cref="ByteString.Empty"/> when the
        /// structure is unique by source timestamp alone.
        /// </param>
        /// <returns>
        /// <c>false</c> when <paramref name="value"/> does not carry the
        /// structure the selector understands; callers then reject the
        /// entry with <see cref="StatusCodes.BadTypeMismatch"/>.
        /// </returns>
        bool TryGetUniquenessKey(in DataValue value, out ByteString uniquenessKey);
    }
}
