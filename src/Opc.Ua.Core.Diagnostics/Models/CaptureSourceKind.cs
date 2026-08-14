/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
