// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server.Historian.InMemory
{
    /// <summary>
    /// Configuration knobs for <see cref="InMemoryHistorianProvider"/>.
    /// </summary>
    public sealed record InMemoryHistorianOptions
    {
        /// <summary>
        /// Maximum source-timestamp age relative to the provider's UTC clock.
        /// The default is one hour. Zero = unbounded. Expired writes are rejected.
        /// The newest stored value at or before the cutoff is retained as a start bound,
        /// subject to <see cref="MaxSamplesPerNode"/>.
        /// </summary>
        public TimeSpan RawDataRetentionPeriod { get; init; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Maximum number of raw samples retained per variable. Zero = unbounded.
        /// When the cap is reached the oldest samples are evicted on insert.
        /// This limit is applied in addition to <see cref="RawDataRetentionPeriod"/>.
        /// </summary>
        public uint MaxSamplesPerNode { get; init; }

        /// <summary>
        /// Maximum number of modified-history entries retained per variable.
        /// The default is 10,000. Zero explicitly opts into unbounded modified history.
        /// </summary>
        public uint MaxModifiedEntriesPerNode { get; init; } = 10_000;

        /// <summary>
        /// Maximum number of annotations retained per variable. Zero = unbounded.
        /// </summary>
        public uint MaxAnnotationsPerNode { get; init; }

        /// <summary>
        /// Default per-node capabilities advertised by the provider.
        /// </summary>
        public HistorianNodeCapabilities DefaultCapabilities { get; init; }
            = HistorianNodeCapabilities.DataReadWrite;
    }
}
