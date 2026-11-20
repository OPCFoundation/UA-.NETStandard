// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.CranesHoists.Client
{
    /// <summary>
    /// Bounds discovery and topology reads without silently truncating a result.
    /// </summary>
    public sealed record CranesHoistsClientOptions
    {
        /// <summary>
        /// Gets the maximum unique objects visited in one inventory.
        /// </summary>
        public int MaximumNodes { get; init; } = 256;

        /// <summary>
        /// Gets the requested maximum references per browse response.
        /// </summary>
        public uint BrowsePageSize { get; init; } = 32;

        /// <summary>
        /// Gets the maximum pages allowed for one browse.
        /// </summary>
        public int MaximumBrowsePages { get; init; } = 64;

        /// <summary>
        /// Validates bounds before making a service call.
        /// </summary>
        public void Validate()
        {
            if (MaximumNodes is < 1 or > 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumNodes));
            }
            if (BrowsePageSize is < 1 or > 256)
            {
                throw new ArgumentOutOfRangeException(nameof(BrowsePageSize));
            }
            if (MaximumBrowsePages is < 1 or > 128)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumBrowsePages));
            }
        }
    }
}
