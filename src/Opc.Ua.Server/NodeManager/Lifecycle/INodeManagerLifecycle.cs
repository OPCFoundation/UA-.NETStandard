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
    /// Prepares and publishes transactions over stable NodeManager slots.
    /// </summary>
    /// <remarks>
    /// Prepare an addition, replacement, removal, or batch with <see cref="PrepareAsync"/>, then commit
    /// the returned owner. Commit waits through reconciliation and readiness, but not generation drain.
    /// Its result describes the authoritative published revision, registrations, slots, post-commit
    /// failures, and asynchronous retirement. Dispose an uncommitted owner to release its candidates.
    /// Request callbacks use the same context-free transaction API as external callers.
    /// </remarks>
    public interface INodeManagerLifecycle
    {
        /// <summary>
        /// Gets the revision of the currently published NodeManager routing view.
        /// </summary>
        long Revision { get; }

        /// <summary>
        /// Gets the stable slots for the live application NodeManagers owned by this provider.
        /// Built-in diagnostics, configuration, and core NodeManagers are excluded.
        /// </summary>
        ArrayOf<NodeManagerSlot> Slots { get; }

        /// <summary>
        /// Gets a snapshot of live application registrations, including managers composed before startup.
        /// Built-in diagnostics, configuration, and core NodeManagers are excluded.
        /// </summary>
        ArrayOf<NodeManagerRegistration> Registrations { get; }

        /// <summary>
        /// Gets whether the server has started its ordered shutdown sequence.
        /// The lifecycle rejects additions and replacements and tears down remaining registrations itself.
        /// </summary>
        bool IsShuttingDown { get; }

        /// <summary>
        /// Gets whether this lifecycle supports preparing an unpublished transaction.
        /// </summary>
        bool SupportsPreparedPublication { get; }

        /// <summary>
        /// Gets whether publication admission can be captured across multiple prepared units.
        /// </summary>
        bool SupportsPublicationIsolation { get; }

        /// <summary>
        /// Captures the current routing, type, and factory revisions without reserving publication.
        /// </summary>
        /// <returns>The capture holding the observed revisions.</returns>
        INodeManagerPublicationCapture CapturePublication();

        /// <summary>
        /// Prepares additions, replacements, and retirements without publishing them.
        /// The returned owner must be committed or asynchronously disposed.
        /// </summary>
        /// <param name="changes">The additions, replacements, and retirements to prepare.</param>
        /// <param name="cancellationToken">The token to cancel the operation.</param>
        /// <returns>The unpublished prepared unit.</returns>
        ValueTask<IPreparedNodeManagerBatch> PrepareAsync(
            ArrayOf<NodeManagerBatchChange> changes,
            CancellationToken cancellationToken = default);
    }
}
