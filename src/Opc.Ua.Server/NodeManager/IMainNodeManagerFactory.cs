// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server
{
    /// <summary>
    /// Interface of the main node manager factory which helps creating main
    /// node managers used by the server.
    /// </summary>
    public interface IMainNodeManagerFactory
    {
        /// <summary>
        /// Creates the configuration node manager.
        /// </summary>
        /// <returns>The configuration node manager.</returns>
        IConfigurationNodeManager CreateConfigurationNodeManager();

        /// <summary>
        /// Creates the core node manager.
        /// </summary>
        /// <param name="dynamicNamespaceIndex">The namespace index of the dynamic namespace.</param>
        /// <returns>The core node manager</returns>
        ICoreNodeManager CreateCoreNodeManager(ushort dynamicNamespaceIndex);
    }
}
