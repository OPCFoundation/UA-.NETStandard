/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Client.Historian
{
    /// <summary>
    /// Options for <see cref="HistoryClient"/>.
    /// </summary>
    public sealed record HistoryClientOptions
    {
        /// <summary>
        /// Default maximum values per HistoryRead request. The framework
        /// passes this through as <c>NumValuesPerNode</c> when the caller
        /// does not override it. Zero means "let the server decide".
        /// </summary>
        public uint DefaultMaxValuesPerNode { get; init; }

        /// <summary>
        /// Maximum decoded values or events accepted in one server response.
        /// Zero preserves the unbounded legacy behavior. This bounds client-side page copies and yields,
        /// not the transport decoder; configure message and array quotas separately.
        /// </summary>
        public int MaxDecodedPageItems { get; init; }

        /// <summary>
        /// Captures a managed session generation for each history enumeration and its continuation cleanup.
        /// The default preserves legacy behavior. For a policy decision that precedes the enumeration,
        /// construct this client with <see cref="SessionRequestContext.Session"/> captured before that decision.
        /// </summary>
        public bool BindSessionGeneration { get; init; }

        /// <summary>
        /// Default timestamps mode for reads.
        /// </summary>
        public TimestampsToReturn DefaultTimestampsToReturn { get; init; }
            = TimestampsToReturn.Source;

        /// <summary>
        /// Maximum number of HistoryRead pages followed by one async enumeration.
        /// Zero means unlimited.
        /// </summary>
        public uint MaxPagesPerRead { get; init; } = 100_000;

        /// <summary>
        /// Maximum elapsed time spent following HistoryRead continuation points.
        /// Zero means unlimited.
        /// </summary>
        public TimeSpan MaxReadDuration { get; init; }
    }
}
