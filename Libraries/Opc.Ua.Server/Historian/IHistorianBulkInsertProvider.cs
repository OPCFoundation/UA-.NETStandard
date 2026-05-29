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
    /// Opt-in capability for providers that can persist samples for many
    /// nodes in a single round-trip more efficiently than N back-to-back
    /// <see cref="IHistorianDataProvider.InsertAsync"/> calls.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The framework's auto-capture pipeline (see
    /// <c>HistorianCaptureSink</c>) prefers this when the resolved
    /// provider implements it; otherwise it falls back to per-node
    /// <see cref="IHistorianDataProvider.InsertAsync"/>.
    /// </para>
    /// <para>
    /// Implementations should amortise per-call overhead (lock
    /// acquisition, transaction setup, remote round-trips) across the
    /// whole batch. The bundled <c>InMemoryHistorianProvider</c>
    /// implementation acquires its lock once per <c>InsertBatchAsync</c>
    /// rather than once per node.
    /// </para>
    /// <para>
    /// Per-value status follows the same semantics as
    /// <see cref="IHistorianDataProvider.InsertAsync"/> — best-effort,
    /// one <see cref="StatusCode"/> per input value per node.
    /// </para>
    /// </remarks>
    public interface IHistorianBulkInsertProvider
    {
        /// <summary>
        /// Bulk-inserts values for one or more nodes.
        /// </summary>
        /// <param name="context">The operation context.</param>
        /// <param name="batch">
        /// Map of historizing variable <see cref="NodeId"/> to the values
        /// to insert for that node, in ascending source-timestamp order.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// A map of <see cref="NodeId"/> to per-value status list, with
        /// one entry per node in the input <paramref name="batch"/>.
        /// </returns>
        ValueTask<IReadOnlyDictionary<NodeId, IList<StatusCode>>> InsertBatchAsync(
            HistorianOperationContext context,
            IReadOnlyDictionary<NodeId, IList<DataValue>> batch,
            CancellationToken ct);
    }
}
