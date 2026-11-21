// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.ViewModels;

[TestFixture]
[NonParallelizable]
public sealed partial class BrowserViewModelWorkflowTests
{
    [Test]
    public async Task LazyOfflineNodeKeepsARetryPlaceholderWithoutOpeningAConnection()
    {
        await using var context = new DesktopConnectionContext(dispatcher: InlineWorkspaceDispatcher.Instance);
        var node = new NodeViewModel(context.Browser, ObjectIds.ObjectsFolder,
            new NodeId("Boiler", 2), "Boiler", NodeClass.Object);
        NodeViewModel placeholder = node.Children.Single();
        Assert.That(placeholder.IsPlaceholder, Is.True);
        Assert.That(placeholder.HasItems, Is.False);
        Assert.That(placeholder.Children, Is.Empty);
        Assert.That(placeholder.ParentNodeId, Is.EqualTo(node.NodeId));
        Assert.That(node.ChildrenLoaded, Is.False);

        node.IsExpanded = true;
        node.IsExpanded = false;
        node.IsExpanded = true;
        await context.Browser.LoadChildrenAsync(node).ConfigureAwait(false);

        Assert.That(node.ChildrenLoaded, Is.False);
        Assert.That(node.IsLoading, Is.False);
        Assert.That(node.LoadError, Does.Contain("Reconnect, then retry"));
        Assert.That(node.Children.Single().IsPlaceholder, Is.True);
        Assert.That(node.HasItems, Is.True);
        Assert.That(node.ParentNodeId, Is.EqualTo(ObjectIds.ObjectsFolder));
        Assert.That(context.ConfigurationsCreated, Is.Zero);
    }
}
