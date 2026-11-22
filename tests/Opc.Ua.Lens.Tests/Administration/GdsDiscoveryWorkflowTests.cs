// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.GdsDiscovery;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;

namespace UaLens.Tests.Administration;

[TestFixture]
[NonParallelizable]
public sealed partial class GdsDiscoveryWorkflowTests
{
    [Test]
    public async Task ConnectSelectionRoutesTheDisplayedEndpointWithoutDiscoveringOrConnecting()
    {
        await using var context = new AdministrationWorkflowContext();
        context.Workspace.Object.EndpointUrl = "opc.tcp://old.test:4840";
        await using var plugin = new GdsDiscoveryPlugin(context.Host);
        EndpointDescription endpoint = Endpoint();
        plugin.SelectedEndpoint = Row(endpoint);

        plugin.ConnectSelectedToConnectionPane();

        Assert.That(context.Workspace.Object.EndpointUrl, Is.EqualTo("opc.tcp://displayed.test:4841"));
        Assert.That(plugin.Status, Is.EqualTo("● opc.tcp://displayed.test:4841 → Connection pane."));
        Assert.That(plugin.Roots.Select(r => r.RootKind), Is.EqualTo(new DiscoveryRootKind?[]
        {
            DiscoveryRootKind.LocalMachine, DiscoveryRootKind.LocalNetwork,
            DiscoveryRootKind.GlobalDiscovery, DiscoveryRootKind.CustomDiscovery
        }));
        Assert.That(plugin.Roots.All(r => r.Children.Count == 0 && !r.IsExpanded), Is.True);
        Assert.That(plugin.SelectedNode, Is.Null);
        Assert.That(plugin.Endpoints, Is.Empty);
        Assert.That(context.ConnectionContext.Discoveries, Is.Empty);
        Assert.That(context.ConnectionContext.ConfigurationsCreated, Is.Zero);
        context.Workspace.Verify(w => w.OpenToolAsync(
            It.IsAny<PluginKind>(), It.IsAny<EndpointDescription?>(), It.IsAny<CancellationToken>()), Times.Never);
        plugin.SelectedEndpoint = null;
        plugin.ConnectSelectedToConnectionPane();
        Assert.That(plugin.Status, Is.EqualTo("● Pick a server or endpoint first."));
        Assert.That(context.Workspace.Object.EndpointUrl, Is.EqualTo("opc.tcp://displayed.test:4841"));
    }
}
