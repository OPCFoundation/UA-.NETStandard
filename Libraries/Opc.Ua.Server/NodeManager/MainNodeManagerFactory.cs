/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

namespace Opc.Ua.Server
{
    /// <summary>
    /// The factory that creates the main node managers of the server. The main
    /// node managers are the one always present when creating a server.
    /// </summary>
    public class MainNodeManagerFactory : IMainNodeManagerFactory
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public MainNodeManagerFactory(
            ApplicationConfiguration applicationConfiguration,
            IServerInternal server)
            : this(applicationConfiguration, server, coordinator: null)
        {
        }

        /// <summary>
        /// Initializes the object with an explicit PushManagement
        /// transaction coordinator, replacing the default one that
        /// <see cref="ConfigurationNodeManager"/> would otherwise create.
        /// </summary>
        /// <param name="applicationConfiguration">The application configuration.</param>
        /// <param name="server">The server.</param>
        /// <param name="coordinator">
        /// The shared PushManagement transaction coordinator, or
        /// <see langword="null"/> to let <see cref="ConfigurationNodeManager"/>
        /// create its own default instance.
        /// </param>
        public MainNodeManagerFactory(
            ApplicationConfiguration applicationConfiguration,
            IServerInternal server,
            IPushConfigurationTransactionCoordinator? coordinator)
            : this(applicationConfiguration, server, coordinator, pendingKeyStore: null)
        {
        }

        /// <summary>
        /// Initializes the object with an explicit PushManagement
        /// transaction coordinator and pending-key store, replacing the
        /// defaults that <see cref="ConfigurationNodeManager"/> would
        /// otherwise create.
        /// </summary>
        /// <param name="applicationConfiguration">The application configuration.</param>
        /// <param name="server">The server.</param>
        /// <param name="coordinator">
        /// The shared PushManagement transaction coordinator, or
        /// <see langword="null"/> to let <see cref="ConfigurationNodeManager"/>
        /// create its own default instance.
        /// </param>
        /// <param name="pendingKeyStore">
        /// The store used to persist regenerated signing-request private
        /// keys (§7.10.10), or <see langword="null"/> to let
        /// <see cref="ConfigurationNodeManager"/> create its own default
        /// <see cref="DirectoryPendingCertificateKeyStore"/>.
        /// </param>
        /// <param name="keyGenerator">
        /// The generator that creates regenerated signing-request keys with
        /// genuine nonce entropy (§7.10.10), or <see langword="null"/> to let
        /// <see cref="ConfigurationNodeManager"/> create its own default
        /// <see cref="AdditionalEntropyCertificateKeyGenerator"/>.
        /// </param>
        /// <param name="trustListEffectHandler">
        /// The handler that applies the §7.10.9 post-<c>ApplyChanges</c>
        /// TrustList effects, or <see langword="null"/> to let
        /// <see cref="ConfigurationNodeManager"/> create its own default
        /// <see cref="PushConfigurationTrustListEffectHandler"/>.
        /// </param>
        /// <param name="serverConfigurationOptions">
        /// Configures the Optional OPC 10000-12 §7.10.3
        /// <c>ServerConfigurationType</c> surface (<c>HasSecureElement</c>,
        /// <c>InApplicationSetup</c>, <c>ResetToServerDefaults</c>,
        /// <c>ConfigurationFile</c>), or <see langword="null"/> to expose only
        /// the always-known identity Properties.
        /// </param>
        public MainNodeManagerFactory(
            ApplicationConfiguration applicationConfiguration,
            IServerInternal server,
            IPushConfigurationTransactionCoordinator? coordinator,
            IPendingCertificateKeyStore? pendingKeyStore,
            IPushCertificateKeyGenerator? keyGenerator = null,
            IPushConfigurationTrustListEffectHandler? trustListEffectHandler = null,
            ServerConfigurationOptions? serverConfigurationOptions = null)
        {
            m_applicationConfiguration = applicationConfiguration;
            m_server = server;
            m_coordinator = coordinator;
            m_pendingKeyStore = pendingKeyStore;
            m_keyGenerator = keyGenerator;
            m_trustListEffectHandler = trustListEffectHandler;
            m_serverConfigurationOptions = serverConfigurationOptions;
        }

        /// <inheritdoc/>
        public IConfigurationNodeManager CreateConfigurationNodeManager()
        {
            return new ConfigurationNodeManager(
                m_server,
                m_applicationConfiguration,
                m_server.Telemetry.CreateLogger<ConfigurationNodeManager>(),
                timeProvider: null,
                m_coordinator,
                m_pendingKeyStore,
                m_keyGenerator,
                m_trustListEffectHandler,
                m_serverConfigurationOptions);
        }

        /// <inheritdoc/>
        public ICoreNodeManager CreateCoreNodeManager(ushort dynamicNamespaceIndex)
        {
            return new CoreNodeManager(m_server, m_applicationConfiguration, dynamicNamespaceIndex);
        }

        private readonly ApplicationConfiguration m_applicationConfiguration;
        private readonly IServerInternal m_server;
        private readonly IPushConfigurationTransactionCoordinator? m_coordinator;
        private readonly IPendingCertificateKeyStore? m_pendingKeyStore;
        private readonly IPushCertificateKeyGenerator? m_keyGenerator;
        private readonly IPushConfigurationTrustListEffectHandler? m_trustListEffectHandler;
        private readonly ServerConfigurationOptions? m_serverConfigurationOptions;
    }
}
