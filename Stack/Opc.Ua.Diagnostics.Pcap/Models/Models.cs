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
using System.Collections.Generic;
using Opc.Ua.Diagnostics.Pcap.Replay;

namespace Opc.Ua.Diagnostics.Pcap.Models
{
    /// <summary>
    /// Output formats supported by the diagnostics pipeline.
    /// </summary>
    public enum FormatKind
    {
        /// <summary>libpcap (binary) - openable in Wireshark.</summary>
        Pcap = 0,

        /// <summary>PcapNg (binary) - openable in Wireshark.</summary>
        PcapNg = 1,

        /// <summary>Per-frame JSON.</summary>
        Json = 2,

        /// <summary>Per-frame CSV.</summary>
        Csv = 3,

        /// <summary>Human-readable per-frame text.</summary>
        Text = 4,

        /// <summary>
        /// OPC UA-specific decoded service-call timeline (text).
        /// </summary>
        ServiceTimeline = 5
    }

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
        /// <see cref="Opc.Ua.Bindings.IFrameCaptureSink"/> on each new
        /// <see cref="Opc.Ua.ITransportChannel"/> created by an OPC UA
        /// client.
        /// </summary>
        InProcessClient = 1,

        /// <summary>
        /// Passive in-process tap that hooks every server-side
        /// <see cref="Opc.Ua.Bindings.TcpServerChannel"/> created by a
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
    /// State a <c>CaptureSession</c> can be in.
    /// </summary>
    public enum CaptureSessionState
    {
        /// <summary>The session has been created but not yet started.</summary>
        Starting = 0,

        /// <summary>The session is actively capturing.</summary>
        Running = 1,

        /// <summary>The session is in the middle of being stopped.</summary>
        Stopping = 2,

        /// <summary>The session stopped cleanly.</summary>
        Completed = 3,

        /// <summary>The session terminated because of an error.</summary>
        Failed = 4,

        /// <summary>The session has been disposed.</summary>
        Disposed = 5
    }

    /// <summary>
    /// Parameters for <c>start_capture</c>.
    /// </summary>
    public sealed class StartCaptureRequest
    {
        /// <summary>The kind of source to create.</summary>
        public CaptureSourceKind Source { get; init; } = CaptureSourceKind.InProcessClient;

        /// <summary>
        /// Network interface name (required for
        /// <see cref="CaptureSourceKind.Nic"/>). Use
        /// <c>list_interfaces</c> to enumerate.
        /// </summary>
        public string? InterfaceName { get; init; }

        /// <summary>
        /// Optional BPF filter expression for
        /// <see cref="CaptureSourceKind.Nic"/>.
        /// </summary>
        public string? BpfFilter { get; init; }

        /// <summary>
        /// Whether to open the NIC in promiscuous mode. Defaults to true
        /// when capturing on a real NIC.
        /// </summary>
        public bool? Promiscuous { get; init; }

        /// <summary>
        /// Path to an existing pcap file (required for
        /// <see cref="CaptureSourceKind.Replay"/>).
        /// </summary>
        public string? PcapFilePath { get; init; }

        /// <summary>
        /// Path to an existing keylog file (optional for
        /// <see cref="CaptureSourceKind.Replay"/>; without it the source
        /// can only replay the raw bytes, not decode them).
        /// </summary>
        public string? KeyLogFilePath { get; init; }

        /// <summary>
        /// Hard byte cap for the capture; the session is stopped when
        /// reached. Default 50 MB.
        /// </summary>
        public long? MaxBytes { get; init; }

        /// <summary>
        /// Hard frame cap for the capture; the session is stopped when
        /// reached. Default unlimited.
        /// </summary>
        public long? MaxFrames { get; init; }

        /// <summary>
        /// Hard duration cap for the capture (seconds). Default 30 min.
        /// </summary>
        public int? MaxDurationSeconds { get; init; }

        /// <summary>
        /// Folder under which the session's pcap and keylog files are
        /// written. If <c>null</c> a per-session temp folder is created.
        /// </summary>
        public string? SessionFolder { get; init; }
    }

    /// <summary>
    /// Convenience request for <c>capture_now</c>.
    /// </summary>
    public sealed class CaptureNowRequest
    {
        /// <summary>The underlying capture parameters.</summary>
        public StartCaptureRequest Start { get; init; } = new();

        /// <summary>How long to capture for before returning. Capped at 60 s.</summary>
        public int DurationSeconds { get; init; } = 10;

        /// <summary>Format to return the captured data in.</summary>
        public FormatKind Format { get; init; } = FormatKind.ServiceTimeline;
    }

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
        /// Optional keylog file used by mock-client replay for offline decoding.
        /// </summary>
        public string? KeyLogFilePath { get; init; }

        /// <summary>
        /// Replay mode.
        /// </summary>
        public ReplayMode Mode { get; init; } = ReplayMode.MockServer;

        /// <summary>
        /// Listen URI scheme for mock-server replay. Defaults to <c>opc.tcp</c>.
        /// </summary>
        public string? ListenScheme { get; init; }

        /// <summary>
        /// Listen port for mock-server replay. Defaults to an ephemeral port.
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

    /// <summary>
    /// Status / metadata describing a capture session.
    /// </summary>
    public sealed class CaptureSessionInfo
    {
        /// <summary>Session id (assigned by <c>start_capture</c>).</summary>
        public string SessionId { get; init; } = string.Empty;

        /// <summary>The source kind.</summary>
        public CaptureSourceKind Source { get; init; }

        /// <summary>Current state.</summary>
        public CaptureSessionState State { get; init; }

        /// <summary>UTC timestamp the session entered Running state.</summary>
        public DateTimeOffset? StartedAt { get; init; }

        /// <summary>UTC timestamp the session stopped.</summary>
        public DateTimeOffset? StoppedAt { get; init; }

        /// <summary>Captured chunk count.</summary>
        public long FrameCount { get; init; }

        /// <summary>Captured byte count.</summary>
        public long ByteCount { get; init; }

        /// <summary>Folder on disk hosting the session's artifacts.</summary>
        public string SessionFolder { get; init; } = string.Empty;

        /// <summary>Path to the pcap file (if any).</summary>
        public string? PcapFilePath { get; init; }

        /// <summary>Path to the keylog file (if any).</summary>
        public string? KeyLogFilePath { get; init; }

        /// <summary>Failure message (if state is Failed).</summary>
        public string? Error { get; init; }
    }

    /// <summary>
    /// Describes a local network interface returned by
    /// <c>list_interfaces</c>.
    /// </summary>
    public sealed class NetworkInterfaceInfo
    {
        /// <summary>Device name as understood by libpcap (e.g. <c>\Device\NPF_{guid}</c>).</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Friendly name (e.g. <c>Ethernet</c>).</summary>
        public string? FriendlyName { get; init; }

        /// <summary>Free-form description.</summary>
        public string? Description { get; init; }

        /// <summary>Bound IP addresses.</summary>
        public IReadOnlyList<string> Addresses { get; init; } = [];

        /// <summary>The link-layer type (e.g. <c>Ethernet</c>).</summary>
        public string? LinkType { get; init; }

        /// <summary>Whether this is the loopback interface.</summary>
        public bool IsLoopback { get; init; }
    }
}
