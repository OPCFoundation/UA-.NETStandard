// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Streams browse pages without retaining the complete result or leaking abandoned continuation points.
    /// </summary>
    public static class SessionBrowseExtensions
    {
        /// <summary>
        /// Enumerates references in server order and releases the current continuation point on every early exit.
        /// Empty continuation pages are followed, but a server that repeatedly makes no progress is rejected.
        /// </summary>
        /// <exception cref="ArgumentNullException">The session is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The requested page size is outside 1..1000.</exception>
        /// <exception cref="ServiceResultException">Browsing fails or repeatedly makes no progress.</exception>
        public static IAsyncEnumerable<ReferenceDescription> EnumerateReferencesAsync(
            this ISessionClient session,
            NodeId nodeId,
            NodeId referenceTypeId,
            BrowseDirection direction = BrowseDirection.Forward,
            bool includeSubtypes = true,
            uint nodeClassMask = 0,
            uint pageSize = 100,
            CancellationToken ct = default)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (pageSize is 0 or > 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(pageSize));
            }
            return EnumerateCoreAsync(session, nodeId, referenceTypeId, direction, includeSubtypes,
                nodeClassMask, pageSize, ct);
        }

        /// <summary>
        /// Owns continuation points for one lazy enumeration.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private static async IAsyncEnumerable<ReferenceDescription> EnumerateCoreAsync(
            ISessionClient session,
            NodeId nodeId,
            NodeId referenceTypeId,
            BrowseDirection direction,
            bool includeSubtypes,
            uint nodeClassMask,
            uint pageSize,
            [EnumeratorCancellation] CancellationToken ct)
        {
            ILogger logger = session.MessageContext.Telemetry.CreateLogger(nameof(SessionBrowseExtensions));
            ByteString continuation = default;
            int emptyPages = 0;
            try
            {
                (_, continuation, ArrayOf<ReferenceDescription> references) = await session.BrowseAsync(
                    null, null, nodeId, pageSize, direction, referenceTypeId, includeSubtypes, nodeClassMask, ct)
                    .ConfigureAwait(false);
                while (true)
                {
                    for (int index = 0; index < references.Count; index++)
                    {
                        ct.ThrowIfCancellationRequested();
                        yield return references[index];
                    }
                    if (continuation.IsEmpty)
                    {
                        yield break;
                    }
                    emptyPages = references.IsEmpty ? emptyPages + 1 : 0;
                    if (emptyPages >= 16)
                    {
                        throw new ServiceResultException(StatusCodes.BadNoData,
                            "The server repeatedly returned empty browse pages with a continuation point.");
                    }
                    ct.ThrowIfCancellationRequested();
                    (_, continuation, references) = await session.BrowseNextAsync(null, false, continuation, ct)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                await session.ReleaseContinuationPointAsync(continuation, logger).ConfigureAwait(false);
            }
        }
    }
}
