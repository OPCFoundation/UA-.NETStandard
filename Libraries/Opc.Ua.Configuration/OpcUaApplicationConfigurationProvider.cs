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
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// Default shared application-configuration provider.
    /// </summary>
    public sealed class OpcUaApplicationConfigurationProvider :
        IOpcUaApplicationConfigurationProvider
    {
        /// <summary>
        /// Initializes a new provider.
        /// </summary>
        public OpcUaApplicationConfigurationProvider(
            OpcUaApplicationOptions options,
            IApplicationInstanceFactory applicationFactory,
            ITelemetryContext telemetry,
            ArrayOf<IOpcUaApplicationConfigurationFeature> features,
            ICertificateManager? certificateManager = null)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (applicationFactory is null)
            {
                throw new ArgumentNullException(nameof(applicationFactory));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            if (features.IsNull)
            {
                throw new ArgumentNullException(nameof(features));
            }

            OpcUaApplicationOptions effectiveOptions = options.Clone();
            for (int i = 0; i < features.Count; i++)
            {
                IOpcUaApplicationConfigurationFeature feature = features[i];
                feature.ApplyDefaults(effectiveOptions);
            }

            if (features.IsEmpty)
            {
                throw new InvalidOperationException(
                    "ConfigureApplication requires AddClient, AddServer, or both.");
            }
            if (string.IsNullOrWhiteSpace(effectiveOptions.ApplicationName))
            {
                throw new InvalidOperationException(
                    "OpcUaApplicationOptions.ApplicationName is required.");
            }

            string applicationName = effectiveOptions.ApplicationName;
            string pkiRoot = string.IsNullOrWhiteSpace(effectiveOptions.PkiRoot)
                ? Path.Combine(
                    Path.GetTempPath(),
                    "OPC Foundation",
                    applicationName,
                    "pki")
                : effectiveOptions.PkiRoot;
            string subjectName = string.IsNullOrWhiteSpace(effectiveOptions.SubjectName)
                ? $"CN={applicationName}, O=OPC Foundation, DC=localhost"
                : effectiveOptions.SubjectName;

            Application = applicationFactory.Create(telemetry);
            Application.ApplicationName = applicationName;

            IApplicationConfigurationBuilderTypes builder = Application.Build(
                effectiveOptions.ApplicationUri ?? string.Empty,
                effectiveOptions.ProductUri ?? string.Empty);
            IApplicationConfigurationBuilderSecurity? securityBuilder = null;
            for (int i = 0; i < features.Count; i++)
            {
                securityBuilder = features[i].Configure(builder);
            }
            if (securityBuilder == null)
            {
                throw new InvalidOperationException(
                    "No application configuration feature selected a client or server type.");
            }

            ApplicationConfiguration configuration = Application.ApplicationConfiguration ??
                throw new InvalidOperationException(
                    "The application configuration builder did not create a configuration.");
            configuration.ApplicationType = Application.ApplicationType;
            if (certificateManager != null)
            {
                configuration.CertificateManager = certificateManager;
            }

            ArrayOf<CertificateIdentifier> certificates =
                ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                    subjectName,
                    CertificateStoreType.Directory,
                    pkiRoot);
            IApplicationConfigurationBuilderSecurityOptions securityOptions =
                securityBuilder.AddSecurityConfiguration(certificates, pkiRoot);
            if (effectiveOptions.AutoAcceptUntrustedCertificates is bool autoAccept)
            {
                securityOptions = securityOptions.SetAutoAcceptUntrustedCertificates(autoAccept);
            }
            if (effectiveOptions.RejectSHA1SignedCertificates is bool rejectSha1)
            {
                securityOptions = securityOptions.SetRejectSHA1SignedCertificates(rejectSha1);
            }
            if (effectiveOptions.MinimumCertificateKeySize is ushort minimumKeySize)
            {
                securityOptions = securityOptions.SetMinimumCertificateKeySize(minimumKeySize);
            }

            Configuration = configuration;
            m_certificateManager = certificateManager;
            m_builder = securityOptions;
        }

        /// <inheritdoc/>
        public IApplicationInstance Application { get; }

        /// <inheritdoc/>
        public ApplicationConfiguration Configuration { get; }

        /// <inheritdoc/>
        public async Task<ApplicationConfiguration> GetAsync(CancellationToken ct = default)
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(OpcUaApplicationConfigurationProvider));
            }

            Task<ApplicationConfiguration> createTask;
            lock (m_lock)
            {
                if (m_disposed != 0)
                {
                    throw new ObjectDisposedException(
                        nameof(OpcUaApplicationConfigurationProvider));
                }
                m_createTask ??= CreateAsync();
                createTask = m_createTask;
            }

            await ((Task)createTask).WaitAsync(ct).ConfigureAwait(false);
            return await createTask.ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) != 0)
            {
                return;
            }

            Task<ApplicationConfiguration>? createTask;
            lock (m_lock)
            {
                createTask = m_createTask;
            }

            try
            {
                if (createTask != null)
                {
                    await createTask.ConfigureAwait(false);
                }
            }
            finally
            {
                await Application.DisposeAsync().ConfigureAwait(false);
                GC.SuppressFinalize(this);
            }
        }

        private async Task<ApplicationConfiguration> CreateAsync()
        {
            ApplicationConfiguration configuration = await m_builder
                .CreateAsync(CancellationToken.None)
                .ConfigureAwait(false);
            if (Application.ApplicationType is
                ApplicationType.Client or ApplicationType.ClientAndServer)
            {
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
            }
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
            using var certificates =
                certificateManager.SnapshotApplicationCertificates();
            return certificates.Count > 0;
        }

        private readonly ICertificateManager? m_certificateManager;
        private readonly IApplicationConfigurationBuilderCreate m_builder;
        private readonly Lock m_lock = new();
        private Task<ApplicationConfiguration>? m_createTask;
        private int m_disposed;
    }
}
