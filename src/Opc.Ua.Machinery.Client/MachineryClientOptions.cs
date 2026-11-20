// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Machinery.Client
{
    /// <summary>
    /// Opts a Machinery client into bounded discovery and strict, evidenced reads.
    /// </summary>
    /// <remarks>
    /// Only an optional path's BadNoMatch denotes absence. Non-Good service,
    /// operation and value statuses, malformed responses and incompatible types
    /// fail explicitly. Read limits apply to decoded values, not transport
    /// decoding; configure session quotas as well. Generated proxies, event
    /// observation, job commands and result transfers retain their own contracts.
    /// </remarks>
    public sealed record MachineryClientOptions
    {
        /// <summary>
        /// Gets the maximum raw references per discovery traversal, including
        /// duplicates. Process-value discovery shares this budget across its
        /// machine and Monitoring roots.
        /// </summary>
        public int MaximumBrowseReferences { get; init; } = 1024;

        /// <summary>
        /// Gets the maximum Browse/BrowseNext pages per traversal, including
        /// empty pages. Limits fail rather than silently truncating discovery.
        /// </summary>
        public int MaximumBrowsePages { get; init; } = 128;

        /// <summary>
        /// Gets the maximum decoded items per array and aggregate structured
        /// snapshot. This does not replace session message and decoding quotas.
        /// </summary>
        public int MaximumReadArrayItems { get; init; } = 1024;

        /// <summary>
        /// Copies and validates limits before a client or factory uses them.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal MachineryClientOptions CopyAndValidate()
        {
            if (MaximumBrowseReferences <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumBrowseReferences));
            }
            if (MaximumBrowsePages <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumBrowsePages));
            }
            if (MaximumReadArrayItems <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumReadArrayItems));
            }
            return this with { };
        }
    }
}
