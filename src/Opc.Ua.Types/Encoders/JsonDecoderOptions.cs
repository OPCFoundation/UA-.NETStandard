// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua
{
    /// <summary>
    /// Json decoder options
    /// </summary>
    public record class JsonDecoderOptions
    {
        /// <summary>
        /// Enforces parsing json according to the specification.
        /// In case parsing fails, an exception is thrown. Set to
        /// false to provide more leeway and allow values to be
        /// set to defaults.
        /// </summary>
        public bool ParseStrict { get; set; } = true;

        /// <summary>
        /// Update namespace table
        /// </summary>
        public bool UpdateNamespaceTable { get; set; }

        /// <summary>
        /// Match property names without regard for case. This
        /// is not compliant with the specification and implies
        /// ParseStrict to be set to false.
        /// </summary>
        public bool CaseInsensitivePropertyMatching { get; set; }
    }
}
