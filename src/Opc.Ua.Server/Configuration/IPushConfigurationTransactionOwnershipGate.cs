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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional async extension of <see cref="IPushConfigurationTransactionCoordinator"/>
    /// that reserves cross-replica ownership of the single server-wide
    /// PushManagement transaction before a synchronous
    /// <see cref="IPushConfigurationTransactionCoordinator.Stage"/> call.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default per-server coordinator does not implement this interface:
    /// its transaction is process-local, so the synchronous
    /// <see cref="IPushConfigurationTransactionCoordinator.ValidateSessionCanParticipate"/>
    /// check is sufficient and no additional asynchronous work is required.
    /// A distributed (high-availability) coordinator that shares transaction
    /// ownership across a redundant server set implements this seam so it can
    /// acquire or renew a shared lease at an <see langword="await"/> boundary,
    /// keeping the coordinator's synchronous <c>Stage</c>/<c>Cancel</c>
    /// contract free of any sync-over-async call.
    /// </para>
    /// <para>
    /// <see cref="ConfigurationNodeManager"/> and <see cref="TrustList"/> call
    /// <see cref="AcquireTransactionOwnershipAsync"/> immediately before every
    /// operation that stages a change; a coordinator that does not implement
    /// the interface simply skips the call, leaving the non-distributed server
    /// behaviour unchanged.
    /// </para>
    /// </remarks>
    public interface IPushConfigurationTransactionOwnershipGate
    {
        /// <summary>
        /// Reserves (acquires or renews) cross-replica ownership of the
        /// server-wide PushManagement transaction for
        /// <paramref name="sessionId"/> so that the subsequent synchronous
        /// <see cref="IPushConfigurationTransactionCoordinator.Stage"/> starts
        /// or continues the transaction on this replica only.
        /// </summary>
        /// <param name="sessionId">
        /// The Session that is about to stage an operation.
        /// </param>
        /// <param name="cancellationToken">A token used to cancel the wait.</param>
        /// <exception cref="ServiceResultException">
        /// Thrown with <see cref="StatusCodes.BadTransactionPending"/> when
        /// another replica currently owns the active transaction.
        /// </exception>
        ValueTask AcquireTransactionOwnershipAsync(
            NodeId sessionId,
            CancellationToken cancellationToken = default);
    }
}
