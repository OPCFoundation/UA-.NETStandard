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

namespace Opc.Ua.Server
{
    /// <summary>
    /// The continuation points a session is holding on behalf of its client, for browses
    /// and for historical reads.
    /// </summary>
    /// <remarks>
    /// A continuation point survives between service calls, so the session owns the
    /// lifetime: saved points can be dropped when the per-session limit is reached, during
    /// immediate retirement, and when the session closes. Graceful retirement preserves
    /// Browse and participating history ownership until each saved or executing point is disposed.
    /// </remarks>
    public interface ISessionContinuationPoints
    {
        /// <summary>
        /// The number of browse continuation points the session will hold before it starts
        /// dropping the oldest.
        /// </summary>
        int MaxBrowse { get; }

        /// <summary>
        /// Saves a browse continuation point, dropping the oldest when the limit is reached.
        /// </summary>
        /// <param name="continuationPoint">The continuation point.</param>
        void SaveBrowse(ContinuationPoint continuationPoint);

        /// <summary>
        /// Restores and removes an available browse continuation point. The caller must
        /// dispose it or save its next page; restoring does not release generation ownership.
        /// </summary>
        /// <param name="continuationPoint">The identifier the client returned.</param>
        /// <returns>The continuation point, or <c>null</c> when it is not held.</returns>
        ContinuationPoint? RestoreBrowse(ByteString continuationPoint);

        /// <summary>
        /// Saves a history continuation point, dropping and disposing the oldest when the
        /// limit is reached.
        /// </summary>
        /// <param name="continuationPoint">The continuation point.</param>
        void SaveHistory(IHistoryContinuationPoint continuationPoint);

        /// <summary>
        /// Restores and removes an available history continuation point. The caller must
        /// dispose it or save its next page; participating history states keep their owners
        /// while checked out.
        /// </summary>
        /// <param name="continuationPoint">The identifier the client returned.</param>
        /// <returns>The continuation point, or <c>null</c> when it is not held.</returns>
        IHistoryContinuationPoint? RestoreHistory(ByteString continuationPoint);

        /// <summary>
        /// Invalidates points requiring a node manager that is going away, so nothing
        /// resumes against an address space that no longer exists. A currently executing
        /// Browse or participating history point remains owned by its request but cannot be saved again.
        /// </summary>
        /// <param name="nodeManager">The node manager being removed.</param>
        void RemoveForManager(IAsyncNodeManager nodeManager);
    }

    /// <summary>
    /// Optional ownership capability used to drain gracefully retired Browse sources and dependencies.
    /// </summary>
    public interface ISessionContinuationPointLifecycle
    {
        /// <summary>
        /// Raised after a Browse continuation releases its source, including after a restored
        /// point completes or fails. Subscribers must schedule cleanup outside the current request.
        /// </summary>
        event Action? BrowseContinuationPointsReleased;

        /// <summary>
        /// Reports saved and currently restored Browse continuations requiring the exact manager.
        /// Implementations must include dependencies by using <see cref="ContinuationPoint.RequiresManager"/>.
        /// Restoring a point transfers its use to the request without releasing any of its owners.
        /// </summary>
        bool HasBrowseForManager(IAsyncNodeManager nodeManager);
    }

    /// <summary>
    /// Optional ownership capability required when sessions participate in dynamic history-source retirement.
    /// </summary>
    public interface ISessionHistoryContinuationPointLifecycle
    {
        /// <summary>
        /// Raised after a history point releases its owners. Cleanup must be scheduled outside the current request.
        /// </summary>
        event Action? HistoryContinuationPointsReleased;

        /// <summary>
        /// Reports saved and checked-out history uses requiring the exact manager, including dependency owners.
        /// </summary>
        bool HasHistoryForManager(IAsyncNodeManager nodeManager);
    }
}
