// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.PubSub.Adapter
{
    /// <summary>
    /// Selects how monitored items are grouped into client Subscriptions when the
    /// server publisher adapter runs in <see cref="ReadMode.Subscription"/>.
    /// </summary>
    public enum SubscriptionAffinity
    {
        /// <summary>
        /// One client Subscription per WriterGroup; its publishing interval matches the
        /// WriterGroup publishing interval (the cadence owner). This is the default.
        /// </summary>
        WriterGroup,

        /// <summary>
        /// One client Subscription per DataSetWriter for stricter per-dataset isolation.
        /// </summary>
        DataSetWriter
    }
}
