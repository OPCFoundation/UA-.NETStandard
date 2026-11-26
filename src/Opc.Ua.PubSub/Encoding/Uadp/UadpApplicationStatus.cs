// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.PubSub.Encoding.Uadp
{
    /// <summary>
    /// ApplicationInformationType status body defined by Part 14 §7.2.4.6.7.
    /// </summary>
    public sealed record UadpApplicationStatus
    {
        /// <summary>
        /// Whether the publisher periodically refreshes the status message.
        /// </summary>
        public bool IsCyclic { get; init; }

        /// <summary>
        /// Current PubSub state.
        /// </summary>
        public PubSubState Status { get; init; }

        /// <summary>
        /// Expected next status report time when <see cref="IsCyclic"/> is true.
        /// </summary>
        public DateTimeUtc NextReportTime { get; init; }

        /// <summary>
        /// Message creation timestamp when <see cref="IsCyclic"/> is true.
        /// </summary>
        public DateTimeUtc Timestamp { get; init; }
    }
}
