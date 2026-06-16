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
using System.Text;
using System.Threading;
using NUnit.Framework;
using Opc.Ua.Bindings.Pcap.Capture;
using Opc.Ua.Bindings.Pcap.KeyLog;

namespace Opc.Ua.Bindings.Pcap.Tests.KeyLog
{
    /// <summary>
    /// Negative / failure-mode tests for <see cref="UaKeyLogJsonReader"/>.
    /// Happy-path round-trip is covered by
    /// <c>UaKeyLogJsonRoundTripTests</c>.
    /// </summary>
    [TestFixture]
    public sealed class UaKeyLogJsonReaderNegativeTests : TempDirectoryFixture
    {
        [Test]
        public void ReadAllAsyncFromFileRejectsNullPath()
        {
            var reader = new UaKeyLogJsonReader();

            Assert.That(
                () => reader.ReadAllAsync(filePath: null!, CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("filePath"));
        }

        [Test]
        public void ReadAllAsyncFromFileRejectsEmptyPath()
        {
            var reader = new UaKeyLogJsonReader();

            Assert.That(
                () => reader.ReadAllAsync(filePath: string.Empty, CancellationToken.None),
                Throws.InstanceOf<ArgumentException>()
                    .With.Property("ParamName").EqualTo("filePath"));
        }

        [Test]
        public void ReadAllAsyncFromStreamRejectsNullStream()
        {
            var reader = new UaKeyLogJsonReader();

            Assert.That(
                () => reader.ReadAllAsync(stream: null!, CancellationToken.None),
                Throws.TypeOf<ArgumentNullException>()
                    .With.Property("ParamName").EqualTo("stream"));
        }

        [Test]
        public async System.Threading.Tasks.Task EmptyFileProducesNoEntries()
        {
            string path = CreateTempPath("empty.uakeys.json");
            File.WriteAllText(path, string.Empty, Encoding.UTF8);

            var reader = new UaKeyLogJsonReader();
            List<ChannelKeyMaterial> materials = await PcapTestHelpers.ToListAsync(
                reader.ReadAllAsync(path, CancellationToken.None)).ConfigureAwait(false);

            Assert.That(materials, Is.Empty);
        }

        [Test]
        public async System.Threading.Tasks.Task BlankLinesAreSkipped()
        {
            string path = CreateTempPath("blank.uakeys.json");
            File.WriteAllText(path, "\r\n   \n\t\n", Encoding.UTF8);

            var reader = new UaKeyLogJsonReader();
            List<ChannelKeyMaterial> materials = await PcapTestHelpers.ToListAsync(
                reader.ReadAllAsync(path, CancellationToken.None)).ConfigureAwait(false);

            Assert.That(materials, Is.Empty,
                "Pure whitespace files must yield no entries (and not throw).");
        }

        [Test]
        public void MalformedJsonLineThrowsPcapDiagnosticsException()
        {
            string path = CreateTempPath("garbage.uakeys.json");
            File.WriteAllText(path, "not valid json at all\n", Encoding.UTF8);

            var reader = new UaKeyLogJsonReader();

            Assert.That(
                async () =>
                {
                    await foreach (ChannelKeyMaterial _ in reader.ReadAllAsync(path, CancellationToken.None))
                    {
                    }
                },
                Throws.InstanceOf<Exception>(),
                "A non-JSON line must surface as either PcapDiagnosticsException " +
                "(JSON deserialised to null) or System.Text.Json's JsonException.");
        }

        [Test]
        public void JsonNullLineThrowsPcapDiagnosticsException()
        {
            // 'null' is valid JSON but JsonSerializer.Deserialize<KeyLogRecord> returns
            // null which the reader treats as an invalid record.
            string path = CreateTempPath("null.uakeys.json");
            File.WriteAllText(path, "null\n", Encoding.UTF8);

            var reader = new UaKeyLogJsonReader();

            Assert.That(
                async () =>
                {
                    await foreach (ChannelKeyMaterial _ in reader.ReadAllAsync(path, CancellationToken.None))
                    {
                    }
                },
                Throws.TypeOf<PcapDiagnosticsException>()
                    .With.Message.Contains("Invalid OPC UA JSON key-log record"));
        }
    }
}
