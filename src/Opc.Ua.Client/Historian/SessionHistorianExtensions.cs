/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Client.Historian
{
    /// <summary>
    /// Convenience extensions that root a <see cref="HistoryClient"/> on
    /// an existing <see cref="ISession"/>.
    /// </summary>
    public static class SessionHistorianExtensions
    {
        /// <summary>
        /// Creates a new <see cref="HistoryClient"/> bound to the
        /// supplied session.
        /// </summary>
        public static HistoryClient Historian(this ISession session, HistoryClientOptions? options = null)
        {
            return new HistoryClient(session, options);
        }
    }
}
