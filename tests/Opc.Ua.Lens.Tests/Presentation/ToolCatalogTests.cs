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

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Tests.Presentation;

[TestFixture]
public sealed class ToolCatalogTests
{
    [Test]
    public void CatalogGroupsFollowTheRegistryOrder()
    {
        IReadOnlyList<ToolCatalogGroup> groups = ToolCatalog.Build();

        Assert.That(
            groups.Select(group => group.Name).ToArray(),
            Is.EqualTo(s_groups));
    }

    [Test]
    public void EveryRegisteredToolAppearsExactlyOnce()
    {
        List<PluginKind> registered = PluginRegistry.All.ToList().ConvertAll(registration => registration.Kind);

        List<PluginKind> catalogued = ToolCatalog.Build()
            .SelectMany(group => group.Tools)
            .Select(entry => entry.Kind)
            .ToList();

        Assert.That(catalogued, Is.EquivalentTo(registered));
        Assert.That(catalogued.Distinct().Count(), Is.EqualTo(catalogued.Count));
    }

    [Test]
    public void CatalogEntriesCarryTheRegistryMetadata()
    {
        ToolCatalogEntry monitor = ToolCatalog.Build()
            .SelectMany(group => group.Tools)
            .Single(entry => entry.Kind == PluginKind.Subscription);
        PluginRegistration registration = PluginRegistry.For(PluginKind.Subscription);

        Assert.That(monitor.DisplayName, Is.EqualTo(registration.DisplayName));
        Assert.That(monitor.Description, Is.EqualTo(registration.Description));
        Assert.That(monitor.Shortcut, Is.EqualTo(registration.InputGesture));
        Assert.That(monitor.ConnectionRequirement, Is.EqualTo(registration.ConnectionRequirement));
    }

    [TestCase("monitor")]
    [TestCase("MONITOR")]
    [TestCase(" values and events ")]
    public void SearchMatchesNameAndDescriptionCaseInsensitively(string query)
    {
        IReadOnlyList<ToolCatalogGroup> groups = ToolCatalog.Build(query);

        List<PluginKind> kinds = groups.SelectMany(group => group.Tools).Select(entry => entry.Kind).ToList();
        Assert.That(kinds, Does.Contain(PluginKind.Subscription));
        Assert.That(groups.All(group => group.Tools.Count > 0), Is.True);
    }

    [Test]
    public void BlankSearchReturnsTheWholeCatalog()
    {
        int all = ToolCatalog.Build().Sum(group => group.Tools.Count);
        int blank = ToolCatalog.Build("   ").Sum(group => group.Tools.Count);

        Assert.That(blank, Is.EqualTo(all));
        Assert.That(all, Is.EqualTo(PluginRegistry.All.Count));
    }

    [Test]
    public void UnmatchedSearchReturnsNoGroups()
    {
        IReadOnlyList<ToolCatalogGroup> groups = ToolCatalog.Build("no-such-tool-xyzzy");

        Assert.That(groups, Is.Empty);
    }

    private static readonly string[] s_groups = ["Explore / Connect", "Observe", "Administer", "Diagnose"];
}
