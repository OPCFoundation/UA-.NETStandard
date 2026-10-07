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
