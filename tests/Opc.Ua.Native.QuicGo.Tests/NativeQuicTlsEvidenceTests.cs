// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers.Binary;
using System.IO;
using NUnit.Framework;

namespace Opc.Ua.Native.QuicGo.Tests
{
    /// <summary>
    /// Checks role-specific transport TLS parsing without treating parser fixtures as network evidence.
    /// </summary>
    [TestFixture]
    public sealed class NativeQuicTlsEvidenceTests
    {
        /// <summary>
        /// Client observations verify the server; server observations never invent a client identity.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void TransportTlsPreservesRoleSpecificFactsWithoutApplicationIdentity(bool client)
        {
            var evidence = new NativeQuicTlsEvidence(new ByteString(ValidEvidence(client)));
            Assert.Multiple(() =>
            {
                Assert.That(evidence.IsClient, Is.EqualTo(client));
                Assert.That(evidence.TlsVersion, Is.EqualTo(0x304));
                Assert.That(evidence.HandshakeConfirmed, Is.True);
                Assert.That(evidence.LocalCertificateUsed, Is.EqualTo(!client));
                Assert.That(evidence.VerifiedCertificateCount, Is.EqualTo(client ? 2 : 0));
                Assert.That(evidence.ApplicationProtocol, Is.EqualTo("h3"));
                Assert.That(evidence.CertificateSha256.Span[0], Is.EqualTo(client ? 1 : 0));
                Assert.That(evidence.SpkiSha256.Span[0], Is.EqualTo(client ? 2 : 0));
                Assert.That(evidence.LocalDatagramsEnabled, Is.True);
                Assert.That(evidence.PeerDatagramsEnabled, Is.True);
                Assert.That(evidence.LocalResetStreamAt09, Is.True);
                Assert.That(evidence.PeerResetStreamAt09, Is.True);
                Assert.That(evidence.MaximumDatagramPayload, Is.EqualTo(1200));
                Assert.That(evidence.LocalMaximumDatagramFrameSize, Is.EqualTo(16383));
                Assert.That(evidence.PeerMaximumDatagramFrameSize, Is.EqualTo(16383));
            });
            Assert.Throws<InvalidDataException>(() =>
                new NativeQuicPeerEvidence(new ByteString(ValidEvidence(client))));
        }

        /// <summary>
        /// Missing or invalid transport observations cannot select transport-only authentication.
        /// </summary>
        [TestCase(4, 0x303u)]
        [TestCase(8, 2u)]
        [TestCase(12, 0u)]
        [TestCase(12, 17u)]
        [TestCase(16, 0u)]
        [TestCase(20, 1u)]
        [TestCase(24, 1u)]
        [TestCase(28, 0u)]
        [TestCase(32, 0u)]
        [TestCase(36, 2u)]
        [TestCase(40, 2u)]
        [TestCase(44, 0u)]
        [TestCase(44, 65537u)]
        [TestCase(48, 0u)]
        [TestCase(56, 0u)]
        [TestCase(64, 0u)]
        [TestCase(64, 256u)]
        [TestCase(68, 1u)]
        [TestCase(72, 0u)]
        [TestCase(104, 0u)]
        [TestCase(392, 1u)]
        [TestCase(904, 0u)]
        [TestCase(908, 1u)]
        [TestCase(912, 1u)]
        [TestCase(916, 0u)]
        [TestCase(916, 2u)]
        public void TransportTlsRejectsMissingOrContradictoryClientFacts(int offset, uint value)
        {
            byte[] bytes = ValidEvidence(client: true);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
            Assert.Throws<InvalidDataException>(() => new NativeQuicTlsEvidence(new ByteString(bytes)));
        }

        /// <summary>
        /// A reliable TLS baseline keeps optional peer datagram/reset capabilities independent in either role.
        /// </summary>
        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public void TransportTlsKeepsOptionalCapabilitiesIndependent(bool client, bool peerDatagrams, bool peerReset)
        {
            byte[] bytes = ValidEvidence(client);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32), peerDatagrams ? 1u : 0u);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), peerReset ? 1u : 0u);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), peerDatagrams ? 1200u : 0u);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(56), peerDatagrams ? 16383ul : 0ul);
            var evidence = new NativeQuicTlsEvidence(new ByteString(bytes));
            Assert.Multiple(() =>
            {
                Assert.That(evidence.HandshakeConfirmed, Is.True);
                Assert.That(evidence.IsClient, Is.EqualTo(client));
                Assert.That(evidence.LocalCertificateUsed, Is.EqualTo(!client));
                Assert.That(evidence.VerifiedCertificateCount, Is.EqualTo(client ? 2 : 0));
                Assert.That(evidence.PeerDatagramsEnabled, Is.EqualTo(peerDatagrams));
                Assert.That(evidence.PeerResetStreamAt09, Is.EqualTo(peerReset));
                Assert.That(evidence.MaximumDatagramPayload, Is.EqualTo(peerDatagrams ? 1200u : 0u));
                Assert.That(evidence.PeerMaximumDatagramFrameSize, Is.EqualTo(peerDatagrams ? 16383ul : 0ul));
            });
        }

        /// <summary>
        /// The separate legacy application-mTLS contract still requires both optional transport extensions.
        /// </summary>
        [TestCase(28)]
        [TestCase(32)]
        [TestCase(36)]
        [TestCase(40)]
        public void ApplicationMutualTlsStillRequiresDatagramsAndReset(int offset)
        {
            byte[] bytes = ValidApplicationEvidence();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), 0);
            Assert.Throws<InvalidDataException>(() => new NativeQuicPeerEvidence(new ByteString(bytes)));
        }

        /// <summary>
        /// A TLS server cannot claim a verified client, a client hash or missing server participation.
        /// </summary>
        [TestCase(12, 1u)]
        [TestCase(72, 1u)]
        [TestCase(104, 1u)]
        [TestCase(912, 0u)]
        public void TransportTlsRejectsInventedServerSidePeerIdentity(int offset, uint value)
        {
            byte[] bytes = ValidEvidence(client: false);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
            Assert.Throws<InvalidDataException>(() => new NativeQuicTlsEvidence(new ByteString(bytes)));
        }

        /// <summary>
        /// The two admission contracts remain disjoint even when mutual authentication is valid.
        /// </summary>
        [Test]
        public void ApplicationMutualTlsCannotBeReinterpretedAsTransportOnlyTls()
        {
            byte[] bytes = ValidApplicationEvidence();
            var application = new NativeQuicPeerEvidence(new ByteString(bytes));
            Assert.That(application.ApplicationUri, Is.EqualTo("urn:test"));
            Assert.That(application.MutualTlsAuthenticated, Is.True);
            Assert.Throws<InvalidDataException>(() => new NativeQuicTlsEvidence(new ByteString(bytes)));
        }

        /// <summary>
        /// A complete, fixed-width native result is mandatory.
        /// </summary>
        [TestCase(0)]
        [TestCase(919)]
        [TestCase(921)]
        public void TransportTlsRejectsWrongEvidenceSize(int length)
        {
            Assert.Throws<InvalidDataException>(() =>
                new NativeQuicTlsEvidence(new ByteString(new byte[length])));
        }

        /// <summary>
        /// Builds the separate valid application-mTLS contract for parser isolation checks.
        /// </summary>
        private static byte[] ValidApplicationEvidence()
        {
            byte[] bytes = ValidEvidence(client: true);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(68), 8);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(908), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(912), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(916), 0);
            "urn:test"u8.CopyTo(bytes.AsSpan(392));
            return bytes;
        }

        /// <summary>
        /// Builds an independent parser fixture with deliberately different role-specific identities.
        /// </summary>
        private static byte[] ValidEvidence(bool client)
        {
            byte[] bytes = new byte[920];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, 920);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x304);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), client ? 1u : 0u);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), client ? 2u : 0u);
            foreach (int offset in new[] { 16, 28, 32, 36, 40, 904, 916 })
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), 1);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), 1200);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(48), 16383);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(56), 16383);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(64), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(912), client ? 0u : 1u);
            bytes[72] = client ? (byte)1 : (byte)0;
            bytes[104] = client ? (byte)2 : (byte)0;
            "h3"u8.CopyTo(bytes.AsSpan(136));
            return bytes;
        }
    }
}
