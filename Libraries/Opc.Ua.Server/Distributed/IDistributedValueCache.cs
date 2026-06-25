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

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server.Distributed
{
    /// <summary>
    /// A read/write value cache backed by a distributed
    /// <see cref="INodeStateStore"/>. Lets a variable's read/write callbacks
    /// cache the last value they observed and serve the last value with a
    /// freshness bound from the shared store, so a replica can answer reads
    /// without re-querying the live source (and a standby can serve the last
    /// value written by the active replica).
    /// </summary>
    public interface IDistributedValueCache
    {
        /// <summary>
        /// Writes the latest value for a node into the cache.
        /// </summary>
        /// <param name="nodeId">The variable node identifier.</param>
        /// <param name="value">The value to cache.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask CacheAsync(NodeId nodeId, in DataValue value, CancellationToken ct = default);

        /// <summary>
        /// Reads the last cached value and reports whether it is still fresh
        /// (its source timestamp is within <paramref name="maxAge"/>).
        /// </summary>
        /// <param name="nodeId">The variable node identifier.</param>
        /// <param name="maxAge">The freshness bound.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>Fresh = true</c> with the value when a value exists and is
        /// within <paramref name="maxAge"/>; otherwise the value may still be
        /// returned (when present) but <c>Fresh = false</c>.
        /// </returns>
        ValueTask<(bool Fresh, DataValue Value)> TryGetAsync(
            NodeId nodeId,
            TimeSpan maxAge,
            CancellationToken ct = default);
    }
}
