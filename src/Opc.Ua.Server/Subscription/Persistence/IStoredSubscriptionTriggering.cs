// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Extends persisted subscription state with the triggering links between its
    /// monitored items (OPC 10000-4 §5.13.1.6).
    /// </summary>
    public interface IStoredSubscriptionTriggering : IStoredSubscription
    {
        /// <summary>
        /// Gets or sets the triggering links of the subscription.
        /// </summary>
        /// <value>
        /// The identifiers of the linked monitored items keyed by the identifier of their
        /// triggering monitored item, or <c>null</c> when no links were persisted.
        /// </value>
        /// <remarks>
        /// Stores that did not persist this field restore the subscription without links.
        /// </remarks>
        IReadOnlyDictionary<uint, IReadOnlyList<uint>>? TriggeringLinks { get; set; }
    }
}
