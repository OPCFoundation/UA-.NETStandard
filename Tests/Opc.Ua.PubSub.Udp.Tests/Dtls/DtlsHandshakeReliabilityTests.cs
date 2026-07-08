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

using System.Linq;
using NUnit.Framework;
using Opc.Ua.PubSub.Tests;
using Opc.Ua.PubSub.Udp.Dtls;

namespace Opc.Ua.PubSub.Udp.Tests.Dtls
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
            byte[] body = [.. Enumerable.Range(0, 100).Select(value => (byte)value)];
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
