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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Pcap.KeyLog;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;
using TextEncoding = System.Text.Encoding;

namespace Opc.Ua.PubSub.Pcap.Tests.Dissection
{
    [TestFixture]
    [Category("PubSub")]
    public sealed class KeyResolverAndKeyLogTests
    {
        [Test]
        public async Task CapturedKeyLogKeyResolverResolvesExactAndWildcardHitsAsync()
        {
            using PubSubKeyMaterial material = CreateMaterial("group-a", 1);
            using CapturedKeyLogKeyResolver resolver = new([material]);

            using PubSubKeyMaterial? exact = await resolver.TryResolveAsync(
                "group-a",
                1,
                material.SecurityPolicyUri).ConfigureAwait(false);
            using PubSubKeyMaterial? wildcard = await resolver.TryResolveAsync(
                null,
                1,
                material.SecurityPolicyUri).ConfigureAwait(false);
            PubSubKeyMaterial? miss = await resolver.TryResolveAsync(
                "group-a",
                2,
                material.SecurityPolicyUri).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(exact, Is.Not.Null);
                Assert.That(wildcard, Is.Not.Null);
                Assert.That(miss, Is.Null);
                Assert.That(exact!.SigningKey.ToArray(), Is.EqualTo(material.SigningKey.ToArray()));
                Assert.That(wildcard!.EncryptingKey.ToArray(), Is.EqualTo(material.EncryptingKey.ToArray()));
            });
        }

        [Test]
        public async Task CapturedKeyLogKeyResolverAddKeyMaterialAsyncImportsStreamAsync()
        {
            using PubSubKeyMaterial material = CreateMaterial("group-b", 3);
            await using AsyncMaterialSource source = new(material);
            using CapturedKeyLogKeyResolver resolver = new();

            await resolver.AddKeyMaterialAsync(source.ReadAsync()).ConfigureAwait(false);
            using PubSubKeyMaterial? resolved = await resolver.TryResolveAsync(
                "group-b",
                3,
                material.SecurityPolicyUri).ConfigureAwait(false);

            Assert.That(resolved, Is.Not.Null);
        }

        [Test]
        public void CapturedKeyLogKeyResolverThrowsAfterDispose()
        {
            CapturedKeyLogKeyResolver resolver = new();
            resolver.Dispose();

            Assert.That(
                async () => await resolver.TryResolveAsync("group", 1, PubSubAes128CtrPolicy.Instance.PolicyUri)
                    .ConfigureAwait(false),
                Throws.TypeOf<ObjectDisposedException>());
        }

        [Test]
        public async Task SksKeyResolverResolvesProviderKeyAndMissesOtherGroupsAsync()
        {
            PubSubSecurityKey securityKey = CreateSecurityKey(5);
            using PubSubSecurityKeyRing ring = new("sks-group");
            ring.SetCurrent(securityKey);
            var resolver = new SksKeyResolver(new StaticSecurityKeyProvider("sks-group", ring));

            using PubSubKeyMaterial? hit = await resolver.TryResolveAsync(
                "sks-group",
                5,
                PubSubAes128CtrPolicy.Instance.PolicyUri).ConfigureAwait(false);
            PubSubKeyMaterial? wrongGroup = await resolver.TryResolveAsync(
                "other-group",
                5,
                PubSubAes128CtrPolicy.Instance.PolicyUri).ConfigureAwait(false);
            PubSubKeyMaterial? wrongToken = await resolver.TryResolveAsync(
                "sks-group",
                6,
                PubSubAes128CtrPolicy.Instance.PolicyUri).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(hit, Is.Not.Null);
                Assert.That(hit!.SecurityGroupId, Is.EqualTo("sks-group"));
                Assert.That(hit.TokenId, Is.EqualTo(5u));
                Assert.That(wrongGroup, Is.Null);
                Assert.That(wrongToken, Is.Null);
            });
        }

        [Test]
        public async Task PubSubKeyLogWriterRoundTripsBase64RecordsFromFileAsync()
        {
            string filePath = Path.GetTempFileName();
            try
            {
                await using (var writer = new PubSubKeyLogWriter(filePath))
                {
                    await writer.AppendAsync(CreateMaterial("log-a", 1)).ConfigureAwait(false);
                    await writer.AppendAsync(CreateMaterial("log-b", 2)).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                }

                var reader = new PubSubKeyLogReader(filePath);
                List<PubSubKeyMaterial> materials = await ReadAllAsync(reader.ReadAllAsync()).ConfigureAwait(false);

                Assert.Multiple(() =>
                {
                    Assert.That(materials, Has.Count.EqualTo(2));
                    Assert.That(materials[0].SecurityGroupId, Is.EqualTo("log-a"));
                    Assert.That(materials[1].TokenId, Is.EqualTo(2u));
                    Assert.That(materials[1].SigningKey.ToArray(), Is.EqualTo(CreateSigning(2)));
                });
                DisposeAll(materials);
            }
            finally
            {
                TryDelete(filePath);
            }
        }

        [Test]
        public async Task PubSubKeyLogReaderReadsHexRecordsAndSkipsBlankLinesFromStreamAsync()
        {
            string jsonLines = Environment.NewLine +
                "{\"securityGroupId\":\"hex-group\",\"tokenId\":7," +
                "\"securityPolicyUri\":\"" +
                PubSubAes128CtrPolicy.Instance.PolicyUri +
                "\"," +
                "\"encoding\":\"hex\",\"signingKey\":\"01020304\",\"encryptingKey\":\"05060708\"," +
                "\"keyNonce\":\"090A\"}" +
                Environment.NewLine +
                Environment.NewLine;
            using var stream = new MemoryStream(TextEncoding.UTF8.GetBytes(jsonLines));

            var reader = new PubSubKeyLogReader();
            List<PubSubKeyMaterial> materials = await ReadAllAsync(reader.ReadAllAsync(stream)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(materials, Has.Count.EqualTo(1));
                Assert.That(materials[0].SecurityGroupId, Is.EqualTo("hex-group"));
                Assert.That(materials[0].SigningKey.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
                Assert.That(materials[0].EncryptingKey.ToArray(), Is.EqualTo(new byte[] { 5, 6, 7, 8 }));
                Assert.That(materials[0].KeyNonce.ToArray(), Is.EqualTo("\t\n"u8.ToArray()));
            });
            DisposeAll(materials);
        }

        [Test]
        public void PubSubKeyLogReaderRejectsUnboundAndInvalidInputs()
        {
            var reader = new PubSubKeyLogReader();

            Assert.Multiple(() =>
            {
                Assert.That(() => reader.ReadAllAsync(), Throws.InvalidOperationException);
                Assert.That(() => new PubSubKeyLogReader(string.Empty), Throws.TypeOf<ArgumentException>());
                Assert.That(() => new PubSubKeyLogWriter(string.Empty), Throws.TypeOf<ArgumentException>());
                Assert.That(() => reader.ReadAllAsync((Stream)null!), Throws.TypeOf<ArgumentNullException>());
            });
        }

        [Test]
        public async Task PubSubKeyLogWriterThrowsAfterDisposeAsync()
        {
            string filePath = Path.GetTempFileName();
            try
            {
                var writer = new PubSubKeyLogWriter(filePath);
                await writer.DisposeAsync().ConfigureAwait(false);

                Assert.That(
                    async () => await writer.AppendAsync(CreateMaterial("disposed", 9)).ConfigureAwait(false),
                    Throws.TypeOf<ObjectDisposedException>());
            }
            finally
            {
                TryDelete(filePath);
            }
        }

        private static PubSubKeyMaterial CreateMaterial(string securityGroupId, uint tokenId)
        {
            return new PubSubKeyMaterial(
                securityGroupId,
                tokenId,
                PubSubAes128CtrPolicy.Instance.PolicyUri,
                CreateSigning(tokenId),
                [5, 6, 7, 8],
                [9, 10, 11, 12]);
        }

        private static PubSubSecurityKey CreateSecurityKey(uint tokenId)
        {
            return new PubSubSecurityKey(
                tokenId,
                ByteString.Create(CreateSigning(tokenId)),
                ByteString.Create([5, 6, 7, 8]),
                ByteString.Create([9, 10, 11, 12]),
                DateTimeUtc.From(DateTime.UtcNow),
                TimeSpan.FromMinutes(10));
        }

        private static byte[] CreateSigning(uint tokenId)
        {
            return [(byte)tokenId, 2, 3, 4];
        }

        private static async Task<List<PubSubKeyMaterial>> ReadAllAsync(
            IAsyncEnumerable<PubSubKeyMaterial> source)
        {
            List<PubSubKeyMaterial> materials = [];
            await foreach (PubSubKeyMaterial material in source.ConfigureAwait(false))
            {
                materials.Add(material);
            }
            return materials;
        }

        private static void DisposeAll(IEnumerable<PubSubKeyMaterial> materials)
        {
            foreach (PubSubKeyMaterial material in materials)
            {
                material.Dispose();
            }
        }

        private static void TryDelete(string filePath)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        private sealed class AsyncMaterialSource : IAsyncDisposable
        {
            public AsyncMaterialSource(PubSubKeyMaterial material)
            {
                m_material = material;
            }

            public async IAsyncEnumerable<PubSubKeyMaterial> ReadAsync()
            {
                await Task.Yield();
                yield return m_material;
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }

            private readonly PubSubKeyMaterial m_material;
        }
    }
}
