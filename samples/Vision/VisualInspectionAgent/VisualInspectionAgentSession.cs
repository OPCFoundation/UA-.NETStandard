/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Configuration;

namespace Vision.VisualInspectionAgent
{
    internal sealed class VisualInspectionAgentSession : IAsyncDisposable
    {
        private VisualInspectionAgentSession(
            ISession session,
            IStreamingSubscription streaming,
            ApplicationConfiguration configuration,
            ITelemetryContext telemetry)
        {
            Session = session;
            Streaming = streaming;
            m_configuration = configuration;
            Telemetry = telemetry;
        }

        public ISession Session { get; }

        public IStreamingSubscription Streaming { get; }

        public ITelemetryContext Telemetry { get; }

        public static async Task<VisualInspectionAgentSession> ConnectAsync(
            VisualInspectionAgentOptions options,
            CancellationToken cancellationToken)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            ITelemetryContext telemetry = DefaultTelemetry.Create(builder =>
                builder.SetMinimumLevel(LogLevel.Warning));
            string pkiRoot = GetPrivateStateRoot();
            var configuration = new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "VisualInspectionAgent",
                ApplicationUri = "urn:localhost:OPCFoundation:VisualInspectionAgent",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "own"),
                        SubjectName = "CN=VisualInspectionAgent, O=OPC Foundation"
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
            await configuration.ValidateAsync(ApplicationType.Client, cancellationToken).ConfigureAwait(false);

            var appInstance = new ApplicationInstance(configuration, telemetry);
            await appInstance.CheckApplicationInstanceCertificatesAsync(true, ct: cancellationToken)
                .ConfigureAwait(false);
            await appInstance.DisposeAsync().ConfigureAwait(false);
            configuration.CertificateManager ??= CertificateManagerFactory.Create(
                configuration.SecurityConfiguration, telemetry);
            if (options.Insecure)
            {
                configuration.CertificateManager.AcceptError = static (_, _) => true;
                Console.Error.WriteLine("WARNING: --insecure accepts any server certificate for the sample only.");
            }

            EndpointDescription? endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                configuration,
                options.ServerUrl,
                useSecurity: true,
                discoverTimeout: 15000,
                telemetry,
                cancellationToken).ConfigureAwait(false);
            if (endpointDescription is null)
            {
                throw ServiceResultException.Create(StatusCodes.BadTimeout, "Could not reach {0}.", options.ServerUrl);
            }

            var endpoint = new ConfiguredEndpoint(
                null,
                endpointDescription,
                EndpointConfiguration.Create(configuration));
            ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
                .UseEndpoint(endpoint)
                .WithSessionName("VisualInspectionAgent")
                .WithSessionTimeout(TimeSpan.FromSeconds(60))
                .WithUserIdentity(new UserIdentity(new AnonymousIdentityToken()))
                .ConnectAsync(cancellationToken).ConfigureAwait(false);
            return new VisualInspectionAgentSession(session, session.DefaultStreaming, configuration, telemetry);
        }

        public async ValueTask DisposeAsync()
        {
            await Streaming.DisposeAsync().ConfigureAwait(false);
            await Session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await Session.DisposeAsync().ConfigureAwait(false);
            (m_configuration.CertificateManager as IDisposable)?.Dispose();
        }

        private static string GetPrivateStateRoot()
        {
            string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDirectory))
            {
                baseDirectory = AppContext.BaseDirectory;
            }
            string root = Path.Combine(baseDirectory, "OPC Foundation", "VisualInspectionAgent", "pki");
            Directory.CreateDirectory(root);
            return root;
        }

        private readonly ApplicationConfiguration m_configuration;
    }
}
