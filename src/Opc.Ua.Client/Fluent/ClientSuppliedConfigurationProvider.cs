/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Client
{
    /// <summary>
    /// <see cref="IOpcUaApplicationConfigurationProvider"/> backed by an
    /// existing OPC UA application configuration XML document supplied via
    /// <see cref="OpcUaClientOptions.ConfigurationFile"/> or
    /// <see cref="OpcUaClientOptions.ConfigurationStream"/>. The document
    /// is loaded lazily on the first <see cref="GetAsync"/>: it is read
    /// through <c>ApplicationInstance.LoadApplicationConfigurationAsync</c>
    /// so every setting applies exactly as on the classic path, the
    /// optional <see cref="OpcUaClientOptions.ConfigureLoadedConfiguration"/>
    /// override callback runs, and the application-instance certificate is
    /// ensured — mirroring how the shared
    /// <c>OpcUaApplicationConfigurationProvider</c> completes client
    /// configurations. A supplied stream is read once and disposed.
    /// </summary>
    internal sealed class ClientSuppliedConfigurationProvider :
        IOpcUaApplicationConfigurationProvider
    {
        public ClientSuppliedConfigurationProvider(
            string? configurationFile,
            Stream? configurationStream,
            Action<ApplicationConfiguration>? configureLoadedConfiguration,
            IApplicationInstanceFactory applicationFactory,
            ITelemetryContext telemetry,
            ICertificateManager? certificateManager = null,
            ICertificatePasswordProvider? certificatePasswordProvider = null)
        {
            if (applicationFactory is null)
            {
                throw new ArgumentNullException(nameof(applicationFactory));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (string.IsNullOrEmpty(configurationFile) && configurationStream == null)
            {
                throw new ArgumentException(
                    "Either a configuration file path or a configuration stream is required.",
                    nameof(configurationFile));
            }

            m_configurationFile = configurationFile;
            m_configurationStream = configurationStream;
            m_configureLoadedConfiguration = configureLoadedConfiguration;
            m_certificateManager = certificateManager;

            Application = applicationFactory.Create(telemetry);
            Application.ApplicationType = ApplicationType.Client;
            Application.CertificatePasswordProvider = certificatePasswordProvider;
        }

        /// <inheritdoc/>
        public IApplicationInstance Application { get; }

        /// <inheritdoc/>
        /// <remarks>
        /// The supplied document loads lazily, so the configuration is only
        /// available after the first <see cref="GetAsync"/> has completed.
        /// </remarks>
        public ApplicationConfiguration Configuration =>
            Volatile.Read(ref m_configuration) ??
            throw new InvalidOperationException(
                "The supplied configuration document has not been loaded yet. " +
                "Await GetAsync (e.g. by connecting a session) first.");

        /// <inheritdoc/>
        public async Task<ApplicationConfiguration> GetAsync(CancellationToken ct = default)
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(ClientSuppliedConfigurationProvider));
            }

            Task<ApplicationConfiguration> loadTask;
            lock (m_lock)
            {
                if (m_disposed != 0)
                {
                    throw new ObjectDisposedException(
                        nameof(ClientSuppliedConfigurationProvider));
                }
                m_loadTask ??= LoadAsync();
                loadTask = m_loadTask;
            }

            await ((Task)loadTask).WaitAsync(ct).ConfigureAwait(false);
            return await loadTask.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            Task<ApplicationConfiguration>? loadTask;
            lock (m_lock)
            {
                loadTask = m_loadTask;
            }

            try
            {
                if (loadTask != null)
                {
                    // Wait for a load in flight so the application is not
                    // disposed under it; a failed load has already surfaced
                    // to the GetAsync caller and must not fail disposal too.
                    await loadTask.ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                if (loadTask == null)
                {
                    // Never consumed: the supplied stream is owned by this
                    // provider and would otherwise leak.
                    m_configurationStream?.Dispose();
                }
                await Application.DisposeAsync().ConfigureAwait(false);
            }
        }

        private async Task<ApplicationConfiguration> LoadAsync()
        {
            ApplicationConfiguration configuration;
            if (m_configurationStream is { } configurationStream)
            {
                using (configurationStream)
                {
                    configuration = await Application
                        .LoadApplicationConfigurationAsync(
                            configurationStream,
                            silent: false,
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                configuration = await Application
                    .LoadApplicationConfigurationAsync(
                        m_configurationFile!,
                        silent: false,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }

            Application.ApplicationName = configuration.ApplicationName;
            if (m_certificateManager != null)
            {
                configuration.CertificateManager = m_certificateManager;
            }
            m_configureLoadedConfiguration?.Invoke(configuration);

            bool haveCertificate = m_certificateManager is null or CertificateManager
                ? await Application
                    .CheckApplicationInstanceCertificatesAsync(
                        silent: true,
                        CertificateFactory.DefaultLifeTime,
                        CancellationToken.None)
                    .ConfigureAwait(false)
                : await HasApplicationCertificateAsync(
                    m_certificateManager,
                    configuration,
                    CancellationToken.None).ConfigureAwait(false);
            if (!haveCertificate)
            {
                throw new InvalidOperationException(
                    "Application instance certificate invalid.");
            }

            Volatile.Write(ref m_configuration, configuration);
            return configuration;
        }

        private static async Task<bool> HasApplicationCertificateAsync(
            ICertificateManager certificateManager,
            ApplicationConfiguration configuration,
            CancellationToken ct)
        {
            await certificateManager.UpdateAsync(
                configuration.SecurityConfiguration,
                configuration.ApplicationUri,
                ct).ConfigureAwait(false);
            using CertificateEntryCollection certificates =
                certificateManager.SnapshotApplicationCertificates();
            return certificates.Count > 0;
        }

        private readonly string? m_configurationFile;
        private readonly Stream? m_configurationStream;
        private readonly Action<ApplicationConfiguration>? m_configureLoadedConfiguration;
        private readonly ICertificateManager? m_certificateManager;
        private readonly Lock m_lock = new();
        private Task<ApplicationConfiguration>? m_loadTask;
        private ApplicationConfiguration? m_configuration;
        private int m_disposed;
    }
}
