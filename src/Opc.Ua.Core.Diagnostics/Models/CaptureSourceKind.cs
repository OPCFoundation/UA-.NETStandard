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
}
