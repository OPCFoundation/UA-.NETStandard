// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <inheritdoc/>
    public class StoredSubscription : IStoredSubscriptionState, IStoredSubscriptionTriggering
    {
        /// <inheritdoc/>
        public uint Id { get; set; }

        /// <inheritdoc/>
        public uint LifetimeCounter { get; set; }

        /// <inheritdoc/>
        public uint MaxLifetimeCount { get; set; }

        /// <inheritdoc/>
        public uint MaxKeepaliveCount { get; set; }

        /// <inheritdoc/>
        public uint MaxMessageCount { get; set; }

        /// <inheritdoc/>
        public uint MaxNotificationsPerPublish { get; set; }

        /// <inheritdoc/>
        public double PublishingInterval { get; set; }

        /// <inheritdoc/>
        public byte Priority { get; set; }

        /// <inheritdoc/>
        public UserIdentityToken? UserIdentityToken { get; set; }

        /// <inheritdoc/>
        public int LastSentMessage { get; set; }

        /// <inheritdoc/>
        public bool IsDurable { get; set; }

        /// <inheritdoc/>
        public bool PublishingEnabled { get; set; } = true;

        /// <inheritdoc/>
        public string? OwnerClientApplicationUri { get; set; }

        /// <inheritdoc/>
        public uint SequenceNumber { get; set; }

        /// <inheritdoc/>
        public List<NotificationMessage> SentMessages { get; set; } = null!;

        /// <inheritdoc/>
        public IEnumerable<IStoredMonitoredItem> MonitoredItems { get; set; } = null!;

        /// <inheritdoc/>
        public IReadOnlyDictionary<uint, IReadOnlyList<uint>>? TriggeringLinks { get; set; }
    }
}
