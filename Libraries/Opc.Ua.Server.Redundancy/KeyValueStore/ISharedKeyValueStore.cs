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

namespace Opc.Ua.Server.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: minimal shared key/value backend used to replicate state across a
    /// <c>RedundantServerSet</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// OPC 10000-4 §6.6 standardizes redundancy behaviour, discovery data, ServiceLevel selection, and Failover
    /// actions; it does not define a shared storage protocol. This value-add extension is the lowest-level abstraction
    /// used by the distributed AddressSpace, session, subscription, continuation-point, nonce, and lease mirrors.
    /// </para>
    /// <para>
    /// Keys are opaque, ordinal strings. Values are <see cref="ByteString"/>
    /// payloads. Implementations must be safe for concurrent calls. External shared stores must be paired with an
    /// <see cref="IRecordProtector"/> for mirrored records that contain secrets or notifications.
    /// </para>
    /// </remarks>
    public interface ISharedKeyValueStore
    {
        /// <summary>
        /// Reads the value stored under <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key to read.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <c>Found = true</c> and the stored value when the key exists;
        /// otherwise <c>Found = false</c> and a null
        /// <see cref="ByteString"/>.
        /// </returns>
        ValueTask<(bool Found, ByteString Value)> TryGetAsync(string key, CancellationToken ct = default);

        /// <summary>
        /// Unconditionally writes <paramref name="value"/> under
        /// <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key to write.</param>
        /// <param name="value">The value to store.</param>
        /// <param name="ct">Cancellation token.</param>
        ValueTask SetAsync(string key, ByteString value, CancellationToken ct = default);

        /// <summary>
        /// Atomically writes <paramref name="value"/> under
        /// <paramref name="key"/> only when the current value matches
        /// <paramref name="expected"/>. A null <paramref name="expected"/>
        /// (<see cref="ByteString.IsNull"/>) requires the key to be absent.
        /// This is the "master write" / single-writer primitive used by the
        /// leader-election layer.
        /// </summary>
        /// <param name="key">The key to write.</param>
        /// <param name="expected">
        /// The value the key is expected to currently hold, or a null
        /// <see cref="ByteString"/> to require absence.
        /// </param>
        /// <param name="value">The value to store on success.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>true</c> when the swap succeeded.</returns>
        ValueTask<bool> CompareAndSwapAsync(string key, ByteString expected, ByteString value, CancellationToken ct = default);

        /// <summary>
        /// Removes <paramref name="key"/>.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns><c>true</c> when a value was removed.</returns>
        ValueTask<bool> DeleteAsync(string key, CancellationToken ct = default);

        /// <summary>
        /// Enumerates a snapshot of every key/value pair whose key starts
        /// with <paramref name="keyPrefix"/>. Used for hydration.
        /// </summary>
        /// <param name="keyPrefix">The key prefix to match (may be empty).</param>
        /// <param name="ct">Cancellation token.</param>
        IAsyncEnumerable<KeyValuePair<string, ByteString>> ScanAsync(string keyPrefix, CancellationToken ct = default);

        /// <summary>
        /// Streams changes for every key that starts with
        /// <paramref name="keyPrefix"/> until <paramref name="ct"/> is
        /// cancelled. Only changes that occur after the call are observed.
        /// </summary>
        /// <param name="keyPrefix">The key prefix to match (may be empty).</param>
        /// <param name="ct">Cancellation token that stops the watch.</param>
        IAsyncEnumerable<KeyValueChange> WatchAsync(string keyPrefix, CancellationToken ct = default);
    }
}
