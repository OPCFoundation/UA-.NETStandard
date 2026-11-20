// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.AI.Server
{
    /// <summary>
    /// Classifies a learning sample submitted to the server-side accounting API.
    /// </summary>
    public enum AILearningSampleKind
    {
        /// <summary>
        /// The sample carries an observation or corrected geometry.
        /// </summary>
        Positive,

        /// <summary>
        /// The sample is an empty or retracted observation and still counts.
        /// </summary>
        Negative
    }
}
