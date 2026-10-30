// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Opc.Ua.Native.QuicGo
{
    /// <summary>
    /// Checked native TLS identity and transport facts. Not derived from HELLO,
    /// an application record, a stream-write count or an unverified certificate assertion.
    /// </summary>
    internal sealed class NativeQuicEvidence
    {
        /// <summary>
        /// Parses fixed-width ABI7 evidence against the explicitly requested authentication policy.
        /// </summary>
        internal NativeQuicEvidence(ByteString payload, bool applicationMutualTls)
        {
            ReadOnlySpan<byte> data = payload.Span;
            if (data.Length != 920 || BinaryPrimitives.ReadUInt32LittleEndian(data) != 920)
            {
                throw new InvalidDataException("Invalid native QUIC peer-evidence layout.");
            }
            uint alpnLength = BinaryPrimitives.ReadUInt32LittleEndian(data[64..]);
            uint uriLength = BinaryPrimitives.ReadUInt32LittleEndian(data[68..]);
            if (alpnLength is 0 or > 255 ||
                (applicationMutualTls ? uriLength is 0 or > 512 : uriLength != 0))
            {
                throw new InvalidDataException("Invalid native QUIC peer-evidence text bounds.");
            }
            TlsVersion = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
            IsClient = ReadFlag(data, 8);
            VerifiedCertificateCount = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
            HandshakeComplete = ReadFlag(data, 16);
            Used0Rtt = ReadFlag(data, 20);
            Resumed = ReadFlag(data, 24);
            LocalDatagramsEnabled = ReadFlag(data, 28);
            PeerDatagramsEnabled = ReadFlag(data, 32);
            LocalResetStreamAt09 = ReadFlag(data, 36);
            PeerResetStreamAt09 = ReadFlag(data, 40);
            HandshakeConfirmed = ReadFlag(data, 904);
            MutualTlsAuthenticated = ReadFlag(data, 908);
            LocalCertificateUsed = ReadFlag(data, 912);
            if (TlsVersion != 0x304 ||
                VerifiedCertificateCount > 16 ||
                !HandshakeComplete || Used0Rtt || Resumed ||
                (applicationMutualTls && (!LocalDatagramsEnabled || !PeerDatagramsEnabled ||
                    !LocalResetStreamAt09 || !PeerResetStreamAt09)) ||
                !HandshakeConfirmed ||
                BinaryPrimitives.ReadUInt32LittleEndian(data[916..]) != (applicationMutualTls ? 0u : 1u))
            {
                throw new InvalidDataException(
                    "Native QUIC peer evidence lacks mandatory authentication/capabilities.");
            }
            if (applicationMutualTls ?
                VerifiedCertificateCount == 0 || !MutualTlsAuthenticated || !LocalCertificateUsed :
                MutualTlsAuthenticated || LocalCertificateUsed == IsClient ||
                (IsClient ? VerifiedCertificateCount == 0 : VerifiedCertificateCount != 0))
            {
                throw new InvalidDataException(
                    "Native QUIC evidence does not match the selected authentication policy.");
            }
            MaximumDatagramPayload = BinaryPrimitives.ReadUInt32LittleEndian(data[44..]);
            LocalMaximumDatagramFrameSize = BinaryPrimitives.ReadUInt64LittleEndian(data[48..]);
            PeerMaximumDatagramFrameSize = BinaryPrimitives.ReadUInt64LittleEndian(data[56..]);
            bool datagramsAvailable = LocalDatagramsEnabled && PeerDatagramsEnabled;
            if (MaximumDatagramPayload > 65536 ||
                (datagramsAvailable ? MaximumDatagramPayload == 0 : MaximumDatagramPayload != 0) ||
                LocalDatagramsEnabled != (LocalMaximumDatagramFrameSize != 0) ||
                PeerDatagramsEnabled != (PeerMaximumDatagramFrameSize != 0))
            {
                throw new InvalidDataException("Invalid native datagram capability evidence.");
            }
            CertificateSha256 = new ByteString(data.Slice(72, 32).ToArray());
            SpkiSha256 = new ByteString(data.Slice(104, 32).ToArray());
            if (!applicationMutualTls &&
                ((CertificateSha256.Span.IndexOfAnyExcept((byte)0) >= 0) != IsClient ||
                (SpkiSha256.Span.IndexOfAnyExcept((byte)0) >= 0) != IsClient ||
                data.Slice(392, 512).IndexOfAnyExcept((byte)0) >= 0))
            {
                throw new InvalidDataException("Server-authenticated TLS cannot invent a peer application identity.");
            }
            ApplicationProtocol = s_utf8.GetString(data.Slice(136, (int)alpnLength));
            ApplicationUri = s_utf8.GetString(data.Slice(392, (int)uriLength));
        }

        /// <summary>
        /// Gets the verified TLS version (0x0304).
        /// </summary>
        public uint TlsVersion { get; }

        /// <summary>
        /// Gets whether this local connection is the QUIC initiator.
        /// </summary>
        public bool IsClient { get; }

        /// <summary>
        /// Gets the length of the actually verified chain, including its trust anchor.
        /// </summary>
        public uint VerifiedCertificateCount { get; }

        /// <summary>
        /// Gets whether local TLS handshake processing completed. This is not an independent
        /// QUIC HANDSHAKE_DONE/handshake-confirmed observation.
        /// </summary>
        public bool HandshakeComplete { get; }

        /// <summary>
        /// Gets actual QUIC handshake confirmation: authenticated HANDSHAKE_DONE receipt at a client,
        /// or native server handshake completion after required client authentication.
        /// </summary>
        public bool HandshakeConfirmed { get; }

        /// <summary>
        /// Gets mutual TLS authentication admitted only after observed use of the configured local
        /// certificate, verified expected peer identity and actual QUIC handshake confirmation.
        /// </summary>
        public bool MutualTlsAuthenticated { get; }

        /// <summary>
        /// Gets whether the configured local certificate was selected by the native TLS callback and
        /// used in the completed handshake. A client additionally observes CertificateRequest.
        /// </summary>
        public bool LocalCertificateUsed { get; }

        /// <summary>
        /// Gets whether native QUIC used early data.
        /// </summary>
        public bool Used0Rtt { get; }

        /// <summary>
        /// Gets whether the native TLS connection resumed a prior session.
        /// </summary>
        public bool Resumed { get; }

        /// <summary>
        /// Gets whether the local native endpoint enabled RFC9221 datagrams.
        /// </summary>
        public bool LocalDatagramsEnabled { get; }

        /// <summary>
        /// Gets whether the peer advertised RFC9221 datagrams.
        /// </summary>
        public bool PeerDatagramsEnabled { get; }

        /// <summary>
        /// Gets the local reset09 capability reported by the source-pinned native emitter.
        /// The reviewed emitter uses empty transport parameter 0x1d; this is not a sent-reset observation.
        /// </summary>
        public bool LocalResetStreamAt09 { get; }

        /// <summary>
        /// Gets exact receipt of peer transport parameter 0x1d, distinguished from legacy reset support.
        /// The source-pinned decoder rejects nonempty values. Actual reset-frame evidence is reported
        /// independently by WaitResetSent or a reset read outcome.
        /// </summary>
        public bool PeerResetStreamAt09 { get; }

        /// <summary>
        /// Gets the exact negotiated ALPN.
        /// </summary>
        public string ApplicationProtocol { get; }

        /// <summary>
        /// Gets the authorized peer URI from a verified certificate SAN.
        /// </summary>
        public string ApplicationUri { get; }

        /// <summary>
        /// Gets the verified leaf certificate SHA256.
        /// </summary>
        public ByteString CertificateSha256 { get; }

        /// <summary>
        /// Gets the verified peer SPKI SHA256.
        /// </summary>
        public ByteString SpkiSha256 { get; }

        /// <summary>
        /// Gets the native current datagram payload limit, which may change with path MTU.
        /// </summary>
        public uint MaximumDatagramPayload { get; }

        /// <summary>
        /// Gets the local emitted RFC9221 max_datagram_frame_size.
        /// </summary>
        public ulong LocalMaximumDatagramFrameSize { get; }

        /// <summary>
        /// Gets the peer's authenticated RFC9221 max_datagram_frame_size.
        /// </summary>
        public ulong PeerMaximumDatagramFrameSize { get; }

        /// <summary>
        /// Reads a native Boolean without accepting an invalid discriminator as false.
        /// </summary>
        private static bool ReadFlag(ReadOnlySpan<byte> data, int offset)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            if (value > 1)
            {
                throw new InvalidDataException("Invalid native QUIC peer-evidence Boolean.");
            }
            return value == 1;
        }

        /// <summary>Reset09 transport parameter; negotiated capability, not transmitted-frame evidence.</summary>
        public const ulong ResetStreamAt09TransportParameter = 0x1d;

        /// <summary>RESET_STREAM_AT codepoint for reset09; use native completion evidence for actual frames.</summary>
        public const ulong ResetStreamAt09FrameType = 0x24;

        private static readonly UTF8Encoding s_utf8 = new(false, true);
    }
}
