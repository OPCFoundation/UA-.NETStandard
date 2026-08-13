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

namespace BinPickingClient
{
    /// <summary>
    /// Connected OPC UA session to the bin-picking cell server, together with the
    /// application configuration and default streaming subscription the sample uses.
    /// </summary>
    internal sealed class BinPickingSampleSession : IAsyncDisposable
    {
        private BinPickingSampleSession(
            ISession session,
            IStreamingSubscription streaming,
            ApplicationConfiguration configuration)
        {
            Session = session;
            m_streaming = streaming;
            m_configuration = configuration;
        }

        public ISession Session { get; }

        public IStreamingSubscription Streaming => m_streaming;

        public static async Task<BinPickingSampleSession> ConnectAsync(
            BinPickingClientOptions options,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken)
        {
            string pkiRoot = GetPrivateStateRoot();
            var configuration = new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "BinPickingClient",
                ApplicationUri = "urn:localhost:OPCFoundation:BinPickingClient",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "own"),
                        SubjectName = "CN=BinPickingClient, O=OPC Foundation"
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
                // Camera frames are multi-megabyte ByteStrings. The default ByteString
                // ceiling is about 1 MB, so a frame this cell happily serves would be
                // refused on decode with BadEncodingLimitsExceeded.
                TransportQuotas = new TransportQuotas
                {
                    MaxMessageSize = 32 * 1024 * 1024,
                    MaxByteStringLength = 32 * 1024 * 1024,
                    MaxArrayLength = 32 * 1024 * 1024
                },
                ClientConfiguration = new ClientConfiguration(),
                ServerConfiguration = new ServerConfiguration()
            };
            await configuration.ValidateAsync(ApplicationType.Client, cancellationToken).ConfigureAwait(false);

            var appInstance = new ApplicationInstance(configuration, telemetry);
            await appInstance
                .CheckApplicationInstanceCertificatesAsync(true, ct: cancellationToken)
                .ConfigureAwait(false);
            await appInstance.DisposeAsync().ConfigureAwait(false);
            configuration.CertificateManager ??= CertificateManagerFactory.Create(
                configuration.SecurityConfiguration, telemetry);
            if (options.Insecure)
            {
                configuration.CertificateManager.AcceptError = static (_, _) => true;
                Console.Error.WriteLine("WARNING: --insecure is demo-only: any server certificate is accepted.");
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
                .WithSessionName("BinPickingClient")
                // Command authority is held per Session and is only handed back when the
                // Session ends, so a client that is killed keeps the robot until its
                // Session lapses. Sixty seconds of a locked cell is a long time in a
                // demo; fifteen still comfortably survives a transient reconnect.
                .WithSessionTimeout(TimeSpan.FromSeconds(15))
                .WithUserIdentity(new UserIdentity(new AnonymousIdentityToken()))
                .ConnectAsync(cancellationToken).ConfigureAwait(false);
            return new BinPickingSampleSession(session, session.DefaultStreaming, configuration);
        }

        public async ValueTask DisposeAsync()
        {
            await m_streaming.DisposeAsync().ConfigureAwait(false);
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
            string root = Path.Combine(baseDirectory, "OPC Foundation", "BinPickingClient", "pki");
            Directory.CreateDirectory(root);
            return root;
        }

        private readonly ApplicationConfiguration m_configuration;
        private readonly IStreamingSubscription m_streaming;
    }
}
