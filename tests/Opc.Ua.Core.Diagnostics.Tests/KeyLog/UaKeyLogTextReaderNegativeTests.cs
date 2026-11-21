// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.Pcap.Capture;
using Opc.Ua.Pcap.KeyLog;

namespace Opc.Ua.Pcap.Tests.KeyLog
{
    /// <summary>
    /// Negative / failure-mode tests for <see cref="UaKeyLogTextReader"/>.
    /// Happy-path round-trip is covered by
    /// <c>UaKeyLogTextRoundTripTests</c>.
    /// </summary>
    [TestFixture]
    public sealed class UaKeyLogTextReaderNegativeTests : TempDirectoryFixture
    {
        [Test]
        public void ReadAllAsyncFromFileRejectsNullPath()
        {
            var reader = new UaKeyLogTextReader();

            Assert.That(
                () => reader.ReadAllAsync(filePath: null!, CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("filePath"));
        }

        [Test]
        public void ReadAllAsyncFromFileRejectsEmptyPath()
        {
            var reader = new UaKeyLogTextReader();

            Assert.That(
                () => reader.ReadAllAsync(filePath: string.Empty, CancellationToken.None),
                Throws.InstanceOf<ArgumentException>()
                    .With.Property("ParamName").EqualTo("filePath"));
        }

        [Test]
        public void ReadAllAsyncFromStreamRejectsNullStream()
        {
            var reader = new UaKeyLogTextReader();

            Assert.That(
                () => reader.ReadAllAsync(stream: null!, CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("stream"));
        }

        [Test]
        public async System.Threading.Tasks.Task EmptyFileProducesNoEntries()
        {
            string path = CreateTempPath("empty.uakeys.txt");
            File.WriteAllText(path, string.Empty, Encoding.UTF8);

            var reader = new UaKeyLogTextReader();
            List<ChannelKeyMaterial> materials = await PcapTestHelpers.ToListAsync(
                reader.ReadAllAsync(path, CancellationToken.None)).ConfigureAwait(false);

            Assert.That(materials, Is.Empty);
        }

        [Test]
        public async System.Threading.Tasks.Task CommentLinesAndBlankLinesAreSkipped()
        {
            // Reader skips lines starting with '#' (Wireshark-style comments)
            // and pure-whitespace lines.
            string path = CreateTempPath("comments.uakeys.txt");
            File.WriteAllText(
                path,
                "# Header comment\n\n   \n# another comment\n",
                Encoding.UTF8);

            var reader = new UaKeyLogTextReader();
            List<ChannelKeyMaterial> materials = await PcapTestHelpers.ToListAsync(
                reader.ReadAllAsync(path, CancellationToken.None)).ConfigureAwait(false);

            Assert.That(materials, Is.Empty);
        }

        [Test]
        public void WrongFieldCountThrowsPcapDiagnosticsException()
        {
            // Reader expects exactly 11 whitespace-separated tokens starting
            // with OPCUA_CHANNEL; supply only 3.
            string path = CreateTempPath("short.uakeys.txt");
            File.WriteAllText(path, "OPCUA_CHANNEL 0x1 0x2\n", Encoding.UTF8);

            var reader = new UaKeyLogTextReader();

            Assert.That(
                async () =>
                {
                    await foreach (ChannelKeyMaterial _ in reader.ReadAllAsync(path, CancellationToken.None))
                    {
                    }
                },
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("Invalid OPC UA text key-log record"));
        }

        [Test]
        public void WrongTagThrowsPcapDiagnosticsException()
        {
            // Reader insists the first token be 'OPCUA_CHANNEL'; supply
            // 'OPCUA_SOMETHING' instead. Even with 11 fields, the tag check
            // must reject it.
            string path = CreateTempPath("bad-tag.uakeys.txt");
            const string line = "OPCUA_SOMETHING 0x1 0x2 http://policy None - - - - - -";
            File.WriteAllText(path, line + "\n", Encoding.UTF8);

            var reader = new UaKeyLogTextReader();

            Assert.That(
                async () =>
                {
                    await foreach (ChannelKeyMaterial _ in reader.ReadAllAsync(path, CancellationToken.None))
                    {
                    }
                },
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("Invalid OPC UA text key-log record"));
        }

        [Test]
        public void UnknownSecurityModeThrowsPcapDiagnosticsException()
        {
            // 11 fields, correct tag, but the 5th field is not a known
            // MessageSecurityMode value.
            string path = CreateTempPath("bad-mode.uakeys.txt");
            const string line = "OPCUA_CHANNEL 0x1 0x2 http://policy ScrambledEggs - - - - - -";
            File.WriteAllText(path, line + "\n", Encoding.UTF8);

            var reader = new UaKeyLogTextReader();

            Assert.That(
                async () =>
                {
                    await foreach (ChannelKeyMaterial _ in reader.ReadAllAsync(path, CancellationToken.None))
                    {
                    }
                },
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("Invalid OPC UA key-log security mode"));
        }

        [Test]
        public void NonHexIntegerThrowsPcapDiagnosticsException()
        {
            // Channel id field is decimal (no leading "0x") — reader insists
            // every integer be a 0x-prefixed hex literal.
            string path = CreateTempPath("decimal.uakeys.txt");
            const string line = "OPCUA_CHANNEL 123 0x2 http://policy None - - - - - -";
            File.WriteAllText(path, line + "\n", Encoding.UTF8);

            var reader = new UaKeyLogTextReader();

            Assert.That(
                async () =>
                {
                    await foreach (ChannelKeyMaterial _ in reader.ReadAllAsync(path, CancellationToken.None))
                    {
                    }
                },
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("Invalid OPC UA key-log integer"));
        }

        [Test]
        public async System.Threading.Tasks.Task DashFieldsBecomeNullByteArrays()
        {
            // Every hex field set to '-' means "absent / null". The parsed
            // material must contain null arrays in those slots and a valid
            // policy + channel/token id elsewhere.
            string path = CreateTempPath("dashes.uakeys.txt");
            const string line = "OPCUA_CHANNEL 0x12345678 0x9ABCDEF0 http://example/policy None - - - - - -";
            File.WriteAllText(path, line + "\n", Encoding.UTF8);

            var reader = new UaKeyLogTextReader();
            List<ChannelKeyMaterial> materials = await PcapTestHelpers.ToListAsync(
                reader.ReadAllAsync(path, CancellationToken.None)).ConfigureAwait(false);

            Assert.That(materials, Has.Count.EqualTo(1));
            ChannelKeyMaterial material = materials[0];
            Assert.That(material.ChannelId, Is.EqualTo(0x12345678U));
            Assert.That(material.TokenId, Is.EqualTo(0x9ABCDEF0U));
            Assert.That(material.SecurityPolicyUri, Is.EqualTo("http://example/policy"));
            Assert.That(material.SecurityMode, Is.EqualTo(MessageSecurityMode.None));
            Assert.That(material.ClientSigningKey, Is.Null);
            Assert.That(material.ClientEncryptingKey, Is.Null);
            Assert.That(material.ClientInitializationVector, Is.Null);
            Assert.That(material.ServerSigningKey, Is.Null);
            Assert.That(material.ServerEncryptingKey, Is.Null);
            Assert.That(material.ServerInitializationVector, Is.Null);
        }
    }
}
