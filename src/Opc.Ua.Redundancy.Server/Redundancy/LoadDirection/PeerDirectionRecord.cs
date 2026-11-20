// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// A fresh, per-Server view of the direction inputs for one member of a <c>RedundantServerSet</c>: its health
    /// <c>ServiceLevel</c> (eligibility) and, when known, its load weight (tie-breaking).
    /// </summary>
    public sealed record PeerDirectionRecord
    {
        /// <summary>
        /// The peer ServerUri / ApplicationUri that identifies the member.
        /// </summary>
        public string ServerUri { get; init; } = string.Empty;

        /// <summary>
        /// The peer's health <c>ServiceLevel</c> (OPC 10000-4 Table 105). Direction eligibility is decided by this
        /// value only.
        /// </summary>
        public byte ServiceLevel { get; init; }

        /// <summary>
        /// The peer's load weight (0 = idle .. 255 = fully loaded), used only to break ties among peers already tied
        /// at the highest health band. Valid only when <see cref="LoadKnown"/> is <c>true</c>.
        /// </summary>
        public byte LoadWeight { get; init; }

        /// <summary>
        /// Whether a fresh load weight was available for this peer. When <c>false</c> the load weight is unknown
        /// (stale or never published) and the peer is treated as least-preferred for tie-breaking.
        /// </summary>
        public bool LoadKnown { get; init; }
    }
}
