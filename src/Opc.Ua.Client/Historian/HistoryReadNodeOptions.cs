/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Client.Historian
{
    /// <summary>
    /// Per-node HistoryRead options used for structured values and arrays.
    /// </summary>
    public sealed record HistoryReadNodeOptions
    {
        /// <summary>
        /// Optional index range applied to returned historical values.
        /// </summary>
        public NumericRange IndexRange { get; init; }

        /// <summary>
        /// Requested data encoding for returned structured values.
        /// </summary>
        public QualifiedName DataEncoding { get; init; } = QualifiedName.Null;
    }
}
