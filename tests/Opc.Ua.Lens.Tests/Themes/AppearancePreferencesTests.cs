// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UaLens.Themes;

namespace UaLens.Tests.Themes
{
    [TestFixture]
    public sealed class AppearancePreferencesTests
    {
        [SetUp]
        public void SetUp()
        {
            m_directory = Path.Combine(Path.GetTempPath(), "UaLens-appearance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_directory);
            m_path = Path.Combine(m_directory, "theme.json");
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(m_directory, recursive: true);
        }

        [Test]
        public async Task FreshPreferencesFollowSystem()
        {
            var preferences = new AppearancePreferences(m_path);
            Assert.That(await preferences.LoadAsync().ConfigureAwait(false), Is.EqualTo(ThemePreset.System));
            Assert.That(File.Exists(m_path), Is.False, "Reading the default must not manufacture a saved preference.");
        }

        [TestCase("DarkNavy")]
        [TestCase("DarkStandard")]
        [TestCase("Light")]
        [TestCase("System")]
        public async Task ExplicitLegacyChoicesRemainExplicit(string saved)
        {
            string original = "{\"theme\":\"" + saved + "\"}";
            await File.WriteAllTextAsync(m_path, original).ConfigureAwait(false);
            var preferences = new AppearancePreferences(m_path);

            Assert.That((await preferences.LoadAsync().ConfigureAwait(false)).ToString(), Is.EqualTo(saved));
            Assert.That(await File.ReadAllTextAsync(m_path).ConfigureAwait(false), Is.EqualTo(original));
        }

        [Test]
        public async Task SavingChoiceReplacesPreviousDocument()
        {
            var preferences = new AppearancePreferences(m_path);
            await preferences.SaveAsync(ThemePreset.DarkStandard).ConfigureAwait(false);
            await preferences.SaveAsync(ThemePreset.Light).ConfigureAwait(false);

            Assert.That(await preferences.LoadAsync().ConfigureAwait(false), Is.EqualTo(ThemePreset.Light));
            Assert.That(Directory.GetFiles(m_directory), Has.Length.EqualTo(1));
        }

        [TestCase("{")]
        [TestCase("null")]
        [TestCase("{\"theme\":\"unknown\"}")]
        public async Task InvalidPreferencesFailWithoutOverwritingOriginal(string original)
        {
            await File.WriteAllTextAsync(m_path, original).ConfigureAwait(false);
            var preferences = new AppearancePreferences(m_path);

            await Assert.ThatAsync(() => preferences.LoadAsync(),
                Throws.InstanceOf<JsonException>()).ConfigureAwait(false);
            Assert.That(await File.ReadAllTextAsync(m_path).ConfigureAwait(false), Is.EqualTo(original));
        }

        [Test]
        public async Task CancelledSavePreservesExistingChoiceAndRemovesTemporaryFile()
        {
            var preferences = new AppearancePreferences(m_path);
            await preferences.SaveAsync(ThemePreset.Light).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);

            await Assert.ThatAsync(
                () => preferences.SaveAsync(ThemePreset.DarkStandard, cancellation.Token),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(await preferences.LoadAsync().ConfigureAwait(false), Is.EqualTo(ThemePreset.Light));
            Assert.That(Directory.GetFiles(m_directory), Has.Length.EqualTo(1));
        }

        private string m_directory = string.Empty;
        private string m_path = string.Empty;
    }
}
