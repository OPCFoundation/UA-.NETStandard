// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.AMB.Server
{
    /// <summary>
    /// Centrally managed event id offsets for the source-generated log
    /// messages of the Opc.Ua.AMB.Server assembly.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids
    /// relative to the offset constant below, using
    /// <c>offset + &lt;zero-based message index&gt;</c>. Every block reserves
    /// at least five spare slots and is rounded up to the next multiple of ten
    /// so ids can be managed from this one location. The 8300 block follows
    /// Pumps (8100) and Scales (8200).
    /// </remarks>
    internal static class AmbServerEventIds
    {
        public const int AmbNodeManager = 8300;
        public const int AssetManagement = 8310;
        public const int ConfigurableAssetId = 8320;
        public const int AssetConfigurationStore = 8330;
        public const int AssetHealth = 8340;
        public const int AssetMaintenance = 8350;
        public const int DocumentationLinks = 8360;
        public const int AssetStructure = 8370;
    }
}
