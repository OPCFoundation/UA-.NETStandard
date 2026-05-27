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
        /// Default capabilities: read-only raw and modified history,
        /// no updates, no annotations, source-timestamp only.
        /// </summary>
        public static HistorianNodeCapabilities ReadOnly { get; } = new();

        /// <summary>
        /// All capabilities enabled. Intended for in-memory engines and tests.
        /// </summary>
        public static HistorianNodeCapabilities ReadWrite { get; } = new()
        {
            InsertData = true,
            ReplaceData = true,
            UpdateData = true,
            DeleteRaw = true,
            DeleteAtTime = true,
            InsertAnnotation = true,
            ServerTimestampSupported = true,
        };

        /// <summary>True if the node supports raw history reads (always true for historizing nodes).</summary>
        public bool ReadRawData { get; init; } = true;

        /// <summary>True if the node supports modified history reads.</summary>
        public bool ReadModifiedData { get; init; } = true;

        /// <summary>True if the node supports read-at-time history reads (may be derived by framework from raw).</summary>
        public bool ReadAtTime { get; init; } = true;

        /// <summary>True if the node supports processed (aggregate) history reads.</summary>
        public bool ReadProcessedData { get; init; } = true;

        /// <summary>True if the node supports inserting new history values (HistoryUpdate / Insert).</summary>
        public bool InsertData { get; init; }

        /// <summary>True if the node supports replacing existing history values (HistoryUpdate / Replace).</summary>
        public bool ReplaceData { get; init; }

        /// <summary>True if the node supports upsert semantics (HistoryUpdate / Update).</summary>
        public bool UpdateData { get; init; }

        /// <summary>True if the node supports range deletion of raw values (DeleteRawModified).</summary>
        public bool DeleteRaw { get; init; }

        /// <summary>True if the node supports point deletion (DeleteAtTime).</summary>
        public bool DeleteAtTime { get; init; }

        /// <summary>True if the node supports inserting annotations on the historizing variable.</summary>
        public bool InsertAnnotation { get; init; }

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
        /// Returns <c>true</c> if any update capability is enabled.
        /// </summary>
        public bool SupportsAnyUpdate
            => InsertData || ReplaceData || UpdateData || DeleteRaw || DeleteAtTime || InsertAnnotation;
    }
}
