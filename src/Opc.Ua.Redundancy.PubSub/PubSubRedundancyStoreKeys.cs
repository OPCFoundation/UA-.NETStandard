// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.PubSub.Redundancy
{
    /// <summary>
    /// Stable <see cref="Ua.Redundancy.ISharedKeyValueStore"/> key prefixes used by the
    /// distributed PubSub redundancy bridges so every store agrees on its keyspace.
    /// </summary>
    /// <remarks>
    /// External lease and state claims are committed by a separately enrolled
    /// <see cref="Ua.Redundancy.FreshnessProtectedKeyValueStore"/>.
    /// </remarks>
    internal static class PubSubRedundancyStoreKeys
    {
        /// <summary>
        /// Prefix for leadership leases (strong keyspace, compare-and-swap).
        /// </summary>
        public const string LeasePrefix = "lease/pubsub/";

        /// <summary>
        /// Prefix for mirrored PubSub component runtime state.
        /// </summary>
        public const string RuntimeStatePrefix = "pubsub/runtime/";

        /// <summary>
        /// Prefix for per-writer sequence/keep-alive checkpoints (Hot standby).
        /// </summary>
        public const string CheckpointPrefix = "pubsub/checkpoint/";

        /// <summary>
        /// Prefix for shared PubSub security-group keys (SKS).
        /// </summary>
        public const string SecurityKeyPrefix = "pubsub/sks/";
    }
}
