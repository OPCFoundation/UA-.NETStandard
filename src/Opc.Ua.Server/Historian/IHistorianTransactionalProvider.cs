/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Opt-in capability for providers that can commit a batch of
    /// history updates atomically (all-or-nothing). The default
    /// <see cref="IHistorianDataProvider"/> contract is per-value
    /// best-effort; providers that offer stronger guarantees implement
    /// this interface so the dispatcher (or callers) can prefer the
    /// atomic path when available.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The method signatures mirror <see cref="IHistorianDataProvider"/>'s
    /// per-value update methods but document an atomic semantics: if any
    /// value cannot be applied, all values are rolled back and the
    /// returned status list contains the per-value failure code; the
    /// archive is left in its pre-call state.
    /// </para>
    /// <para>
    /// Implementations should still return a status for every input
    /// value (so callers can identify which value caused the rollback)
    /// but every successful value gets <see cref="StatusCodes.Good"/>
    /// or <see cref="StatusCodes.GoodEntryInserted"/> /
    /// <see cref="StatusCodes.GoodEntryReplaced"/> only when the entire
    /// batch committed.
    /// </para>
    /// </remarks>
    public interface IHistorianTransactionalProvider
    {
        /// <summary>
        /// Inserts a batch of values atomically.
        /// </summary>
        ValueTask<IList<StatusCode>> InsertAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken ct);

        /// <summary>
        /// Replaces a batch of values atomically.
        /// </summary>
        ValueTask<IList<StatusCode>> ReplaceAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken ct);

        /// <summary>
        /// Upserts a batch of values atomically.
        /// </summary>
        ValueTask<IList<StatusCode>> UpdateAtomicAsync(
            HistorianOperationContext context,
            NodeId nodeId,
            IList<DataValue> values,
            CancellationToken ct);
    }
}
