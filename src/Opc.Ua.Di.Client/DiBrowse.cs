// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
                    if (page.Count == 0)
                    {
                        // A server that hands out continuation points
                        // without references would never let the loop end.
                        break;
                    }
                    references.AddRange(page);
                }
            }
            finally
            {
                // OPC 10000-4 §5.9.3.2 requires releasing a continuation point
                // the client stops following - after an empty page, a
                // cancellation or a failed call alike. An exhausted point is
                // empty and needs no release.
                await session
                    .ReleaseContinuationPointAsync(continuationPoint, logger)
                    .ConfigureAwait(false);
            }
            return references;
        }
    }
}
