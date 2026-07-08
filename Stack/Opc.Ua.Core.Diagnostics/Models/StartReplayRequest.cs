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

using Opc.Ua.Bindings;
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
