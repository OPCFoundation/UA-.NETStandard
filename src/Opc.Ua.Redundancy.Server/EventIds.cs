// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// Centrally managed event id offsets for the source-generated log messages of the
    /// Opc.Ua.Redundancy.Server assembly.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids relative to the
    /// offset constant below, using <c>offset + &lt;zero-based message index&gt;</c>. Every block
    /// reserves at least five spare slots for future messages and is rounded up to the next
    /// multiple of ten so that ids can be documented and managed from this single location. The
    /// class name is prefixed with the assembly token to avoid CS0436 collisions with the
    /// event-id classes of other assemblies exposed through <c>InternalsVisibleTo</c>.
    /// </remarks>
    internal static class RedundancyServerEventIds
    {
        /// <summary>
        /// Event identifier offset for address-space synchronization messages.
        /// </summary>
        public const int AddressSpaceSynchronizer = 0;

        /// <summary>
        /// Event identifier offset for distributed push-configuration transaction messages.
        /// </summary>
        public const int DistributedPushConfigurationTransactionCoordinator = 10;

        /// <summary>
        /// Event identifier offset for distributed session management messages.
        /// </summary>
        public const int DistributedSessionManager = 20;

        /// <summary>
        /// Event identifier offset for load-direction startup messages.
        /// </summary>
        public const int LoadDirectionStartupTask = 40;

        /// <summary>
        /// Event identifier offset for publishing peer direction during startup.
        /// </summary>
        public const int PeerDirectionPublishStartupTask = 50;

        /// <summary>
        /// Event identifier offset for peer discovery startup messages.
        /// </summary>
        public const int PeerDiscoveryStartupTask = 60;

        /// <summary>
        /// Event identifier offset for replicated address-space synchronization messages.
        /// </summary>
        public const int ReplicatedAddressSpaceSynchronizer = 70;

        /// <summary>
        /// Event identifier offset for server load-direction messages.
        /// </summary>
        public const int ServerLoadDirector = 80;

        /// <summary>
        /// Event identifier offset for server redundancy startup messages.
        /// </summary>
        public const int ServerRedundancyStartupTask = 90;

        /// <summary>
        /// Event identifier offset for shared key-value subscription store messages.
        /// </summary>
        public const int SharedKeyValueSubscriptionStore = 110;

        /// <summary>
        /// Event identifier offset for shared key-value session store messages.
        /// </summary>
        public const int SharedKeyValueSessionStore = 120;

        /// <summary>
        /// Event identifier offset for shared history continuation store messages.
        /// </summary>
        public const int SharedHistoryContinuationStore = 130;

        /// <summary>
        /// Event identifier offset for service-level publication messages.
        /// </summary>
        public const int ServiceLevelStartupTask = 140;

        /// <summary>
        /// Event identifier offset for service-level policy evaluation messages.
        /// </summary>
        public const int LeaderServiceLevelProvider = 150;

        /// <summary>
        /// Event identifier offset for the replicated event feed.
        /// </summary>
        public const int ReplicatedEventFeed = 160;
    }
}
