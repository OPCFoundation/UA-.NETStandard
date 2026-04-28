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

using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Quickstarts.ReferenceServer;
using TUnit.Core.Interfaces;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Shared fixture that starts a ReferenceServer in-process and
    /// provides a connected <see cref="ISession"/> for tests.
    /// This fixture is created once and shared across all tests.
    /// </summary>
    public sealed class AotTestFixture : IAsyncInitializer, IAsyncDisposable
    {
        public AotServerFixture<ReferenceServer> ServerFixture { get; private set; } = null!;
        public ISession Session { get; private set; }
        public string ServerUrl { get; private set; } = null!;
        public ITelemetryContext Telemetry { get; private set; } = null!;
        private ApplicationConfiguration m_clientConfiguration;
        private string m_pkiRoot;

        public async Task InitializeAsync()
        {
            Telemetry = DefaultTelemetry.Create(builder =>
                builder.SetMinimumLevel(LogLevel.Warning));

            // Start server
            ServerFixture = new AotServerFixture<ReferenceServer>(
                t => new ReferenceServer(t), Telemetry)
            {
                AutoAccept = true,
                SecurityNone = true,
                AllNodeManagers = true
            };
            await ServerFixture.LoadConfigurationAsync(
                Path.Combine(Directory.GetCurrentDirectory(), "pki")).ConfigureAwait(false);
            await ServerFixture.StartAsync().ConfigureAwait(false);

            ServerUrl = $"opc.tcp://localhost:{ServerFixture.Port}" +
                "/Quickstarts/ReferenceServer";

            m_pkiRoot = Path.Combine(
                Path.GetTempPath(), "OpcUaAotTests", "pki");

            // Create a client config programmatically
            m_clientConfiguration = new ApplicationConfiguration(Telemetry)
            {
                ApplicationName = "AotTestClient",
                ApplicationUri = "urn:localhost:OPCFoundation:AotTestClient",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(m_pkiRoot, "own"),
                        SubjectName = "CN=AotTestClient, O=OPC Foundation"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(m_pkiRoot, "issuer")
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(m_pkiRoot, "trusted")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(m_pkiRoot, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = true
                },
                TransportQuotas = new TransportQuotas
                {
                    MaxMessageSize = 4 * 1024 * 1024
                },
                ClientConfiguration = new ClientConfiguration(),
                ServerConfiguration = new ServerConfiguration()
            };
            await m_clientConfiguration.ValidateAsync(
                ApplicationType.Client).ConfigureAwait(false);
            m_clientConfiguration.CertificateValidator
                .CertificateValidation += (s, e) => e.Accept = true;

            // Connect session
            EndpointDescription endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                m_clientConfiguration, ServerUrl, useSecurity: false,
                Telemetry, CancellationToken.None).ConfigureAwait(false);
            var configuredEndpoint = new ConfiguredEndpoint(
                null, endpointDescription,
                EndpointConfiguration.Create(m_clientConfiguration));

            var sessionFactory = new ClassicSessionFactory(Telemetry);
#pragma warning disable CA2000 // Dispose objects before losing scope
            Session = await sessionFactory.CreateAsync(
                m_clientConfiguration,
                configuredEndpoint,
                updateBeforeConnect: false,
                sessionName: "AotTest",
                sessionTimeout: 60000,
                identity: new UserIdentity(new AnonymousIdentityToken()),
                preferredLocales: default,
                ct: CancellationToken.None).ConfigureAwait(false);
#pragma warning restore CA2000 // Dispose objects before losing scope
        }

        /// <summary>
        /// Creates a new independent <see cref="ISession"/> connected to the
        /// same server. Callers are responsible for closing and disposing.
        /// </summary>
        public async Task<ISession> CreateSessionAsync(
            string sessionName = "AotTestNewSession")
        {
            EndpointDescription endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                m_clientConfiguration, ServerUrl, useSecurity: false,
                Telemetry, CancellationToken.None).ConfigureAwait(false);
            var configuredEndpoint = new ConfiguredEndpoint(
                null, endpointDescription,
                EndpointConfiguration.Create(m_clientConfiguration));

            var sessionFactory = new ClassicSessionFactory(Telemetry);
#pragma warning disable CA2000 // Dispose objects before losing scope
            return await sessionFactory.CreateAsync(
                m_clientConfiguration,
                configuredEndpoint,
                updateBeforeConnect: false,
                sessionName: sessionName,
                sessionTimeout: 60000,
                identity: new UserIdentity(new AnonymousIdentityToken()),
                preferredLocales: default,
                ct: CancellationToken.None).ConfigureAwait(false);
#pragma warning restore CA2000 // Dispose objects before losing scope
        }

        public async ValueTask DisposeAsync()
        {
            if (Session != null)
            {
                Session.DeleteSubscriptionsOnClose = true;
                await Session.CloseAsync(CancellationToken.None)
                    .ConfigureAwait(false);
                Session.Dispose();
                Session = null;
            }

            if (ServerFixture != null)
            {
                await ServerFixture.StopAsync().ConfigureAwait(false);
            }

            // Clean up pki folders
            foreach (string dir in new[] {
                m_pkiRoot,
                Path.Combine(Directory.GetCurrentDirectory(), "pki") })
            {
                if (dir != null && Directory.Exists(dir))
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch
                    {
                        // ignore cleanup errors
                    }
                }
            }
        }
    }
}
