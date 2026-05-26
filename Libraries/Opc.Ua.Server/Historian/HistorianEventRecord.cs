/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System.Collections.Generic;

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// A historical event record stored by an
    /// <see cref="IHistorianEventProvider"/>. Events are keyed by
    /// <see cref="EventId"/> within a notifier and timestamped by
    /// <see cref="SourceTimestamp"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Fields"/> maps field BrowseNames (the last segment of a
    /// <c>SimpleAttributeOperand</c> browse path) to their values. The
    /// framework's event-filter selection pass looks up each select-clause
    /// operand by browse-name; richer dotted browse-path resolution is
    /// the provider's responsibility (concatenate names with "/" when
    /// storing nested operand paths).
    /// </para>
    /// </remarks>
    public sealed record HistorianEventRecord(
        ByteString EventId,
        NodeId EventType,
        DateTimeUtc SourceTimestamp,
        IReadOnlyDictionary<string, Variant> Fields);

    /// <summary>
    /// Validated event-read request envelope passed to
    /// <see cref="IHistorianEventProvider.ReadEventsAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="Filter"/> is preserved verbatim. Providers that can
    /// evaluate <c>WhereClause</c> should do so for efficiency; providers
    /// that cannot may return every event in the requested time range
    /// and let the framework evaluate the filter post-fetch (currently
    /// the framework only honours <c>SelectClauses</c>; <c>WhereClause</c>
    /// is documented as best-effort — see <c>Docs/HistoricalAccess.md</c>).
    /// </para>
    /// </remarks>
    public sealed record HistorianEventReadRequest
    {
        /// <summary>The notifier (or area) being read.</summary>
        public required NodeId NodeId { get; init; }

        /// <summary>Effective start time.</summary>
        public required DateTimeUtc StartTime { get; init; }

        /// <summary>Effective end time.</summary>
        public required DateTimeUtc EndTime { get; init; }

        /// <summary>Maximum events per call. Zero = unbounded.</summary>
        public uint MaxValues { get; init; }

        /// <summary>True for forward-in-time reads.</summary>
        public bool IsForward { get; init; }

        /// <summary>The event filter from the client request.</summary>
        public required EventFilter Filter { get; init; }
    }
}
