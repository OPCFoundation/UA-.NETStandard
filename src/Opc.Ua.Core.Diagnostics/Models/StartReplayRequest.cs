/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Opc.Ua.Pcap.Replay;

namespace Opc.Ua.Pcap.Models
{
    /// <summary>
    /// Parameters for replaying a pcap recording.
    /// </summary>
    public sealed class StartReplayRequest
    {
        /// <summary>
        /// Path to the pcap file to replay.
        /// </summary>
        public string PcapFilePath { get; init; } = string.Empty;

        /// <summary>
        /// Optional keylog file used by mock-client replay for offline
        /// decoding.
        /// </summary>
        public string? KeyLogFilePath { get; init; }

        /// <summary>
        /// Replay mode.
        /// </summary>
        public ReplayMode Mode { get; init; } = ReplayMode.MockServer;

        /// <summary>
        /// Listen URI scheme for mock-server replay. Defaults to
        /// <c>opc.tcp</c>.
        /// </summary>
        public string? ListenScheme { get; init; }

        /// <summary>
        /// Listen port for mock-server replay. Defaults to an ephemeral
        /// port.
        /// </summary>
        public int? ListenPort { get; init; }

        /// <summary>
        /// Target endpoint URL for mock-client replay.
        /// </summary>
        public string? TargetEndpointUrl { get; init; }

        /// <summary>
        /// Replay speed multiplier.
        /// </summary>
        public double Speed { get; init; } = 1.0;
    }
}
