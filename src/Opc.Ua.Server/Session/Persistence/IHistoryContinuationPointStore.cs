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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Durable single-use store for portable HistoryRead continuation points.
    /// </summary>
    public interface IHistoryContinuationPointStore
    {
        /// <summary>
        /// Persists a continuation point before it is returned to a client.
        /// </summary>
        ValueTask StoreAsync(
            HistoryContinuationPointEnvelope envelope,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Atomically claims a continuation point for one resume operation.
        /// </summary>
        ValueTask<bool> TryTakeAsync(
            NodeId ownerSessionId,
            Guid id,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Schedules removal of a continuation point without blocking a
        /// synchronous session cleanup path. Implementations must not throw;
        /// cleanup failures are reported through their diagnostics channel.
        /// </summary>
        void ScheduleRemove(
            NodeId ownerSessionId,
            Guid id);

        /// <summary>
        /// Loads every portable continuation owned by a mirrored session.
        /// </summary>
        ValueTask<ArrayOf<HistoryContinuationPointEnvelope>> LoadAsync(
            NodeId ownerSessionId,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Optional server-internal access to the configured history continuation store.
    /// </summary>
    public interface IHistoryContinuationPointStoreProvider
    {
        /// <summary>
        /// Configured portable HistoryRead continuation store, if any.
        /// </summary>
        IHistoryContinuationPointStore? HistoryContinuationPointStore { get; }
    }
}
