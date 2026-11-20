/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Severity of a <see cref="PubSubConfigurationIssue"/>.
    /// </summary>
    public enum PubSubConfigurationIssueSeverity
    {
        /// <summary>
        /// Informational. Does not invalidate the configuration.
        /// </summary>
        Info,

        /// <summary>
        /// Warning. Does not invalidate the configuration but signals a
        /// potential problem the operator should review.
        /// </summary>
        Warning,

        /// <summary>
        /// Error. Invalidates the configuration.
        /// </summary>
        Error
    }
}
