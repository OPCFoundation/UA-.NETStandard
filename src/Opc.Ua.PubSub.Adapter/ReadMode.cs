/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PubSub.Adapter
{
    /// <summary>
    /// Selects how the external-server publisher adapter obtains the source values
    /// for the PublishedDataSets it samples.
    /// </summary>
    public enum ReadMode
    {
        /// <summary>
        /// Each publish cycle issues a Read service call to the external server for
        /// the PublishedDataSet's variables.
        /// </summary>
        Cyclic,

        /// <summary>
        /// A client Subscription with monitored items keeps a latest-value cache that
        /// the publish cycle samples without a network round-trip.
        /// </summary>
        Subscription
    }
}
