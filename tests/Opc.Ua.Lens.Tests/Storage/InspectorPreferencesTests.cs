// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using UaLens.Storage;
using UaLens.ViewModels;

namespace UaLens.Tests.Storage;

[TestFixture]
public sealed class InspectorPreferencesTests
{
    [SetUp]
    public void SetUp()
    {
        m_directory = Path.Combine(Path.GetTempPath(), "UaLens-inspectors-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(m_directory);
        m_path = Path.Combine(m_directory, "inspectors.json");
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(m_directory, recursive: true);
    }

    [Test]
    public async Task FreshPreferencesShowBothInspectors()
    {
        var preferences = new InspectorPreferences(m_path);

        Assert.That(
            await preferences.LoadAsync().ConfigureAwait(false),
            Is.EqualTo(SidePanelMode.AttrsAndRefs));
        Assert.That(File.Exists(m_path), Is.False);
    }

    [Test]
    public async Task FreshPreferencesShowBothReferenceDirections()
    {
        var preferences = new InspectorPreferences(m_path);

        InspectorSettings settings = await preferences.LoadSettingsAsync().ConfigureAwait(false);

        Assert.That(settings.ReferenceDirection, Is.EqualTo(ReferenceDirectionFilter.Both));
    }

    [Test]
    public async Task ExistingVisibilityPreferenceDefaultsToBothReferenceDirections()
    {
        await File.WriteAllTextAsync(m_path, "{\"mode\":\"AttrsOnly\"}").ConfigureAwait(false);
        var preferences = new InspectorPreferences(m_path);

        InspectorSettings settings = await preferences.LoadSettingsAsync().ConfigureAwait(false);

        Assert.That(settings.Mode, Is.EqualTo(SidePanelMode.AttrsOnly));
        Assert.That(settings.ReferenceDirection, Is.EqualTo(ReferenceDirectionFilter.Both));
    }

    [TestCase((int)SidePanelMode.None)]
    [TestCase((int)SidePanelMode.AttrsOnly)]
    [TestCase((int)SidePanelMode.AttrsAndRefs)]
    [TestCase((int)SidePanelMode.RefsOnly)]
    public async Task SavedVisibilityIsRestored(int value)
    {
        var mode = (SidePanelMode)value;
        var preferences = new InspectorPreferences(m_path);

        await preferences.SaveAsync(mode).ConfigureAwait(false);

        Assert.That(await preferences.LoadAsync().ConfigureAwait(false), Is.EqualTo(mode));
    }

    [TestCase((int)ReferenceDirectionFilter.Forward)]
    [TestCase((int)ReferenceDirectionFilter.Backward)]
    [TestCase((int)ReferenceDirectionFilter.Both)]
    public async Task SavedReferenceDirectionIsRestored(int value)
    {
        var direction = (ReferenceDirectionFilter)value;
        var preferences = new InspectorPreferences(m_path);

        await preferences.SaveSettingsAsync(
            new InspectorSettings(SidePanelMode.RefsOnly, direction)).ConfigureAwait(false);

        InspectorSettings settings = await preferences.LoadSettingsAsync().ConfigureAwait(false);
        Assert.That(settings.Mode, Is.EqualTo(SidePanelMode.RefsOnly));
        Assert.That(settings.ReferenceDirection, Is.EqualTo(direction));
    }

    [TestCase("{")]
    [TestCase("null")]
    [TestCase("{\"mode\":\"unknown\"}")]
    public async Task InvalidPreferencesFailWithoutOverwritingOriginal(string original)
    {
        await File.WriteAllTextAsync(m_path, original).ConfigureAwait(false);
        var preferences = new InspectorPreferences(m_path);

        await Assert.ThatAsync(
            () => preferences.LoadAsync(),
            Throws.InstanceOf<JsonException>()).ConfigureAwait(false);
        Assert.That(await File.ReadAllTextAsync(m_path).ConfigureAwait(false), Is.EqualTo(original));
    }

    private string m_directory = string.Empty;
    private string m_path = string.Empty;
}
