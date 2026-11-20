// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using UaLens.Connection;

namespace UaLens.Tests.Connection
{
    [TestFixture]
    public sealed class FavoritesStoreTests
    {
        [SetUp]
        public void SetUp()
        {
            m_directory = Path.Combine(Path.GetTempPath(), "UaLens-favorites-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_directory);
            m_path = Path.Combine(m_directory, "favorites.json");
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(m_directory, recursive: true);
        }

        [Test]
        public async Task MissingStoreDoesNotCreateAFile()
        {
            Assert.That(await FavoritesStore.LoadAsync(path: m_path).ConfigureAwait(false), Is.Empty);
            Assert.That(File.Exists(m_path), Is.False);
        }

        [Test]
        public async Task EquivalentHostsDeduplicateButDistinctPathsRemain()
        {
            string[] endpoints =
            [
                "opc.tcp://localhost:62541/Server",
                "opc.tcp://LOCALHOST:62541/Server",
                "opc.tcp://localhost:62541/server"
            ];
            await FavoritesStore.SaveAsync(endpoints, path: m_path).ConfigureAwait(false);

            Assert.That(await FavoritesStore.LoadAsync(path: m_path).ConfigureAwait(false), Is.EqualTo(
            [
                endpoints[0], endpoints[2]
            ]));
        }

        [TestCase("{")]
        [TestCase("null")]
        [TestCase("{\"version\":\"99\",\"favouriteEndpoints\":[]}")]
        [TestCase("{\"version\":\"1\",\"favouriteEndpoints\":[\"not an endpoint\"]}")]
        public async Task InvalidStoreReportsFailureAndPreservesOriginal(string original)
        {
            await File.WriteAllTextAsync(m_path, original).ConfigureAwait(false);

            await Assert.ThatAsync(() => FavoritesStore.LoadAsync(path: m_path),
                Throws.InstanceOf<JsonException>()).ConfigureAwait(false);
            Assert.That(await File.ReadAllTextAsync(m_path).ConfigureAwait(false), Is.EqualTo(original));
        }

        [Test]
        public async Task EmbeddedCredentialsAreRejectedBeforeReplacingAStore()
        {
            await FavoritesStore.SaveAsync(["opc.tcp://localhost:62541/Server"], path: m_path).ConfigureAwait(false);
            string original = await File.ReadAllTextAsync(m_path).ConfigureAwait(false);

            await Assert.ThatAsync(
                () => FavoritesStore.SaveAsync(["opc.tcp://user@example.invalid:62541/Server"], path: m_path),
                Throws.InstanceOf<JsonException>()).ConfigureAwait(false);
            Assert.That(await File.ReadAllTextAsync(m_path).ConfigureAwait(false), Is.EqualTo(original));
        }

        private string m_directory = string.Empty;
        private string m_path = string.Empty;
    }
}
