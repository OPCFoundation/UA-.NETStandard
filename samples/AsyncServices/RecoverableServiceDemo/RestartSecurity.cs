/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * Licensed under the OPC Foundation MIT License 1.00.
 * See LICENSE.txt in the project root for license information.
 * ======================================================================*/

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Redundancy;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using ClientSession = Opc.Ua.Client.ISession;

namespace RecoverableServiceDemo
{
    /// <summary>
    /// Run-lifetime secret storage. Losing this supervisor is deliberately NOT a supported TEST restart.
    /// No recovery key or private-key password is saved in an argument, source file, checkpoint or log.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class RestartSecrets : IAsyncDisposable
    {
        private RestartSecrets()
        {
            m_registry = new SecretRegistry(m_store);
            Passwords = new CertificatePasswordProvider(m_registry, s_passwordId);
        }

        internal CertificatePasswordProvider Passwords { get; }

        internal static async Task<RestartSecrets> CreateAsync(CancellationToken ct)
        {
            var secrets = new RestartSecrets();
            byte[] key = RandomNumberGenerator.GetBytes(32);
            byte[] passwordEntropy = RandomNumberGenerator.GetBytes(32);
            char[] passwordChars = new char[44];
            byte[] password = new byte[44];
            try
            {
                RestartPipe.Require(Convert.TryToBase64Chars(passwordEntropy, passwordChars, out int characters),
                    "Cannot generate a private-key password.");
                int bytes = Encoding.UTF8.GetBytes(passwordChars.AsSpan(0, characters), password);
                await secrets.m_store.SetAsync(s_keyId, key, ct).ConfigureAwait(false);
                await secrets.m_store.SetAsync(s_passwordId, password.AsMemory(0, bytes), ct).ConfigureAwait(false);
                return secrets;
            }
            catch
            {
                await secrets.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(passwordEntropy);
                CryptographicOperations.ZeroMemory(password);
                Array.Clear(passwordChars);
            }
        }

        internal static async Task<RestartSecrets> ReadAsync(BinaryReader reader, CancellationToken ct)
        {
            byte[] key = RestartPipe.ReadBytes(reader, 32);
            byte[] password = RestartPipe.ReadBytes(reader, 128);
            var secrets = new RestartSecrets();
            try
            {
                RestartPipe.Require(key.Length == 32 && password.Length > 0, "Incomplete TEST secret bootstrap.");
                await secrets.m_store.SetAsync(s_keyId, key, ct).ConfigureAwait(false);
                await secrets.m_store.SetAsync(s_passwordId, password, ct).ConfigureAwait(false);
                return secrets;
            }
            catch
            {
                await secrets.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(password);
            }
        }

        internal void Write(BinaryWriter writer)
        {
            using ISecret key = m_registry.TryGet(s_keyId) ??
                throw new InvalidOperationException("The TEST protection authority was lost.");
            using ISecret password = m_registry.TryGet(s_passwordId) ??
                throw new InvalidOperationException("The TEST certificate authority was lost.");
            RestartPipe.WriteBytes(writer, key.Bytes);
            RestartPipe.WriteBytes(writer, password.Bytes);
        }

        internal AesCbcHmacRecordProtector CreateProtector()
        {
            using ISecret key = m_registry.TryGet(s_keyId) ??
                throw new InvalidOperationException("The TEST protection authority was lost.");
            return new AesCbcHmacRecordProtector(key.Bytes);
        }

        public async ValueTask DisposeAsync()
        {
            _ = await m_store.RemoveAsync(s_keyId).ConfigureAwait(false);
            _ = await m_store.RemoveAsync(s_passwordId).ConfigureAwait(false);
        }

        private readonly InMemorySecretStore m_store = new();
        private readonly SecretRegistry m_registry;

        private static readonly SecretIdentifier s_keyId =
            new("restart-test/records", InMemorySecretStore.DefaultStoreType);

        private static readonly SecretIdentifier s_passwordId =
            new("restart-test/pfx", InMemorySecretStore.DefaultStoreType);
    }

    /// <summary>Non-secret, run-owned configuration carried over verified local IPC, not a new RSE DTO.</summary>
    [SupportedOSPlatform("windows")]
    internal sealed class RestartConfiguration
    {
        internal required string Root { get; init; }
        internal required string EndpointUrl { get; init; }
        internal required string ServerUri { get; init; }
        internal required RecoverableExecutionOwnerId Owner { get; init; }
        internal required ByteString ServerCertificateHash { get; init; }
        internal required ByteString ClientCertificateHash { get; init; }
        internal required ByteString UserCertificateHash { get; init; }
        internal required string UserIssuer { get; init; }
        internal required string UserSubject { get; init; }
        internal required Uuid TimeDomain { get; init; }

        internal void Write(BinaryWriter writer)
        {
            writer.Write(Root);
            writer.Write(EndpointUrl);
            writer.Write(ServerUri);
            WriteOwner(writer, Owner);
            RestartPipe.WriteBytes(writer, ServerCertificateHash.Span);
            RestartPipe.WriteBytes(writer, ClientCertificateHash.Span);
            RestartPipe.WriteBytes(writer, UserCertificateHash.Span);
            writer.Write(UserIssuer);
            writer.Write(UserSubject);
            RestartPipe.WriteId(writer, TimeDomain);
        }

        internal static RestartConfiguration Read(BinaryReader reader)
        {
            return new RestartConfiguration
            {
                Root = reader.ReadString(),
                EndpointUrl = reader.ReadString(),
                ServerUri = reader.ReadString(),
                Owner = ReadOwner(reader),
                ServerCertificateHash = new ByteString(RestartPipe.ReadBytes(reader, 32)),
                ClientCertificateHash = new ByteString(RestartPipe.ReadBytes(reader, 32)),
                UserCertificateHash = new ByteString(RestartPipe.ReadBytes(reader, 32)),
                UserIssuer = reader.ReadString(),
                UserSubject = reader.ReadString(),
                TimeDomain = RestartPipe.ReadId(reader)
            };
        }

        internal static void WriteOwner(BinaryWriter writer, RecoverableExecutionOwnerId owner)
        {
            RestartPipe.WriteId(writer, owner.AuthorityId);
            RestartPipe.WriteId(writer, owner.ApplicationId);
            RestartPipe.WriteId(writer, owner.UserId);
        }

        internal static RecoverableExecutionOwnerId ReadOwner(BinaryReader reader)
        {
            return new RecoverableExecutionOwnerId(
                RestartPipe.ReadId(reader), RestartPipe.ReadId(reader), RestartPipe.ReadId(reader));
        }

        internal const int MessageLimit = 131_072;

        /// <summary>
        /// A journal admission reserves 4 * MessageLimit + 12296 = 536584 bytes.
        /// These are deliberate generic-backend bounds, not unlimited RSE quotas.
        /// </summary>
        internal const int JournalCapacity = 4 * 1024 * 1024;
        internal const int CheckpointCapacity = 2 * 1024 * 1024;
        internal const double ExecutionMilliseconds = 30_000;
        internal const double RetentionMilliseconds = 30_000;
    }

    [SupportedOSPlatform("windows")]
    internal static class RestartSecurity
    {
        internal static async Task<ApplicationInstance> CreateApplicationAsync(
            string name, string uri, string root, RestartSecrets secrets, ITelemetryContext telemetry,
            string? endpoint, CancellationToken ct)
        {
            var application = new ApplicationInstance(telemetry)
            {
                ApplicationName = name,
                ApplicationType = endpoint == null ? ApplicationType.Client : ApplicationType.Server,
                CertificatePasswordProvider = secrets.Passwords
            };
            try
            {
                ArrayOf<CertificateIdentifier> certificates =
                    ApplicationConfigurationBuilder.CreateDefaultApplicationCertificates(
                        $"CN={name}, O=OPC Foundation, DC=localhost", CertificateStoreType.Directory, root);
                if (endpoint == null)
                {
                    _ = await application.Build(uri, "urn:OPCFoundation:RseRestartTest")
                        .SetMaxMessageSize(RestartConfiguration.MessageLimit)
                        .SetOperationTimeout(5000)
                        .AsClient()
                        .AddSecurityConfiguration(certificates, root)
                        .AddCertificatePasswordProvider(secrets.Passwords)
                        .SetAutoAcceptUntrustedCertificates(false)
                        .CreateAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    _ = await application.Build(uri, "urn:OPCFoundation:RseRestartTest")
                        .SetMaxMessageSize(RestartConfiguration.MessageLimit)
                        .SetOperationTimeout(5000)
                        .AsServer([endpoint])
                        .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256)
                        .AddUserTokenPolicy(UserTokenType.Certificate)
                        .AddSecurityConfiguration(certificates, root)
                        .AddCertificatePasswordProvider(secrets.Passwords)
                        .SetAutoAcceptUntrustedCertificates(false)
                        .CreateAsync(ct).ConfigureAwait(false);
                }
                RestartPipe.Require(await application.CheckApplicationInstanceCertificatesAsync(
                    true, CertificateFactory.DefaultLifeTime, ct: ct).ConfigureAwait(false),
                    "The run-owned runtime certificate could not be loaded/generated.");
                return application;
            }
            catch
            {
                await application.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        internal static CertificateEntry AcquireCertificate(ApplicationInstance application)
        {
            return application.ApplicationConfiguration!.CertificateManager!
                .AcquireApplicationCertificateBySecurityPolicy(SecurityPolicies.Basic256Sha256) ??
                throw new InvalidOperationException("The runtime RSA application certificate is missing.");
        }

        internal static async Task TrustAsync(
            ApplicationInstance application, TrustListIdentifier list, Certificate certificate, CancellationToken ct)
        {
            ITrustListTransaction update = await application.ApplicationConfiguration!.CertificateManager!
                .BeginUpdateAsync(list, ct).ConfigureAwait(false);
            await using ConfiguredAsyncDisposable updateLifetime = update.ConfigureAwait(false);
            // Trust stores receive public DER only; never copy a private key into an unpassworded trust entry.
            using var publicCertificate = Certificate.FromRawData(new ByteString(certificate.RawData));
            RestartPipe.Require(!publicCertificate.HasPrivateKey, "A trust entry unexpectedly contains a private key.");
            await update.AddTrustedCertificateAsync(publicCertificate, ct).ConfigureAwait(false);
            await update.CommitAsync(ct).ConfigureAwait(false);
        }

        internal static ByteString Hash(ReadOnlySpan<byte> certificate)
        {
            return new ByteString(SHA256.HashData(certificate));
        }
    }

    /// <summary>
    /// Pins the actual activated channel credentials and X.509 user proof to explicit run-local identities.
    /// It never treats a display name, advertised URI or saved operation token as owner authentication.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed class RestartClientIdentity(RestartConfiguration configuration)
        : IRecoverableExecutionIdentityProvider
    {
        public ValueTask<RecoverableExecutionIdentity> VerifyAsync(
            ClientSession session, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            EndpointDescription endpoint = session.ConfiguredEndpoint.Description;
            if (!session.Connected ||
                endpoint.SecurityMode != MessageSecurityMode.SignAndEncrypt ||
                endpoint.SecurityPolicyUri != SecurityPolicies.Basic256Sha256 ||
                RestartSecurity.Hash(session.TransportChannel.ServerChannelCertificate) !=
                    configuration.ServerCertificateHash ||
                RestartSecurity.Hash(session.TransportChannel.ClientChannelCertificate) !=
                    configuration.ClientCertificateHash ||
                session.Identity.TokenType != UserTokenType.Certificate ||
                session.Identity.TokenHandler.Token is not X509IdentityToken user ||
                RestartSecurity.Hash(user.CertificateData.Span) != configuration.UserCertificateHash)
            {
                throw new ServiceResultException(StatusCodes.BadIdentityTokenRejected);
            }
            return new ValueTask<RecoverableExecutionIdentity>(
                new RecoverableExecutionIdentity(
                    configuration.ServerCertificateHash,
                    configuration.Owner.ApplicationId.ToByteString(),
                    configuration.Owner.UserId.ToByteString()));
        }
    }
}
