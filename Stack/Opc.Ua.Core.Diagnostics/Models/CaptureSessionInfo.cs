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
