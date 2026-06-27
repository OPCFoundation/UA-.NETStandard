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

namespace Opc.Ua.Server.Redundancy
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: bridges a local node graph (<see cref="ILocalAddressSpace"/>) to a
    /// shared <see cref="INodeStateStore"/> so address-space topology and
    /// variable values replicate across server replicas.
    /// </summary>
    /// <remarks>
    /// A synchronizer runs in one of two roles, selected by the
    /// leader-election predicate supplied at construction:
    /// <list type="bullet">
    /// <item>
    /// <b>Writer (leader):</b> captures committed local changes and writes
    /// them through to the store (shared read, master write).
    /// </item>
    /// <item>
    /// <b>Reader (standby):</b> applies topology and value changes from the
    /// store change-feed to its local graph and never writes.
    /// </item>
    /// </list>
    /// Active/active multi-writer with conflict resolution is layered on top
    /// later (CRDT); this single-writer model is the active/passive default.
    /// </remarks>
    public interface IAddressSpaceSynchronizer : IAsyncDisposable
    {
        /// <summary>
        /// <c>true</c> when this synchronizer currently acts as the writer
        /// (leader).
        /// </summary>
        bool IsWriter { get; }

        /// <summary>
        /// Seeds the store from the local graph when the store is empty and
        /// this replica is the writer; otherwise hydrates the local graph
        /// from the store. Call once before <see cref="Start"/>.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        ValueTask SeedOrHydrateAsync(CancellationToken ct = default);

        /// <summary>
        /// Starts background replication: outbound capture for a writer, or
        /// the inbound apply loop for a reader.
        /// </summary>
        void Start();
    }
}
