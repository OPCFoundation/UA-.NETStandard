/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Decides which acknowledgements a session sends with its next Publish request and which
    /// it defers. Register it with the session options, the session builder or dependency
    /// injection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Publish workers call the provider concurrently and without holding SDK locks, so an
    /// implementation must be thread-safe. Each acknowledgement is validated again after the
    /// decision; one whose subscription was retired in the meantime is not sent.
    /// </para>
    /// <para>
    /// Deferred acknowledgements are presented again with later Publish requests until they
    /// are sent or end, for example because their subscription is deleted. If the provider
    /// throws, the whole batch is deferred (fail-closed).
    /// </para>
    /// </remarks>
    public interface ISubscriptionAcknowledgementProvider
    {
        /// <summary>
        /// Decides about a batch of acknowledgements.
        /// </summary>
        /// <param name="acknowledgements">The acknowledgements eligible for the next request.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>Which acknowledgements to defer.</returns>
        ValueTask<AcknowledgementDecision> DecideAsync(
            ArrayOf<AcknowledgementHandle> acknowledgements,
            CancellationToken ct = default);
    }
}
