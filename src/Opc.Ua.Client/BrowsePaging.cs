// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Limits that apply to one browse that follows continuation points and
    /// collects the pages in memory: the references and pages kept for one
    /// node, and the time the whole browse may take.
    /// </summary>
    internal readonly struct BrowsePagingLimits
    {
        /// <summary>
        /// Starts the limits now.
        /// </summary>
        /// <param name="maxReferencesPerNode">References kept for one node, zero for no limit.</param>
        /// <param name="maxPagesPerNode">Pages read for one node, zero for no limit.</param>
        /// <param name="maxDuration">Time the browse may take, zero or negative for no limit.</param>
        /// <param name="timeProvider">The clock that measures the duration.</param>
        public BrowsePagingLimits(
            uint maxReferencesPerNode,
            uint maxPagesPerNode,
            TimeSpan maxDuration,
            TimeProvider? timeProvider)
        {
            MaxReferencesPerNode = maxReferencesPerNode;
            MaxPagesPerNode = maxPagesPerNode;
            MaxDuration = maxDuration > TimeSpan.Zero ? maxDuration : TimeSpan.Zero;
            m_timeProvider = timeProvider ?? TimeProvider.System;
            m_startTimestamp = MaxDuration > TimeSpan.Zero ? m_timeProvider.GetTimestamp() : 0;
        }

        /// <summary>
        /// Starts the limits of <paramref name="options"/> now.
        /// </summary>
        public static BrowsePagingLimits Start(BrowserOptions options, TimeProvider? timeProvider)
        {
            return new BrowsePagingLimits(
                options.MaxReferencesPerNode,
                options.MaxPagesPerNode,
                options.MaxBrowseDuration,
                timeProvider);
        }

        /// <summary>
        /// Starts the default limits of <see cref="BrowserOptions"/> now.
        /// </summary>
        public static BrowsePagingLimits StartDefault(TimeProvider? timeProvider = null)
        {
            return new BrowsePagingLimits(
                BrowserOptions.DefaultMaxReferencesPerNode,
                BrowserOptions.DefaultMaxPagesPerNode,
                BrowserOptions.DefaultMaxBrowseDuration,
                timeProvider);
        }

        /// <summary>
        /// References kept for one node, zero for no limit.
        /// </summary>
        public uint MaxReferencesPerNode { get; }

        /// <summary>
        /// Pages read for one node, zero for no limit.
        /// </summary>
        public uint MaxPagesPerNode { get; }

        /// <summary>
        /// Time the browse may take, zero for no limit.
        /// </summary>
        public TimeSpan MaxDuration { get; }

        /// <summary>
        /// Whether the browse ran longer than <see cref="MaxDuration"/>.
        /// </summary>
        public bool IsExpired =>
            MaxDuration > TimeSpan.Zero &&
            m_timeProvider.GetElapsedTime(m_startTimestamp) > MaxDuration;

        private readonly TimeProvider m_timeProvider;
        private readonly long m_startTimestamp;
    }

    /// <summary>
    /// Tracks the pages followed for one continuation point chain against
    /// <see cref="BrowsePagingLimits"/>. A chain ends when the server stops
    /// returning a continuation point, or when this counter says to stop
    /// following it: the caller then releases the point.
    /// </summary>
    internal struct BrowsePageCounter
    {
        /// <summary>
        /// Records a page of a Browse or BrowseNext answer.
        /// </summary>
        /// <param name="limits">The limits of the browse.</param>
        /// <param name="referenceCount">References on the page.</param>
        /// <param name="hasContinuationPoint">Whether the page carries a
        /// continuation point, so the chain would be followed further.</param>
        /// <returns><see langword="null"/> to go on, or the status with which
        /// the chain stops. A page without continuation point completes the
        /// chain and is always accepted.</returns>
        public ServiceResult? Record(
            in BrowsePagingLimits limits,
            int referenceCount,
            bool hasContinuationPoint)
        {
            m_pages++;
            m_references += referenceCount;
            m_emptyRounds = referenceCount == 0 ? m_emptyRounds + 1 : 0;
            if (!hasContinuationPoint)
            {
                return null;
            }
            if (m_emptyRounds >= kMaxEmptyRounds)
            {
                // An empty page with a continuation point is a legal "not
                // done yet" answer (Part 4 §5.9.2.1), so it is followed, but
                // only for a bounded number of rounds in a row.
                return new ServiceResult(
                    StatusCodes.BadNoData,
                    new LocalizedText(
                        "The server kept returning a continuation point without references."));
            }
            if (limits.MaxReferencesPerNode > 0 && m_references >= limits.MaxReferencesPerNode)
            {
                return new ServiceResult(
                    StatusCodes.BadEncodingLimitsExceeded,
                    new LocalizedText(
                        "The browse reached the configured maximum number of references per node."));
            }
            if (limits.MaxPagesPerNode > 0 && m_pages >= limits.MaxPagesPerNode)
            {
                return new ServiceResult(
                    StatusCodes.BadEncodingLimitsExceeded,
                    new LocalizedText(
                        "The browse reached the configured maximum number of pages per node."));
            }
            if (limits.IsExpired)
            {
                return new ServiceResult(
                    StatusCodes.BadTimeout,
                    new LocalizedText("The browse exceeded the configured maximum duration."));
            }
            return null;
        }

        /// <summary>
        /// Consecutive pages without references that are followed for one
        /// continuation point. A server that runs out of time may legally
        /// answer with zero references and a continuation point
        /// (Part 4 §5.9.2.1), so one empty page is not the end of the data.
        /// </summary>
        internal const int kMaxEmptyRounds = 10;

        private uint m_pages;
        private long m_references;
        private int m_emptyRounds;
    }
}
