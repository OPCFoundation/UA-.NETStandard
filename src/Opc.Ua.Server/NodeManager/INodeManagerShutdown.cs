/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Drains accepted work that can call back into the server before serialized
    /// address-space deletion or disposal begins.
    /// </summary>
    public interface INodeManagerShutdown
    {
        /// <summary>
        /// Stops admission and drains accepted work while session notifications and
        /// the address space are still available. Repeated calls join the same work.
        /// </summary>
        ValueTask PrepareForShutdownAsync();
    }
}
