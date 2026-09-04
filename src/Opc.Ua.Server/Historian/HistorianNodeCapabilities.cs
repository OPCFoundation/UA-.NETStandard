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

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Capabilities advertised by an <see cref="IHistorianProvider"/> for a
    /// specific historizing variable. The framework uses these to build
    /// per-variable <c>HistoricalDataConfigurationType</c> companion
    /// objects and to compute server-wide <c>HistoryServerCapabilities</c>
    /// flags.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All values are <em>per-node</em>: a provider may advertise
    /// <see cref="InsertData"/> for one variable while not supporting
    /// inserts on another. The framework aggregates these flags into the
    /// server-wide capability node using a union policy by default
    /// (see <see cref="HistorianProviderRegistry"/>).
    /// </para>
    /// </remarks>
    public sealed record HistorianNodeCapabilities
    {
        /// <summary>
        /// Default capabilities: read-only raw, modified, at-time, and
        /// processed data history; no updates, annotations, events, structured
        /// data, or server timestamps.
        /// </summary>
        public static HistorianNodeCapabilities ReadOnly { get; } = new();

        /// <summary>
        /// Read/write capabilities for an ordinary historical data variable.
        /// </summary>
        public static HistorianNodeCapabilities DataReadWrite { get; } = new()
        {
            InsertData = true,
            ReplaceData = true,
            UpdateData = true,
            DeleteRaw = true,
            DeleteAtTime = true,
            ServerTimestampSupported = true
        };

        /// <summary>
        /// Read/write capabilities for a StructuredHistoryData variable.
        /// </summary>
        public static HistorianNodeCapabilities StructuredReadWrite { get; } =
            new()
            {
                ReadProcessedData = false,
                ReadStructuredData = true,
                ReadModifiedStructuredData = true,
                ReadAtTimeStructuredData = true,
                InsertStructuredData = true,
                ReplaceStructuredData = true,
                UpdateStructuredData = true,
                DeleteStructuredData = true,
                ServerTimestampSupported = true
            };

        /// <summary>
        /// Read/write capabilities for a historical event notifier.
        /// </summary>
        public static HistorianNodeCapabilities EventReadWrite { get; } = new()
        {
            ReadRawData = false,
            ReadModifiedData = false,
            ReadAtTime = false,
            ReadProcessedData = false,
            ReadEventHistory = true,
            InsertEvent = true,
            ReplaceEvent = true,
            UpdateEvent = true,
            DeleteEvent = true
        };

        /// <summary>
        /// All capabilities enabled. Intended for combined adapters and tests.
        /// </summary>
        public static HistorianNodeCapabilities ReadWrite { get; } = new()
        {
            InsertData = true,
            ReplaceData = true,
            UpdateData = true,
            DeleteRaw = true,
            DeleteAtTime = true,
            InsertAnnotation = true,
            ReadEventHistory = true,
            InsertEvent = true,
            ReplaceEvent = true,
            UpdateEvent = true,
            DeleteEvent = true,
            ReadStructuredData = true,
            ReadModifiedStructuredData = true,
            ReadAtTimeStructuredData = true,
            InsertStructuredData = true,
            ReplaceStructuredData = true,
            UpdateStructuredData = true,
            DeleteStructuredData = true,
            ServerTimestampSupported = true
        };

        /// <summary>
        /// No capabilities enabled at all — including the read flags that
        /// otherwise default to <see langword="true"/> on a bare
        /// <c>new()</c>. This is the conservative baseline for rolling up
        /// "what does this provider actually support right now" across
        /// zero or more explicitly registered nodes (see
        /// <see cref="IHistorianProvider.GetCapabilitiesAsync"/> called
        /// with <see cref="NodeId.Null"/>), as opposed to
        /// <see cref="ReadOnly"/>, which is the default template handed
        /// to a newly historized node.
        /// </summary>
        public static HistorianNodeCapabilities None { get; } = new()
        {
            ReadRawData = false,
            ReadModifiedData = false,
            ReadAtTime = false,
            ReadProcessedData = false
        };

        /// <summary>
        /// True if the node supports raw history reads (always true for historizing nodes).
        /// </summary>
        public bool ReadRawData { get; init; } = true;

        /// <summary>
        /// True if the node supports modified history reads.
        /// </summary>
        public bool ReadModifiedData { get; init; } = true;

        /// <summary>
        /// True if the node supports read-at-time history reads (may be derived by framework from raw).
        /// </summary>
        public bool ReadAtTime { get; init; } = true;

        /// <summary>
        /// True if the node supports processed (aggregate) history reads.
        /// </summary>
        public bool ReadProcessedData { get; init; } = true;

        /// <summary>
        /// True if the node supports inserting new history values (HistoryUpdate / Insert).
        /// </summary>
        public bool InsertData { get; init; }

        /// <summary>
        /// True if the node supports replacing existing history values (HistoryUpdate / Replace).
        /// </summary>
        public bool ReplaceData { get; init; }

        /// <summary>
        /// True if the node supports upsert semantics (HistoryUpdate / Update).
        /// </summary>
        public bool UpdateData { get; init; }

        /// <summary>
        /// True if the node supports range deletion of raw values (DeleteRawModified).
        /// </summary>
        public bool DeleteRaw { get; init; }

        /// <summary>
        /// True if the node supports point deletion (DeleteAtTime).
        /// </summary>
        public bool DeleteAtTime { get; init; }

        /// <summary>
        /// True if the node supports inserting annotations on the historizing variable.
        /// </summary>
        public bool InsertAnnotation { get; init; }

        /// <summary>
        /// True if the node supports event-history reads.
        /// </summary>
        public bool ReadEventHistory { get; init; }

        /// <summary>
        /// True if the node supports inserting historical events.
        /// </summary>
        public bool InsertEvent { get; init; }

        /// <summary>
        /// True if the node supports replacing historical events.
        /// </summary>
        public bool ReplaceEvent { get; init; }

        /// <summary>
        /// True if the node supports updating historical events.
        /// </summary>
        public bool UpdateEvent { get; init; }

        /// <summary>
        /// True if the node supports deleting historical events.
        /// </summary>
        public bool DeleteEvent { get; init; }

        /// <summary>
        /// True if the node supports raw StructuredHistoryData reads.
        /// </summary>
        public bool ReadStructuredData { get; init; }

        /// <summary>
        /// True if the node supports modified StructuredHistoryData reads.
        /// </summary>
        public bool ReadModifiedStructuredData { get; init; }

        /// <summary>
        /// True if the node supports at-time StructuredHistoryData reads.
        /// </summary>
        public bool ReadAtTimeStructuredData { get; init; }

        /// <summary>
        /// True if the node supports inserting StructuredHistoryData.
        /// </summary>
        public bool InsertStructuredData { get; init; }

        /// <summary>
        /// True if the node supports replacing StructuredHistoryData.
        /// </summary>
        public bool ReplaceStructuredData { get; init; }

        /// <summary>
        /// True if the node supports updating StructuredHistoryData.
        /// </summary>
        public bool UpdateStructuredData { get; init; }

        /// <summary>
        /// True if the node supports deleting StructuredHistoryData.
        /// </summary>
        public bool DeleteStructuredData { get; init; }

        /// <summary>
        /// Maximum raw data values returned per page. Zero means no provider limit.
        /// </summary>
        public uint MaxReturnDataValues { get; init; }

        /// <summary>
        /// Maximum historical events returned per page. Zero means no provider limit.
        /// </summary>
        public uint MaxReturnEventValues { get; init; }

        /// <summary>
        /// Whether provider resume tokens can be replayed on another replica.
        /// </summary>
        public bool PortableResumeTokens { get; init; }

        /// <summary>
        /// Event types the provider is configured to historize.
        /// </summary>
        public ArrayOf<NodeId> EventTypes { get; init; } = [];

        /// <summary>
        /// Event fields retained when server-reported events are captured.
        /// Mandatory BaseEventType fields are always retained.
        /// </summary>
        public ArrayOf<SimpleAttributeOperand> EventFields { get; init; }
            = [];

        /// <summary>
        /// Additional event fields that Insert and Update requests must supply.
        /// EventType and Time are always mandatory under Part 11.
        /// </summary>
        public ArrayOf<SimpleAttributeOperand> MandatoryEventFields { get; init; }
            = [];

        /// <summary>
        /// Event fields used to sort historical events.
        /// </summary>
        public ArrayOf<SimpleAttributeOperand> SortByEventFields { get; init; }
            = [];

        /// <summary>True if the storage backend persists <see cref="DataValue.ServerTimestamp"/>.</summary>
        public bool ServerTimestampSupported { get; init; }

        /// <summary>
        /// Whether the historized signal is stepped or interpolated. Mirrors
        /// <c>HistoricalDataConfigurationType.Stepped</c> (Part 11 §5.2.3).
        /// </summary>
        public bool Stepped { get; init; }

        /// <summary>
        /// Human-readable definition surfaced as
        /// <c>HistoricalDataConfigurationType.Definition</c>.
        /// </summary>
        public string? Definition { get; init; }

        /// <summary>
        /// Maximum time between samples in milliseconds. Zero means unspecified.
        /// </summary>
        public double MaxTimeInterval { get; init; }

        /// <summary>
        /// Minimum time between samples in milliseconds. Zero means unspecified.
        /// </summary>
        public double MinTimeInterval { get; init; }

        /// <summary>
        /// Exception deviation used by the historian, when configured.
        /// </summary>
        public double? ExceptionDeviation { get; init; }

        /// <summary>
        /// Format of <see cref="ExceptionDeviation"/>, when configured.
        /// </summary>
        public ExceptionDeviationFormat? ExceptionDeviationFormat { get; init; }

        /// <summary>
        /// Time, in milliseconds, that values may be retained in the archive.
        /// Zero means no limit.
        /// </summary>
        public double MaxTimeStoredValues { get; init; }

        /// <summary>
        /// Maximum number of samples retained in the archive. Zero means no limit.
        /// </summary>
        public uint MaxCountStoredValues { get; init; }

        /// <summary>
        /// Start of the archived data window, if known.
        /// </summary>
        public DateTimeUtc StartOfArchive { get; init; } = DateTimeUtc.MinValue;

        /// <summary>
        /// Start of the on-line archived data window, if known.
        /// </summary>
        public DateTimeUtc StartOfOnlineArchive { get; init; } = DateTimeUtc.MinValue;

        /// <summary>
        /// The default <see cref="AggregateConfiguration"/> advertised on
        /// the node's <c>HistoricalDataConfiguration</c> companion object
        /// (Part 11 §5.2.3) and used by the server when a client requests
        /// aggregates with <c>UseServerCapabilitiesDefaults=true</c>. A client
        /// reads these values to reproduce the server's aggregate results, so
        /// they must match what the server actually computes with. Defaults to
        /// the Part 13 v1.05.07 §4.2.1.2 defaults (PercentDataGood /
        /// PercentDataBad = 100, TreatUncertainAsBad = true,
        /// UseSlopedExtrapolation = false).
        /// </summary>
        public AggregateConfiguration DefaultAggregateConfiguration { get; init; } = new()
        {
            PercentDataGood = 100,
            PercentDataBad = 100,
            TreatUncertainAsBad = true,
            UseSlopedExtrapolation = false,
            UseServerCapabilitiesDefaults = false
        };

        /// <summary>
        /// Returns <c>true</c> if any update capability is enabled.
        /// </summary>
        public bool SupportsAnyUpdate
            => InsertData ||
                ReplaceData ||
                UpdateData ||
                DeleteRaw ||
                DeleteAtTime ||
                InsertAnnotation ||
                SupportsAnyEventUpdate ||
                SupportsAnyStructuredUpdate;

        /// <summary>
        /// Returns <c>true</c> if any event update capability is enabled.
        /// </summary>
        public bool SupportsAnyEventUpdate
            => InsertEvent || ReplaceEvent || UpdateEvent || DeleteEvent;

        /// <summary>
        /// Returns <c>true</c> if any structured update capability is enabled.
        /// </summary>
        public bool SupportsAnyStructuredUpdate
            => InsertStructuredData ||
                ReplaceStructuredData ||
                UpdateStructuredData ||
                DeleteStructuredData;
    }
}
