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
        private bool m_disposed;
        public ApplicationConfiguration ClientConfig { get; }
        public Certificate ClientCertificate { get; }

        private CertSessionContext(
            ApplicationConfiguration clientConfig,
            Certificate clientCertificate,
            string pkiRoot)
        {
            ClientConfig = clientConfig;
            ClientCertificate = clientCertificate;
            m_pkiRoot = pkiRoot;
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

                var clientApp = new ApplicationInstance(telemetry)
                {
                    ApplicationName = "ConformanceTestClient",
                    ApplicationType = ApplicationType.Client
                };

                ApplicationConfiguration clientConfig;
                await using (clientApp.ConfigureAwait(false))
                {
                    clientConfig = await clientApp
                        .Build(applicationUri, "urn:opcfoundation.org:ConformanceTestClient")
                        .AsClient()
                        .AddSecurityConfiguration(new[] { certIdentifier }.ToArrayOf(), pkiRoot)
                        .SetMinimumCertificateKeySize(1024)
                        .SetAutoAcceptUntrustedCertificates(true)
                        .SetRejectSHA1SignedCertificates(false)
                        .CreateAsync()
                        .ConfigureAwait(false);
                }

                return new CertSessionContext(clientConfig, clientCertificate, pkiRoot);
            }
            catch
            {
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

        public ValueTask DisposeAsync()
        {
            if (m_disposed)
            {
                return default;
            }
            m_disposed = true;
            ClientCertificate?.Dispose();
            TryDeleteDirectory(m_pkiRoot);
            return default;
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
