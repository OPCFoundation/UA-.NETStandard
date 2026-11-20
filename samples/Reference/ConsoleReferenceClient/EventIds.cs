// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Quickstarts.ConsoleReferenceClient
{
    /// <summary>
    /// Centrally managed event id offsets for the source-generated log messages of the
    /// ConsoleReferenceClient assembly.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids relative to the
    /// offset constant below, using <c>offset + &lt;zero-based message index&gt;</c>. Every block
    /// reserves at least five spare slots for future messages and is rounded up to the next
    /// multiple of ten so that ids can be documented and managed from this single location.
    /// </remarks>
    internal static class ConsoleReferenceClientEventIds
    {
        public const int AlarmClientSample = 0;
        public const int ClientCertificate = 10;
        public const int ClientReconnect = 20;
        public const int ClientSamples = 40;
        public const int ConnectTester = 110;
        public const int UAClient = 130;
    }
}
