// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Native.QuicGo
{
    /// <summary>
    /// Server-authenticated transport-only TLS facts, with no peer application identity or authorization.
    /// A server has no verified client certificate; a client verifies the configured server hostname and chain.
    /// </summary>
    public sealed class NativeQuicTlsEvidence
    {
        /// <summary>
        /// Requires the explicit ABI5 transport-only discriminator and role-correct observations.
        /// </summary>
        internal NativeQuicTlsEvidence(ByteString payload)
        {
            m_evidence = new NativeQuicEvidence(payload, applicationMutualTls: false);
        }

        /// <summary>
        /// Gets whether this local endpoint initiated the connection.
        /// </summary>
        public bool IsClient => m_evidence.IsClient;

        /// <summary>
        /// Gets the native TLS version.
        /// </summary>
        public uint TlsVersion => m_evidence.TlsVersion;

        /// <summary>
        /// Gets independent native handshake confirmation.
        /// </summary>
        public bool HandshakeConfirmed => m_evidence.HandshakeConfirmed;

        /// <summary>
        /// Gets whether native TLS handshake processing completed.
        /// </summary>
        public bool HandshakeComplete => m_evidence.HandshakeComplete;

        /// <summary>
        /// Gets whether native QUIC used early data.
        /// </summary>
        public bool Used0Rtt => m_evidence.Used0Rtt;

        /// <summary>
        /// Gets whether TLS resumed a previous session.
        /// </summary>
        public bool Resumed => m_evidence.Resumed;

        /// <summary>
        /// Gets actual local certificate participation, present only on the TLS server.
        /// </summary>
        public bool LocalCertificateUsed => m_evidence.LocalCertificateUsed;

        /// <summary>
        /// Gets the verified peer-chain count, zero at a server without client authentication.
        /// </summary>
        public uint VerifiedCertificateCount => m_evidence.VerifiedCertificateCount;

        /// <summary>
        /// Gets the negotiated ALPN.
        /// </summary>
        public string ApplicationProtocol => m_evidence.ApplicationProtocol;

        /// <summary>
        /// Gets the verified server certificate SHA256 at a client; zero at a server.
        /// </summary>
        public ByteString CertificateSha256 => m_evidence.CertificateSha256;

        /// <summary>
        /// Gets the verified server SPKI SHA256 at a client; zero at a server.
        /// </summary>
        public ByteString SpkiSha256 => m_evidence.SpkiSha256;

        /// <summary>
        /// Gets actual local RFC9221 enablement.
        /// </summary>
        public bool LocalDatagramsEnabled => m_evidence.LocalDatagramsEnabled;

        /// <summary>
        /// Gets actual peer RFC9221 enablement.
        /// </summary>
        public bool PeerDatagramsEnabled => m_evidence.PeerDatagramsEnabled;

        /// <summary>
        /// Gets exact local reset09 capability.
        /// </summary>
        public bool LocalResetStreamAt09 => m_evidence.LocalResetStreamAt09;

        /// <summary>
        /// Gets exact peer reset09 capability.
        /// </summary>
        public bool PeerResetStreamAt09 => m_evidence.PeerResetStreamAt09;

        /// <summary>
        /// Gets the current native datagram payload limit.
        /// </summary>
        public uint MaximumDatagramPayload => m_evidence.MaximumDatagramPayload;

        /// <summary>
        /// Gets the actual local RFC9221 transport parameter.
        /// </summary>
        public ulong LocalMaximumDatagramFrameSize => m_evidence.LocalMaximumDatagramFrameSize;

        /// <summary>
        /// Gets the actual peer RFC9221 transport parameter.
        /// </summary>
        public ulong PeerMaximumDatagramFrameSize => m_evidence.PeerMaximumDatagramFrameSize;

        /// <summary>The policy-checked observations, never an application-pair credential.</summary>
        private readonly NativeQuicEvidence m_evidence;
    }

    /// <summary>
    /// Retrieves transport-only facts through the existing injectable and directly constructible backend.
    /// </summary>
    public static class NativeQuicTlsEvidenceExtensions
    {
        /// <summary>
        /// Retrieves actual native TLS observations without accepting them as application authentication.
        /// </summary>
        public static async ValueTask<NativeQuicTlsEvidence> GetTlsEvidenceAsync(
            this INativeQuicBackend backend,
            NativeQuicConnectionHandle connection,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(backend);
            NativeQuicCompletion result = await backend.ConnectionAsync(
                connection, NativeQuicConnectionOperation.Information, NativeQuicRequest.Default, cancellationToken)
                .ConfigureAwait(false);
            if (result.Status != NativeQuicStatus.Success)
            {
                throw new IOException($"Native QUIC transport evidence failed: {result.Status}.");
            }
            return new NativeQuicTlsEvidence(result.Payload);
        }
    }
}
