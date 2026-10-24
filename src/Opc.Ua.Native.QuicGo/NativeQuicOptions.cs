// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Opc.Ua.Native.QuicGo
{
    /// <summary>
    /// Caller-owned exportable PKCS8 signing material. Hardware/non-exportable keys are not
    /// supported by this interface; providers must refuse rather than fall back to another identity.
    /// </summary>
    public sealed class NativeQuicPrivateKey : IDisposable
    {
        /// <summary>
        /// Copies unencrypted PKCS8 material. The caller should clear its source buffer.
        /// </summary>
        public NativeQuicPrivateKey(ReadOnlySpan<byte> pkcs8)
        {
            if (pkcs8.IsEmpty || pkcs8.Length > 16384)
            {
                throw new ArgumentOutOfRangeException(nameof(pkcs8));
            }
            m_pkcs8 = pkcs8.ToArray();
        }

        /// <summary>
        /// Clears the owned encoded key. Parsed native signers live only as long as their TLS owner.
        /// </summary>
        public void Dispose()
        {
            lock (m_gate)
            {
                if (m_pkcs8 is not null)
                {
                    CryptographicOperations.ZeroMemory(m_pkcs8);
                    m_pkcs8 = null;
                }
            }
        }

        /// <summary>
        /// Takes a bounded key copy so disposal cannot race native input copying.
        /// </summary>
        internal byte[] Copy()
        {
            lock (m_gate)
            {
                ObjectDisposedException.ThrowIf(m_pkcs8 is null, this);
                return [.. m_pkcs8];
            }
        }

        private readonly Lock m_gate = new();
        private byte[]? m_pkcs8;
    }

    /// <summary>
    /// Explicit role-specific certificate and trust snapshot supplied by a certificate-manager provider.
    /// No system store, certificate generation or implicit key export is attempted.
    /// </summary>
    public sealed class NativeQuicCredentials
    {
        /// <summary>
        /// Creates supplied credentials. DER bundles are bounded to 16384 bytes and 16 certificates natively.
        /// </summary>
        public NativeQuicCredentials(
            ByteString certificateDer,
            ByteString issuerChainDer,
            NativeQuicPrivateKey privateKey,
            ByteString trustRootsDer,
            string applicationUri)
            : this(certificateDer, issuerChainDer, privateKey, trustRootsDer, applicationUri, transportOnly: false)
        {
        }

        /// <summary>
        /// Creates a role-specific immutable credential snapshot without inventing an application identity.
        /// </summary>
        private NativeQuicCredentials(
            ByteString certificateDer,
            ByteString issuerChainDer,
            NativeQuicPrivateKey? privateKey,
            ByteString trustRootsDer,
            string applicationUri,
            bool transportOnly)
        {
            if (!transportOnly)
            {
                ArgumentNullException.ThrowIfNull(privateKey);
                ValidateUri(applicationUri);
            }
            ValidateBlob(certificateDer, !transportOnly || privateKey is not null, nameof(certificateDer));
            ValidateBlob(issuerChainDer, false, nameof(issuerChainDer));
            ValidateBlob(trustRootsDer, !transportOnly || privateKey is null, nameof(trustRootsDer));
            CertificateDer = new ByteString(certificateDer.Span.ToArray());
            IssuerChainDer = new ByteString(issuerChainDer.Span.ToArray());
            TrustRootsDer = new ByteString(trustRootsDer.Span.ToArray());
            PrivateKey = privateKey;
            ApplicationUri = applicationUri;
            IsTransportOnly = transportOnly;
        }

        /// <summary>
        /// Gets the exact leaf DER certificate.
        /// </summary>
        public ByteString CertificateDer { get; }

        /// <summary>
        /// Gets concatenated DER intermediates; these are not trust anchors.
        /// </summary>
        public ByteString IssuerChainDer { get; }

        /// <summary>
        /// Gets explicitly selected DER trust anchors.
        /// </summary>
        public ByteString TrustRootsDer { get; }

        /// <summary>
        /// Gets the exact local application URI for application-mTLS; empty for transport-only TLS.
        /// </summary>
        public string ApplicationUri { get; }

        /// <summary>
        /// Gets whether application authentication is deliberately left to the independent UASC layer.
        /// </summary>
        public bool IsTransportOnly { get; }

        /// <summary>
        /// Gets the caller-owned key capability.
        /// </summary>
        internal NativeQuicPrivateKey? PrivateKey { get; }

        /// <summary>
        /// Creates explicit server trust for a TLS client that has no client certificate or signing key.
        /// Normal chain, lifetime, EKU and hostname validation remain mandatory.
        /// </summary>
        public static NativeQuicCredentials ForTlsClient(ByteString trustRootsDer)
        {
            return new NativeQuicCredentials(default, default, null, trustRootsDer, string.Empty, transportOnly: true);
        }

        /// <summary>
        /// Creates server TLS credentials without requesting client certificates or authorizing applications.
        /// The certificate and key must be supplied by the host's certificate-store provider.
        /// </summary>
        public static NativeQuicCredentials ForTlsServer(
            ByteString certificateDer, ByteString issuerChainDer, NativeQuicPrivateKey privateKey)
        {
            ArgumentNullException.ThrowIfNull(privateKey);
            return new NativeQuicCredentials(
                certificateDer, issuerChainDer, privateKey, default, string.Empty, transportOnly: true);
        }

        /// <summary>
        /// Validates the structural URI policy before native SAN verification.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        internal static void ValidateUri(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            if (!Uri.TryCreate(value, UriKind.Absolute, out _) || Encoding.UTF8.GetByteCount(value) > 512)
            {
                throw new ArgumentException("An absolute bounded application URI is required.", nameof(value));
            }
        }

        /// <summary>
        /// Validates a bounded DER input without synchronous cryptography.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        private static void ValidateBlob(ByteString value, bool required, string parameter)
        {
            if ((required && value.IsEmpty) || value.Length > 16384)
            {
                throw new ArgumentOutOfRangeException(parameter);
            }
        }
    }

    /// <summary>
    /// One authorized peer: BOTH the exact URI SAN and SHA256 SPKI pin must match
    /// an ordinarily verified TLS chain. Multiple rules admit explicit applications or key rotations.
    /// </summary>
    public sealed class NativeQuicPeer
    {
        /// <summary>
        /// Creates an exact application/key rule. Wildcards and trust-only rules are not supported.
        /// </summary>
        public NativeQuicPeer(string applicationUri, ByteString spkiSha256)
        {
            NativeQuicCredentials.ValidateUri(applicationUri);
            if (spkiSha256.Length != 32 || spkiSha256.Span.IndexOfAnyExcept((byte)0) < 0)
            {
                throw new ArgumentException("A nonzero 32-byte SHA256 SPKI pin is required.", nameof(spkiSha256));
            }
            ApplicationUri = applicationUri;
            SpkiSha256 = new ByteString(spkiSha256.Span.ToArray());
        }

        /// <summary>
        /// Gets the exact required application URI SAN.
        /// </summary>
        public string ApplicationUri { get; }

        /// <summary>
        /// Gets the SHA256 of the peer SubjectPublicKeyInfo DER.
        /// </summary>
        public ByteString SpkiSha256 { get; }
    }

    /// <summary>
    /// Independent endpoint options, unrelated to flow records or HTTP/3. Policy is an immutable snapshot;
    /// credential/authorization providers may supply a different explicit rule set for each listener.
    /// </summary>
    public sealed class NativeQuicOptions
    {
        /// <summary>
        /// Creates endpoint options. Listeners require a numeric host:port (port zero is allowed);
        /// connectors also support DNS, and require an independently configured expected server name.
        /// </summary>
        public NativeQuicOptions(
            string address,
            string applicationProtocol,
            NativeQuicCredentials credentials,
            ArrayOf<NativeQuicPeer> allowedPeers,
            string serverName = "")
            : this(address, applicationProtocol, credentials, allowedPeers, serverName, transportOnly: false)
        {
        }

        /// <summary>
        /// Validates a mutually exclusive authentication-policy snapshot before native admission.
        /// </summary>
        private NativeQuicOptions(
            string address,
            string applicationProtocol,
            NativeQuicCredentials credentials,
            ArrayOf<NativeQuicPeer> allowedPeers,
            string serverName,
            bool transportOnly)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(address);
            ArgumentException.ThrowIfNullOrWhiteSpace(applicationProtocol);
            ArgumentNullException.ThrowIfNull(credentials);
            ArgumentNullException.ThrowIfNull(serverName);
            if (Encoding.UTF8.GetByteCount(address) > 512 ||
                Encoding.UTF8.GetByteCount(applicationProtocol) > 255 ||
                Encoding.UTF8.GetByteCount(serverName) > 253)
            {
                throw new ArgumentOutOfRangeException(nameof(address), "Native endpoint text bounds exceeded.");
            }
            if (credentials.IsTransportOnly != transportOnly)
            {
                throw new ArgumentException(
                    "Credentials must match the explicitly selected TLS policy.", nameof(credentials));
            }
            if ((transportOnly && allowedPeers.Count != 0) ||
                (!transportOnly && allowedPeers.Count is < 1 or > 32))
            {
                throw new ArgumentOutOfRangeException(nameof(allowedPeers));
            }
            foreach (NativeQuicPeer peer in allowedPeers)
            {
                ArgumentNullException.ThrowIfNull(peer);
            }
            Address = address;
            ApplicationProtocol = applicationProtocol;
            Credentials = credentials;
            AllowedPeers = new ArrayOf<NativeQuicPeer>(allowedPeers.Span.ToArray().AsMemory());
            ServerName = serverName;
            AuthenticationPolicy = transportOnly ? 1u : 0u;
        }

        /// <summary>
        /// Gets the numeric listener endpoint or connector endpoint.
        /// </summary>
        public string Address { get; }

        /// <summary>
        /// Gets the exact ALPN; no fallback protocols are offered.
        /// </summary>
        public string ApplicationProtocol { get; }

        /// <summary>
        /// Gets supplied certificate, signing and trust material.
        /// </summary>
        public NativeQuicCredentials Credentials { get; }

        /// <summary>
        /// Gets the explicit immutable peer allowlist.
        /// </summary>
        public ArrayOf<NativeQuicPeer> AllowedPeers { get; }

        /// <summary>
        /// Gets the connector's required TLS hostname.
        /// </summary>
        public string ServerName { get; }

        /// <summary>
        /// Gets the immutable ABI5 policy discriminator; zero retains application-pair mutual TLS.
        /// </summary>
        internal uint AuthenticationPolicy { get; }

        /// <summary>
        /// Gets the finite creation deadline in milliseconds (1..60000).
        /// </summary>
        public uint TimeoutMilliseconds { get; init; } = 30000;

        /// <summary>
        /// Gets the bounded incoming stream count per direction (1..32).
        /// </summary>
        public uint StreamLimit { get; init; } = 32;

        /// <summary>
        /// Gets the transport idle deadline (1000..600000 milliseconds).
        /// </summary>
        public uint IdleTimeoutMilliseconds { get; init; } = 60000;

        /// <summary>
        /// Creates explicitly selected server-authenticated TLS options. A connector requires a hostname
        /// and ForTlsClient credentials; a listener requires ForTlsServer credentials and no hostname.
        /// This policy provides no application identity or authorization; UASC must establish those independently.
        /// </summary>
        public static NativeQuicOptions ForServerAuthenticatedTls(
            string address,
            string applicationProtocol,
            NativeQuicCredentials credentials,
            string serverName = "",
            uint timeoutMilliseconds = 30000,
            uint streamLimit = 32,
            uint idleTimeoutMilliseconds = 60000)
        {
            return new NativeQuicOptions(
                address, applicationProtocol, credentials, default, serverName, transportOnly: true)
            {
                TimeoutMilliseconds = timeoutMilliseconds,
                StreamLimit = streamLimit,
                IdleTimeoutMilliseconds = idleTimeoutMilliseconds
            };
        }
    }
}
