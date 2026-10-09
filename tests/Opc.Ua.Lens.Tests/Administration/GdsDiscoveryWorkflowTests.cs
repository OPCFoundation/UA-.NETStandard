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
