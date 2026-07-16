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

namespace Opc.Ua.Client
{
    /// <summary>
    /// Resolves a redundant Server application URI to a connectable Endpoint.
    /// </summary>
    /// <remarks>
    /// See OPC 10000-4 §6.6.2.4.5.1 for non-transparent <c>RedundantServerSet</c> peer resolution via
    /// <c>FindServers</c>.
    /// </remarks>
    public interface IRedundantServerEndpointResolver
    {
        /// <summary>
        /// Resolves a redundant server URI to a configured endpoint.
        /// </summary>
        /// <param name="serverUri">The redundant server application URI.</param>
        /// <param name="currentEndpoint">The currently connected endpoint.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The resolved endpoint, or <see langword="null"/> if none is available.</returns>
        ValueTask<ConfiguredEndpoint?> ResolveAsync(
            string serverUri,
            ConfiguredEndpoint currentEndpoint,
            CancellationToken ct = default);
    }
}
