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

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Tests.Desktop;
using UaLens.ViewModels;

namespace UaLens.Tests.ViewModels;

[TestFixture]
[NonParallelizable]
public sealed class BrowserViewModelWorkflowTests
{
    [Test]
    public async Task LazyNodeCreatesALeafPlaceholderAndAttemptsOnlyOneExpansionWhileOffline()
    {
        await using var context = new DesktopConnectionContext();
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

        Assert.That(node.ChildrenLoaded, Is.True);
        Assert.That(node.Children.Single(), Is.SameAs(placeholder));
        Assert.That(node.ParentNodeId, Is.EqualTo(ObjectIds.ObjectsFolder));
        Assert.That(context.ConfigurationsCreated, Is.Zero);
    }

    [Test]
    [Platform("Win,Linux")]
    public Task CanceledOrUnchangedViewSelectionDoesNotDiscardExistingTreeButNewKindReloads()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            await using var context = new DesktopConnectionContext();
            BrowserViewModel browser = context.Browser;
            var root = new NodeViewModel(browser, NodeId.Null, ObjectIds.RootFolder, "Retained root", NodeClass.Object);
            browser.Roots.Add(root);
            await browser.SetViewKindAsync(BrowseViewKind.Objects, default).ConfigureAwait(true);
            Assert.That(browser.Roots.Single(), Is.SameAs(root));
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(true);

            Assert.That(() => browser.SetViewKindAsync(BrowseViewKind.DataTypes, cancellation.Token),
                Throws.InstanceOf<System.OperationCanceledException>());

            Assert.That(browser.CurrentViewKind, Is.EqualTo(BrowseViewKind.Objects));
            Assert.That(browser.Roots.Single(), Is.SameAs(root));
            await browser.SetViewKindAsync(BrowseViewKind.DataTypes, default).ConfigureAwait(true);
            Assert.That(browser.CurrentViewKind, Is.EqualTo(BrowseViewKind.DataTypes));
            Assert.That(browser.Roots, Is.Empty);
            Assert.That(context.ConfigurationsCreated, Is.Zero);
        });
    }
}
