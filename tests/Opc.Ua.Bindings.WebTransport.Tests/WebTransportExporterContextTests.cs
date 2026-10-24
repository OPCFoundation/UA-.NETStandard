// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Buffers.Binary;
using System.Linq;
using NUnit.Framework;
using Opc.Ua.DataChannels;

namespace Opc.Ua.Bindings.WebTransport.Tests
{
    /// <summary>
    /// Verifies the Part 32 WebTransport exporter context encoding.
    /// </summary>
    [TestFixture]
    public sealed class WebTransportExporterContextTests
    {
        /// <summary>
        /// Preserves the exporter context field order and network byte order.
        /// </summary>
        [Test]
        public void SerializesSessionLabelAndDigest()
        {
            byte[] digest = Enumerable.Repeat((byte)0xA5, 32).ToArray();
            byte[] label = "OPCUA-WEBTRANSPORT-DRAFT"u8.ToArray();
            byte[] expected = new byte[8 + 1 + label.Length + 1 + digest.Length];
            BinaryPrimitives.WriteUInt64BigEndian(expected, 0x0102030405060708);
            expected[8] = checked((byte)label.Length);
            label.CopyTo(expected, 9);
            expected[9 + label.Length] = checked((byte)digest.Length);
            digest.CopyTo(expected, 10 + label.Length);

            byte[] actual = WebTransportUascBaselineConnection.CreateExporterContext(
                0x0102030405060708, new ByteString(digest));

            Assert.That(actual, Is.EqualTo(expected));
        }

        /// <summary>
        /// Rejects missing and incorrectly sized transcript digests.
        /// </summary>
        [TestCase(0)]
        [TestCase(31)]
        [TestCase(33)]
        public void RejectsIncorrectTranscriptDigestLength(int length)
        {
            ByteString digest = new(Enumerable.Repeat((byte)0xA5, length).ToArray());

            Assert.That(
                () => WebTransportUascBaselineConnection.CreateExporterContext(0, digest),
                Throws.ArgumentException);
        }
    }
}
