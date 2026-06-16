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

namespace Opc.Ua.Pcap.Audit
{
    /// <summary>
    /// Categorizes a <see cref="PcapAuditEvent"/>. Each kind corresponds
    /// to one user-visible MCP tool or capture-manager operation that
    /// touches secret material, opens an output file, or stands up a
    /// network endpoint.
    /// </summary>
    public enum PcapAuditEventKind
    {
        /// <summary>
        /// A capture session was started.
        /// </summary>
        StartCapture,

        /// <summary>
        /// A capture session was stopped.
        /// </summary>
        StopCapture,

        /// <summary>
        /// Channel keys were dumped to a keylog.
        /// </summary>
        DumpKeys,

        /// <summary>
        /// An offline pcap was decoded with a keylog.
        /// </summary>
        DecodePcapWithKeys,

        /// <summary>
        /// A replay session was started.
        /// </summary>
        StartReplay,

        /// <summary>
        /// A replay session was stopped.
        /// </summary>
        StopReplay,

        /// <summary>
        /// A captured frame was observed (rate-limited).
        /// </summary>
        FrameCaptured
    }
}
