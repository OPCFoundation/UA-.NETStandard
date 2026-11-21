// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using System.Text.Json.Serialization;
using Opc.Ua.Pcap.Serialization;

namespace Opc.Ua.Pcap.Models
{
    /// <summary>
    /// Parameters for <c>start_capture</c>.
    /// </summary>
    public sealed class StartCaptureRequest
    {
        /// <summary>
        /// The kind of source to create.
        /// </summary>
        [Description(
            "Capture source: nic | inproc-client | inproc-server | replay. " +
            "CLR names such as InProcessClient are also accepted.")]
        [JsonConverter(typeof(CaptureSourceKindJsonConverter))]
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
        /// Path to the pcap (or pcapng) file to write to (in-process
        /// capture sources) or to read from
        /// (<see cref="CaptureSourceKind.Replay"/>).
        /// </summary>
        /// <remarks>
        /// For in-process sources this overrides the conventional
        /// <c>capture.pcap</c> file name inside <see cref="SessionFolder"/>;
        /// when <c>null</c> (default) the conventional name is used. Absolute
        /// paths inside the configured capture base folder are used
        /// unchanged; relative paths are resolved against
        /// <see cref="SessionFolder"/>. The
        /// <see cref="CaptureSourceKind.Replay"/> source requires this
        /// property to be set.
        /// </remarks>
        public string? PcapFilePath { get; init; }

        /// <summary>
        /// Requires a complete PCAP global header and record boundaries during offline replay.
        /// Defaults to false so existing active-file snapshot readers keep tolerating a partial trailing header.
        /// </summary>
        public bool RequireCompleteReplay { get; init; }

        /// <summary>
        /// Path to the keylog file to write to (in-process capture
        /// sources) or to read from
        /// (<see cref="CaptureSourceKind.Replay"/>).
        /// </summary>
        /// <remarks>
        /// For in-process sources this overrides the conventional
        /// <c>keys.uakeys.json</c> file name inside
        /// <see cref="SessionFolder"/>; when <c>null</c> (default) the
        /// conventional name is used. Absolute paths are written
        /// unchanged when they are inside the configured capture base
        /// folder; relative paths are resolved against
        /// <see cref="SessionFolder"/>. The sibling NSS-format
        /// <c>*.uakeys.txt</c> file is derived by changing the extension. For
        /// <see cref="CaptureSourceKind.Replay"/> the keylog is optional;
        /// without it the source can only replay raw bytes, not decode them.
        /// </remarks>
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
}
