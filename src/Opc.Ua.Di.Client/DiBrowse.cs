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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;

namespace Opc.Ua.Di.Client
{
    /// <summary>
    /// Browses one node and follows its continuation points, so a server that
    /// paginates the references returns all of them.
    /// </summary>
    internal static class DiBrowse
    {
        /// <summary>
        /// Returns every reference of the node. A node the server reports a
        /// bad status for (it no longer exists or cannot be browsed) has
        /// none. A failed service call, such as a lost session or a timeout,
        /// propagates, and so does a continuation point the server rejects
        /// halfway, which would otherwise pass for a complete result.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static async ValueTask<List<ReferenceDescription>> BrowseAllAsync(
            ISession session,
            BrowseDescription nodeToBrowse,
            ILogger logger,
            CancellationToken ct)
        {
            BrowseResponse response = await session
                .BrowseAsync(
                    requestHeader: null,
                    view: null,
                    requestedMaxReferencesPerNode: 0,
                    nodesToBrowse: new[] { nodeToBrowse }.ToArrayOf(),
                    ct: ct)
                .ConfigureAwait(false);

            var references = new List<ReferenceDescription>();
            if (response.Results.Count == 0 ||
                StatusCode.IsBad(response.Results[0].StatusCode))
            {
                return references;
            }

            BrowseResult result = response.Results[0];
            references.AddRange(result.References);
            ByteString continuationPoint = result.ContinuationPoint;
            try
            {
                while (!continuationPoint.IsEmpty)
                {
                    (_, continuationPoint, ArrayOf<ReferenceDescription> page) = await session
                        .BrowseNextAsync(null, false, continuationPoint, ct)
                        .ConfigureAwait(false);
                    if (page.Count == 0 && !continuationPoint.IsEmpty)
                    {
                        // A server that hands out continuation points
                        // without references would never let the loop end.
                        await session
                            .ReleaseContinuationPointAsync(continuationPoint, logger)
                            .ConfigureAwait(false);
                        break;
                    }
                    references.AddRange(page);
                }
            }
            catch (OperationCanceledException) when (!continuationPoint.IsEmpty)
            {
                // OPC 10000-4 §5.9.3.2: a client that stops following a
                // continuation point releases it.
                await session
                    .ReleaseContinuationPointAsync(continuationPoint, logger)
                    .ConfigureAwait(false);
                throw;
            }
            return references;
        }
    }
}
