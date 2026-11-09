/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Creates a candidate that borrows a NodeManager resource already in use.
    /// </summary>
    /// <remarks>
    /// The lifecycle calls <see cref="CreateBorrowedAsync"/> instead of
    /// <see cref="IAsyncNodeManagerFactory.CreateAsync"/> when this interface is implemented.
    /// The factory retains responsibility for cleanup until it declares the candidate lease.
    /// </remarks>
    public interface INodeManagerBorrowedResourceFactory : IAsyncNodeManagerFactory
    {
        /// <summary>
        /// Creates a private candidate and declares its cleanup lease.
        /// </summary>
        /// <param name="preparation">The candidate's staging server and declaration.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="cancellationToken">Cancels candidate creation.</param>
        /// <returns>A task that completes after candidate creation.</returns>
        ValueTask CreateBorrowedAsync(
            INodeManagerBorrowedResourcePreparation preparation,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Declares the candidate and cleanup lease before creating generation-bound resources.
    /// </summary>
    public interface INodeManagerBorrowedResourcePreparation
    {
        /// <summary>
        /// Gets the candidate's staging server.
        /// </summary>
        IServerInternal Server { get; }

        /// <summary>
        /// Transfers one candidate lease to the lifecycle.
        /// </summary>
        /// <param name="lease">The candidate and its cleanup lease.</param>
        /// <remarks>
        /// Declare the lease before assigning its NodeManager to a continuation point or creating
        /// another generation-bound resource. The lifecycle accepts cleanup ownership after it
        /// reserves both the lease and its candidate. It rolls back an accepted lease if declaration
        /// or later factory work fails. If the lease or candidate is already owned, or the candidate
        /// cannot be identified, the lifecycle rejects the declaration without accepting cleanup.
        /// A rejected second or late declaration also remains the caller's responsibility.
        /// The caller must not release a facade or lease owned by another candidate.
        /// </remarks>
        /// <exception cref="System.ArgumentNullException"><paramref name="lease"/> is null.</exception>
        /// <exception cref="NodeManagerAlreadyRegisteredException">
        /// The lease or candidate belongs to another registration.
        /// </exception>
        /// <exception cref="System.InvalidOperationException">
        /// The factory cannot accept another declaration, or the candidate is invalid.
        /// </exception>
        void DeclareCandidate(INodeManagerBorrowedResourceLease lease);
    }

    /// <summary>
    /// Owns the preparation and retirement cleanup of a candidate that borrows a live resource.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The candidate's address-space preparation must keep its changes private from users of the
    /// borrowed resource. The routing publication makes the prepared candidate visible; it does not
    /// call a separate provider commit callback.
    /// </para>
    /// <para>
    /// Rollback must undo partial preparation and release candidate-owned resources without deleting,
    /// detaching, or disposing the borrowed resource. Retirement must release candidate-owned
    /// resources after admitted work drains, without destroying the borrowed resource. A failed
    /// cleanup can be retried, so each operation must retain its unfinished work.
    /// </para>
    /// <para>
    /// The provider must keep the borrowed resource available to all of its users until they release
    /// it. The lifecycle waits for work admitted by this server; it cannot track external users.
    /// </para>
    /// </remarks>
    public interface INodeManagerBorrowedResourceLease
    {
        /// <summary>
        /// Gets the candidate owned by this lease.
        /// </summary>
        IAsyncNodeManager NodeManager { get; }

        /// <summary>
        /// Reverses preparation and releases an unpublished candidate.
        /// </summary>
        /// <param name="cancellationToken">Cancels cleanup.</param>
        /// <returns>A task that completes after candidate cleanup.</returns>
        ValueTask RollbackAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Releases a published candidate after its admitted work has drained.
        /// </summary>
        /// <param name="cancellationToken">Cancels cleanup.</param>
        /// <returns>A task that completes after candidate cleanup.</returns>
        ValueTask RetireAsync(CancellationToken cancellationToken = default);
    }
}
