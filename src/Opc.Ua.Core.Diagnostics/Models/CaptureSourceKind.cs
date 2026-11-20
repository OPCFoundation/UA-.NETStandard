// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Models
{
    /// <summary>
    /// Identifies a kind of capture source the
    /// <c>CaptureSessionManager</c> can create.
    /// </summary>
    public enum CaptureSourceKind
    {
        /// <summary>
        /// Live capture from a network interface via SharpPcap (requires
        /// libpcap / Npcap and usually elevated privileges).
        /// </summary>
        Nic = 0,

        /// <summary>
        /// Passive in-process tap that hooks the channel
        /// <see cref="IFrameCaptureSink"/> on each new
        /// <see cref="ITransportChannel"/> created by an OPC UA
        /// client.
        /// </summary>
        InProcessClient = 1,

        /// <summary>
        /// Passive in-process tap that hooks every server-side
        /// <see cref="TcpServerChannel"/> created by a
        /// hosted OPC UA server.
        /// </summary>
        InProcessServer = 2,

        /// <summary>
        /// Replay-only source that re-reads an existing pcap file plus
        /// optional keylog from disk.
        /// </summary>
        Replay = 3
    }

    /// <summary>
    /// Convenience helpers for <see cref="CaptureSourceKind"/>.
    /// </summary>
    public static class CaptureSourceKindExtensions
    {
        /// <summary>
        /// Attempts to parse a capture-source name (case-insensitive).
        /// </summary>
        public static bool TryParse(this string? value, out CaptureSourceKind kind)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "nic":
                    kind = CaptureSourceKind.Nic;
                    return true;
                case "inproc-client":
                case "inprocclient":
                case "in-process-client":
                case "inprocessclient":
                    kind = CaptureSourceKind.InProcessClient;
                    return true;
                case "inproc-server":
                case "inprocserver":
                case "in-process-server":
                case "inprocessserver":
                    kind = CaptureSourceKind.InProcessServer;
                    return true;
                case "replay":
                    kind = CaptureSourceKind.Replay;
                    return true;
                default:
                    kind = CaptureSourceKind.Nic;
                    return false;
            }
        }

        /// <summary>
        /// Returns the canonical wire name for a capture source.
        /// </summary>
        public static string ToWireName(this CaptureSourceKind kind)
        {
            return kind switch
            {
                CaptureSourceKind.Nic => "nic",
                CaptureSourceKind.InProcessClient => "inproc-client",
                CaptureSourceKind.InProcessServer => "inproc-server",
                CaptureSourceKind.Replay => "replay",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown capture source kind.")
            };
        }

        /// <summary>
        /// The canonical capture-source names used on the wire.
        /// </summary>
        public const string SupportedNames = "nic, inproc-client, inproc-server, or replay";
    }
}
