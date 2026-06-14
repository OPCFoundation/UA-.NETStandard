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

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Rest
{
    /// <summary>
    /// Extension methods for REST API sessions.
    /// </summary>
    public static class RestApiSessionExtensions
    {
        /// <summary>
        /// Creates, opens, and returns a session-bound REST API wrapper.
        /// </summary>
        /// <param name="client">The REST API client.</param>
        /// <param name="options">The session options.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The activated REST API session.</returns>
        public static async Task<IRestApiSession> UseSessionAsync(
            this IRestApiClient client,
            RestApiSessionOptions options,
            CancellationToken ct = default)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var session = new RestApiSession(client, options);
            await session.OpenAsync(ct).ConfigureAwait(false);
            return session;
        }
    }
}
