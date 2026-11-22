// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Opc.Ua.Pcap.Serialization;

namespace Opc.Ua.Pcap.Models
{
    /// <summary>
    /// Status / metadata describing a capture session.
    /// </summary>
    public sealed class CaptureSessionInfo
    {
        /// <summary>
        /// Session id (assigned by <c>start_capture</c>).
        /// </summary>
        public string SessionId { get; init; } = string.Empty;

        /// <summary>
        /// The source kind.
        /// </summary>
        [Description(
            "Capture source: nic | inproc-client | inproc-server | replay. " +
            "CLR names such as InProcessClient are also accepted.")]
        [JsonConverter(typeof(CaptureSourceKindJsonConverter))]
        public CaptureSourceKind Source { get; init; }

        /// <summary>
        /// Current state.
        /// </summary>
        public CaptureSessionState State { get; init; }

        /// <summary>
        /// UTC timestamp the session entered Running state.
        /// </summary>
        public DateTimeOffset? StartedAt { get; init; }

        /// <summary>
        /// UTC timestamp the session stopped.
        /// </summary>
        public DateTimeOffset? StoppedAt { get; init; }

        /// <summary>
        /// Captured chunk count.
        /// </summary>
        public long FrameCount { get; init; }

        /// <summary>
        /// Captured byte count.
        /// </summary>
        public long ByteCount { get; init; }

        /// <summary>
        /// Folder on disk hosting the session's artifacts.
        /// </summary>
        public string SessionFolder { get; init; } = string.Empty;

        /// <summary>
        /// Path to the pcap file (if any).
        /// </summary>
        public string? PcapFilePath { get; init; }

        /// <summary>
        /// Path to the keylog file (if any).
        /// </summary>
        public string? KeyLogFilePath { get; init; }

        /// <summary>
        /// Failure message (if state is Failed).
        /// </summary>
        public string? Error { get; init; }
    }
}
