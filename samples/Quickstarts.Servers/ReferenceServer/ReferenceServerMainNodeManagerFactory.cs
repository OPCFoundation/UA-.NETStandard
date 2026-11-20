/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// Main node manager factory variant that returns the reference-server-specific
    /// <see cref="ReferenceServerConfigurationNodeManager"/> in place of the default
    /// <see cref="ConfigurationNodeManager"/>. This keeps CTT-only address-space
    /// tweaks out of the SDK.
    /// </summary>
    internal sealed class ReferenceServerMainNodeManagerFactory : IMainNodeManagerFactory
    {
        public ReferenceServerMainNodeManagerFactory(
            ApplicationConfiguration applicationConfiguration,
            IServerInternal server,
            ServerConfigurationOptions? serverConfigurationOptions = null)
        {
            m_applicationConfiguration = applicationConfiguration;
            m_server = server;
            m_serverConfigurationOptions = serverConfigurationOptions;
        }

        /// <inheritdoc/>
        public IConfigurationNodeManager CreateConfigurationNodeManager()
        {
            return new ReferenceServerConfigurationNodeManager(
                m_server, m_applicationConfiguration, m_serverConfigurationOptions);
        }

        /// <inheritdoc/>
        public ICoreNodeManager CreateCoreNodeManager(ushort dynamicNamespaceIndex)
        {
            return new CoreNodeManager(
                m_server, m_applicationConfiguration, dynamicNamespaceIndex);
        }

        private readonly ApplicationConfiguration m_applicationConfiguration;
        private readonly IServerInternal m_server;
        private readonly ServerConfigurationOptions? m_serverConfigurationOptions;
    }
}
