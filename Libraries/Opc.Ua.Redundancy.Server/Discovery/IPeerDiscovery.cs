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

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: a source of redundant-server peers. Implementations resolve the
    /// current peer set from a mechanism (static configuration, DNS, an LDS(-ME), Kubernetes, …) and raise
    /// <see cref="PeersChanged"/> whenever the set changes so the redundancy metadata (and, when present, the
    /// CRDT gossip fabric) can be updated dynamically at runtime. Static configuration is the fallback when no
    /// dynamic mechanism is available.
    /// </summary>
    public interface IPeerDiscovery
    {
        /// <summary>
        /// Raised when the discovered peer set changes between refreshes.
        /// </summary>
        event Action<ArrayOf<DiscoveredPeer>>? PeersChanged;

        /// <summary>
        /// Gets the most recently discovered peer set.
        /// </summary>
        ArrayOf<DiscoveredPeer> Peers { get; }

        /// <summary>
        /// Refreshes the peer set once, raising <see cref="PeersChanged"/> if it changed.
        /// </summary>
        /// <param name="ct">A cancellation token.</param>
        /// <returns>The discovered peer set.</returns>
        ValueTask<ArrayOf<DiscoveredPeer>> RefreshAsync(CancellationToken ct = default);
    }
}
