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
using System.Threading;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Capabilities;
using UaLens.ViewModels;
using UaLens.Views;

namespace UaLens.Tests.Presentation;

[TestFixture]
public sealed class ToolCatalogTests
{
    [Test]
    public void CatalogGroupsFollowTheRegistryOrder()
    {
        ArrayOf<ToolCatalogGroup> groups = ToolCatalog.Build();

        Assert.That(
            groups.ToList().Select(group => group.Name).ToArray(),
            Is.EqualTo(s_groups));
    }

    [Test]
    public void EveryRegisteredToolAppearsExactlyOnce()
    {
        List<PluginKind> registered = PluginRegistry.All.ToList().ConvertAll(registration => registration.Kind);

        List<PluginKind> catalogued = ToolCatalog.Build().ToList()
            .SelectMany(group => group.Tools.ToList())
            .Select(entry => entry.Kind)
            .ToList();

        Assert.That(catalogued, Is.EquivalentTo(registered));
        Assert.That(catalogued.Distinct().Count(), Is.EqualTo(catalogued.Count));
    }

    [Test]
    public void CatalogEntriesCarryTheRegistryMetadata()
    {
        ToolCatalogEntry monitor = ToolCatalog.Build().ToList()
            .SelectMany(group => group.Tools.ToList())
            .Single(entry => entry.Kind == PluginKind.Subscription);
        PluginRegistration registration = PluginRegistry.For(PluginKind.Subscription);

        Assert.That(monitor.DisplayName, Is.EqualTo(registration.DisplayName));
        Assert.That(monitor.Description, Is.EqualTo(registration.Description));
        Assert.That(monitor.Shortcut, Is.EqualTo(registration.InputGesture));
        Assert.That(monitor.ConnectionRequirement, Is.EqualTo(registration.ConnectionRequirement));
        Assert.That(monitor.Availability.State, Is.EqualTo(CapabilityState.RequiresConfiguration));
        Assert.That(monitor.Availability.CanExecute, Is.False);
        Assert.That(monitor.AvailabilityText, Does.Contain("Configure offline"));
    }

    [TestCase("monitor")]
    [TestCase("MONITOR")]
    [TestCase(" values and events ")]
    public void SearchMatchesNameAndDescriptionCaseInsensitively(string query)
    {
        ArrayOf<ToolCatalogGroup> groups = ToolCatalog.Build(query);

        List<PluginKind> kinds = groups.ToList()
            .SelectMany(group => group.Tools.ToList()).Select(entry => entry.Kind).ToList();
        Assert.That(kinds, Does.Contain(PluginKind.Subscription));
        Assert.That(groups.ToList().All(group => group.Tools.Count > 0), Is.True);
    }

    [Test]
    public void BlankSearchReturnsTheWholeCatalog()
    {
        int all = ToolCatalog.Build().ToList().Sum(group => group.Tools.Count);
        int blank = ToolCatalog.Build("   ").ToList().Sum(group => group.Tools.Count);

        Assert.That(blank, Is.EqualTo(all));
        Assert.That(all, Is.EqualTo(PluginRegistry.All.Count));
    }

    [Test]
    public void UnmatchedSearchReturnsNoGroups()
    {
        ArrayOf<ToolCatalogGroup> groups = ToolCatalog.Build("no-such-tool-xyzzy");

        Assert.That(groups.Count, Is.Zero);
    }

    [Test]
    public void NetworkRuntimeMetadataDoesNotPretendToBeLocalOrAGdsSession()
    {
        PluginRegistration network = PluginRegistry.For(PluginKind.Subscription) with
        {
            ConnectionScope = ToolConnectionScope.IndependentNetwork
        };

        Assert.That(network.ConnectionRequirement, Does.Contain("independent network runtime"));
        Assert.That(network.ConnectionRequirement, Does.Not.Contain("primary"));
        CapabilityResult availability = ToolCatalog.GetAvailability(network);
        Assert.That(availability.State, Is.EqualTo(CapabilityState.RequiresConfiguration));
        Assert.That(availability.Reason, Does.Contain("explicitly Start"));
        Assert.That(availability.CanExecute, Is.False);
    }

    [Test]
    public void OfflineCatalogRetainsEveryDocumentAndDistinguishesLocalFromLiveOperations()
    {
        List<ToolCatalogEntry> entries = ToolCatalog.Build().ToList()
            .SelectMany(group => group.Tools.ToList()).ToList();

        Assert.That(entries, Has.Count.EqualTo(PluginRegistry.All.Count));
        Assert.That(entries.Single(entry => entry.Kind == PluginKind.CertificateManager).Availability.State,
            Is.EqualTo(CapabilityState.Supported));
        Assert.That(entries.Single(entry => entry.Kind == PluginKind.GdsPush).Availability.State,
            Is.EqualTo(CapabilityState.RequiresConfiguration));
        Assert.That(entries.Single(entry => entry.Kind == PluginKind.RoleManagement).Capability,
            Is.EqualTo(new CapabilityRequest(ObjectIds.Server_ServerCapabilities_RoleSet, CapabilityOperation.Browse)));
        Assert.That(entries.Single(entry => entry.Kind == PluginKind.RoleManagement).AvailabilityText,
            Does.StartWith("RoleSet browsing:"));
        Assert.That(entries.Single(entry => entry.Kind == PluginKind.EventView).CapabilityLabel,
            Is.EqualTo("Default Server event source"));
    }

    [TestCase((int)CapabilityState.Supported)]
    [TestCase((int)CapabilityState.Unsupported)]
    [TestCase((int)CapabilityState.Unknown)]
    [TestCase((int)CapabilityState.RequiresConfiguration)]
    [TestCase((int)CapabilityState.Denied)]
    public void LiveCatalogProjectsCachedEvidenceWithoutStartingNetworkOperations(int state)
    {
        PluginRegistration registration = PluginRegistry.For(PluginKind.RoleManagement);
        var request = new CapabilityRequest(ObjectIds.Server_ServerCapabilities_RoleSet, CapabilityOperation.Browse);
        var expected = new CapabilityResult((CapabilityState)state, "Actionable target-specific evidence.");
        var capabilities = new Mock<ICapabilityService>(MockBehavior.Strict);
        capabilities.Setup(value => value.GetCached(request)).Returns(expected);

        CapabilityResult result = ToolCatalog.GetAvailability(
            registration, primaryConnected: true, capabilities.Object);

        Assert.That(result, Is.SameAs(expected));
        Assert.That(result.Reason, Is.EqualTo("Actionable target-specific evidence."));
        capabilities.Verify(value => value.GetCached(request), Times.Once);
        capabilities.Verify(value => value.ProbeAsync(
            It.IsAny<CapabilityRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        capabilities.VerifyNoOtherCalls();
    }

    [Test]
    public void PrimaryEvidenceDoesNotClassifySecondarySessionsOrIndependentNetworkRuntimes()
    {
        var capabilities = new Mock<ICapabilityService>(MockBehavior.Strict);
        PluginRegistration secondary = PluginRegistry.For(PluginKind.GdsPush);
        PluginRegistration network = PluginRegistry.For(PluginKind.Subscription) with
        {
            ConnectionScope = ToolConnectionScope.IndependentNetwork
        };

        Assert.That(ToolCatalog.GetAvailability(secondary, true, capabilities.Object).State,
            Is.EqualTo(CapabilityState.RequiresConfiguration));
        Assert.That(ToolCatalog.GetAvailability(network, true, capabilities.Object).State,
            Is.EqualTo(CapabilityState.RequiresConfiguration));
        capabilities.VerifyNoOtherCalls();
    }

    private static readonly string[] s_groups = ["Explore / Connect", "Observe", "Administer", "Diagnose"];
}
