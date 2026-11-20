/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Publishes the local Server's direction signals (health <c>ServiceLevel</c> and load weight) to the shared
    /// store so peers can read them through an <see cref="IPeerDirectionView"/>.
    /// </summary>
    public interface IPeerDirectionPublisher
    {
        /// <summary>
        /// Publishes the local Server's current health <c>ServiceLevel</c> (eligibility signal).
        /// </summary>
        /// <param name="serviceLevel">The health service level.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask PublishServiceLevelAsync(byte serviceLevel, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publishes the local Server's current load weight (tie-breaking signal).
        /// </summary>
        /// <param name="loadWeight">The load weight (0 = idle .. 255 = fully loaded).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask PublishLoadWeightAsync(byte loadWeight, CancellationToken cancellationToken = default);
    }
}
