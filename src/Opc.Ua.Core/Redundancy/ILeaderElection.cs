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

namespace Opc.Ua.Redundancy
{
    /// <summary>
    /// Determines whether this replica is the writer (leader) in an
    /// active/passive or single-writer active/active deployment. Supply the
    /// <see cref="IsLeader"/> predicate to an address-space synchronizer so
    /// only the leader writes to the shared store ("shared read, master
    /// write"), and to an <c>IServiceLevelProvider</c> so the leader advertises
    /// the highest OPC UA <c>ServiceLevel</c>.
    /// </summary>
    public interface ILeaderElection : IAsyncDisposable
    {
        /// <summary>
        /// <c>true</c> when this replica currently holds leadership.
        /// </summary>
        bool IsLeader { get; }

        /// <summary>
        /// Raised when leadership is gained (<c>true</c>) or lost
        /// (<c>false</c>).
        /// </summary>
        event Action<bool>? LeadershipChanged;

        /// <summary>
        /// Attempts to acquire or renew leadership once and returns the
        /// resulting leadership state. Safe to call repeatedly.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        ValueTask<bool> TryAcquireOrRenewAsync(CancellationToken ct = default);

        /// <summary>
        /// Starts the background acquire/renew loop (if any).
        /// </summary>
        void Start();
    }
}
