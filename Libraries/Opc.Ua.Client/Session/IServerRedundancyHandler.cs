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
    /// Handles server redundancy detection and failover target
    /// selection for a managed session.
    /// </summary>
    /// <remarks>
    /// See OPC UA Part 4 §6.6.2 for the server redundancy model.
    /// </remarks>
    public interface IServerRedundancyHandler
    {
        /// <summary>
        /// Reads redundancy information from the connected session.
        /// </summary>
        /// <param name="session">The active session to read from.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// A <see cref="ServerRedundancyInfo"/> describing the server's
        /// redundancy configuration.
        /// </returns>
        ValueTask<ServerRedundancyInfo> FetchRedundancyInfoAsync(
            ISession session,
            CancellationToken ct = default);

        /// <summary>
        /// Selects the best server to fail over to from the redundant
        /// server set.
        /// </summary>
        /// <param name="redundancyInfo">
        /// The redundancy information previously obtained via
        /// <see cref="FetchRedundancyInfoAsync"/>.
        /// </param>
        /// <param name="currentEndpoint">
        /// The endpoint the session is currently connected to.
        /// </param>
        /// <returns>
        /// A <see cref="ConfiguredEndpoint"/> for the best failover
        /// target, or <see langword="null"/> if no failover target is
        /// available (e.g., transparent redundancy or no suitable
        /// backup server).
        /// </returns>
        ConfiguredEndpoint? SelectFailoverTarget(
            ServerRedundancyInfo redundancyInfo,
            ConfiguredEndpoint currentEndpoint);
    }
}
