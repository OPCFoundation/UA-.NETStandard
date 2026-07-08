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
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Pcap.Audit;
using Opc.Ua.Bindings;

namespace Opc.Ua.Pcap.Tests.Audit
{
    /// <summary>
    ///
    /// Tests for the tamper-evident audit JSON-lines sink.
    ///
    /// </summary>
    [TestFixture]
    public sealed class HashChainedAuditFileSinkTests : TempDirectoryFixture
    {
        [Test]
        public async Task OnEventAsyncAppendsHashChainedJsonLine()
        {
            string filePath = CreateTempPath("audit.jsonl");
            byte[] key = CreateKey();

            await using (var sink = new HashChainedAuditFileSink(filePath, key, logger: null))
            {
                await sink.OnEventAsync(CreateEvent(0), CancellationToken.None).ConfigureAwait(false);
            }

            string line = AssertSingleLine(filePath);
            using var document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;

            Assert.That(root.TryGetProperty("event", out _), Is.True);
            Assert.That(root.TryGetProperty("hmac", out JsonElement hmac), Is.True);
            Assert.That(root.TryGetProperty("prev", out JsonElement previous), Is.True);
            Assert.That(Convert.FromBase64String(hmac.GetString()!), Has.Length.EqualTo(32));
            Assert.That(Convert.FromBase64String(previous.GetString()!), Has.Length.EqualTo(32));
        }

        [Test]
        public async Task VerifyChainReturnsAllVerifiedForCleanFile()
        {
            string filePath = CreateTempPath("audit.jsonl");
            byte[] key = CreateKey();

            await WriteEventsAsync(filePath, key, eventCount: 5).ConfigureAwait(false);

            AuditChainVerification result = HashChainedAuditFileSink.VerifyChain(filePath, key);

            Assert.That(result.LinesVerified, Is.EqualTo(5));
            Assert.That(result.FirstCorruptLine, Is.EqualTo(-1));
            Assert.That(result.CorruptionReason, Is.Null);
        }

        [Test]
        public async Task VerifyChainDetectsTamperedEventLine()
        {
            string filePath = CreateTempPath("audit.jsonl");
            byte[] key = CreateKey();
            await WriteEventsAsync(filePath, key, eventCount: 5).ConfigureAwait(false);
            string[] lines = File.ReadAllLines(filePath);

            lines[2] = lines[2].Replace("session-2", "tampered-session", StringComparison.Ordinal);
            File.WriteAllLines(filePath, lines);

            AuditChainVerification result = HashChainedAuditFileSink.VerifyChain(filePath, key);

            Assert.That(result.FirstCorruptLine, Is.EqualTo(3));
        }

        [Test]
        public async Task VerifyChainDetectsTamperedHmacLine()
        {
            string filePath = CreateTempPath("audit.jsonl");
            byte[] key = CreateKey();
            await WriteEventsAsync(filePath, key, eventCount: 5).ConfigureAwait(false);
            string[] lines = File.ReadAllLines(filePath);

            lines[2] = MutateHmac(lines[2]);
            File.WriteAllLines(filePath, lines);

            AuditChainVerification result = HashChainedAuditFileSink.VerifyChain(filePath, key);

            Assert.That(result.FirstCorruptLine, Is.EqualTo(3));
        }

        [Test]
        public async Task OnEventAsyncIsThreadSafe()
        {
            string filePath = CreateTempPath("audit.jsonl");
            byte[] key = CreateKey();
            await using var sink = new HashChainedAuditFileSink(filePath, key, logger: null);

            Task[] tasks = [.. Enumerable.Range(0, 10).Select(worker => Task.Run(async () =>
            {
                for (int ii = 0; ii < 10; ii++)
                {
                    await sink.OnEventAsync(CreateEvent((worker * 10) + ii), CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }))];

            await Task.WhenAll(tasks).ConfigureAwait(false);

            AuditChainVerification result = HashChainedAuditFileSink.VerifyChain(filePath, key);

            Assert.That(result.LinesVerified, Is.EqualTo(100));
            Assert.That(result.FirstCorruptLine, Is.EqualTo(-1));
        }

        [Test]
        public void ConstructorRejectsWrongKeyLength()
        {
            string filePath = CreateTempPath("audit.jsonl");

            Assert.That(
                () => new HashChainedAuditFileSink(filePath, new byte[31], logger: null),
                Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("hmacKey"));
        }

        [Test]
        [Platform("Linux,MacOSX")]
        public async Task OnNonWindowsFileIsModeUserAndGroupRead()
        {
            if (OperatingSystem.IsWindows())
            {
                Assert.Ignore("Unix file modes are not available on Windows.");
                return;
            }

            string filePath = CreateTempPath("audit.jsonl");
            await using var sink = new HashChainedAuditFileSink(filePath, CreateKey(), logger: null);

            Assert.That(
                File.GetUnixFileMode(filePath),
                Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead));
        }

        private static async Task WriteEventsAsync(string filePath, byte[] key, int eventCount)
        {
            await using var sink = new HashChainedAuditFileSink(filePath, key, logger: null);
            for (int ii = 0; ii < eventCount; ii++)
            {
                await sink.OnEventAsync(CreateEvent(ii), CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static PcapAuditEvent CreateEvent(int index)
        {
            string formattedIndex = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new PcapAuditEvent(
                PcapAuditEventKind.DumpKeys,
                DateTimeOffset.UtcNow.AddSeconds(index),
                "session-" + formattedIndex,
                resourcePath: "resource-" + formattedIndex,
                remoteEndpoint: "opc.tcp://localhost:4840",
                properties: new Dictionary<string, string> { ["index"] = formattedIndex });
        }

        private static byte[] CreateKey()
        {
            return RandomNumberGenerator.GetBytes(32);
        }

        private static string AssertSingleLine(string filePath)
        {
            string[] lines = File.ReadAllLines(filePath);
            Assert.That(lines, Has.Length.EqualTo(1));
            return lines[0];
        }

        private static string MutateHmac(string line)
        {
            using var document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            string hmac = root.GetProperty("hmac").GetString()!;
            char replacement = hmac[0] == 'A' ? 'B' : 'A';
            string mutatedHmac = replacement + hmac[1..];

            return "{\"event\":" + root.GetProperty("event").GetRawText() +
                ",\"hmac\":\"" + mutatedHmac + "\",\"prev\":\"" + root.GetProperty("prev").GetString() + "\"}";
        }
    }
}
