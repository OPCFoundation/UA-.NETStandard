/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: reports the local Server's current <b>load weight</b> (0 = idle,
    /// 255 = fully loaded) used only to break ties among peers already tied at the highest health
    /// <c>ServiceLevel</c> when directing a Client to the best Server in a <c>RedundantServerSet</c>.
    /// </summary>
    /// <remarks>
    /// The load weight is deliberately a <b>separate</b> signal from <c>ServiceLevel</c>: <c>ServiceLevel</c> keeps
    /// its OPC UA meaning (health/eligibility and Failover), while a stale or missing load weight only affects
    /// tie-breaking and never eligibility. The default <see cref="ConstantLoadWeightProvider"/> reports a fixed 0,
    /// which reduces load direction to random selection among equally-healthy peers.
    /// </remarks>
    public interface ILoadWeightProvider
    {
        /// <summary>
        /// The current load weight (0 = idle .. 255 = fully loaded).
        /// </summary>
        byte GetLoadWeight();

        /// <summary>
        /// Raised with the new load weight whenever it changes.
        /// </summary>
        event Action<byte>? LoadWeightChanged;
    }
}
