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

using System;

namespace Opc.Ua.Client.Historian
{
    /// <summary>
    /// Historical profile and conformance-unit claims published by a server.
    /// </summary>
    public sealed record HistoricalConformanceInfo
    {
        /// <summary>
        /// Historical Server facet URIs present in ServerProfileArray.
        /// </summary>
        public ArrayOf<string> ServerProfiles { get; init; } = [];

        /// <summary>
        /// Historical conformance units published by the server.
        /// </summary>
        public ArrayOf<QualifiedName> ConformanceUnits { get; init; }
            = [];
    }

    /// <summary>
    /// Snapshot of <c>Server.ServerCapabilities.HistoryServerCapabilities</c>
    /// returned by <see cref="HistoryClient.GetServerCapabilitiesAsync"/>.
    /// </summary>
    public sealed record HistoryServerCapabilitiesInfo
    {
        /// <summary>
        /// Whether the server supports raw/modified history reads.
        /// </summary>
        public bool AccessHistoryData { get; init; }

        /// <summary>
        /// Whether the server supports event-history reads.
        /// </summary>
        public bool AccessHistoryEvents { get; init; }

        /// <summary>
        /// Maximum data values returned per request (0 = no limit).
        /// </summary>
        public uint MaxReturnDataValues { get; init; }

        /// <summary>
        /// Maximum events returned per request (0 = no limit).
        /// </summary>
        public uint MaxReturnEventValues { get; init; }

        /// <summary>
        /// Whether the server supports inserting raw values.
        /// </summary>
        public bool InsertData { get; init; }

        /// <summary>
        /// Whether the server supports replacing raw values.
        /// </summary>
        public bool ReplaceData { get; init; }

        /// <summary>
        /// Whether the server supports upserting raw values.
        /// </summary>
        public bool UpdateData { get; init; }

        /// <summary>
        /// Whether the server supports range delete.
        /// </summary>
        public bool DeleteRaw { get; init; }

        /// <summary>
        /// Whether the server supports point delete.
        /// </summary>
        public bool DeleteAtTime { get; init; }

        /// <summary>
        /// Whether the server supports inserting annotations.
        /// </summary>
        public bool InsertAnnotation { get; init; }

        /// <summary>
        /// Whether the server supports inserting historical events.
        /// </summary>
        public bool InsertEvent { get; init; }

        /// <summary>
        /// Whether the server supports replacing historical events.
        /// </summary>
        public bool ReplaceEvent { get; init; }

        /// <summary>
        /// Whether the server supports updating historical events.
        /// </summary>
        public bool UpdateEvent { get; init; }

        /// <summary>
        /// Whether the server supports deleting historical events.
        /// </summary>
        public bool DeleteEvent { get; init; }

        /// <summary>
        /// Whether the server persists ServerTimestamp on history.
        /// </summary>
        public bool ServerTimestampSupported { get; init; }
    }

    /// <summary>
    /// Snapshot of <c>HistoricalDataConfigurationType</c> for a single
    /// historizing variable, returned by
    /// <see cref="HistoryClient.GetConfigurationAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Properties that resolve return their values; properties the server
    /// has not surfaced come back as <c>null</c>.
    /// <see cref="HasConfiguration"/> indicates whether the companion object
    /// was found (linked via <c>HasHistoricalConfiguration</c>, Part 11 §5.2.3).
    /// </para>
    /// </remarks>
    public sealed record HistoricalDataConfigurationInfo
    {
        /// <summary><c>true</c> when at least one property was resolved.</summary>
        public bool HasConfiguration { get; init; }

        /// <summary>
        /// Whether the signal is stepped (per Part 11 §5.2.3).
        /// </summary>
        public bool? Stepped { get; init; }

        /// <summary>
        /// Free-form description of the historized signal.
        /// </summary>
        public string? Definition { get; init; }

        /// <summary>
        /// Maximum time between samples (milliseconds).
        /// </summary>
        public double? MaxTimeInterval { get; init; }

        /// <summary>
        /// Minimum time between samples (milliseconds).
        /// </summary>
        public double? MinTimeInterval { get; init; }

        /// <summary>
        /// Exception deviation value used by the historizer.
        /// </summary>
        public double? ExceptionDeviation { get; init; }

        /// <summary>
        /// Format of <see cref="ExceptionDeviation"/>.
        /// </summary>
        public ExceptionDeviationFormat? ExceptionDeviationFormat { get; init; }

        /// <summary>
        /// Start of the archive window (oldest available).
        /// </summary>
        public DateTime? StartOfArchive { get; init; }

        /// <summary>
        /// Start of the online archive window.
        /// </summary>
        public DateTime? StartOfOnlineArchive { get; init; }

        /// <summary>
        /// Whether ServerTimestamp is retained for this historical node.
        /// </summary>
        public bool? ServerTimestampSupported { get; init; }

        /// <summary>
        /// Maximum retained time window in milliseconds.
        /// </summary>
        public double? MaxTimeStoredValues { get; init; }

        /// <summary>
        /// Maximum retained sample count.
        /// </summary>
        public uint? MaxCountStoredValues { get; init; }

        /// <summary>
        /// The node's advertised default <see cref="Ua.AggregateConfiguration"/>
        /// (PercentDataGood, PercentDataBad, TreatUncertainAsBad,
        /// UseSlopedExtrapolation), read from the <c>AggregateConfiguration</c>
        /// object of the <c>HistoricalDataConfiguration</c> companion. A client
        /// requesting aggregates with <c>UseServerCapabilitiesDefaults=true</c>
        /// uses these values to reproduce the server's aggregate results.
        /// <c>null</c> when the server does not expose the object.
        /// </summary>
        public AggregateConfiguration? AggregateConfiguration { get; init; }
    }

    /// <summary>
    /// Snapshot of <c>HistoricalEventConfigurationType</c> for an event notifier.
    /// </summary>
    public sealed record HistoricalEventConfigurationInfo
    {
        /// <summary>
        /// Whether the notifier exposes a historical event configuration.
        /// </summary>
        public bool HasConfiguration { get; init; }

        /// <summary>
        /// Event types the historian can store with all mandatory fields.
        /// </summary>
        public ArrayOf<NodeId> EventTypes { get; init; } = [];

        /// <summary>
        /// Start of the complete event archive.
        /// </summary>
        public DateTime? StartOfArchive { get; init; }

        /// <summary>
        /// Start of the online event archive.
        /// </summary>
        public DateTime? StartOfOnlineArchive { get; init; }

        /// <summary>
        /// Event fields supported for sorted historical reads.
        /// </summary>
        public ArrayOf<SimpleAttributeOperand> SortByEventFields { get; init; }
            = [];
    }
}
