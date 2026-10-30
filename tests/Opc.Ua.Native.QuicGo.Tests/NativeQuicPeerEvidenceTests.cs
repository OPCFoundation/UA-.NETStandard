// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers.Binary;
using System.IO;
using NUnit.Framework;

namespace Opc.Ua.Native.QuicGo.Tests
{
    /// <summary>
    /// Verifies fail-closed parsing of the independently observed ABI2 authentication facts.
    /// Actual TLS/QUIC generation is exercised by the native and managed network tests.
    /// </summary>
    [TestFixture]
    public sealed class NativeQuicPeerEvidenceTests
    {
        /// <summary>
        /// TLS completion alone never substitutes for confirmation, mutual authentication or local participation.
        /// </summary>
        [TestCase(904)]
        [TestCase(908)]
        [TestCase(912)]
        public void MissingAdmissionObservationIsRejected(int offset)
        {
            byte[] bytes = ValidEvidence();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), 0);
            Assert.Throws<InvalidDataException>(() => new NativeQuicPeerEvidence(new ByteString(bytes)));
        }

        /// <summary>
        /// Invalid Boolean encodings and reserved values cannot be interpreted as successful observations.
        /// </summary>
        [TestCase(8)]
        [TestCase(16)]
        [TestCase(20)]
        [TestCase(24)]
        [TestCase(28)]
        [TestCase(32)]
        [TestCase(36)]
        [TestCase(40)]
        [TestCase(904)]
        [TestCase(908)]
        [TestCase(912)]
        [TestCase(916)]
        public void InvalidBooleanOrReservedValueIsRejected(int offset)
        {
            byte[] bytes = ValidEvidence();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), 2);
            Assert.Throws<InvalidDataException>(() => new NativeQuicPeerEvidence(new ByteString(bytes)));
        }

        /// <summary>
        /// Typed properties expose the supplied observation fields and exact protocol codepoint mapping.
        /// </summary>
        [Test]
        public void TypedFactsPreserveTheCheckedNativeValues()
        {
            var evidence = new NativeQuicPeerEvidence(new ByteString(ValidEvidence()));
            Assert.That(evidence.HandshakeComplete, Is.True);
            Assert.That(evidence.HandshakeConfirmed, Is.True);
            Assert.That(evidence.MutualTlsAuthenticated, Is.True);
            Assert.That(evidence.LocalCertificateUsed, Is.True);
            Assert.That(evidence.Used0Rtt, Is.False);
            Assert.That(evidence.Resumed, Is.False);
            Assert.That(evidence.LocalDatagramsEnabled, Is.True);
            Assert.That(evidence.PeerDatagramsEnabled, Is.True);
            Assert.That(evidence.LocalResetStreamAt09, Is.True);
            Assert.That(evidence.PeerResetStreamAt09, Is.True);
            Assert.That(evidence.ApplicationProtocol, Is.EqualTo("h3"));
            Assert.That(evidence.ApplicationUri, Is.EqualTo("urn:test"));
            Assert.That(NativeQuicPeerEvidence.ResetStreamAt09TransportParameter, Is.EqualTo(0x1d));
            Assert.That(NativeQuicPeerEvidence.ResetStreamAt09FrameType, Is.EqualTo(0x24));
        }

        /// <summary>
        /// Produces a parser fixture, not proof of any actual network authentication.
        /// </summary>
        private static byte[] ValidEvidence()
        {
            byte[] bytes = new byte[920];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, 920);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x304);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 2);
            foreach (int offset in new[] { 16, 28, 32, 36, 40, 904, 908, 912 })
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), 1);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), 1200);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(48), 16383);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(56), 16383);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(64), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(68), 8);
            "h3"u8.CopyTo(bytes.AsSpan(136));
            "urn:test"u8.CopyTo(bytes.AsSpan(392));
            return bytes;
        }
    }
}
