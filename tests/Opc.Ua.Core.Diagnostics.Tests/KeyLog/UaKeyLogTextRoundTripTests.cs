// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.KeyLog;

namespace Opc.Ua.Pcap.Tests.KeyLog
{
    [TestFixture]
    public sealed class UaKeyLogTextRoundTripTests : TempDirectoryFixture
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

            if (SecurityPolicies.Default.GetInfo(SecurityPolicies.ECC_nistP256_AesGcm) is not null)
            {
                records.Add(PcapTestHelpers.CreateMaterial(
                    SecurityPolicies.ECC_nistP256_AesGcm,
                    MessageSecurityMode.SignAndEncrypt));
            }

            string path = CreateTempPath("keys.uakeys.txt");
            var writer = new UaKeyLogTextWriter(path);
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

            List<ChannelKeyMaterial> roundTripped = await PcapTestHelpers.ToListAsync(
                new UaKeyLogTextReader().ReadAllAsync(path, CancellationToken.None)).ConfigureAwait(false);

            Assert.That(roundTripped, Has.Count.EqualTo(records.Count));
            for (int index = 0; index < records.Count; index++)
            {
                PcapTestHelpers.AssertMaterialEqual(roundTripped[index], records[index], includeJsonOnlyFields: false);
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
            string path = CreateTempPath("renewal.uakeys.txt");

            var writer = new UaKeyLogTextWriter(path);
            try
            {
                await writer.AppendAsync(first, CancellationToken.None).ConfigureAwait(false);
                await writer.AppendAsync(second, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            List<ChannelKeyMaterial> records = await PcapTestHelpers.ToListAsync(
                new UaKeyLogTextReader().ReadAllAsync(path, CancellationToken.None)).ConfigureAwait(false);

            Assert.That(records.Select(static record => record.TokenId), Is.EqualTo(new uint[] { 1, 2 }).AsCollection);
        }

        [Test]
        public async Task EmptyFileYieldsNoRecords()
        {
            string path = CreateTempPath("empty.uakeys.txt");
            await System.IO.File.WriteAllTextAsync(path, string.Empty, CancellationToken.None).ConfigureAwait(false);

            List<ChannelKeyMaterial> records = await PcapTestHelpers.ToListAsync(
                new UaKeyLogTextReader().ReadAllAsync(path, CancellationToken.None)).ConfigureAwait(false);

            Assert.That(records, Is.Empty);
        }

        [Test]
        public async Task CommentAndWhitespaceLinesAreSkipped()
        {
            ChannelKeyMaterial record = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.Basic256Sha256,
                MessageSecurityMode.SignAndEncrypt);
            string path = CreateTempPath("comments.uakeys.txt");
            string linePath = CreateTempPath("line.uakeys.txt");
            string line;
            var writer = new UaKeyLogTextWriter(linePath);
            try
            {
                await writer.AppendAsync(record, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }

            string[] lines = await System.IO.File.ReadAllLinesAsync(linePath, CancellationToken.None)
                .ConfigureAwait(false);
            line = lines.Single(static value => value.StartsWith("OPCUA_CHANNEL", System.StringComparison.Ordinal));
            await System.IO.File.WriteAllTextAsync(
                path,
                "# comment\r\n   \r\n" + line + "\r\n",
                CancellationToken.None).ConfigureAwait(false);

            List<ChannelKeyMaterial> records = await PcapTestHelpers.ToListAsync(
                new UaKeyLogTextReader().ReadAllAsync(path, CancellationToken.None)).ConfigureAwait(false);

            Assert.That(records, Has.Count.EqualTo(1));
            PcapTestHelpers.AssertMaterialEqual(records[0], record, includeJsonOnlyFields: false);
        }
    }
}
