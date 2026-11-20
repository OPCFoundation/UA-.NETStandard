// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.PlasticsRubber.Client
{
    /// <summary>
    /// Bounds high-level dataset operations without assuming that a remote peer enforces the same limits.
    /// </summary>
    public sealed record ProductionDatasetClientOptions
    {
        /// <summary>
        /// Gets the maximum bytes copied by one upload or download.
        /// </summary>
        public int MaximumFileBytes { get; init; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets the maximum datasets accepted in one list operation.
        /// </summary>
        public int MaximumDatasetCount { get; init; } = 1024;

        /// <summary>
        /// Gets the bounded stream buffer and FileType chunk size.
        /// </summary>
        public int ChunkSize { get; init; } = 64 * 1024;

        /// <summary>
        /// Gets the deadline for an entire operation, including asynchronous server commit completion.
        /// </summary>
        public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Validates immutable settings at client construction.
        /// </summary>
        internal void Validate()
        {
            if (MaximumFileBytes <= 0 || MaximumDatasetCount <= 0 || ChunkSize <= 0 ||
                ChunkSize > MaximumFileBytes || OperationTimeout < TimeSpan.FromMilliseconds(1) ||
                OperationTimeout > TimeSpan.FromDays(1))
            {
                throw new ArgumentOutOfRangeException(nameof(ProductionDatasetClientOptions));
            }
        }
    }
}
