/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// A single page of history values returned by an
    /// <see cref="IHistorianProvider"/> read.
    /// </summary>
    /// <typeparam name="T">
    /// The page payload type — typically
    /// <see cref="HistoricalDataValue"/>, <see cref="ModifiedDataValue"/>,
    /// <see cref="DataValue"/> or <see cref="Annotation"/>.
    /// </typeparam>
    /// <remarks>
    /// <para>
    /// The page <see cref="Values"/> collection is returned in chronological
    /// order (ascending by <c>SourceTimestamp</c>; for reverse-time reads
    /// the framework reverses the order). When the page does not exhaust
    /// the requested time window the provider supplies a non-empty
    /// <see cref="NextToken"/> so the framework can issue the follow-up
    /// page read on behalf of the client via a HistoryRead continuation
    /// point.
    /// </para>
    /// </remarks>
    public readonly record struct HistorianPage<T>(
        ArrayOf<T> Values,
        HistorianResumeToken NextToken = default)
    {
        /// <summary>
        /// An empty page with no continuation token.
        /// </summary>
        public static HistorianPage<T> Empty { get; } = new([], default);

        /// <summary>
        /// Returns <c>true</c> when this page exhausts the request.
        /// </summary>
        public bool IsFinal => NextToken.IsEmpty;
    }
}
