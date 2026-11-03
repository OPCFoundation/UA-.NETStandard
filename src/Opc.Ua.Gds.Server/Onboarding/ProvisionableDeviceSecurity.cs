/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace Opc.Ua.Gds.Server.Onboarding
{
    /// <summary>
    /// Public DCA certificate, issuer chain, and complete device trust configuration.
    /// The installer's existing certificate store retains the corresponding private key.
    /// </summary>
    public sealed record ProvisionableDeviceCredentials(
        ByteString Certificate,
        ArrayOf<ByteString> IssuerCertificates,
        TrustListDataType TrustList);

    /// <summary>
    /// The persisted device security transition, independent of discovery candidates and installed software.
    /// </summary>
    public enum ProvisionableDeviceSecurityStage
    {
        /// <summary>
        /// No registrar has installed DCA credentials.
        /// </summary>
        Unconfigured,

        /// <summary>
        /// One authenticated registrar has claimed installation; the same operation must be recovered.
        /// </summary>
        Installing,

        /// <summary>
        /// The installer completed and subsequent registrars must validate against installed trust.
        /// </summary>
        Installed,

        /// <summary>
        /// Security reset was requested and must finish before any new registrar is accepted.
        /// </summary>
        Resetting,

        /// <summary>
        /// The first authorized registrar owns configuration before complete credentials are available.
        /// </summary>
        Configuring
    }

    /// <summary>
    /// Device-specific certificate-store, endpoint, software-gate, and security-default operations.
    /// Implementations must be idempotent across interrupted and repeated invocations.
    /// </summary>
    public interface IProvisionableDeviceSecurityInstaller : IServerConfigurationResetProvider
    {
        /// <summary>
        /// Validates possession of the existing private key, installs credentials through certificate stores,
        /// and activates only DCA-certificate endpoints after the required software update has completed.
        /// The host keeps operational endpoints unavailable until recovery returns successfully.
        /// Repeated installation of the same credentials must succeed without generating a different key.
        /// ResetToServerDefaultsAsync restores device identity and security defaults, preserving installed software.
        /// </summary>
        ValueTask InstallAsync(
            ProvisionableDeviceCredentials credentials,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Retains factory security defaults before any network configuration handler can replace them.
    /// </summary>
    public interface IProvisionableDeviceSecurityDefaults
    {
        /// <summary>
        /// Retains the initial identity private key and trust configuration before listeners open.
        /// Repeated calls verify and reuse the original retained defaults without replacing them.
        /// </summary>
        ValueTask PreserveDefaultsAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Owns serialized device trust, installation recovery, and security reset.
    /// Implementations sharing storage must coordinate every writer, including pending registrar ownership.
    /// </summary>
    public interface IProvisionableDeviceSecurity : IServerConfigurationResetProvider
    {
        /// <summary>
        /// Returns the persisted transition without establishing that startup recovery has executed.
        /// </summary>
        ValueTask<ProvisionableDeviceSecurityStage> GetStageAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Requires actual authenticated channel evidence and applicable installed trust.
        /// An unconfigured device retains its first SecurityAdmin registrar before configuration proceeds.
        /// </summary>
        ValueTask RequireRegistrarAsync(ISystemContext context, CancellationToken cancellationToken = default);

        /// <summary>
        /// Authorizes and persists a registrar's installation before invoking idempotent platform effects.
        /// </summary>
        ValueTask InstallAsync(
            ISystemContext context,
            ProvisionableDeviceCredentials credentials,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Restores credentials or completes pending installation/reset before the host exposes operational endpoints.
        /// </summary>
        ValueTask RecoverAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Serializes device installation, startup recovery, and security reset using an explicit secret-store scope.
    /// One instance exclusively owns the scope. The secret store must publish complete values before returning;
    /// restart durability is a property of the selected store, not of ISecretStore itself.
    /// This adapter provides no multi-process or distributed writer coordination.
    /// </summary>
    public sealed class ProvisionableDeviceSecurity :
        IProvisionableDeviceSecurity,
        IPushConfigurationAuthorization,
        IDisposable
    {
        /// <summary>
        /// Borrows host-owned stores, validation, and platform-specific installation.
        /// The application URI identifies this DCA; the scope uniquely identifies its security state.
        /// </summary>
        public ProvisionableDeviceSecurity(
            ISecretStore secrets,
            string scope,
            string applicationUri,
            ICertificateManager certificates,
            IProvisionableDeviceSecurityInstaller installer,
            ITelemetryContext telemetry)
        {
            m_secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
            m_certificates = certificates ?? throw new ArgumentNullException(nameof(certificates));
            m_installer = installer ?? throw new ArgumentNullException(nameof(installer));
            if (string.IsNullOrWhiteSpace(scope))
            {
                throw new ArgumentException("An exclusive device security scope is required.", nameof(scope));
            }
            if (!Uri.TryCreate(applicationUri, UriKind.Absolute, out _))
            {
                throw new ArgumentException("An absolute DCA application URI is required.", nameof(applicationUri));
            }
            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            m_applicationUri = applicationUri;
            m_identifier = new SecretIdentifier(
                "DeviceSecurity-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(scope))),
                secrets.StoreType);
            m_context = ServiceMessageContext.Create(telemetry);
        }

        /// <summary>
        /// Reads the persisted transition without treating it as proof that startup recovery has executed.
        /// </summary>
        public async ValueTask<ProvisionableDeviceSecurityStage> GetStageAsync(
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return (await LoadAsync(cancellationToken).ConfigureAwait(false)).Stage;
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Retains the first SecurityAdmin registrar before configuration and checks subsequent calls against
        /// pending ownership or the installed trust list.
        /// Hosts invoke this gate on registrar operations; it does not replace SecureChannel certificate validation.
        /// </summary>
        public async ValueTask RequireRegistrarAsync(
            ISystemContext context,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ByteString peer = GetPeer(context);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Checkpoint checkpoint = await LoadAsync(cancellationToken).ConfigureAwait(false);
                await RequireRegistrarAsync(checkpoint, peer, cancellationToken).ConfigureAwait(false);
                if (checkpoint.Stage == ProvisionableDeviceSecurityStage.Unconfigured)
                {
                    AuthorizationHelper.HasAuthorization(context, [Role.SecurityAdmin]);
                    await SaveAsync(new(ProvisionableDeviceSecurityStage.Configuring, peer, null), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <inheritdoc/>
        ValueTask IPushConfigurationAuthorization.AuthorizeAsync(
            ISystemContext context,
            CancellationToken cancellationToken)
        {
            return RequireRegistrarAsync(context, cancellationToken);
        }

        /// <summary>
        /// Claims one authenticated registrar before invoking the installer. Failed installation remains pending;
        /// another registrar cannot replace it. Configured devices accept other registrars only through installed
        /// trust, as required by Part 21 section 7.3. SecurityAdmin authorization remains required.
        /// </summary>
        public async ValueTask InstallAsync(
            ISystemContext context,
            ProvisionableDeviceCredentials credentials,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ByteString peer = GetPeer(context);
            AuthorizationHelper.HasAuthorization(context, [Role.SecurityAdmin]);
            if (credentials == null)
            {
                throw new ArgumentNullException(nameof(credentials));
            }
            // Snapshot before the first await so caller-owned arrays and model objects cannot change the operation.
            byte[] proposed = Encode(new(ProvisionableDeviceSecurityStage.Installing, peer, credentials));
            Checkpoint pending = Decode(proposed);
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Checkpoint current = await LoadAsync(cancellationToken).ConfigureAwait(false);
                await RequireRegistrarAsync(current, peer, cancellationToken).ConfigureAwait(false);
                if (current.Stage == ProvisionableDeviceSecurityStage.Installing &&
                    !Encode(current).AsSpan().SequenceEqual(proposed))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "A different credential installation is already pending.");
                }
                await ValidateCredentialsAsync(pending.Credentials!, cancellationToken).ConfigureAwait(false);
                await m_secrets.SetAsync(m_identifier, proposed, cancellationToken).ConfigureAwait(false);
                await CompleteInstallationAsync(pending, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Restores installed credentials, resumes a claimed installation, or completes a previously requested reset.
        /// Call before advertising or accepting operational endpoints. Failure leaves the persisted transition intact.
        /// </summary>
        public async ValueTask RecoverAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Checkpoint checkpoint = await LoadAsync(cancellationToken).ConfigureAwait(false);
                switch (checkpoint.Stage)
                {
                    case ProvisionableDeviceSecurityStage.Installing:
                    case ProvisionableDeviceSecurityStage.Installed:
                        await ValidateCredentialsAsync(checkpoint.Credentials!, cancellationToken)
                            .ConfigureAwait(false);
                        await CompleteInstallationAsync(checkpoint, cancellationToken).ConfigureAwait(false);
                        break;
                    case ProvisionableDeviceSecurityStage.Resetting:
                        await CompleteResetAsync(cancellationToken).ConfigureAwait(false);
                        break;
                    case ProvisionableDeviceSecurityStage.Unconfigured:
                    case ProvisionableDeviceSecurityStage.Configuring:
                        if (m_installer is IProvisionableDeviceSecurityDefaults defaults)
                        {
                            await defaults.PreserveDefaultsAsync(cancellationToken).ConfigureAwait(false);
                        }
                        break;
                    default:
                        throw new InvalidDataException("The device security stage is invalid.");
                }
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Persists reset intent before invoking the host's security reset, without deleting installed software.
        /// The standard configuration node manager owns network authorization, auditing and shutdown.
        /// An application-controlled physical reset may call this same local entry point.
        /// </summary>
        public async ValueTask ResetToServerDefaultsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await LoadAsync(cancellationToken).ConfigureAwait(false);
                await SaveAsync(new(ProvisionableDeviceSecurityStage.Resetting, default, null), cancellationToken)
                    .ConfigureAwait(false);
                await CompleteResetAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Releases the local gate after the host has drained operations; borrowed providers are not disposed.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref m_disposed, 1) == 0)
            {
                m_gate.Dispose();
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Uses persisted public credentials only after certificate and key installation succeeds.
        /// </summary>
        private async ValueTask CompleteInstallationAsync(Checkpoint checkpoint, CancellationToken ct)
        {
            ProvisionableDeviceCredentials detached = Decode(Encode(checkpoint)).Credentials!;
            await m_installer.InstallAsync(detached, ct).ConfigureAwait(false);
            await SaveAsync(checkpoint with { Stage = ProvisionableDeviceSecurityStage.Installed }, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// A failed or interrupted platform reset remains marked Resetting until an explicit retry or startup recovery.
        /// </summary>
        private async ValueTask CompleteResetAsync(CancellationToken ct)
        {
            await m_installer.ResetToServerDefaultsAsync(ct).ConfigureAwait(false);
            await SaveAsync(new(ProvisionableDeviceSecurityStage.Unconfigured, default, null), ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Pins pending work to its actual channel peer; installed trust admits other validated registrar peers.
        /// </summary>
        private async ValueTask RequireRegistrarAsync(Checkpoint checkpoint, ByteString peer, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            switch (checkpoint.Stage)
            {
                case ProvisionableDeviceSecurityStage.Unconfigured:
                    return;
                case ProvisionableDeviceSecurityStage.Installing:
                case ProvisionableDeviceSecurityStage.Configuring:
                    if (checkpoint.Peer != peer)
                    {
                        throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                    }
                    return;
                case ProvisionableDeviceSecurityStage.Installed:
                    using (Certificate certificate = Certificate.FromRawData(peer))
                    using (var chain = new CertificateCollection { certificate })
                    {
                        CertificateValidationResult result = await m_certificates.ValidateAsync(
                            chain, TrustListIdentifier.Peers, Options(checkpoint.Credentials!.TrustList), ct)
                            .ConfigureAwait(false);
                        result.ThrowIfInvalid();
                    }
                    return;
                default:
                    throw new ServiceResultException(
                        StatusCodes.BadInvalidState, "Device security reset must complete before registrar access.");
            }
        }

        /// <summary>
        /// Rejects the wrong DCA URI, partial trust, and certificates that do not validate in the supplied trust scope.
        /// </summary>
        private async ValueTask ValidateCredentialsAsync(
            ProvisionableDeviceCredentials credentials, CancellationToken ct)
        {
            using Certificate certificate = Certificate.FromRawData(credentials.Certificate);
            if (!X509Utils.GetApplicationUrisFromCertificate(certificate).Contains(m_applicationUri))
            {
                throw new ServiceResultException(StatusCodes.BadCertificateUriInvalid);
            }
            using var chain = new CertificateCollection { certificate };
            foreach (ByteString encoded in credentials.IssuerCertificates)
            {
                using Certificate issuer = Certificate.FromRawData(encoded);
                chain.Add(issuer);
            }
            CertificateValidationResult result = await m_certificates.ValidateAsync(
                chain, TrustListIdentifier.Peers, Options(credentials.TrustList), ct).ConfigureAwait(false);
            result.ThrowIfInvalid();
        }

        /// <summary>
        /// Never inherits global auto-accept callbacks or unrelated trusted stores.
        /// </summary>
        private static ValidationOptions Options(TrustListDataType trust)
        {
            if (trust.SpecifiedLists != (uint)TrustListMasks.All)
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "A complete trust list is required.");
            }
            return new ValidationOptions
            {
                TrustScope = new CertificateValidationScope(trust.TrustedCertificates, trust.IssuerCertificates,
                    (trust.TrustedCrls.ToArray() ?? []).Concat(trust.IssuerCrls.ToArray() ?? []).ToArrayOf()),
                AutoAcceptUntrustedCertificates = false,
                AcceptError = static (_, _) => false,
                RecordRejectedCertificates = false,
                RejectUnknownRevocationStatus = true,
                RejectSHA1SignedCertificates = true,
                MinimumCertificateKeySize = 2048
            };
        }

        /// <summary>
        /// Reads server-owned channel evidence rather than accepting a caller-supplied certificate or fingerprint.
        /// </summary>
        private static ByteString GetPeer(ISystemContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            AuthorizationHelper.HasAuthenticatedSecureChannel(context, requireEncryption: true);
            AuthorizationHelper.GetClientCertificateFingerprint(context);
            if (context is not SessionSystemContext
                {
                    SessionId.IsNull: false,
                    UserIdentity: not null,
                    OperationContext: OperationContext { ChannelContext: { EndpointDescription: { } endpoint } channel }
                } ||
                string.IsNullOrEmpty(endpoint.SecurityPolicyUri) ||
                endpoint.SecurityPolicyUri == SecurityPolicies.None)
            {
                throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            }
            using Certificate peer = Certificate.FromRawData(channel.ClientChannelCertificate);
            return ByteString.From(peer.RawData.AsSpan());
        }

        /// <summary>
        /// Missing configuration alone denotes Unconfigured; malformed state never enables first-registrar trust.
        /// </summary>
        private async ValueTask<Checkpoint> LoadAsync(CancellationToken ct)
        {
            using ISecret? secret = await m_secrets.GetAsync(m_identifier, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return secret == null
                ? new(ProvisionableDeviceSecurityStage.Unconfigured, default, null)
                : Decode(secret.Bytes);
        }

        /// <summary>
        /// Publishes one complete checkpoint through the explicitly selected host store.
        /// </summary>
        private ValueTask SaveAsync(Checkpoint checkpoint, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return m_secrets.SetAsync(m_identifier, Encode(checkpoint), ct);
        }

        /// <summary>
        /// Uses the standard binary codec and includes the DCA URI to detect reuse by a different application.
        /// </summary>
        private byte[] Encode(Checkpoint checkpoint)
        {
            using var buffer = new MemoryStream();
            using (var encoder = new BinaryEncoder(buffer, m_context, leaveOpen: true))
            {
                encoder.WriteUInt32(null, 2);
                encoder.WriteString(null, m_applicationUri);
                encoder.WriteInt32(null, (int)checkpoint.Stage);
                if (checkpoint.Stage == ProvisionableDeviceSecurityStage.Configuring)
                {
                    if (checkpoint.Peer.IsEmpty)
                    {
                        throw new InvalidDataException("Device configuration ownership is missing.");
                    }
                    encoder.WriteByteString(null, checkpoint.Peer);
                }
                if (checkpoint.Stage is ProvisionableDeviceSecurityStage.Installing or
                    ProvisionableDeviceSecurityStage.Installed)
                {
                    ProvisionableDeviceCredentials credentials = checkpoint.Credentials ??
                        throw new InvalidDataException("Device credentials are missing.");
                    if (checkpoint.Peer.IsEmpty || credentials.Certificate.IsEmpty || credentials.TrustList == null)
                    {
                        throw new InvalidDataException("Device installation evidence is incomplete.");
                    }
                    encoder.WriteByteString(null, checkpoint.Peer);
                    encoder.WriteByteString(null, credentials.Certificate);
                    encoder.WriteByteStringArray(null, credentials.IssuerCertificates);
                    encoder.WriteEncodeable(null, credentials.TrustList);
                }
            }
            if (buffer.Length > kMaximumCheckpointBytes)
            {
                throw new InvalidDataException("The device security checkpoint exceeds its size limit.");
            }
            return buffer.ToArray();
        }

        /// <summary>
        /// Rejects unknown versions, stages, applications, incomplete credentials, and trailing data.
        /// </summary>
        private Checkpoint Decode(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length > kMaximumCheckpointBytes)
            {
                throw new InvalidDataException("The device security checkpoint exceeds its size limit.");
            }
            try
            {
                using var buffer = new MemoryStream(bytes.ToArray(), writable: false);
                using var decoder = new BinaryDecoder(buffer, m_context);
                uint version = decoder.ReadUInt32(null);
                if (version is not (1 or 2) || decoder.ReadString(null) != m_applicationUri)
                {
                    throw new InvalidDataException("The device security checkpoint version or application is invalid.");
                }
                int stageCode = decoder.ReadInt32(null);
                if (stageCode < 0 || stageCode > (int)ProvisionableDeviceSecurityStage.Configuring ||
                    (version == 1 && stageCode == (int)ProvisionableDeviceSecurityStage.Configuring))
                {
                    throw new InvalidDataException("The device security checkpoint stage is invalid.");
                }
                var stage = (ProvisionableDeviceSecurityStage)stageCode;
                ByteString peer = default;
                ProvisionableDeviceCredentials? credentials = null;
                if (stage == ProvisionableDeviceSecurityStage.Configuring)
                {
                    peer = decoder.ReadByteString(null);
                    if (peer.IsEmpty)
                    {
                        throw new InvalidDataException("The device security checkpoint lacks its registrar owner.");
                    }
                }
                if (stage is ProvisionableDeviceSecurityStage.Installing or ProvisionableDeviceSecurityStage.Installed)
                {
                    peer = decoder.ReadByteString(null);
                    ByteString certificate = decoder.ReadByteString(null);
                    ArrayOf<ByteString> issuers = decoder.ReadByteStringArray(null);
                    TrustListDataType trust = decoder.ReadEncodeable<TrustListDataType>(null);
                    if (peer.IsEmpty || certificate.IsEmpty || trust == null)
                    {
                        throw new InvalidDataException("The device security checkpoint lacks installation evidence.");
                    }
                    credentials = new(certificate, issuers, trust);
                }
                if (decoder.Position != buffer.Length)
                {
                    throw new InvalidDataException("The device security checkpoint contains trailing content.");
                }
                return new(stage, peer, credentials);
            }
            catch (Exception error) when (error is ServiceResultException or EndOfStreamException or ArgumentException)
            {
                throw new InvalidDataException("The device security checkpoint is invalid.", error);
            }
        }

        /// <summary>
        /// Rejects operations after host disposal.
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref m_disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(ProvisionableDeviceSecurity));
            }
        }

        /// <summary>The private serialized state, never exposed as a mutable recovery contract.</summary>
        private sealed record Checkpoint(
            ProvisionableDeviceSecurityStage Stage,
            ByteString Peer,
            ProvisionableDeviceCredentials? Credentials);

        /// <summary>The checkpoint byte limit in addition to standard codec quotas.</summary>
        private const int kMaximumCheckpointBytes = 16 * 1024 * 1024;

        /// <summary>The host-owned storage.</summary>
        private readonly ISecretStore m_secrets;

        /// <summary>The unique device state key.</summary>
        private readonly SecretIdentifier m_identifier;

        /// <summary>The DCA application URI retained in every checkpoint.</summary>
        private readonly string m_applicationUri;

        /// <summary>The host's scoped certificate validation infrastructure.</summary>
        private readonly ICertificateManager m_certificates;

        /// <summary>The host's idempotent platform operations.</summary>
        private readonly IProvisionableDeviceSecurityInstaller m_installer;

        /// <summary>The private codec context retaining standard quotas.</summary>
        private readonly ServiceMessageContext m_context;

        /// <summary>The local serialization gate, not a distributed transaction.</summary>
        private readonly SemaphoreSlim m_gate = new(1, 1);

        /// <summary>Whether this instance has been disposed.</summary>
        private int m_disposed;
    }
}
