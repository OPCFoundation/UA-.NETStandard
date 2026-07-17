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

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Decides, for a <c>GetEndpoints</c> request handled by the local Server, which member of the
    /// <c>RedundantServerSet</c> a Client should be directed to.
    /// </summary>
    public interface IServerDirectionPolicy
    {
        /// <summary>
        /// Returns the ServerUri of the peer the Client should be directed to, or <c>null</c> when the local Server
        /// should serve the request itself. The decision is: eligibility by health <c>ServiceLevel</c> tier first
        /// (a peer in a strictly higher tier wins), then least load among the top tier (with the local Server
        /// included), with random tie-breaking among equally-loaded members and fail-to-self on a stale/unknown view.
        /// </summary>
        /// <param name="localServerUri">The local ServerUri.</param>
        /// <param name="localServiceLevel">The local health <c>ServiceLevel</c>.</param>
        /// <param name="localLoadWeight">The local load weight.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask<string?> SelectTargetServerUriAsync(
            string localServerUri,
            byte localServiceLevel,
            byte localLoadWeight,
            CancellationToken cancellationToken = default);
    }
}
