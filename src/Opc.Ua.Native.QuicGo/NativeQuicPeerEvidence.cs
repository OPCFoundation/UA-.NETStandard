// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

namespace Opc.Ua.Native.QuicGo
{
    /// <summary>
    /// An application-pair mutual-TLS identity admitted from independently observed native facts.
    /// Server-authenticated transport-only TLS cannot produce this evidence.
    /// </summary>
    public sealed class NativeQuicPeerEvidence
    {
        /// <summary>
        /// Requires the original application URI, verified peer and local certificate participation.
        /// </summary>
        internal NativeQuicPeerEvidence(ByteString payload)
        {
            m_evidence = new NativeQuicEvidence(payload, applicationMutualTls: true);
        }

        /// <summary>
        /// Gets the verified TLS version.
        /// </summary>
        public uint TlsVersion => m_evidence.TlsVersion;

        /// <summary>
        /// Gets whether the local endpoint initiated the connection.
        /// </summary>
        public bool IsClient => m_evidence.IsClient;

        /// <summary>
        /// Gets the actually verified certificate count, including the trust anchor.
        /// </summary>
        public uint VerifiedCertificateCount => m_evidence.VerifiedCertificateCount;

        /// <summary>
        /// Gets whether native TLS handshake processing completed.
        /// </summary>
        public bool HandshakeComplete => m_evidence.HandshakeComplete;

        /// <summary>
        /// Gets independent native QUIC handshake confirmation.
        /// </summary>
        public bool HandshakeConfirmed => m_evidence.HandshakeConfirmed;

        /// <summary>
        /// Gets actual mutual application authentication, not inferred from supplied credentials.
        /// </summary>
        public bool MutualTlsAuthenticated => m_evidence.MutualTlsAuthenticated;

        /// <summary>
        /// Gets observed selection and use of the configured local certificate.
        /// </summary>
        public bool LocalCertificateUsed => m_evidence.LocalCertificateUsed;

        /// <summary>
        /// Gets whether native QUIC used early data.
        /// </summary>
        public bool Used0Rtt => m_evidence.Used0Rtt;

        /// <summary>
        /// Gets whether TLS resumed a previous session.
        /// </summary>
        public bool Resumed => m_evidence.Resumed;

        /// <summary>
        /// Gets actual local RFC9221 enablement.
        /// </summary>
        public bool LocalDatagramsEnabled => m_evidence.LocalDatagramsEnabled;

        /// <summary>
        /// Gets actual peer RFC9221 enablement.
        /// </summary>
        public bool PeerDatagramsEnabled => m_evidence.PeerDatagramsEnabled;

        /// <summary>
        /// Gets exact emitted reset09 capability, not a sent-reset observation.
        /// </summary>
        public bool LocalResetStreamAt09 => m_evidence.LocalResetStreamAt09;

        /// <summary>
        /// Gets exact received reset09 capability, not a received-reset observation.
        /// </summary>
        public bool PeerResetStreamAt09 => m_evidence.PeerResetStreamAt09;

        /// <summary>
        /// Gets the exact negotiated ALPN.
        /// </summary>
        public string ApplicationProtocol => m_evidence.ApplicationProtocol;

        /// <summary>
        /// Gets the authorized peer URI from the verified certificate SAN.
        /// </summary>
        public string ApplicationUri => m_evidence.ApplicationUri;

        /// <summary>
        /// Gets the verified peer certificate SHA256.
        /// </summary>
        public ByteString CertificateSha256 => m_evidence.CertificateSha256;

        /// <summary>
        /// Gets the verified peer SPKI SHA256.
        /// </summary>
        public ByteString SpkiSha256 => m_evidence.SpkiSha256;

        /// <summary>
        /// Gets the observed current native datagram payload limit.
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

        /// <summary>Reset09 capability parameter, not transmitted-frame evidence.</summary>
        public const ulong ResetStreamAt09TransportParameter = NativeQuicEvidence.ResetStreamAt09TransportParameter;

        /// <summary>Reset09 frame codepoint; actual frames have independent native observations.</summary>
        public const ulong ResetStreamAt09FrameType = NativeQuicEvidence.ResetStreamAt09FrameType;

        /// <summary>The policy-checked immutable native observations.</summary>
        private readonly NativeQuicEvidence m_evidence;
    }
}
