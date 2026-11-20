// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server
{
    /// <summary>
    /// Identifies the continuation point family stored in a continuation-point mirror.
    /// </summary>
    public enum ContinuationPointKind
    {
        /// <summary>
        /// Browse or BrowseNext continuation point.
        /// </summary>
        Browse = 0,

        /// <summary>
        /// HistoryRead continuation point.
        /// </summary>
        History = 1
    }
}
