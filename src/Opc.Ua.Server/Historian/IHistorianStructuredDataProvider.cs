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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Opt-in capability for providers that store StructuredHistoryData
    /// (Part 11 §6.8.3, <c>UpdateStructureDataDetails</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This interface is deliberately <strong>update only</strong>.
    /// Structured entries are read back through the ordinary history read
    /// paths — <see cref="IHistorianDataProvider.ReadRawAsync"/>,
    /// <see cref="IHistorianModifiedProvider.ReadModifiedAsync"/> and
    /// <see cref="IHistorianAtTimeProvider"/> — so there is exactly one
    /// read pipeline for raw and structured history and clients page
    /// through both with the same continuation points.
    /// </para>
    /// <para>
    /// The difference to <see cref="IHistorianDataProvider"/> is
    /// identity. Raw history is unique by <c>SourceTimestamp</c>;
    /// structured history is unique by the composite
    /// <see cref="HistoricalValueKey"/> built from the source timestamp
    /// and the canonical uniqueness key of an
    /// <see cref="IHistorianStructuredDataKeySelector"/>. A variable can
    /// therefore hold several structured entries at one instant.
    /// </para>
    /// <para>
    /// Per-entry, best-effort status semantics apply, exactly as for
    /// <see cref="IHistorianDataProvider"/>:
    /// <list type="bullet">
    ///   <item><c>Insert</c> fails with
    ///     <see cref="StatusCodes.BadEntryExists"/> when the composite key
    ///     is already present.</item>
    ///   <item><c>Replace</c> fails with
    ///     <see cref="StatusCodes.BadNoEntryExists"/> when the composite
    ///     key is absent — including when the client changed one of the
    ///     uniqueness fields, because that produces a different entry.
    ///     Such an edit is expressed as <c>Remove</c> + <c>Insert</c>.</item>
    ///   <item><c>Update</c> is an upsert on the composite key.</item>
    ///   <item><c>Remove</c> deletes the entries identified by the
    ///     composite keys of the supplied values and fails with
    ///     <see cref="StatusCodes.BadNoEntryExists"/> for keys that are
    ///     not stored.</item>
    ///   <item>Every prior version of a replaced, updated or removed
    ///     entry is retained in modified history when the provider also
    ///     implements <see cref="IHistorianModifiedProvider"/>.</item>
    ///   <item>Values that do not carry the structure understood by the
    ///     node's selector are rejected with
    ///     <see cref="StatusCodes.BadTypeMismatch"/>.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public interface IHistorianStructuredDataProvider
    {
        /// <summary>
        /// Returns the uniqueness-key selector used for the node. Nodes
        /// that are not registered for structured history report the
        /// timestamp-only default, which is the raw-history rule.
        /// </summary>
        /// <param name="nodeId">The historizing variable.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask<IHistorianStructuredDataKeySelector> GetKeySelectorAsync(
            NodeId nodeId,
            CancellationToken ct);

        /// <summary>
        /// Inserts structured entries; fails per-entry with
        /// <see cref="StatusCodes.BadEntryExists"/> when the composite key
        /// is already stored.
        /// </summary>
        ValueTask<HistorianUpdateOutcome<DataValue>> InsertStructuredDataAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct);

        /// <summary>
        /// Replaces structured entries; fails per-entry with
        /// <see cref="StatusCodes.BadNoEntryExists"/> when the composite
        /// key is not stored.
        /// </summary>
        ValueTask<HistorianUpdateOutcome<DataValue>> ReplaceStructuredDataAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct);

        /// <summary>
        /// Upserts structured entries — insert when the composite key is
        /// absent, replace otherwise.
        /// </summary>
        ValueTask<HistorianUpdateOutcome<DataValue>> UpdateStructuredDataAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct);

        /// <summary>
        /// Removes the structured entries identified by the composite
        /// keys of the supplied values.
        /// </summary>
        ValueTask<HistorianUpdateOutcome<DataValue>> RemoveStructuredDataAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            ArrayOf<DataValue> values,
            CancellationToken ct);
    }
}
