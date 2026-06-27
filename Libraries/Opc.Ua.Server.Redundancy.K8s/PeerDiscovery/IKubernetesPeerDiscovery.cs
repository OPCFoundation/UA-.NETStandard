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

namespace Opc.Ua.Server.Redundancy.K8s
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: discovers OPC UA peer ServerUris from Kubernetes EndpointSlices.
    /// </summary>
    public interface IKubernetesPeerDiscovery
    {
        /// <summary>
        /// Raised when the discovered peer ServerUri set changes.
        /// </summary>
        event Action<ArrayOf<string>>? PeerServerUrisChanged;

        /// <summary>
        /// Gets the last discovered peer ServerUris.
        /// </summary>
        ArrayOf<string> PeerServerUris { get; }

        /// <summary>
        /// Refreshes peer discovery once.
        /// </summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The discovered peer ServerUris.</returns>
        ValueTask<ArrayOf<string>> RefreshAsync(CancellationToken ct = default);
    }
}
