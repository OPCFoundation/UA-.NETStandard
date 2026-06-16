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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Core.Diagnostics.KeyLog;

namespace Opc.Ua.Core.Diagnostics.Tests.KeyLog
{
    /// <summary>
    /// Behavioural tests for the stand-alone keylog observer that
    /// <c>AddPcapFromEnvironment</c> installs when only
    /// <c>OPCUA_KEYLOGFILE</c> is set.
    /// </summary>
    [TestFixture]
    public sealed class StandaloneKeyLogObserverTests : TempDirectoryFixture
    {
        [Test]
        public async Task FrameCallbacksAreNoOps()
        {
            string keyLogPath = CreateTempPath("keys.uakeys.json");
            await using StandaloneKeyLogObserver observer =
                StandaloneKeyLogObserver.Create(keyLogPath);

            IFrameCaptureSink sink = observer;
            sink.OnFrameSent(channelId: 1, chunk: new byte[] { 1, 2, 3 });
            sink.OnFrameReceived(channelId: 1, chunk: new byte[] { 4, 5, 6 });

            // Force a flush via dispose; the file may exist (header / open
            // handle) but must not contain any frame bytes that we wrote.
            await observer.DisposeAsync().ConfigureAwait(false);

            if (File.Exists(keyLogPath))
            {
                string content = await File.ReadAllTextAsync(keyLogPath).ConfigureAwait(false);
                Assert.That(content, Does.Not.Contain("\"channelId\""));
            }
        }

        [Test]
        public async Task TokenActivationProducesJsonRecord()
        {
            string keyLogPath = CreateTempPath("keys.uakeys.json");
            await using StandaloneKeyLogObserver observer =
                StandaloneKeyLogObserver.Create(keyLogPath);

            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.Aes256_Sha256_RsaPss,
                MessageSecurityMode.SignAndEncrypt);
            using ChannelToken token = MaterializeToken(material);

            IFrameCaptureSink sink = observer;
            sink.OnTokenActivated(material.ChannelId, token, previousToken: null);

            await observer.DisposeAsync().ConfigureAwait(false);

            Assert.That(File.Exists(keyLogPath), Is.True);
            string[] lines = await File.ReadAllLinesAsync(keyLogPath).ConfigureAwait(false);
            Assert.That(lines, Has.Length.EqualTo(1));
            Assert.That(lines[0], Does.Contain("\"channelId\""));
            Assert.That(lines[0], Does.Contain(material.SecurityPolicyUri));
        }

        [Test]
        public async Task TextExtensionSelectsTextFormat()
        {
            string keyLogPath = CreateTempPath("keys.uakeys.txt");
            await using StandaloneKeyLogObserver observer =
                StandaloneKeyLogObserver.Create(keyLogPath);

            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.Aes256_Sha256_RsaPss,
                MessageSecurityMode.SignAndEncrypt);
            using ChannelToken token = MaterializeToken(material);

            IFrameCaptureSink sink = observer;
            sink.OnTokenActivated(material.ChannelId, token, previousToken: null);

            await observer.DisposeAsync().ConfigureAwait(false);

            Assert.That(File.Exists(keyLogPath), Is.True);
            string content = await File.ReadAllTextAsync(keyLogPath).ConfigureAwait(false);
            // NSS-style text format is space-delimited hex tokens, NOT
            // JSON braces.
            Assert.That(content, Does.Not.Contain("{"));
            Assert.That(content.Trim(), Has.Length.GreaterThan(0));
        }

        [Test]
        public async Task ParentDirectoryIsCreated()
        {
            string nested = CreateTempPath(Path.Combine("a", "b", "c"));
            string keyLogPath = Path.Combine(nested, "keys.uakeys.json");

            Assert.That(Directory.Exists(nested), Is.False);

            await using StandaloneKeyLogObserver observer =
                StandaloneKeyLogObserver.Create(keyLogPath);

            Assert.That(Directory.Exists(nested), Is.True);
        }

        [Test]
        public void CreateThrowsOnNullPath()
        {
            Assert.That(
                () => StandaloneKeyLogObserver.Create(keyLogFilePath: null!),
                Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void CreateThrowsOnWhitespacePath()
        {
            Assert.That(
                () => StandaloneKeyLogObserver.Create("   "),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task TokenActivationsAfterDisposeAreIgnored()
        {
            string keyLogPath = CreateTempPath("keys.uakeys.json");
            StandaloneKeyLogObserver observer =
                StandaloneKeyLogObserver.Create(keyLogPath);

            await observer.DisposeAsync().ConfigureAwait(false);

            ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                SecurityPolicies.Aes256_Sha256_RsaPss,
                MessageSecurityMode.SignAndEncrypt);
            using ChannelToken token = MaterializeToken(material);

            IFrameCaptureSink sink = observer;
            Assert.That(
                () => sink.OnTokenActivated(material.ChannelId, token, previousToken: null),
                Throws.Nothing);

            // Second dispose must be safe.
            await observer.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task FilePathReportsResolvedPath()
        {
            string keyLogPath = CreateTempPath("keys.uakeys.json");
            StandaloneKeyLogObserver observer = StandaloneKeyLogObserver.Create(keyLogPath);
            try
            {
                Assert.That(observer.FilePath, Is.EqualTo(keyLogPath));
            }
            finally
            {
                await observer.DisposeAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task MultipleTokenActivationsAllAppear()
        {
            string keyLogPath = CreateTempPath("keys.uakeys.json");
            StandaloneKeyLogObserver observer = StandaloneKeyLogObserver.Create(keyLogPath);

            IFrameCaptureSink sink = observer;
            for (uint i = 0; i < 5; i++)
            {
                ChannelKeyMaterial material = PcapTestHelpers.CreateMaterial(
                    SecurityPolicies.Aes256_Sha256_RsaPss,
                    MessageSecurityMode.SignAndEncrypt,
                    channelId: i + 1);
                using ChannelToken token = MaterializeToken(material);
                sink.OnTokenActivated(material.ChannelId, token, previousToken: null);
            }

            await observer.DisposeAsync().ConfigureAwait(false);

            string[] lines = await File.ReadAllLinesAsync(keyLogPath).ConfigureAwait(false);
            Assert.That(lines.Count(static l => l.Length > 0), Is.EqualTo(5));
        }

        private static ChannelToken MaterializeToken(ChannelKeyMaterial material)
        {
            // ChannelToken's key fields are internal; InternalsVisibleTo on
            // Opc.Ua.Core gives this test project access so we can build a
            // fully-populated token without going through the live
            // ComputeKeys path.
            return new ChannelToken
            {
                ChannelId = material.ChannelId,
                TokenId = material.TokenId,
                CreatedAt = material.CreatedAt,
                Lifetime = material.Lifetime,
                SecurityPolicy = SecurityPolicies.GetInfo(material.SecurityPolicyUri),
                ClientNonce = material.ClientNonce,
                ServerNonce = material.ServerNonce,
                ClientSigningKey = material.ClientSigningKey,
                ClientEncryptingKey = material.ClientEncryptingKey,
                ClientInitializationVector = material.ClientInitializationVector,
                ServerSigningKey = material.ServerSigningKey,
                ServerEncryptingKey = material.ServerEncryptingKey,
                ServerInitializationVector = material.ServerInitializationVector
            };
        }
    }
}
