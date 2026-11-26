// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Scales.Server
{
    /// <summary>
    /// Centrally managed event id offsets for the source-generated log
    /// messages of the Opc.Ua.Scales.Server assembly.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids
    /// relative to the offset constant below, using
    /// <c>offset + &lt;zero-based message index&gt;</c>. Every block reserves
    /// at least five spare slots and is rounded up to the next multiple of ten
    /// so ids can be managed from this one location.
    /// </remarks>
    internal static class ScalesServerEventIds
    {
        public const int ScalesNodeManager = 8200;
        public const int ScaleHandle = 8210;
        public const int ScaleProducts = 8220;
        public const int ScaleRecipes = 8230;
        public const int ScaleRuntimeServices = 8240;
    }
}
