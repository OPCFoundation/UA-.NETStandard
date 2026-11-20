// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Centrally managed event id offsets for source-generated log messages in this assembly.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids relative to the
    /// offset constant below. Each assembly owns its own offsets, so this package carries its
    /// own copy rather than sharing one with the other MCP packages.
    /// </remarks>
    internal static class McpPubSubEventIds
    {
        public const int PubSubRuntimeManager = 0;
    }
}
