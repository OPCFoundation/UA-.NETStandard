// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.GMS.Client
{
    /// <summary>
    /// Bounds native client workflows; exceeding a bound rejects the read rather than silently truncating it.
    /// </summary>
    public sealed record GmsClientOptions
    {
        /// <summary>
        /// Gets the aggregate native element bound, from one through 4096.
        /// </summary>
        public int MaximumElements { get; init; } = 128;

        /// <summary>
        /// Gets the aggregate examined browse-reference bound, from one through 100000.
        /// </summary>
        public int MaximumReferences { get; init; } = 16384;

        /// <summary>
        /// Gets the raw sample payload bound, from one through one mebibyte.
        /// </summary>
        public int MaximumPayloadBytes { get; init; } = 1024 * 1024;

        /// <summary>
        /// Rejects invalid limits before starting any service operation.
        /// </summary>
        public void Validate()
        {
            const string message = "GMS reads require finite positive element, reference and payload limits.";
            if (MaximumElements is < 1 or > 4096)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumElements), message);
            }
            if (MaximumReferences is < 1 or > 100000)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumReferences), message);
            }
            if (MaximumPayloadBytes is < 1 or > 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumPayloadBytes), message);
            }
        }
    }
}
