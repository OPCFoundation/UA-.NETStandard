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

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    public partial class StandardServer
    {
        /// <summary>
        /// Configuration hot-reload of the server: watches the configuration file,
        /// reloads it when it changes, applies the trust-list part of the security
        /// configuration, and fans certificate changes of the certificate manager out
        /// to the endpoints.
        /// </summary>
        /// <remarks>
        /// Nested in <see cref="StandardServer"/> because a file change is dispatched
        /// through the protected virtual <see cref="OnConfigurationChangedAsync"/> and
        /// <see cref="OnUpdateConfigurationAsync"/> hooks, which derived servers override.
        /// </remarks>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Design",
            "CA1001:Types that own disposable fields should be disposable",
            Justification = "The owning server releases the watcher and the subscription as separate shutdown stages.")]
        private sealed class ConfigurationReload
        {
            /// <summary>
            /// Creates the configuration hot-reload of a server.
            /// </summary>
            /// <param name="server">The server whose configuration is reloaded.</param>
            public ConfigurationReload(StandardServer server)
            {
                m_server = server;
            }

            /// <summary>
            /// Starts watching the configuration file (when the configuration was loaded
            /// from a file) and subscribes to the certificate changes of the certificate
            /// manager.
            /// </summary>
            /// <param name="configuration">The configuration the server started with.</param>
            public void Start(ApplicationConfiguration configuration)
            {
                StandardServer server = m_server;

                // monitor the configuration file.
                if (!string.IsNullOrEmpty(configuration.SourceFilePath))
                {
                    server.m_logger.ServerConfigurationWatcherStarted();
                    m_configurationWatcher = new ConfigurationWatcher(configuration, server.MessageContext.Telemetry);
                    m_configurationWatcher!.Changed += server.OnConfigurationChangedAsync;
                }

                // Log availability of the new CertificateManager
                if (server.CertificateManager != null)
                {
                    server.m_logger.CertificateManagerInitializedWithCountTrustLists(
                        server.CertificateManager.TrustLists.Count);

                    // Subscribe to CertificateManager change notifications and
                    // fan-out to OnCertificateUpdateAsync so endpoint descriptions
                    // and transport listeners pick up cert hot-updates.
                    m_certManagerSubscription = server.CertificateManager.CertificateChanges
                        .Subscribe(new CertificateManagerChangeObserver(server, server.m_logger));
                }
            }

            /// <summary>
            /// Stops watching the configuration file.
            /// </summary>
            public void DisposeConfigurationWatcher()
            {
                m_configurationWatcher?.Dispose();
                m_configurationWatcher = null;
            }

            /// <summary>
            /// Stops the fan-out of certificate changes.
            /// </summary>
            public void DisposeCertificateManagerSubscription()
            {
                m_certManagerSubscription?.Dispose();
                m_certManagerSubscription = null;
            }

            /// <summary>
            /// Loads the changed configuration file and applies it through the
            /// <see cref="OnUpdateConfigurationAsync"/> hook of the server.
            /// </summary>
            /// <param name="args">The change of the configuration file.</param>
            [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("ReflectionAnalysis", "IL2072",
                Justification = "Configuration.GetType() returns a concrete type whose constructor is preserved at the call site.")]
            public async Task ReloadAsync(ConfigurationWatcherEventArgs args)
            {
                StandardServer server = m_server;
                try
                {
                    ApplicationConfiguration configuration = await ApplicationConfiguration
                        .LoadAsync(
                            new FileInfo(args.FilePath),
                            server.Configuration!.ApplicationType,
                            server.Configuration!.GetType(),
                            server.MessageContext.Telemetry)
                        .ConfigureAwait(false);

                    await server.OnUpdateConfigurationAsync(configuration)
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    server.m_logger.NotLoadUpdatedConfigurationFileFromFilePath(e, args.FilePath);
                }
            }

            /// <summary>
            /// Applies the trusted issuer, trusted peer and rejected certificate stores of
            /// an updated configuration and updates the certificate manager.
            /// </summary>
            /// <param name="configuration">The updated configuration.</param>
            /// <param name="cancellationToken">Cancels waiting for the server lock.</param>
            public async ValueTask ApplyAsync(
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken)
            {
                StandardServer server = m_server;
                await server.m_semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    // update security configuration.
                    configuration.SecurityConfiguration.Validate(server.MessageContext.Telemetry);

                    ApplicationConfiguration currentConfiguration = server.Configuration!;
                    SecurityConfiguration currentSecurityConfiguration = currentConfiguration
                        .SecurityConfiguration;

                    currentSecurityConfiguration.TrustedIssuerCertificates = configuration
                        .SecurityConfiguration
                        .TrustedIssuerCertificates;
                    currentSecurityConfiguration.TrustedPeerCertificates = configuration
                        .SecurityConfiguration
                        .TrustedPeerCertificates;
                    currentSecurityConfiguration.RejectedCertificateStore = configuration
                        .SecurityConfiguration
                        .RejectedCertificateStore;

                    if (server.CertificateManager != null)
                    {
                        await server.CertificateManager.UpdateAsync(
                                server.Configuration!.SecurityConfiguration,
                                server.Configuration.ApplicationUri,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    server.m_logger.FailedToUpdateConfiguration(e);
                }
                finally
                {
                    server.m_semaphoreSlim.Release();
                }
            }

            private readonly StandardServer m_server;
            private ConfigurationWatcher? m_configurationWatcher;
            private IDisposable? m_certManagerSubscription;
        }
    }
}
