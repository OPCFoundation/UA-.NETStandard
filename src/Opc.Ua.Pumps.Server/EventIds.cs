// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Pumps.Server
{
    /// <summary>
    /// Centrally managed event id offsets for the source-generated log
    /// messages of the Opc.Ua.Pumps.Server assembly.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids
    /// relative to the offset constant below, using
    /// <c>offset + &lt;zero-based message index&gt;</c>. Every block reserves
    /// at least five spare slots and is rounded up to the next multiple of ten
    /// so ids can be managed from this one location.
    /// </remarks>
    internal static class PumpsServerEventIds
    {
        public const int PumpsNodeManager = 8100;
        public const int PumpBuilder = 8110;
    }
}
