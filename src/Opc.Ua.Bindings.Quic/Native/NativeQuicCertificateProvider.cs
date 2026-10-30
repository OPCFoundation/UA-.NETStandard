// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.IO;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Native.QuicGo;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Bindings.Native
{
    /// <summary>
    /// Explicit certificate-manager selection for native TLS. No identity generation or system-trust fallback.
    /// </summary>
    public sealed class NativeQuicCertificateOptions
    {
        /// <summary>
        /// Selects a loaded application certificate, a configured trusted store and an exact peer allowlist.
        /// Registry loading uses the manager's existing store and secret/password providers.
        /// </summary>
        public NativeQuicCertificateOptions(
            string applicationUri,
            NodeId certificateType,
            ArrayOf<NativeQuicPeer> allowedPeers,
            TrustListIdentifier? trustList = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(applicationUri);
            if (!Uri.TryCreate(applicationUri, UriKind.Absolute, out _) || certificateType.IsNull ||
                allowedPeers.Count is < 1 or > 32)
            {
                throw new ArgumentException("An explicit application identity and peer policy are required.");
            }
            foreach (NativeQuicPeer peer in allowedPeers)
            {
                ArgumentNullException.ThrowIfNull(peer);
            }
            ApplicationUri = applicationUri;
            CertificateType = certificateType;
            AllowedPeers = new ArrayOf<NativeQuicPeer>(allowedPeers.Span.ToArray().AsMemory());
            TrustList = trustList ?? TrustListIdentifier.Peers;
        }

        /// <summary>
        /// Gets the exact local URI SAN required in the selected certificate.
        /// </summary>
        public string ApplicationUri { get; }

        /// <summary>
        /// Gets the explicit registry certificate type.
        /// </summary>
        public NodeId CertificateType { get; }

        /// <summary>
        /// Gets the selected manager trust store; issuer stores never become implicit trust anchors.
        /// </summary>
        public TrustListIdentifier TrustList { get; }

        /// <summary>
        /// Gets the exact permitted peer application/key combinations.
        /// </summary>
        public ArrayOf<NativeQuicPeer> AllowedPeers { get; }
    }

    /// <summary>
    /// Bridges the existing certificate registry, configured stores, revocation policy and lifecycle to native TLS.
    /// The native signer supports only exportable software RSA2048..4096 and ECDSA P256/P384/P521 keys.
    /// </summary>
    public sealed class NativeQuicCertificateProvider : INativeQuicCredentialProvider, IDisposable
    {
        /// <summary>
        /// Creates a provider without loading/generating an identity or modifying trust.
        /// </summary>
        public NativeQuicCertificateProvider(
            ICertificateManager certificateManager,
            NativeQuicCertificateOptions options,
            TimeProvider? timeProvider = null)
        {
            m_manager = certificateManager ?? throw new ArgumentNullException(nameof(certificateManager));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_clock = timeProvider ?? TimeProvider.System;
            m_changes = m_manager.CertificateChanges.Subscribe(new ChangeObserver(this));
        }

        /// <inheritdoc/>
        public async ValueTask<NativeQuicCredentialLease> AcquireAsync(CancellationToken cancellationToken = default)
        {
            CancellationToken invalidated;
            lock (m_gate)
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                invalidated = m_generation.Token;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, invalidated);
            using CertificateEntry entry = m_manager.AcquireApplicationCertificateByType(m_options.CertificateType) ??
                throw new AuthenticationException("The selected application certificate is not loaded.");
            CheckLocal(entry.Certificate);
            DateTimeOffset expiresAt = new(entry.NotAfter.ToUniversalTime());
            foreach (Certificate issuer in entry.IssuerChain)
            {
                if (issuer.NotAfter.ToUniversalTime() < expiresAt.UtcDateTime)
                {
                    expiresAt = new DateTimeOffset(issuer.NotAfter.ToUniversalTime());
                }
            }
            using ICertificateStore trustedStore = m_manager.OpenTrustedStore(m_options.TrustList);
            using CertificateCollection trusted = await trustedStore.EnumerateAsync(linked.Token).ConfigureAwait(false);
            ByteString roots = EncodeBundle(trusted, true);
            ByteString issuers = EncodeBundle(entry.IssuerChain, false);
            ByteString spki = GetPublicKeySha256(entry.Certificate);
            byte[] exported = ExportPrivateKey(entry.Certificate);
            NativeQuicPrivateKey? key = null;
            try
            {
                key = new NativeQuicPrivateKey(exported);
                var credentials = new NativeQuicCredentials(
                    entry.Certificate.RawData.ToByteString(), issuers, key, roots, m_options.ApplicationUri);
                linked.Token.ThrowIfCancellationRequested();
                var lease = new NativeQuicCredentialLease(
                    credentials, key, m_options.AllowedPeers, spki, expiresAt, invalidated);
                key = null;
                return lease;
            }
            finally
            {
                key?.Dispose();
                CryptographicOperations.ZeroMemory(exported);
            }
        }

        /// <inheritdoc/>
        public async ValueTask ValidatePeerAsync(
            NativeQuicCredentialLease credentials,
            NativeQuicPeerEvidence peer,
            ArrayOf<ByteString> verifiedChain,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(credentials);
            ArgumentNullException.ThrowIfNull(peer);
            credentials.Invalidated.ThrowIfCancellationRequested();
            if (verifiedChain.Count != peer.VerifiedCertificateCount || verifiedChain.Count is < 1 or > 16)
            {
                throw new AuthenticationException("The native verified certificate chain is incomplete.");
            }
            bool allowed = false;
            foreach (NativeQuicPeer rule in credentials.AllowedPeers)
            {
                allowed |= rule.ApplicationUri == peer.ApplicationUri &&
                    CryptographicOperations.FixedTimeEquals(rule.SpkiSha256.Span, peer.SpkiSha256.Span);
            }
            if (!allowed)
            {
                throw new AuthenticationException("The peer application/key is not authorized.");
            }
            using var chain = new CertificateCollection();
            foreach (ByteString der in verifiedChain)
            {
                using Certificate certificate = Certificate.FromRawData(der.Span.ToArray());
                chain.Add(certificate);
            }
            using Certificate leaf = chain[0].AddRef();
            if (!CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(verifiedChain[0].Span), peer.CertificateSha256.Span) ||
                !CryptographicOperations.FixedTimeEquals(GetPublicKeySha256(leaf).Span, peer.SpkiSha256.Span) ||
                !HasApplicationUri(leaf, peer.ApplicationUri))
            {
                throw new AuthenticationException("Native certificate evidence does not match its verified identity.");
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, credentials.Invalidated);
            CertificateValidationResult validation = await m_manager.ValidateAsync(
                chain,
                m_options.TrustList,
                new Security.Certificates.CertificateValidationOptions
                {
                    AutoAcceptUntrustedCertificates = false,
                    RejectSHA1SignedCertificates = true,
                    AcceptError = static (_, _) => false,
                    RecordRejectedCertificates = false
                },
                linked.Token).ConfigureAwait(false);
            validation.ThrowIfInvalid();
            using CertificateEntry current = m_manager.AcquireApplicationCertificateByType(m_options.CertificateType) ??
                throw new AuthenticationException("The selected application identity was removed.");
            CheckLocal(current.Certificate);
            if (!current.Certificate.RawData.AsSpan().SequenceEqual(credentials.Credentials.CertificateDer.Span) ||
                credentials.ExpiresAt <= m_clock.GetUtcNow())
            {
                throw new AuthenticationException("The native local credential snapshot is no longer current.");
            }
            linked.Token.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Computes the authorized SHA256 SPKI identity without exporting a private key.
        /// </summary>
        public static ByteString GetPublicKeySha256(Certificate certificate)
        {
            ArgumentNullException.ThrowIfNull(certificate);
            using RSA? rsa = certificate.GetRSAPublicKey();
            if (rsa != null)
            {
                return new ByteString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));
            }
            using ECDsa? ecdsa = certificate.GetECDsaPublicKey();
            if (ecdsa != null)
            {
                return new ByteString(SHA256.HashData(ecdsa.ExportSubjectPublicKeyInfo()));
            }
            throw new NotSupportedException("Native QUIC supports only RSA and named-curve ECDSA application keys.");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            CancellationTokenSource generation;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                generation = m_generation;
            }
            m_changes.Dispose();
            generation.Cancel();
            generation.Dispose();
        }

        /// <summary>
        /// Validates the configured identity without treating a HELLO assertion as certificate evidence.
        /// </summary>
        private void CheckLocal(Certificate certificate)
        {
            DateTime now = m_clock.GetUtcNow().UtcDateTime;
            if (!certificate.HasPrivateKey || !HasApplicationUri(certificate, m_options.ApplicationUri) ||
                certificate.NotBefore.ToUniversalTime() > now || certificate.NotAfter.ToUniversalTime() <= now)
            {
                throw new AuthenticationException("The configured native application identity is not usable.");
            }
        }

        /// <summary>
        /// Checks every URI SAN using the stack's certificate parser.
        /// </summary>
        private static bool HasApplicationUri(Certificate certificate, string applicationUri)
        {
            foreach (string value in X509Utils.GetApplicationUrisFromCertificate(certificate))
            {
                if (value == applicationUri)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Encodes only explicitly supplied certificates within native ABI bounds.
        /// </summary>
        private static ByteString EncodeBundle(CertificateCollection certificates, bool required)
        {
            if (certificates.Count > 16 || (required && certificates.Count == 0))
            {
                throw new AuthenticationException("An explicit bounded native trust bundle is required.");
            }
            using var stream = new MemoryStream();
            foreach (Certificate certificate in certificates)
            {
                byte[] der = certificate.RawData;
                if (stream.Length + der.Length > 16384)
                {
                    throw new NotSupportedException("The selected certificate bundle exceeds native credential capacity.");
                }
                stream.Write(der);
            }
            return new ByteString(stream.ToArray());
        }

        /// <summary>
        /// Refuses unsupported and non-exportable signing capabilities; never substitutes another identity.
        /// </summary>
        private static byte[] ExportPrivateKey(Certificate certificate)
        {
            try
            {
                using RSA? rsa = certificate.GetRSAPrivateKey();
                if (rsa != null)
                {
                    if (rsa.KeySize is < 2048 or > 4096)
                    {
                        throw new NotSupportedException("Native QUIC requires RSA2048..4096.");
                    }
                    return rsa.ExportPkcs8PrivateKey();
                }
                using ECDsa? ecdsa = certificate.GetECDsaPrivateKey();
                if (ecdsa != null)
                {
                    string? curve = ecdsa.ExportParameters(false).Curve.Oid.Value;
                    if (curve is not ("1.2.840.10045.3.1.7" or "1.3.132.0.34" or "1.3.132.0.35"))
                    {
                        throw new NotSupportedException("Native QUIC requires ECDSA P256, P384 or P521.");
                    }
                    return ecdsa.ExportPkcs8PrivateKey();
                }
                throw new NotSupportedException("Native QUIC requires an exportable software application key.");
            }
            catch (CryptographicException exception)
            {
                throw new NotSupportedException(
                    "The selected signing provider cannot export the software PKCS8 capability required by native QUIC.",
                    exception);
            }
        }

        /// <summary>
        /// Invalidates only the selected identity/trust scope, including acquisitions still in progress.
        /// </summary>
        private void OnChange(CertificateChangeEvent change)
        {
            bool relevant = change.Kind == CertificateChangeKind.ApplicationCertificateUpdated
                ? change.CertificateType is not { IsNull: false } type || type == m_options.CertificateType
                : change.Kind is CertificateChangeKind.TrustListUpdated or CertificateChangeKind.CrlUpdated &&
                    change.TrustList == m_options.TrustList;
            if (!relevant)
            {
                return;
            }
            Invalidate();
        }

        /// <summary>
        /// Rotates the invalidation generation without invoking callbacks under synchronization.
        /// </summary>
        private void Invalidate()
        {
            CancellationTokenSource previous;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
                previous = m_generation;
                m_generation = new CancellationTokenSource();
            }
            previous.Cancel();
            previous.Dispose();
        }

        /// <summary>
        /// Converts certificate manager lifecycle notifications into fail-closed snapshot invalidation.
        /// </summary>
        private sealed class ChangeObserver(NativeQuicCertificateProvider owner) : IObserver<CertificateChangeEvent>
        {
            /// <inheritdoc/>
            public void OnCompleted()
            {
                owner.Dispose();
            }

            /// <inheritdoc/>
            public void OnError(Exception error)
            {
                owner.Dispose();
            }

            /// <inheritdoc/>
            public void OnNext(CertificateChangeEvent value)
            {
                owner.OnChange(value);
            }
        }

        private readonly ICertificateManager m_manager;
        private readonly NativeQuicCertificateOptions m_options;
        private readonly TimeProvider m_clock;
        private readonly IDisposable m_changes;
        private readonly Lock m_gate = new();
        private CancellationTokenSource m_generation = new();
        private bool m_disposed;
    }
}
