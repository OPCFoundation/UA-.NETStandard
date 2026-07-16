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
    /// Optional seam consulted by <c>GetEndpoints</c> to direct a Client to a different member of a
    /// <c>RedundantServerSet</c> (an extension beyond OPC 10000-4 §6.6). It is opt-in and gated: normal discovery is
    /// unaffected, and the standard client-driven <c>RedundantServerArray</c> / <c>ServiceLevel</c> selection remains
    /// the authoritative Failover mechanism.
    /// </summary>
    public interface IGetEndpointsDirector
    {
        /// <summary>
        /// Decides whether a <c>GetEndpoints</c> request should be answered with a peer Server's endpoints instead of
        /// the local Server's.
        /// </summary>
        /// <param name="endpointUrl">The URL the Client used to reach the discovery endpoint.</param>
        /// <param name="localEndpoints">The endpoints the local Server would otherwise return.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>
        /// <c>Redirect = true</c> and the peer's <c>Endpoints</c> when the request should be directed to a peer;
        /// otherwise <c>Redirect = false</c> and the local Server serves its own endpoints.
        /// </returns>
        ValueTask<(bool Redirect, ArrayOf<EndpointDescription> Endpoints)> TryGetDirectedEndpointsAsync(
            string? endpointUrl,
            ArrayOf<EndpointDescription> localEndpoints,
            CancellationToken cancellationToken = default);
    }
}
