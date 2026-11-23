// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using SampleDocument = Opc.Ua.WotCon.Tests.Samples.WotAggregationDocumentGenerator.SampleDocument;

namespace Opc.Ua.WotCon.Tests.Samples
{
    /// <summary>
    /// Holds the per-process sample cache to its contract: one read per
    /// input, never a value a writer has replaced, never a cached failure.
    /// </summary>
    /// <remarks>
    /// Non-parallelizable because a write clears the whole cache, which would
    /// make concurrently running sample tests convert their inputs again.
    /// </remarks>
    [TestFixture]
    [Category("WotCon")]
    [Category("Samples")]
    [NonParallelizable]
    public sealed class SampleInputCacheTests
    {
        [SetUp]
        public void SetUp()
        {
            m_directory = Path.Combine(
                Path.GetTempPath(), "wot-sample-cache-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_directory))
            {
                Directory.Delete(m_directory, recursive: true);
            }
        }

        [Test]
        public async Task SampleWriteReplacesTheCachedManifestDocumentsAsync()
        {
            WriteManifest("{\"version\":1}");

            ArrayOf<SampleDocument> first = SampleInputCache.ManifestDocuments(m_directory);
            ArrayOf<SampleDocument> again = SampleInputCache.ManifestDocuments(
                m_directory + Path.DirectorySeparatorChar);
            Assert.That(again.Memory, Is.EqualTo(first.Memory), "The second read is served from the cache.");

            byte[] rewritten = Encoding.UTF8.GetBytes("{\"version\":2}");
            await WotAggregationDocumentGenerator.WriteBytesAsync(
                Path.Combine(m_directory, "set", "a.json"), ByteString.From(rewritten)).ConfigureAwait(false);

            ArrayOf<SampleDocument> afterWrite = SampleInputCache.ManifestDocuments(m_directory);
            Assert.That(afterWrite.Count, Is.EqualTo(1));
            Assert.That(afterWrite[0].Json.ToArray(), Is.EqualTo(rewritten));
            Assert.That(first[0].Json.ToArray(), Is.EqualTo(Encoding.UTF8.GetBytes("{\"version\":1}")),
                "A value already handed out is not changed by the write.");
        }

        [Test]
        public void FailedLoadIsRetriedNotCached()
        {
            Assert.That(
                () => SampleInputCache.ManifestDocuments(m_directory),
                Throws.InstanceOf<IOException>());

            WriteManifest("{\"version\":1}");

            ArrayOf<SampleDocument> documents = SampleInputCache.ManifestDocuments(m_directory);
            Assert.That(documents.Count, Is.EqualTo(1));
            Assert.That(documents[0].ResourceId, Is.EqualTo("a"));
        }

        private void WriteManifest(string document)
        {
            Directory.CreateDirectory(Path.Combine(m_directory, "set"));
            File.WriteAllText(
                Path.Combine(m_directory, "documents.json"),
                "[{\"resourceId\":\"a\",\"path\":\"set/a.json\",\"documentKind\":\"ThingDescription\"}]");
            File.WriteAllText(Path.Combine(m_directory, "set", "a.json"), document);
        }

        private string m_directory = string.Empty;
    }
}
