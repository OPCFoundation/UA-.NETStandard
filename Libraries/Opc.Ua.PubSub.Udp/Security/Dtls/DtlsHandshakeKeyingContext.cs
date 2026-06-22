/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Security.Cryptography;

namespace Opc.Ua.PubSub.Udp.Security.Dtls
{
    /// <summary>
    /// Binds TLS 1.3 traffic secrets to DTLS record protection and KeyUpdate.
    /// </summary>
    internal sealed class DtlsHandshakeKeyingContext : IDisposable
    {
        public DtlsHandshakeKeyingContext(DtlsProfile profile, ReadOnlySpan<byte> sharedSecret,
            ReadOnlySpan<byte> handshakeTranscriptHash, ReadOnlySpan<byte> applicationTranscriptHash)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            m_schedule = new DtlsKeySchedule(profile.CipherSuite);
            Secrets = m_schedule.DeriveTrafficSecrets(sharedSecret, handshakeTranscriptHash, applicationTranscriptHash);
        }

        public DtlsProfile Profile { get; }

        public DtlsTrafficSecrets Secrets { get; private set; }

        public DtlsRecordProtection CreateClientApplicationWriteProtection()
        {
            return new DtlsRecordProtection(Profile, Secrets.ClientApplicationTrafficSecret, epoch: 3);
        }

        public DtlsRecordProtection CreateServerApplicationWriteProtection()
        {
            return new DtlsRecordProtection(Profile, Secrets.ServerApplicationTrafficSecret, epoch: 3);
        }

        public byte[] ComputeClientFinished(ReadOnlySpan<byte> transcriptHash)
        {
            return m_schedule.ComputeFinished(Secrets.ClientFinishedKey, transcriptHash);
        }

        public byte[] ComputeServerFinished(ReadOnlySpan<byte> transcriptHash)
        {
            return m_schedule.ComputeFinished(Secrets.ServerFinishedKey, transcriptHash);
        }

        public void VerifyFinished(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
        {
            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                throw new DtlsHandshakeException("DTLS Finished verify_data mismatch.");
            }
        }

        public void UpdateApplicationTrafficSecret(bool client)
        {
            byte[] next = DtlsHkdf.ExpandLabel(
                m_schedule.HashAlgorithmName,
                client ? Secrets.ClientApplicationTrafficSecret : Secrets.ServerApplicationTrafficSecret,
                "traffic upd",
                ReadOnlySpan<byte>.Empty,
                m_schedule.HashLength);
            if (client)
            {
                CryptographicOperations.ZeroMemory(Secrets.ClientApplicationTrafficSecret);
                Secrets = Secrets with { ClientApplicationTrafficSecret = next };
            }
            else
            {
                CryptographicOperations.ZeroMemory(Secrets.ServerApplicationTrafficSecret);
                Secrets = Secrets with { ServerApplicationTrafficSecret = next };
            }
        }

        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            CryptographicOperations.ZeroMemory(Secrets.ClientHandshakeTrafficSecret);
            CryptographicOperations.ZeroMemory(Secrets.ServerHandshakeTrafficSecret);
            CryptographicOperations.ZeroMemory(Secrets.ClientApplicationTrafficSecret);
            CryptographicOperations.ZeroMemory(Secrets.ServerApplicationTrafficSecret);
            CryptographicOperations.ZeroMemory(Secrets.ClientFinishedKey);
            CryptographicOperations.ZeroMemory(Secrets.ServerFinishedKey);
            m_disposed = true;
        }

        private readonly DtlsKeySchedule m_schedule;
        private bool m_disposed;
    }
}
