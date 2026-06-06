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

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Diagnostics.Pcap.KeyLog;

namespace Opc.Ua.Diagnostics.Pcap.Tests.KeyLog
{
    [TestFixture]
    public sealed class UaKeyLogJsonRoundTripTests : TempDirectoryFixture
    {
        [Test]
        public async Task RecordsRoundTripAcrossSecurityModes()
        {
            var records = new List<ChannelKeyMaterial>
            {
                PcapTestHelpers.CreateMaterial(SecurityPolicies.None, MessageSecurityMode.None),
                PcapTestHelpers.CreateMaterial(SecurityPolicies.Basic256Sha256, MessageSecurityMode.Sign),
                PcapTestHelpers.CreateMaterial(SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt),
                PcapTestHelpers.CreateMaterial(SecurityPolicies.Aes256_Sha256_RsaPss, MessageSecurityMode.SignAndEncrypt),
                PcapTestHelpers.CreateMaterial(SecurityPolicies.Aes128_Sha256_RsaOaep, MessageSecurityMode.SignAndEncrypt)
            };

            if (SecurityPolicies.GetInfo(SecurityPolicies.ECC_nistP256_AesGcm) is not null)
            {
                records.Add(PcapTestHelpers.CreateMaterial(
                    SecurityPolicies.ECC_nistP256_AesGcm,
                    MessageSecurityMode.SignAndEncrypt));
            }

            string path = CreateTempPath("keys.uakeys.json");
            var writer = new UaKeyLogJsonWriter(path);
            try
            {
                foreach (ChannelKeyMaterial record in records)
                {
                    await writer.AppendAsync(record, CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            var roundTripped = await PcapTestHelpers.ToListAsync(
                new UaKeyLogJsonReader().ReadAllAsync(path, CancellationToken.None)).ConfigureAwait(false);

            Assert.That(roundTripped, Has.Count.EqualTo(records.Count));
            for (int index = 0; index < records.Count; index++)
            {
                PcapTestHelpers.AssertMaterialEqual(roundTripped[index], records[index], includeJsonOnlyFields: true);
            }
        }

        [Test]
        public async Task MultipleRecordsRoundTripInOrder()
        {
            ChannelKeyMaterial first = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt,
                tokenId: 1);
            ChannelKeyMaterial second = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt,
                tokenId: 2);
            string path = CreateTempPath("renewal.uakeys.json");

            var writer = new UaKeyLogJsonWriter(path);
            try
            {
                await writer.AppendAsync(first, CancellationToken.None).ConfigureAwait(false);
                await writer.AppendAsync(second, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            var records = await PcapTestHelpers.ToListAsync(
                new UaKeyLogJsonReader().ReadAllAsync(path, CancellationToken.None)).ConfigureAwait(false);

            Assert.That(records.Select(static record => record.TokenId), Is.EqualTo(new uint[] { 1, 2 }).AsCollection);
        }

        [Test]
        public async Task EmptyFileYieldsNoRecords()
        {
            string path = CreateTempPath("empty.uakeys.json");
            await System.IO.File.WriteAllTextAsync(path, string.Empty, CancellationToken.None).ConfigureAwait(false);

            var records = await PcapTestHelpers.ToListAsync(
                new UaKeyLogJsonReader().ReadAllAsync(path, CancellationToken.None)).ConfigureAwait(false);

            Assert.That(records, Is.Empty);
        }
    }
}
