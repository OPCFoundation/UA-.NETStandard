// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
