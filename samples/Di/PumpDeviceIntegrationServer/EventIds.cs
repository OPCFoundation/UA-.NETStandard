// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Pumps
{
    /// <summary>
    /// Centrally managed event id offsets for source-generated log messages in this assembly.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids relative to the
    /// offset constant below, using <c>offset + &lt;zero-based message index&gt;</c>. Every block
    /// reserves at least five spare slots for future messages and is rounded up to the next
    /// multiple of ten so that ids can be documented and managed from this single location.
    /// </remarks>
    internal static class PumpDeviceIntegrationServerEventIds
    {
        public const int PumpNodeManager = 0;
        public const int OpenUsdComposition = 10;
        public const int OpenUsdRepresentation = 20;
    }
}
