// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace UaLens.Plugins.GdsDiscovery;

/// <summary>
/// Neutral, non-emoji tree markers for the discovery tree. Navigation relies on
/// the node's text label; these short monospace tags only categorize a row and
/// deliberately avoid emoji as a navigation language.
/// </summary>
internal static class DiscoveryGlyphs
{
    public const string LocalMachine = "loc";

    public const string LocalNetwork = "lan";

    public const string GlobalDiscovery = "gds";

    public const string CustomDiscovery = "dir";

    public const string Server = "srv";

    public const string CustomUrl = "url";

    public const string Favorite = "fav";

    public const string MdnsFolder = "net";

    public const string Info = "i";

    public const string Interface = "if";

    public const string None = "-";
}
