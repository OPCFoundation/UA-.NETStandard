// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Centrally managed event id offsets for source-generated log messages in this assembly.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids relative to the
    /// offset constant below, using <c>offset + &lt;zero-based message index&gt;</c>. Every block
    /// reserves at least five spare slots for future messages and is rounded up to the next
    /// multiple of ten so that ids can be documented and managed from this single location.
    /// Each assembly owns its own offsets; the library packages carry their own copy of this
    /// class rather than sharing one.
    /// </remarks>
    internal static class McpHostEventIds
    {
        public const int Program = 0;
    }
}
