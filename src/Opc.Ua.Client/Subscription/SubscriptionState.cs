// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Client.Subscriptions
{
    /// <summary>
    /// Session state
    /// </summary>
    public enum SubscriptionState
    {
        /// <summary>
        /// Subscription is opened
        /// </summary>
        Opened,

        /// <summary>
        /// Subscription created
        /// </summary>
        Created,

        /// <summary>
        /// Subscription modified
        /// </summary>
        Modified,

        /// <summary>
        /// Subscription error
        /// </summary>
        Error,

        /// <summary>
        /// Subscription closed
        /// </summary>
        Deleted
    }
}
