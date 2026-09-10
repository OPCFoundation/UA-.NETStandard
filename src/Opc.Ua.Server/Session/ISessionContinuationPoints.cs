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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The continuation points a session is holding on behalf of its client, for browses
    /// and for historical reads.
    /// </summary>
    /// <remarks>
    /// A continuation point survives between service calls, so the session owns the
    /// lifetime: points are dropped when the per-session limit is reached, when the node
    /// manager that issued them goes away, and when the session closes. A dropped history
    /// point is disposed.
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
        /// Restores and removes a browse continuation point.
        /// </summary>
        /// <param name="continuationPoint">The identifier the client returned.</param>
        /// <returns>The continuation point, or <c>null</c> when it is not held.</returns>
        ContinuationPoint? RestoreBrowse(ByteString continuationPoint);

        /// <summary>
        /// Saves a history continuation point, dropping and disposing the oldest when the
        /// limit is reached. Ownership transfers to the session when this method is called;
        /// the implementation disposes the point if it cannot retain it.
        /// </summary>
        /// <param name="continuationPoint">The continuation point.</param>
        void SaveHistory(IHistoryContinuationPoint continuationPoint);

        /// <summary>
        /// Saves and durably mirrors a portable history continuation point. Ownership transfers
        /// to the session when this method is called; the implementation disposes the point if
        /// persistence fails.
        /// </summary>
        ValueTask SaveHistoryAsync(
            IHistoryContinuationPoint continuationPoint,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Restores and removes a history continuation point, transferring ownership to the
        /// caller. The caller must dispose it or save it back into the session.
        /// </summary>
        /// <param name="continuationPoint">The identifier the client returned.</param>
        /// <returns>The continuation point, or <c>null</c> when it is not held.</returns>
        IHistoryContinuationPoint? RestoreHistory(ByteString continuationPoint);

        /// <summary>
        /// Releases and disposes a history continuation point without resuming it.
        /// Portable records are scheduled for durable removal.
        /// </summary>
        bool ReleaseHistory(ByteString continuationPoint);

        /// <summary>
        /// Atomically claims and removes a portable history continuation point, transferring
        /// ownership to the caller. The caller must dispose it, save a successor, or restore it
        /// when the resumed operation does not complete.
        /// </summary>
        ValueTask<IHistoryContinuationPoint?> RestoreHistoryAsync(
            ByteString continuationPoint,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Drops every point issued by a node manager that is going away, so nothing
        /// resumes against an address space that no longer exists.
        /// </summary>
        /// <param name="nodeManager">The node manager being removed.</param>
        void RemoveForManager(IAsyncNodeManager nodeManager);
    }
}
