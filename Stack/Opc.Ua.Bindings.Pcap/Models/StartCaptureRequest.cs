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

namespace Opc.Ua.Bindings.Pcap.Models
{
    /// <summary>
    /// Parameters for <c>start_capture</c>.
    /// </summary>
    public sealed class StartCaptureRequest
    {
        /// <summary>
        /// The kind of source to create.
        /// </summary>
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
        /// paths are written verbatim; relative paths are resolved against
        /// <see cref="SessionFolder"/>. The <see cref="CaptureSourceKind.Replay"/>
        /// source requires this property to be set.
        /// </remarks>
        public string? PcapFilePath { get; init; }

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
        /// verbatim; relative paths are resolved against
        /// <see cref="SessionFolder"/>. The sibling NSS-format
        /// <c>*.uakeys.txt</c> file is derived by changing the
        /// extension. For <see cref="CaptureSourceKind.Replay"/> the
        /// keylog is optional; without it the source can only replay
        /// raw bytes, not decode them.
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
