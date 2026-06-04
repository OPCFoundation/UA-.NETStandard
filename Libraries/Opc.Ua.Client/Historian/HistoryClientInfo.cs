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
    /// Servers running this stack do not yet auto-install the
    /// companion object under historizing variables, so this snapshot
    /// is filled best-effort: properties that resolve return their
    /// values, properties that the server has not surfaced come back
    /// as <c>null</c>. <see cref="HasConfiguration"/> indicates whether
    /// any property was populated.
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
        /// Start of the archive window (oldest available).
        /// </summary>
        public DateTime? StartOfArchive { get; init; }

        /// <summary>
        /// Start of the online archive window.
        /// </summary>
        public DateTime? StartOfOnlineArchive { get; init; }
    }
}
