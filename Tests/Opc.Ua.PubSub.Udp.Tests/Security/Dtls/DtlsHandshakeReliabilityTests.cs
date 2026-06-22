/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System.Linq;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Udp.Security.Dtls;

namespace Opc.Ua.PubSub.Udp.Tests.Security.Dtls
{
    /// <summary>
    /// Tests DTLS 1.3 handshake reliability helpers from RFC 9147 §5.3 and §7.
    /// </summary>
    [TestFixture]
    [Category("Unit")]
    [TestSpec("RFC 9147 §5.3")]
    [TestSpec("RFC 9147 §7")]
    public sealed class DtlsHandshakeReliabilityTests
    {
        [Test]
        public void FragmentsReassembleOutOfOrder()
        {
            byte[] body = Enumerable.Range(0, 100).Select(value => (byte)value).ToArray();
            var fragments = DtlsHandshakeReassembler.Fragment(DtlsHandshakeType.Certificate, 3, body, 37);
            var reassembler = new DtlsHandshakeReassembler();

            byte[]? reassembled = null;
            for (int ii = fragments.Count - 1; ii >= 0; ii--)
            {
                bool complete = reassembler.TryAdd(DtlsHandshakeCodec.DecodeFrame(fragments[ii]), out reassembled);
                Assert.That(complete, Is.EqualTo(ii == 0));
            }

            Assert.That(reassembled, Is.EqualTo(body));
        }

        [Test]
        public void AckRoundTripsRecordNumbers()
        {
            DtlsRecordNumber[] records = [new(1, 7), new(2, 9)];

            var decoded = DtlsAckCodec.Decode(DtlsAckCodec.Encode(records));

            Assert.That(decoded, Is.EqualTo(records));
        }
    }
}
