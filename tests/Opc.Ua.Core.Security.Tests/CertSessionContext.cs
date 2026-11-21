// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Core.Security.Tests
{
    /// <summary>
    /// Helper that opens an OPC UA session against the in-process
    /// reference server using a custom client application instance
    /// certificate. Used by the
    /// <see cref="SecurityCertValidationTests"/> and
    /// <see cref="SecurityNoneSession10Tests"/> conformance fixtures
    /// to verify the server's behaviour when the client presents a
    /// flawed certificate (expired, not-yet-valid, weak crypto,
    /// wrong host name, etc.).
    /// </summary>
    internal sealed class CertSessionContext : IAsyncDisposable
    {
        private readonly string m_pkiRoot;

        /// <summary>
        /// Owns the client application services that must stop before the temporary PKI directory is removed.
        /// </summary>
        private readonly ApplicationInstance m_application;
        private bool m_disposed;
        public ApplicationConfiguration ClientConfig { get; }
        public Certificate ClientCertificate { get; }

        /// <summary>
        /// Retains the configured client, its certificate, and the application responsible for its lifetime.
        /// </summary>
        private CertSessionContext(
            ApplicationConfiguration clientConfig,
            Certificate clientCertificate,
            string pkiRoot,
            ApplicationInstance application)
        {
            ClientConfig = clientConfig;
            ClientCertificate = clientCertificate;
            m_pkiRoot = pkiRoot;
            m_application = application;
        }

        /// <summary>
        /// Builds an <see cref="ApplicationConfiguration"/> that uses
        /// the supplied client certificate as its application instance
        /// certificate. The certificate is persisted to a temporary
        /// PKI directory which is cleaned up when the context is
        /// disposed.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="clientCertificate"/> is <c>null</c>.</exception>
        public static async Task<CertSessionContext> CreateAsync(
            Certificate clientCertificate,
            string applicationUri,
            ITelemetryContext telemetry)
        {
            if (clientCertificate == null)
            {
                throw new ArgumentNullException(nameof(clientCertificate));
            }
            if (applicationUri == null)
            {
                throw new ArgumentNullException(nameof(applicationUri));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            string pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();
            Directory.CreateDirectory(pkiRoot);
            ApplicationInstance? clientApp = null;

            try
            {
                string ownStorePath = Path.Combine(pkiRoot, "own");
                Directory.CreateDirectory(ownStorePath);

                using (ICertificateStore store = CertificateStoreIdentifier.CreateStore(
                    CertificateStoreType.Directory, telemetry))
                {
                    store.Open(ownStorePath, false);
                    await store.AddAsync(clientCertificate).ConfigureAwait(false);
                }

                var certIdentifier = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = ownStorePath,
                    RawData = clientCertificate.RawData,
                    Thumbprint = clientCertificate.Thumbprint
                };

                clientApp = new ApplicationInstance(telemetry)
                {
                    ApplicationName = "ConformanceTestClient",
                    ApplicationType = ApplicationType.Client
                };

                ApplicationConfiguration clientConfig = await clientApp
                    .Build(applicationUri, "urn:opcfoundation.org:ConformanceTestClient")
                    .AsClient()
                    .AddSecurityConfiguration(new[] { certIdentifier }.ToArrayOf(), pkiRoot)
                    .SetMinimumCertificateKeySize(1024)
                    .SetAutoAcceptUntrustedCertificates(true)
                    .SetRejectSHA1SignedCertificates(false)
                    .CreateAsync()
                    .ConfigureAwait(false);

                return new CertSessionContext(clientConfig, clientCertificate, pkiRoot, clientApp);
            }
            catch
            {
                if (clientApp != null)
                {
                    await clientApp.DisposeAsync().ConfigureAwait(false);
                }
                TryDeleteDirectory(pkiRoot);
                throw;
            }
        }

        /// <summary>
        /// Creates and opens a session against the supplied configured
        /// endpoint using the certificate provided to
        /// <see cref="CreateAsync"/>. Bypasses the retry wrapper so
        /// the caller observes the first error directly.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="endpoint"/> is <c>null</c>.</exception>
        public async Task<ISession> OpenSessionAsync(
            ConfiguredEndpoint endpoint,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken = default)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            var sessionFactory = new DefaultSessionFactory(telemetry);
            return await sessionFactory.CreateAsync(
                ClientConfig,
                endpoint,
                updateBeforeConnect: false,
                checkDomain: false,
                sessionName: "CertSession",
                sessionTimeout: 60000,
                identity: null,
                preferredLocales: default,
                ct: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Disposes the client application before releasing its certificate and temporary PKI storage.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return;
            }
            m_disposed = true;
            await m_application.DisposeAsync().ConfigureAwait(false);
            ClientCertificate?.Dispose();
            TryDeleteDirectory(m_pkiRoot);
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
