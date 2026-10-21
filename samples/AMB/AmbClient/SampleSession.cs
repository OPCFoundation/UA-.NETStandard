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
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;

namespace AmbSample.Client
{
    /// <summary>
    /// Connects a demo session. Certificate handling is deliberately the
    /// minimum that lets the sample run against its own server.
    /// </summary>
    internal sealed class SampleSession : IAsyncDisposable
    {
        private SampleSession(
            ManagedSession session,
            ApplicationInstance application)
        {
            Session = session;
            m_application = application;
        }

        public ManagedSession Session { get; }

        public IStreamingSubscription Streaming => Session.DefaultStreaming;

        public static async Task<SampleSession> ConnectAsync(
            AmbClientOptions options,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken)
        {
            string pkiRoot = GetPrivateStateRoot();
            var configuration = new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "AmbClient",
                ApplicationUri = "urn:localhost:OPCFoundation:AmbClient",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "own"),
                        SubjectName = "CN=AmbClient, O=OPC Foundation"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "issuer")
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "trusted")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = options.Insecure
                },
                TransportQuotas = new TransportQuotas { MaxMessageSize = 8 * 1024 * 1024 },
                ClientConfiguration = new ClientConfiguration(),
                ServerConfiguration = new ServerConfiguration()
            };
            await configuration.ValidateAsync(ApplicationType.Client, cancellationToken)
                .ConfigureAwait(false);

            var appInstance = new ApplicationInstance(configuration, telemetry);
            await appInstance
                .CheckApplicationInstanceCertificatesAsync(true, ct: cancellationToken)
                .ConfigureAwait(false);
            // The application instance owns the certificate manager the
            // session validates with, so it lives as long as the session and
            // is disposed with it. Disposing it here would leave the session
            // with a disposed CertificateManager and every connect attempt
            // failing with ObjectDisposedException.
            configuration.CertificateManager ??= CertificateManagerFactory.Create(
                configuration.SecurityConfiguration,
                telemetry);
            try
            {
                EndpointDescription? endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                    configuration,
                    options.ServerUrl,
                    useSecurity: true,
                    discoverTimeout: 15000,
                    telemetry,
                    cancellationToken).ConfigureAwait(false) ?? throw ServiceResultException.Create(
                        StatusCodes.BadTimeout,
                        "Could not reach {0}.",
                        options.ServerUrl);

                ManagedSession session = await CreateSessionBuilder(
                    configuration, telemetry, options, endpointDescription)
                    .ConnectAsync(cancellationToken).ConfigureAwait(false);
                return new SampleSession(session, appInstance);
            }
            catch
            {
                await appInstance.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            // Tear the streaming subscription down while the channel is still
            // open, so removing its monitored items does not race the close.
            // A failing step does not keep the later ones from running.
            try
            {
                await Streaming.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    await Session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        await Session.DisposeAsync().ConfigureAwait(false);
                    }
                    finally
                    {
                        await m_application.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>
        /// Prepares the actual connection while retaining the operator's host across discovery and recovery.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The requested or discovered endpoint URL is invalid, or discovery changed the requested transport.
        /// </exception>
        internal static ManagedSessionBuilder CreateSessionBuilder(
            ApplicationConfiguration configuration,
            ITelemetryContext telemetry,
            AmbClientOptions options,
            EndpointDescription endpointDescription)
        {
            configuration.CertificateManager.AcceptError = options.Insecure ? static (_, _) => true : null;
            if (options.Insecure)
            {
                Console.Error.WriteLine(
                    "WARNING: --insecure is demo-only: any server certificate is accepted.");
            }
            string requestedUrl = options.ServerUrl;
            if (!Uri.TryCreate(requestedUrl, UriKind.Absolute, out Uri? requested) ||
                string.IsNullOrEmpty(requested.Host))
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument,
                    "The requested endpoint must have a valid absolute URL with a hostname.");
            }
            string? advertisedUrl = endpointDescription.EndpointUrl;
            if (!Uri.TryCreate(advertisedUrl, UriKind.Absolute, out Uri? advertised) ||
                string.IsNullOrEmpty(advertised.Host))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The discovered endpoint has no valid absolute URL with a hostname.");
            }
            if (!string.Equals(requested.Scheme, advertised.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed,
                    "The discovered endpoint changed the requested transport.");
            }
            EndpointDescription description = CoreUtils.Clone(endpointDescription)!;
            description.EndpointUrl = new UriBuilder(advertised)
            {
                Host = requested.IdnHost,
                Port = requested.Port
            }.Uri.AbsoluteUri;
            description.Server.DiscoveryUrls = [requested.AbsoluteUri];
            var endpoint = new ConfiguredEndpoint(
                null, description, EndpointConfiguration.Create(configuration));
            if (description.ServerCertificate.Length > 0)
            {
                using var certificate = Certificate.FromRawData(description.ServerCertificate);
                configuration.CertificateManager.ValidateDomains(certificate, endpoint, serverValidation: true);
            }
            return new ManagedSessionBuilder(configuration, telemetry)
                .UseEndpoint(endpoint)
                .WithCheckDomain(true)
                .WithSessionName("AmbClient")
                .WithSessionTimeout(TimeSpan.FromSeconds(60))
                .WithUserIdentity(new UserIdentity(new AnonymousIdentityToken()));
        }

        private static string GetPrivateStateRoot()
        {
            string baseDirectory = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDirectory))
            {
                baseDirectory = AppContext.BaseDirectory;
            }
            string root = Path.Combine(
                baseDirectory,
                "OPC Foundation",
                "AmbClient",
                "pki");
            Directory.CreateDirectory(root);
            return root;
        }

        private readonly ApplicationInstance m_application;
    }
}
