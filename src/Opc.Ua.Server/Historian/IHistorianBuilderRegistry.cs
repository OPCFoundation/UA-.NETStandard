/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Registers historian builders for server-managed lifecycle handling.
    /// </summary>
    internal interface IHistorianBuilderRegistry
    {
        /// <summary>
        /// Registers a historian builder whose capture pipeline must be managed by the server.
        /// </summary>
        /// <param name="builder">The builder whose capture pipeline the server will drain during shutdown.</param>
        void RegisterHistorianBuilder(HistorianBuilder builder);

        /// <summary>
        /// Removes a historian builder after its owner disposes it or its owning NodeManager generation retires.
        /// </summary>
        /// <param name="builder">The builder whose capture pipeline has been drained.</param>
        void UnregisterHistorianBuilder(HistorianBuilder builder);
    }
}
