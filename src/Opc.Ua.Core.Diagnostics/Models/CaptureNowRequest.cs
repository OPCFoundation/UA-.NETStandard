// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.ComponentModel;
using System.Text.Json.Serialization;
using Opc.Ua.Pcap.Serialization;

namespace Opc.Ua.Pcap.Models
{
    /// <summary>
    /// Convenience request for <c>capture_now</c>.
    /// </summary>
    public sealed class CaptureNowRequest
    {
        /// <summary>
        /// The underlying capture parameters.
        /// </summary>
        public StartCaptureRequest Start { get; init; } = new();

        /// <summary>
        /// How long to capture for before returning. Capped at 60 s.
        /// </summary>
        public int DurationSeconds { get; init; } = 10;

        /// <summary>
        /// Format to return the captured data in.
        /// </summary>
        [Description(
            "Output format: pcap | pcapng | json | csv | text | service-timeline. " +
            "CLR names such as ServiceTimeline are also accepted.")]
        [JsonConverter(typeof(FormatKindJsonConverter))]
        public FormatKind Format { get; init; } = FormatKind.ServiceTimeline;
    }
}
