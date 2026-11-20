/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// Centrally managed event id offsets for the source-generated log messages of the
    /// Opc.Ua.Types assembly.
    /// </summary>
    /// <remarks>
    /// Each per-file <c>&lt;ClassName&gt;Log</c> class allocates its event ids relative to the
    /// offset constant below, using <c>offset + &lt;zero-based message index&gt;</c>. Every block
    /// reserves at least five spare slots for future messages and is rounded up to the next
    /// multiple of ten so that ids can be documented and managed from this single location.
    /// </remarks>
    internal static class TypesEventIds
    {
        public const int BaseVariableState = 0;
        public const int DiagnosticInfo = 10;
        public const int Encoding = 20;
        public const int LocalizedText = 40;
        public const int Matrix = 50;
        public const int XmlSchemaValidator = 60;
        public const int BackgroundTaskScope = 70;

        /// <summary>Optional method-validation observer failures.</summary>
        public const int MethodState = 80;
    }
}
