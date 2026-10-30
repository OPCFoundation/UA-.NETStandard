// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings.Native
{
    /// <summary>
    /// Acquires explicit native TLS material and applies the application's additional certificate policy.
    /// This seam is independent of flow framing, UASC, Sessions and HTTP/3.
    /// </summary>
    public interface INativeQuicCredentialProvider
    {
        /// <summary>
        /// Acquires an owned immutable identity/trust snapshot. Missing or non-exportable keys must fail closed.
        /// </summary>
        ValueTask<NativeQuicCredentialLease> AcquireAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates actual native peer evidence and its verified DER chain before exposing application traffic.
        /// Implementations must also reject a superseded local credential snapshot.
        /// </summary>
        ValueTask ValidatePeerAsync(
            NativeQuicCredentialLease credentials,
            NativeQuicPeerEvidence peer,
            ArrayOf<ByteString> verifiedChain,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// An owned credential snapshot. Native creation borrows Credentials until completion.
    /// ReleasePrivateKey clears software key bytes immediately after creation; keep the lease for peer validation.
    /// </summary>
    public sealed class NativeQuicCredentialLease : IDisposable
    {
        /// <summary>
        /// Takes ownership of the native private key and copies the public policy values.
        /// The invalidation token must cancel when this identity or trust policy ceases to be usable.
        /// </summary>
        public NativeQuicCredentialLease(
            NativeQuicCredentials credentials,
            NativeQuicPrivateKey privateKey,
            ArrayOf<NativeQuicPeer> allowedPeers,
            ByteString localKeyThumbprint,
            DateTimeOffset expiresAt,
            CancellationToken invalidated = default)
        {
            Credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            ArgumentNullException.ThrowIfNull(privateKey);
            if (allowedPeers.Count is < 1 or > 32 || localKeyThumbprint.Length != 32)
            {
                throw new ArgumentOutOfRangeException(nameof(allowedPeers));
            }
            foreach (NativeQuicPeer peer in allowedPeers)
            {
                ArgumentNullException.ThrowIfNull(peer);
            }
            AllowedPeers = new ArrayOf<NativeQuicPeer>(allowedPeers.Span.ToArray().AsMemory());
            LocalKeyThumbprint = localKeyThumbprint.Copy();
            ExpiresAt = expiresAt;
            Invalidated = invalidated;
            m_privateKey = privateKey;
        }

        /// <summary>
        /// Creates a public-only snapshot for an accepted connection without retaining the listener's key.
        /// </summary>
        private NativeQuicCredentialLease(NativeQuicCredentialLease source)
        {
            Credentials = source.Credentials;
            AllowedPeers = source.AllowedPeers;
            LocalKeyThumbprint = source.LocalKeyThumbprint;
            ExpiresAt = source.ExpiresAt;
            Invalidated = source.Invalidated;
        }

        /// <summary>
        /// Gets the supplied native credentials. They cannot create another endpoint after ReleasePrivateKey.
        /// </summary>
        public NativeQuicCredentials Credentials { get; }

        /// <summary>
        /// Gets the exact URI and SHA256 SPKI rules required in addition to chain validation.
        /// </summary>
        public ArrayOf<NativeQuicPeer> AllowedPeers { get; }

        /// <summary>
        /// Gets SHA256 of the actual local certificate's SubjectPublicKeyInfo DER.
        /// </summary>
        public ByteString LocalKeyThumbprint { get; }

        /// <summary>
        /// Gets the earliest local certificate/issuer expiry.
        /// </summary>
        public DateTimeOffset ExpiresAt { get; }

        /// <summary>
        /// Gets the continuing identity/trust validity signal, independent of caller request cancellation.
        /// </summary>
        public CancellationToken Invalidated { get; }

        /// <summary>
        /// Clears the exported software key after the native owner has copied its credentials.
        /// </summary>
        public void ReleasePrivateKey()
        {
            Interlocked.Exchange(ref m_privateKey, null)?.Dispose();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            ReleasePrivateKey();
        }

        /// <summary>
        /// Creates an independent public lease; listener disposal does not revoke accepted connections.
        /// </summary>
        internal NativeQuicCredentialLease Fork()
        {
            Invalidated.ThrowIfCancellationRequested();
            return new NativeQuicCredentialLease(this);
        }

        private NativeQuicPrivateKey? m_privateKey;
    }
}
