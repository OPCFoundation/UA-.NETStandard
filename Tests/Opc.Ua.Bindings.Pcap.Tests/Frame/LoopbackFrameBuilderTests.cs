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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings.Pcap.Frame;

namespace Opc.Ua.Bindings.Pcap.Tests.Frame
{
    [TestFixture]
    public sealed class LoopbackFrameBuilderTests : TempDirectoryFixture
    {
        [Test]
        public void HeadersAreCorrect()
        {
            byte[] chunk = [0xDE, 0xAD, 0xBE, 0xEF];
            byte[] packet = LoopbackFrameBuilder.Build(fromClient: true, 0x1234, chunk);

            Assert.That(packet[..4], Is.EqualTo(new byte[] { 0x02, 0x00, 0x00, 0x00 }).AsCollection);
            Assert.That(packet[4] >> 4, Is.EqualTo(4));
            Assert.That(packet[4] & 0x0F, Is.EqualTo(5));
            Assert.That(BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(6)), Is.EqualTo(44));
            Assert.That(packet[13], Is.EqualTo(6));
            Assert.That(packet[36] >> 4, Is.EqualTo(5));
            Assert.That(packet[44..], Is.EqualTo(chunk).AsCollection);
            Assert.That(packet, Has.Length.EqualTo(48));
        }

        [Test]
        public void Ipv4ChecksumVerifies()
        {
            byte[] packet = LoopbackFrameBuilder.Build(fromClient: true, 0x1234, [1, 2, 3]);
            ushort sum = PcapTestHelpers.FoldOnesComplement(PcapTestHelpers.SumWords(packet.AsSpan(4, 20)));

            Assert.That(sum, Is.EqualTo(0xFFFF));
        }

        [Test]
        public void TcpChecksumVerifies()
        {
            byte[] packet = LoopbackFrameBuilder.Build(fromClient: true, 0x1234, [1, 2, 3]);
            uint sum = PcapTestHelpers.SumWords(packet.AsSpan(16, 4)) + PcapTestHelpers.SumWords(packet.AsSpan(20, 4));
            sum += 6;
            sum += (uint)(packet.Length - 24);
            sum += PcapTestHelpers.SumWords(packet.AsSpan(24));

            Assert.That(PcapTestHelpers.FoldOnesComplement(sum), Is.EqualTo(0xFFFF));
        }

        [Test]
        public void DirectionFlipsSourceAndDestination()
        {
            byte[] fromClient = LoopbackFrameBuilder.Build(fromClient: true, 0x1234, [1]);
            byte[] fromServer = LoopbackFrameBuilder.Build(fromClient: false, 0x1234, [1]);

            Assert.That(fromClient.AsSpan(16, 4).ToArray(), Is.EqualTo(fromServer.AsSpan(20, 4).ToArray()).AsCollection);
            Assert.That(fromClient.AsSpan(20, 4).ToArray(), Is.EqualTo(fromServer.AsSpan(16, 4).ToArray()).AsCollection);
            Assert.That(fromClient.AsSpan(24, 2).ToArray(), Is.EqualTo(fromServer.AsSpan(26, 2).ToArray()).AsCollection);
            Assert.That(fromClient.AsSpan(26, 2).ToArray(), Is.EqualTo(fromServer.AsSpan(24, 2).ToArray()).AsCollection);
        }

        [Test]
        public async Task BuiltPacketsRoundTripThroughPcap()
        {
            byte[] chunk = [0xDE, 0xAD, 0xBE, 0xEF];
            byte[] packet = LoopbackFrameBuilder.Build(fromClient: true, 0x1234, chunk);
            string path = CreateTempPath("loopback.pcap");

            var writer = new PcapFileWriter(path, PcapFileWriter.LinkTypeNull);
            try
            {
                await writer.WriteAsync(DateTimeOffset.UtcNow, packet, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            List<PcapRecord> records = await PcapTestHelpers.ToListAsync(
                PcapFileReader.ReadAllAsync(path, CancellationToken.None),
                maxCount: 1).ConfigureAwait(false);

            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].Data.ToArray(), Is.EqualTo(packet).AsCollection);
            Assert.That(records[0].Data.ToArray()[44..], Is.EqualTo(chunk).AsCollection);
        }
    }
}
