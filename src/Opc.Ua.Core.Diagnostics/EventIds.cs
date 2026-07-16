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

namespace Opc.Ua
{
    /// <summary>
    /// Centrally managed event id offsets for the source-generated log messages of the
    /// Opc.Ua.Core.Diagnostics assembly.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids relative to the
    /// offset constant below, using <c>offset + &lt;zero-based message index&gt;</c>. Every block
    /// reserves at least five spare slots for future messages and is rounded up to the next
    /// multiple of ten so that ids can be documented and managed from this single location. The
    /// class name is prefixed with the assembly token to avoid CS0436 collisions with the
    /// event-id classes of other assemblies exposed through <c>InternalsVisibleTo</c>.
    /// </remarks>
    internal static class CoreDiagnosticsEventIds
    {
        public const int CapturingByteTransport = 0;
        public const int CaptureSession = 10;
        public const int CaptureSessionManager = 20;
        public const int CsvFormatter = 30;
        public const int HashChainedAuditFileSink = 40;
        public const int InProcessCaptureSource = 50;
        public const int JsonFormatter = 60;
        public const int LoggerPcapAuditSink = 70;
        public const int MockClientReplay = 80;
        public const int MockServerReplay = 90;
        public const int NicCaptureSource = 100;
        public const int OpcUaFrameParser = 110;
        public const int PcapEnvironmentAutoStartHostedService = 120;
        public const int PcapFormatter = 130;
        public const int PcapNgFormatter = 140;
        public const int PcapServerCapture = 150;
        public const int ReplayCaptureSource = 160;
        public const int ServiceCallReassembler = 170;
        public const int ServiceTimelineFormatter = 190;
        public const int StandaloneKeyLogObserver = 200;
        public const int TextFormatter = 210;
    }
}
