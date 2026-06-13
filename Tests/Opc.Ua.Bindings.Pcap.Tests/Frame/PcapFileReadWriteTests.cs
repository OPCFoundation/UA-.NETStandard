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
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings.Pcap.Capture;
using Opc.Ua.Bindings.Pcap.Frame;

namespace Opc.Ua.Bindings.Pcap.Tests.Frame
{
    [TestFixture]
    public sealed class PcapFileReadWriteTests : TempDirectoryFixture
    {
        [Test]
        public async Task OneRecordRoundTrips()
        {
            string path = CreateTempPath("one.pcap");
            byte[] payload = { 1, 2, 3, 4, 5 };
            var timestamp = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(123450);

            var writer = new PcapFileWriter(path, PcapFileWriter.LinkTypeEthernet);
            try
            {
                await writer.WriteAsync(timestamp, payload, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            var records = await PcapTestHelpers.ToListAsync(
                PcapFileReader.ReadAllAsync(path, CancellationToken.None),
                maxCount: 1).ConfigureAwait(false);

            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That((records[0].Timestamp - timestamp).Duration(), Is.LessThanOrEqualTo(TimeSpan.FromTicks(10)));
            Assert.That(records[0].OriginalLength, Is.EqualTo(payload.Length));
            Assert.That(records[0].Data.ToArray(), Is.EqualTo(payload).AsCollection);
        }

        [Test]
        public async Task ManyRecordsRoundTripInOrder()
        {
            string path = CreateTempPath("many.pcap");
            var payloads = new List<byte[]>();

            var writer = new PcapFileWriter(path, PcapFileWriter.LinkTypeEthernet);
            try
            {
                for (int index = 0; index < 100; index++)
                {
                    byte[] payload = RandomNumberGenerator.GetBytes(10 + (index % 91));
                    payloads.Add(payload);
                    await writer.WriteAsync(DateTimeOffset.UnixEpoch.AddMilliseconds(index), payload, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            var records = await PcapTestHelpers.ToListAsync(
                PcapFileReader.ReadAllAsync(path, CancellationToken.None),
                maxCount: 100).ConfigureAwait(false);

            Assert.That(records, Has.Count.EqualTo(payloads.Count));
            for (int index = 0; index < payloads.Count; index++)
            {
                Assert.That(records[index].Data.ToArray(), Is.EqualTo(payloads[index]).AsCollection);
            }
        }

        [Test]
        public async Task LinkTypeIsPreserved()
        {
            string nullPath = CreateTempPath("null.pcap");
            string ethernetPath = CreateTempPath("ethernet.pcap");

            await WriteSinglePacketAsync(nullPath, PcapFileWriter.LinkTypeNull).ConfigureAwait(false);
            await WriteSinglePacketAsync(ethernetPath, PcapFileWriter.LinkTypeEthernet).ConfigureAwait(false);

            var nullRecords = await PcapTestHelpers.ToListAsync(
                PcapFileReader.ReadAllAsync(nullPath, CancellationToken.None),
                maxCount: 1).ConfigureAwait(false);
            var ethernetRecords = await PcapTestHelpers.ToListAsync(
                PcapFileReader.ReadAllAsync(ethernetPath, CancellationToken.None),
                maxCount: 1).ConfigureAwait(false);

            Assert.That(nullRecords[0].LinkType, Is.EqualTo(PcapFileWriter.LinkTypeNull));
            Assert.That(ethernetRecords[0].LinkType, Is.EqualTo(PcapFileWriter.LinkTypeEthernet));
        }

        [Test]
        public async Task TruncatedPartialHeaderStopsAfterCompleteRecords()
        {
            string path = CreateTempPath("truncated.pcap");
            byte[] payload = { 9, 8, 7 };
            var writer = new PcapFileWriter(path, PcapFileWriter.LinkTypeEthernet);
            try
            {
                await writer.WriteAsync(DateTimeOffset.UtcNow, payload, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            using (FileStream stream = File.Open(path, FileMode.Append, FileAccess.Write))
            {
                await stream.WriteAsync(new byte[] { 1, 2, 3, 4 }, CancellationToken.None).ConfigureAwait(false);
            }

            var records = await PcapTestHelpers.ToListAsync(
                PcapFileReader.ReadAllAsync(path, CancellationToken.None),
                maxCount: 1).ConfigureAwait(false);

            Assert.That(records, Has.Count.EqualTo(1));
            Assert.That(records[0].Data.ToArray(), Is.EqualTo(payload).AsCollection);
        }

        [Test]
        public void BadMagicThrowsDiagnosticsException()
        {
            string path = CreateTempPath("bad.pcap");
            File.WriteAllBytes(path, new byte[24]);

            Assert.That(
                () => PcapTestHelpers.ToListAsync(PcapFileReader.ReadAllAsync(path, CancellationToken.None)),
                Throws.TypeOf<PcapDiagnosticsException>());
        }

        private static async Task WriteSinglePacketAsync(string path, uint linkType)
        {
            var writer = new PcapFileWriter(path, linkType);
            try
            {
                await writer.WriteAsync(DateTimeOffset.UtcNow, new byte[] { 1 }, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
