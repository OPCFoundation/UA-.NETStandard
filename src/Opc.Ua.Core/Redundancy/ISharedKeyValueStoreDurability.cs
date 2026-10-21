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

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Optional qualification for stable-storage commits by an <see cref="ISharedKeyValueStore"/>.
    /// Linearizability or process-local storage alone does not provide this guarantee.
    /// </summary>
    /// <remarks>
    /// Successful writes, successful compare-and-swap changes and deletions survive supported restarts
    /// with intact storage. Recovery never treats missing or corrupt committed state as an empty store.
    /// Failed mutations may have committed; callers must reconcile, not infer non-execution or replay work.
    /// This interface supplies neither a restart-safe clock nor replication, stale-restore detection
    /// or an atomic transaction with an external business effect.
    /// </remarks>
    public interface ISharedKeyValueStoreDurability
    {
        /// <summary>
        /// Initializes and verifies the storage needed to uphold the durable-commit contract.
        /// Unavailable, corrupt or uncertain storage throws instead of reporting successful readiness.
        /// </summary>
        ValueTask VerifyDurabilityAsync(CancellationToken cancellationToken = default);
    }
}
