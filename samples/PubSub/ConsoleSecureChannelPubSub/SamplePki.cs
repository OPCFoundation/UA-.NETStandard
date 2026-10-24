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
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;

namespace Quickstarts.ConsoleSecureChannelPubSub
{
    /// <summary>
    /// Owns only the sample's explicitly selected PKI and secret-store entry.
    /// All key creation and loading use ApplicationInstance and CertificateManager.
    /// </summary>
    internal sealed class SamplePki : IAsyncDisposable
    {
        /// <summary>
        /// Creates a secret-store-backed password provider.
        /// </summary>
        private SamplePki(string root, bool deleteOnDispose)
        {
            Root = root;
            m_deleteOnDispose = deleteOnDispose;
            Password = new CertificatePasswordProvider(new SecretRegistry(m_secrets), m_secretId);
        }

        /// <summary>
        /// Gets the explicitly owned PKI root.
        /// </summary>
        internal string Root { get; }

        /// <summary>
        /// Gets the injected private-key password provider.
        /// </summary>
        internal ICertificatePasswordProvider Password { get; }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            await m_secrets.RemoveAsync(m_secretId).ConfigureAwait(false);
            if (m_deleteOnDispose && m_created && Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        /// <summary>
        /// Loads an operator-provided secret, or creates a process-local random demo secret.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal static async Task<SamplePki> CreateAsync(SampleOptions options, CancellationToken cancellationToken)
        {
            bool demo = options.Mode == "demo";
            string root = options.Pki ??
                Path.GetFullPath(
                    Path.Combine("secure-channel-pubsub-state", Guid.NewGuid().ToString("N")));
            var pki = new SamplePki(root, demo);
            byte[] password;
            if (demo)
            {
                password = Encoding.UTF8.GetBytes(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            }
            else
            {
                string supplied = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable) ??
                    throw new InvalidOperationException(
                        $"Set {PasswordEnvironmentVariable} to a private-key password before setup/server/client.");
                if (supplied.Length < 16)
                {
                    throw new InvalidOperationException($"{PasswordEnvironmentVariable} needs at least 16 characters.");
                }
                password = Encoding.UTF8.GetBytes(supplied);
            }
            try
            {
                await pki.m_secrets.SetAsync(pki.m_secretId, password, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(password);
            }
            return pki;
        }

        /// <summary>
        /// Provisions two owned identities and trusts exactly the other application's public certificate.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal async Task ProvisionAsync(ITelemetryContext telemetry, CancellationToken cancellationToken)
        {
            if (Directory.Exists(Root) || File.Exists(Root))
            {
                throw new InvalidOperationException(
                    "Setup requires a new --pki directory; existing trust is not replaced.");
            }
            Directory.CreateDirectory(Root);
            m_created = true;
            ApplicationInstance server = await OpenApplicationAsync(
                true, ["opc.tcp://localhost:4840"], telemetry, true, cancellationToken).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable serverLifetime = server.ConfigureAwait(false);
            ApplicationInstance client = await OpenApplicationAsync(
                false, [], telemetry, true, cancellationToken).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable clientLifetime = client.ConfigureAwait(false);
            ICertificateManager serverManager = Manager(server);
            ICertificateManager clientManager = Manager(client);
            using CertificateEntry serverEntry = OwnCertificate(serverManager);
            using CertificateEntry clientEntry = OwnCertificate(clientManager);
            using var serverPublic = new Certificate(serverEntry.Certificate.RawData);
            using var clientPublic = new Certificate(clientEntry.Certificate.RawData);
            await TrustAsync(serverManager, TrustListIdentifier.Peers, clientPublic, cancellationToken)
                .ConfigureAwait(false);
            await TrustAsync(clientManager, TrustListIdentifier.Peers, serverPublic, cancellationToken)
                .ConfigureAwait(false);
            await TrustAsync(serverManager, TrustListIdentifier.Users, clientPublic, cancellationToken)
                .ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(Root, MarkerName), Marker, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Builds a current application configuration with strict certificate and user trust.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal async Task<ApplicationInstance> OpenApplicationAsync(
            bool server,
            string[] endpoints,
            ITelemetryContext telemetry,
            bool provision,
            CancellationToken cancellationToken)
        {
            string markerPath = Path.Combine(Root, MarkerName);
            if (!provision &&
                (!File.Exists(markerPath) ||
                    await File.ReadAllTextAsync(markerPath, cancellationToken).ConfigureAwait(false) != Marker))
            {
                throw new InvalidOperationException("The selected PKI is not initialized. Run setup --pki first.");
            }
            string name = server ? "SecureChannelPubSubServer" : "SecureChannelPubSubClient";
            string applicationUri = server
                ? SampleConfiguration.ServerApplicationUri
                : SampleConfiguration.ClientApplicationUri;
            string pkiRoot = Path.Combine(Root, server ? "server" : "client");
            var application = new ApplicationInstance(telemetry)
            {
                ApplicationName = name,
                ApplicationType = server ? ApplicationType.Server : ApplicationType.Client,
                DisableCertificateAutoCreation = !provision,
                CertificatePasswordProvider = Password
            };
            try
            {
                ArrayOf<CertificateIdentifier> certificates =
                [
                    new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "own"),
                        SubjectName = $"CN={name}, O=OPC Foundation, DC=localhost",
                        CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                    }
                ];
                IApplicationConfigurationBuilderTypes builder = application.Build(
                    applicationUri, SampleConfiguration.ModelNamespace);
                IApplicationConfigurationBuilderSecurityOptions security = server
                    ? builder.AsServer(endpoints)
                        .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256)
                        .AddUserTokenPolicy(new UserTokenPolicy
                        {
                            PolicyId = "certificate",
                            TokenType = UserTokenType.Certificate,
                            SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                        })
                        .AddSecurityConfiguration(certificates, pkiRoot)
                    : builder.AsClient().AddSecurityConfiguration(certificates, pkiRoot);
                ApplicationConfiguration configuration = await security
                    .SetAutoAcceptUntrustedCertificates(false)
                    .SetAddAppCertToTrustedStore(false)
                    .AddCertificatePasswordProvider(Password)
                    .CreateAsync(cancellationToken).ConfigureAwait(false);
                TransportQuotas quotas = configuration.TransportQuotas
                    ?? throw new InvalidOperationException("The application configuration has no transport quotas.");
                quotas.OperationTimeout = 15000;
                quotas.MaxMessageSize = 1024 * 1024;
                quotas.MaxBufferSize = 65535;
                bool valid = await application.CheckApplicationInstanceCertificatesAsync(
                    true, ct: cancellationToken).ConfigureAwait(false);
                if (!valid)
                {
                    throw new InvalidOperationException(
                        "Application certificate validation failed. Check the PKI path and private-key password.");
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
        /// Loads the sole pinned public peer certificate from the manager's trusted store.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal static async Task<Certificate> PeerCertificateAsync(
            ICertificateManager manager,
            string expectedApplicationUri,
            CancellationToken cancellationToken)
        {
            using ICertificateStore store = manager.OpenTrustedStore(TrustListIdentifier.Peers);
            using CertificateCollection certificates = await store.EnumerateAsync(cancellationToken)
                .ConfigureAwait(false);
            if (certificates.Count != 1 ||
                !X509Utils.GetApplicationUrisFromCertificate(certificates[0])
                    .Contains(expectedApplicationUri, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "This sample requires exactly its configured peer certificate and application URI in Peers.");
            }
            return certificates[0].AddRef();
        }

        /// <summary>
        /// Obtains the initialized manager without a private-key loading fallback.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal static ICertificateManager Manager(ApplicationInstance application)
        {
            return application.ApplicationConfiguration?.CertificateManager ??
                throw new InvalidOperationException("ApplicationInstance did not initialize its certificate manager.");
        }

        /// <summary>
        /// Acquires the current managed application identity.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal static CertificateEntry OwnCertificate(ICertificateManager manager)
        {
            return manager.AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256) ??
                throw new InvalidOperationException("The configured RSA/SHA-256 application identity is unavailable.");
        }

        /// <summary>
        /// Changes trust through an explicit manager transaction, never an acceptance callback.
        /// </summary>
        private static async Task TrustAsync(
            ICertificateManager manager,
            TrustListIdentifier trustList,
            Certificate certificate,
            CancellationToken cancellationToken)
        {
            ITrustListTransaction transaction = await manager.BeginUpdateAsync(trustList, cancellationToken)
                .ConfigureAwait(false);
            await using ConfiguredAsyncDisposable transactionLifetime = transaction.ConfigureAwait(false);
            await transaction.AddTrustedCertificateAsync(certificate, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>The environment-to-secret-store input used only for persistent process-mode PKI.</summary>
        internal const string PasswordEnvironmentVariable = "OPCUA_SAMPLE_KEY_PASSWORD";
        private const string MarkerName = "secure-channel-pubsub.identity";
        private const string Marker = "OPC Foundation secure-channel PubSub sample PKI v1";
        private readonly InMemorySecretStore m_secrets = new();
        private readonly SecretIdentifier m_secretId = new("application-keys", InMemorySecretStore.DefaultStoreType);
        private readonly bool m_deleteOnDispose;
        private bool m_created;
    }
}
