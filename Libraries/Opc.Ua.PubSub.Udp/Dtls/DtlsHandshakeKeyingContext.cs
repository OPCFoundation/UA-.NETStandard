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

namespace Opc.Ua.PubSub.Udp.Dtls
{
    /// <summary>
    /// Binds TLS 1.3 traffic secrets to DTLS record protection and KeyUpdate.
    /// </summary>
    internal sealed class DtlsHandshakeKeyingContext : IDisposable
    {
        /// <summary>
        /// Initializes a new <see cref="DtlsHandshakeKeyingContext"/> by deriving the TLS 1.3
        /// handshake traffic secrets and Finished keys from the negotiated shared secret and the
        /// handshake transcript hash. Application traffic secrets are derived separately via
        /// <see cref="InstallApplicationSecrets"/> once the full handshake transcript (through the
        /// server Finished) is available (RFC 8446 §7.1).
        /// </summary>
        public DtlsHandshakeKeyingContext(DtlsProfile profile, ReadOnlySpan<byte> sharedSecret,
            ReadOnlySpan<byte> handshakeTranscriptHash)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            m_schedule = new DtlsKeySchedule(profile.CipherSuite);
            byte[] handshakeSecret = m_schedule.DeriveHandshakeSecret(sharedSecret);
            try
            {
                byte[] clientHandshakeTrafficSecret = m_schedule.DeriveSecret(
                    handshakeSecret, "c hs traffic", handshakeTranscriptHash);
                byte[] serverHandshakeTrafficSecret = m_schedule.DeriveSecret(
                    handshakeSecret, "s hs traffic", handshakeTranscriptHash);
                m_masterSecret = m_schedule.DeriveMasterSecret(handshakeSecret);
                Secrets = new DtlsTrafficSecrets(
                    clientHandshakeTrafficSecret,
                    serverHandshakeTrafficSecret,
                    [],
                    [],
                    m_schedule.FinishedKey(clientHandshakeTrafficSecret),
                    m_schedule.FinishedKey(serverHandshakeTrafficSecret));
            }
            finally
            {
                CryptoUtils.ZeroMemory(handshakeSecret);
            }
        }

        /// <summary>
        /// Initializes a new <see cref="DtlsHandshakeKeyingContext"/> deriving both the handshake
        /// traffic secrets (over <paramref name="handshakeTranscriptHash"/>) and the application
        /// traffic secrets (over <paramref name="applicationTranscriptHash"/>) up front.
        /// </summary>
        public DtlsHandshakeKeyingContext(DtlsProfile profile, ReadOnlySpan<byte> sharedSecret,
            ReadOnlySpan<byte> handshakeTranscriptHash, ReadOnlySpan<byte> applicationTranscriptHash)
            : this(profile, sharedSecret, handshakeTranscriptHash)
        {
            InstallApplicationSecrets(applicationTranscriptHash);
        }

        /// <summary>
        /// Negotiated DTLS profile whose cipher suite drives key derivation.
        /// </summary>
        public DtlsProfile Profile { get; }

        /// <summary>
        /// Current TLS 1.3 traffic secrets derived for the connection.
        /// </summary>
        public DtlsTrafficSecrets Secrets { get; private set; }

        /// <summary>
        /// Derives the TLS 1.3 client/server application traffic secrets from the master secret over
        /// the supplied application transcript hash (Hash(ClientHello…server Finished) per RFC 8446
        /// §7.1) and installs them so application record protection can be created.
        /// </summary>
        public void InstallApplicationSecrets(ReadOnlySpan<byte> applicationTranscriptHash)
        {
            byte[] clientApplicationTrafficSecret = m_schedule.DeriveSecret(
                m_masterSecret, "c ap traffic", applicationTranscriptHash);
            byte[] serverApplicationTrafficSecret = m_schedule.DeriveSecret(
                m_masterSecret, "s ap traffic", applicationTranscriptHash);
            CryptoUtils.ZeroMemory(Secrets.ClientApplicationTrafficSecret);
            CryptoUtils.ZeroMemory(Secrets.ServerApplicationTrafficSecret);
            Secrets = Secrets with
            {
                ClientApplicationTrafficSecret = clientApplicationTrafficSecret,
                ServerApplicationTrafficSecret = serverApplicationTrafficSecret
            };
        }

        /// <summary>
        /// Creates record protection for the client application traffic epoch.
        /// </summary>
        public DtlsRecordProtection CreateClientApplicationWriteProtection()
        {
            return new DtlsRecordProtection(Profile, Secrets.ClientApplicationTrafficSecret, epoch: 3);
        }

        /// <summary>
        /// Creates record protection for the server application traffic epoch.
        /// </summary>
        public DtlsRecordProtection CreateServerApplicationWriteProtection()
        {
            return new DtlsRecordProtection(Profile, Secrets.ServerApplicationTrafficSecret, epoch: 3);
        }

        /// <summary>
        /// Computes the client Finished verify_data over the supplied transcript hash.
        /// </summary>
        public byte[] ComputeClientFinished(ReadOnlySpan<byte> transcriptHash)
        {
            return m_schedule.ComputeFinished(Secrets.ClientFinishedKey, transcriptHash);
        }

        /// <summary>
        /// Computes the server Finished verify_data over the supplied transcript hash.
        /// </summary>
        public byte[] ComputeServerFinished(ReadOnlySpan<byte> transcriptHash)
        {
            return m_schedule.ComputeFinished(Secrets.ServerFinishedKey, transcriptHash);
        }

        /// <summary>
        /// Verifies a received Finished verify_data against the expected value in constant time.
        /// </summary>
        public void VerifyFinished(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
        {
            if (!CryptoUtils.FixedTimeEquals(expected, actual))
            {
                throw new DtlsHandshakeException("DTLS Finished verify_data mismatch.");
            }
        }

        /// <summary>
        /// Advances the client or server application traffic secret for a KeyUpdate.
        /// </summary>
        public void UpdateApplicationTrafficSecret(bool client)
        {
            byte[] next = DtlsHkdf.ExpandLabel(
                m_schedule.HashAlgorithmName,
                client ? Secrets.ClientApplicationTrafficSecret : Secrets.ServerApplicationTrafficSecret,
                "traffic upd",
                [],
                m_schedule.HashLength);
            if (client)
            {
                CryptoUtils.ZeroMemory(Secrets.ClientApplicationTrafficSecret);
                Secrets = Secrets with { ClientApplicationTrafficSecret = next };
            }
            else
            {
                CryptoUtils.ZeroMemory(Secrets.ServerApplicationTrafficSecret);
                Secrets = Secrets with { ServerApplicationTrafficSecret = next };
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            CryptoUtils.ZeroMemory(Secrets.ClientHandshakeTrafficSecret);
            CryptoUtils.ZeroMemory(Secrets.ServerHandshakeTrafficSecret);
            CryptoUtils.ZeroMemory(Secrets.ClientApplicationTrafficSecret);
            CryptoUtils.ZeroMemory(Secrets.ServerApplicationTrafficSecret);
            CryptoUtils.ZeroMemory(Secrets.ClientFinishedKey);
            CryptoUtils.ZeroMemory(Secrets.ServerFinishedKey);
            CryptoUtils.ZeroMemory(m_masterSecret);
            m_disposed = true;
        }

        private readonly DtlsKeySchedule m_schedule;
        private readonly byte[] m_masterSecret;
        private bool m_disposed;
    }
}
