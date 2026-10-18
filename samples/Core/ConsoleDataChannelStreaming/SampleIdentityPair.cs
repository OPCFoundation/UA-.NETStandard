/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;

namespace ConsoleDataChannelStreaming
{
    /// <summary>
    /// Creates two owned loopback application identities through the certificate manager and secret store.
    /// </summary>
    internal sealed class SampleIdentityPair : IAsyncDisposable
    {
        /// <summary>
        /// Selects an exclusively owned directory and an ephemeral secret-store password.
        /// </summary>
        private SampleIdentityPair(string root)
        {
            Root = root;
            Password = new CertificatePasswordProvider(new SecretRegistry(m_secrets), m_secretId);
        }

        /// <summary>
        /// Gets the newly created PKI directory, deleted only by its owner.
        /// </summary>
        public string Root { get; }

        /// <summary>
        /// Gets the initialized listening application's configuration and manager.
        /// </summary>
        public ApplicationInstance Server => m_server ?? throw new InvalidOperationException("No server identity.");

        /// <summary>
        /// Gets the initialized connecting application's configuration and manager.
        /// </summary>
        public ApplicationInstance Client => m_client ?? throw new InvalidOperationException("No client identity.");

        /// <summary>
        /// Gets the manager's injected password provider; no password is written to disk.
        /// </summary>
        public ICertificatePasswordProvider Password { get; }

        /// <summary>
        /// Provisions exact mutual public trust, without auto-accept or a certificate-validation callback.
        /// </summary>
        public static async Task<SampleIdentityPair> CreateAsync(
            string? stateDirectory,
            ArrayOf<string> endpoints,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken)
        {
            string root = Path.GetFullPath(
                stateDirectory ?? "data-channel-sample-state-" + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(root) || File.Exists(root))
            {
                throw new ArgumentException("--state must name a fresh directory; existing state is never replaced.");
            }
            Directory.CreateDirectory(root);
            var pair = new SampleIdentityPair(root);
            try
            {
                byte[] password = Encoding.UTF8.GetBytes(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
                try
                {
                    await pair.m_secrets.SetAsync(pair.m_secretId, password, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(password);
                }
                pair.m_server = await pair.CreateApplicationAsync(true, endpoints, telemetry, cancellationToken)
                    .ConfigureAwait(false);
                pair.m_client = await pair.CreateApplicationAsync(false, [], telemetry, cancellationToken)
                    .ConfigureAwait(false);
                ICertificateManager server = Manager(pair.Server);
                ICertificateManager client = Manager(pair.Client);
                using CertificateEntry serverIdentity = OwnCertificate(server);
                using CertificateEntry clientIdentity = OwnCertificate(client);
                using var serverPublic = new Certificate(serverIdentity.Certificate.RawData);
                using var clientPublic = new Certificate(clientIdentity.Certificate.RawData);
                await TrustAsync(server, TrustListIdentifier.Peers, clientPublic, cancellationToken)
                    .ConfigureAwait(false);
                await TrustAsync(client, TrustListIdentifier.Peers, serverPublic, cancellationToken)
                    .ConfigureAwait(false);
                await TrustAsync(server, TrustListIdentifier.Users, clientPublic, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                await pair.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            return pair;
        }

        /// <summary>
        /// Resolves an initialized manager without a raw key-loading fallback.
        /// </summary>
        public static ICertificateManager Manager(ApplicationInstance application)
        {
            return application.ApplicationConfiguration?.CertificateManager ??
                throw new InvalidOperationException("The application certificate manager was not initialized.");
        }

        /// <summary>
        /// Acquires the configured software application identity from its registry.
        /// </summary>
        public static CertificateEntry OwnCertificate(ICertificateManager manager)
        {
            return manager.AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256) ??
                throw new InvalidOperationException("The configured RSA/SHA-256 identity is unavailable.");
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (m_client != null)
                {
                    await m_client.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                try
                {
                    if (m_server != null)
                    {
                        await m_server.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    await m_secrets.RemoveAsync(m_secretId).ConfigureAwait(false);
                    if (Directory.Exists(Root))
                    {
                        Directory.Delete(Root, recursive: true);
                    }
                }
            }
        }

        /// <summary>
        /// Configures strict certificate validation and certificate-only optional Session authentication.
        /// </summary>
        private async Task<ApplicationInstance> CreateApplicationAsync(
            bool server,
            ArrayOf<string> endpoints,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken)
        {
            string name = server ? "DataChannelSampleServer" : "DataChannelSampleClient";
            string root = Path.Combine(Root, server ? "server" : "client");
            var application = new ApplicationInstance(telemetry)
            {
                ApplicationName = name,
                ApplicationType = server ? ApplicationType.Server : ApplicationType.Client,
                CertificatePasswordProvider = Password
            };
            try
            {
                ArrayOf<CertificateIdentifier> certificates =
                [
                    new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(root, "own"),
                        SubjectName = $"CN={name}, O=OPC Foundation, DC=localhost",
                        CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                    }
                ];
                IApplicationConfigurationBuilderTypes builder = application.Build(
                    server ? ServerApplicationUri : ClientApplicationUri, ModelNamespace);
                IApplicationConfigurationBuilderSecurityOptions security = server
                    ? builder.AsServer(endpoints.ToArray() ?? [])
                        .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256)
                        .AddUserTokenPolicy(new UserTokenPolicy
                        {
                            PolicyId = "certificate",
                            TokenType = UserTokenType.Certificate,
                            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                        })
                        .SetMinPublishingInterval(10)
                        .SetPublishingResolution(10)
                        .AddSecurityConfiguration(certificates, root)
                    : builder.AsClient().AddSecurityConfiguration(certificates, root);
                ApplicationConfiguration configuration = await security
                    .SetAutoAcceptUntrustedCertificates(false)
                    .SetAddAppCertToTrustedStore(false)
                    .AddCertificatePasswordProvider(Password)
                    .CreateAsync(cancellationToken).ConfigureAwait(false);
                configuration.TransportQuotas ??= new TransportQuotas();
                configuration.TransportQuotas.OperationTimeout = 10000;
                configuration.TransportQuotas.MaxMessageSize = 1024 * 1024;
                configuration.TransportQuotas.MaxBufferSize = 65536;
                if (!await application.CheckApplicationInstanceCertificatesAsync(true, ct: cancellationToken)
                    .ConfigureAwait(false))
                {
                    throw new InvalidOperationException("Manager-backed certificate provisioning failed.");
                }
                return application;
            }
            catch
            {
                await application.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Trusts only the other freshly provisioned application through an explicit manager transaction.
        /// </summary>
        private static async Task TrustAsync(
            ICertificateManager manager,
            TrustListIdentifier list,
            Certificate certificate,
            CancellationToken cancellationToken)
        {
            ITrustListTransaction transaction = await manager.BeginUpdateAsync(list, cancellationToken)
                .ConfigureAwait(false);
            await using var transactionLifetime = transaction.ConfigureAwait(false);
            await transaction.AddTrustedCertificateAsync(certificate, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>The exact listening application identity, also present in its certificate SAN.</summary>
        internal const string ServerApplicationUri = "urn:localhost:OPCFoundation:DataChannelSampleServer";
        /// <summary>The exact connecting application identity, also present in its certificate SAN.</summary>
        internal const string ClientApplicationUri = "urn:localhost:OPCFoundation:DataChannelSampleClient";
        /// <summary>The sample's own resource/model namespace, not a protocol registration.</summary>
        internal const string ModelNamespace = "urn:opcfoundation:samples:data-channel-streaming";
        private readonly InMemorySecretStore m_secrets = new();
        private readonly SecretIdentifier m_secretId =
            new("sample-application-keys", InMemorySecretStore.DefaultStoreType);
        private ApplicationInstance? m_server;
        private ApplicationInstance? m_client;
    }
}
